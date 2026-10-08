using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Avalonia.Threading;
using System.Net.Http;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Microsoft.Extensions.DependencyInjection;
using ReactiveUI;
using QuickApp.Core.Services;
using QuickApp.Platform;
using QuickApp.ViewModels;
using QuickApp.Views;

namespace QuickApp;

public partial class App : Application
{
    private ServiceProvider? _services;
    private ISingleInstance? _singleInstance;
    private IHotkeyService? _hotkey;
    private string? _hotkeySignature;
    private string? _hotkeyError;
    private DockWindow? _dock;
    private SettingsWindow? _settings;
    private TrayIcon? _tray;
    private TrayViewModel? _trayViewModel;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            base.OnFrameworkInitializationCompleted();
            return;
        }

        // 单实例必须在建窗口之前判断
        _singleInstance = PlatformServices.CreateSingleInstance(AppLog.Info);
        if (!_singleInstance.IsFirstInstance)
        {
            _singleInstance.SignalExistingInstance();
            _singleInstance.Dispose();
            AppLog.Info("已有实例在运行，本进程退出");
            Environment.Exit(0);
            return;
        }

        _services = BuildServices();
        var viewModel = _services.GetRequiredService<DockViewModel>();
        var appIcon = CreateAppIcon();

        // 推荐应用：卡片装配 + 后台周期刷新（版本徽标保持新鲜）
        var recommendedApps = BuildRecommendedApps(_services, viewModel);
        _ = recommendedApps.RunPeriodicRefreshAsync();

        _dock = new DockWindow();
        _dock.Attach(viewModel);
        _dock.Icon = appIcon;
        _dock.SettingsRequested += (_, _) => ShowSettings();
        _dock.AboutRequested += (_, _) => ShowSettings("about");

        _singleInstance.Listen(() => _dock.ActivateFromExternal());

        RegisterGlobalHotkey(viewModel);
        viewModel.HotkeyBindingsChanged += () => RegisterGlobalHotkey(viewModel, notify: true);
        if (OperatingSystem.IsWindows()) viewModel.CheckHotkeyAvailability = gesture =>
        {
            using IHotkeyService probe = PlatformServices.CreateHotkeyService(AppLog.Info);
            return probe.TryRegister(gesture, () => { }, out string? error) ? null : error;
        };

        desktop.MainWindow = _dock;
        _dock.Show();

        CreateTrayIcon(viewModel);
        if (OperatingSystem.IsWindows() && _hotkeyError is not null) viewModel.Toast(_hotkeyError, durationMs: 10000);

        if (viewModel.HasConfigLoadError)
        {
            viewModel.Toast("配置读取失败，原数据未覆盖，请在设置的数据页重新加载。", "重试",
                ReactiveCommand.Create(() => viewModel.ReloadConfig()), 10000);
        }

        if (viewModel.Settings.CheckUpdates)
        {
            _ = viewModel.CheckUpdateAsync();
        }

        AppLog.Info("Dock 已显示，配置：" + viewModel.ConfigFilePath + "，快捷项：" + viewModel.Items.Count);
        base.OnFrameworkInitializationCompleted();
    }

    private static ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();

        services.AddSingleton(_ => new ConfigStore(AppContext.BaseDirectory, AppLog.Info));
        services.AddSingleton<ILauncher, ProcessLauncher>();
        services.AddSingleton<IAutoStartService>(_ => PlatformServices.CreateAutoStartService(AppLog.Info));
        services.AddSingleton<IInstalledAppProvider>(_ => PlatformServices.CreateInstalledAppProvider(AppLog.Info));
        services.AddSingleton<IIconProvider>(_ => PlatformServices.CreateIconProvider(AppPaths.IconCacheDirectory(), AppLog.Info));
        services.AddSingleton<IUpdateChecker>(_ => new UpdateChecker(
            new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(12) },
            owner: "dotnet9",
            repo: "QuickApp",
            log: AppLog.Info,
            preferInstaller: !AppPaths.IsPortable(AppContext.BaseDirectory),
            stateFile: AppPaths.UpdateStateFile(AppContext.BaseDirectory)));
        services.AddSingleton<IUpdateDownloader>(_ => new UpdateDownloader(
            new HttpClient { Timeout = TimeSpan.FromMinutes(10) }));
        services.AddSingleton(sp => new DockViewModel(
            sp.GetRequiredService<ConfigStore>(),
            sp.GetRequiredService<ILauncher>(),
            sp.GetRequiredService<IIconProvider>(),
            sp.GetRequiredService<IInstalledAppProvider>(),
            sp.GetRequiredService<IUpdateChecker>(),
            sp.GetRequiredService<IUpdateDownloader>(),
            sp.GetRequiredService<IAutoStartService>(),
            appName: "QuickApp"));

        // 推荐应用：目录内嵌、安装包地址运行时按系统实时解析（推荐软件发新版无需更新 QuickApp）
        services.AddSingleton<IRecommendedAppsService>(_ => new RecommendedAppsService(
            new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(12) },
            log: AppLog.Info));
        services.AddSingleton(_ => new RecommendedAppsStateStore(AppContext.BaseDirectory, AppLog.Info));

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// 装配推荐应用控制器：卡片安装完成后自动加入 Dock、Dock 成员变化反向同步到卡片；
    /// 下载与安装能力由 DockViewModel 提供（复用自更新的下载器与静默安装逻辑）。
    /// </summary>
    private static RecommendedAppsController BuildRecommendedApps(ServiceProvider services, DockViewModel dock)
    {
        var controller = new RecommendedAppsController(
            services.GetRequiredService<IRecommendedAppsService>(),
            services.GetRequiredService<RecommendedAppsStateStore>(),
            refreshCard: card => card.RefreshAsync(),
            installCompleted: card => dock.RefreshRecommendedIcon(card.Id),
            dockMembershipChanged: (card, joining) =>
            {
                if (joining)
                {
                    dock.AddRecommendedItem(card.App, card.InstalledRecord?.LauncherPath);
                }
                else
                {
                    dock.RemoveRecommendedItem(card.Id);
                }
            },
            toast: message =>
            {
                dock.Toast(message);
                return message;
            });

        foreach (RecommendedAppCardViewModel card in controller.Cards)
        {
            card.DownloadAsync = dock.DownloadRecommendedAssetAsync;
            card.RunInstallerAsync = installerPath => dock.InstallRecommendedPackageAsync(card.App, installerPath);
            card.Initialize(
                inDock: dock.IsRecommendedInDock(card.Id),
                record: services.GetRequiredService<RecommendedAppsStateStore>().Find(card.Id));
        }

        dock.RecommendedApps = controller;
        return controller;
    }

    /// <summary>全局唤起热键（默认 Ctrl+Alt+Space）：弹出搜索界面并聚焦，见 <see cref="ToggleDockFromHotkey"/>。</summary>
    private void RegisterGlobalHotkey(DockViewModel viewModel, bool notify = false)
    {
        try
        {
            var bindings = new List<HotkeyBinding>
            {
                new(viewModel.Settings.Hotkey, "唤出 QuickApp", () => ToggleDockFromHotkey(viewModel))
            };
            bindings.AddRange(viewModel.CreateItemHotkeyBindings());
            string signature = string.Join("\n", bindings.Select(binding => binding.Gesture + "\0" + binding.Name + "\0" + binding.Identity));
            if (_hotkeySignature == signature) return;
            _hotkey ??= PlatformServices.CreateHotkeyService(AppLog.Info);
            IReadOnlyList<string> errors = _hotkey.RegisterBindings(bindings);
            _hotkeySignature = errors.Count == 0 ? signature : null;
            _hotkeyError = errors.Count > 0 ? errors[0] : null;
            foreach (string error in errors) AppLog.Info("全局热键注册失败：" + error);
            if (errors.Count > 0 && notify) Dispatcher.UIThread.Post(() => viewModel.Toast(errors[0]));
            if (errors.Count == 0) AppLog.Info("已注册 " + bindings.Count + " 个全局快捷键");
        }
        catch (Exception ex)
        {
            AppLog.Error("注册全局热键失败", ex);
        }
    }

    /// <summary>
    /// 全局唤起热键（默认 Ctrl+Alt+Space）：未在搜索态时弹出搜索并聚焦输入框（输入法切英文），
    /// 已在搜索态且未钉住时收回，等效一个开关。
    /// </summary>
    private void ToggleDockFromHotkey(DockViewModel viewModel)
    {
        if (viewModel.IsDockVisible && viewModel.IsSearchOpen && !viewModel.IsPinned)
        {
            viewModel.IsDockVisible = false;
            return;
        }

        viewModel.IsDockVisible = true;
        _dock?.SummonSearchFromHotkey();
    }

    /// <summary>
    /// 托盘视图：菜单状态与命令都在 <see cref="TrayViewModel"/>，
    /// 这里只创建 NativeMenu 并把 Header / Command 绑定到视图模型（原型 08 · 托盘菜单）。
    /// </summary>
    private void CreateTrayIcon(DockViewModel viewModel)
    {
        try
        {
            _trayViewModel = new TrayViewModel(viewModel, () => ShowSettings(), RequestShutdown);
            var appIcon = CreateAppIcon();

            var toggle = new NativeMenuItem { Header = _trayViewModel.ToggleDockText, Command = _trayViewModel.ToggleDockCommand };
            var autoStart = new NativeMenuItem { Header = _trayViewModel.AutoStartText, Command = _trayViewModel.ToggleAutoStartCommand };
            var pin = new NativeMenuItem { Header = _trayViewModel.PinText, Command = _trayViewModel.TogglePinCommand };
            var settings = new NativeMenuItem { Header = "设置", Command = _trayViewModel.ShowSettingsCommand };
            var exit = new NativeMenuItem { Header = "退出", Command = _trayViewModel.ExitCommand };

            // 文案随视图模型属性刷新（Dock 显隐/钉住/开机启动也可能在托盘之外变化）
            _trayViewModel.ObservableForProperty(t => t.ToggleDockText).Subscribe(x => toggle.Header = x.Value);
            _trayViewModel.ObservableForProperty(t => t.AutoStartText).Subscribe(x => autoStart.Header = x.Value);
            _trayViewModel.ObservableForProperty(t => t.PinText).Subscribe(x => pin.Header = x.Value);

            var menu = new NativeMenu
            {
                Items =
                {
                    toggle,
                    autoStart,
                    pin,
                    new NativeMenuItemSeparator(),
                    settings,
                    new NativeMenuItemSeparator(),
                    exit
                }
            };

            _tray = new TrayIcon
            {
                Icon = appIcon,
                ToolTipText = "QuickApp 快捷应用",
                Menu = menu,
                IsVisible = true
            };

            _tray.Clicked += (_, _) => _trayViewModel.ToggleDockCommand.Execute(null);

            var trayIcons = new TrayIcons { _tray };
            TrayIcon.SetIcons(this, trayIcons);
        }
        catch (Exception ex)
        {
            AppLog.Error("创建托盘菜单失败", ex);
        }
    }
    /// <summary>
    /// 应用图标来自仓库根目录 logo.ico：csproj 里以 ApplicationIcon 嵌入 exe，
    /// 同时作为 AvaloniaResource 内嵌，供窗口、托盘加载同一份图标。
    /// </summary>
    private static WindowIcon CreateAppIcon()
    {
        try
        {
            using Stream stream = AssetLoader.Open(new Uri("avares://QuickApp/Assets/logo.ico"));
            return new WindowIcon(stream);
        }
        catch (Exception ex)
        {
            // 资源加载意外失败时退回矢量绘制，窗口/托盘不至于没有图标
            AppLog.Error("加载内嵌 logo.ico 失败，改用运行时绘制", ex);
            return DrawAppIcon();
        }
    }

    /// <summary>logo 的矢量兜底绘制：品牌蓝渐变圆角方块 + 白色 Q 环 + 琥珀闪电尾巴，坐标同 logo.svg。</summary>
    private static WindowIcon DrawAppIcon()
    {
        const int size = 32;
        const double k = size / 512.0;
        var bitmap = new RenderTargetBitmap(new PixelSize(size, size), new Vector(96, 96));

        using (DrawingContext context = bitmap.CreateDrawingContext())
        {
            var background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.Parse("#5B82F8"), 0),
                    new GradientStop(Color.Parse("#3D5CE8"), 0.55),
                    new GradientStop(Color.Parse("#2F49C9"), 1)
                }
            };
            context.DrawRectangle(background, null, new RoundedRect(new Rect(0, 0, size, size), 112 * k));

            using (context.PushTransform(Matrix.CreateScale(k, k)))
            {
                // Q 环：圆心 (254,244) 半径 124，线宽 60
                var ringPen = new Pen(new SolidColorBrush(Colors.White), 60);
                context.DrawEllipse(null, ringPen, new Point(254, 244), 124, 124);

                // Q 的尾巴：一道闪电，fill + 同色描边得到圆角连接，顶点与 logo.svg 一致
                var boltBrush = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0.5, 0, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(0.5, 1, RelativeUnit.Relative),
                    GradientStops =
                    {
                        new GradientStop(Color.Parse("#FFD44E"), 0),
                        new GradientStop(Color.Parse("#FFAB1F"), 1)
                    }
                };
                Geometry bolt = Geometry.Parse(
                    "M315.5,221.9 L283.4,343.3 L329.7,326.4 L347.1,420.2 L384.4,280.3 L333.9,298.6 Z");
                context.DrawGeometry(boltBrush, new Pen(boltBrush, 16) { LineJoin = PenLineJoin.Round }, bolt);
            }
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream);
        stream.Position = 0;
        return new WindowIcon(stream);
    }

    private void ShowSettings(string? tab = null)
    {
        if (_services is null)
        {
            return;
        }

        DockViewModel viewModel = _services.GetRequiredService<DockViewModel>();
        if (_settings is not null)
        {
            _settings.ShowTab(tab);
            _settings.Activate();
            return;
        }

        _settings = new SettingsWindow();
        _settings.Attach(viewModel);
        _settings.ShowTab(tab);
        _settings.Icon = CreateAppIcon();
        _settings.Closed += (_, _) =>
        {
            viewModel.ApplySettings();
            _settings = null;
        };
        _settings.Show();
    }

    /// <summary>真正退出：保存配置、撤掉托盘。</summary>
    public void RequestShutdown()
    {
        try
        {
            _services?.GetService<DockViewModel>()?.Save();
            if (_tray is not null)
            {
                _tray.IsVisible = false;
                _tray.Dispose();
                _tray = null;
            }

            _singleInstance?.Dispose();
            _singleInstance = null;

            _hotkey?.Dispose();
            _hotkey = null;

            // 停掉开始菜单目录的 FileSystemWatcher
            (_services?.GetService<IInstalledAppProvider>() as IDisposable)?.Dispose();

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Shutdown();
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("退出时出错", ex);
            Environment.Exit(0);
        }
    }
}
