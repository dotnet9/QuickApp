using System;
using System.IO;
using System.Net.Http;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Microsoft.Extensions.DependencyInjection;
using QuickApp.Core.Services;
using QuickApp.Platform.Windows;
using QuickApp.ViewModels;
using QuickApp.Views;

namespace QuickApp;

public partial class App : Application
{
    private ServiceProvider? _services;
    private SingleInstanceService? _singleInstance;
    private HotkeyService? _hotkey;
    private DockWindow? _dock;
    private SettingsWindow? _settings;
    private TrayIcon? _tray;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            base.OnFrameworkInitializationCompleted();
            return;
        }

        // 单实例必须在建窗口之前判断
        _singleInstance = new SingleInstanceService(AppLog.Info);
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

        _dock = new DockWindow();
        _dock.Attach(viewModel);
        _dock.Icon = appIcon;
        _dock.SettingsRequested += (_, _) => ShowSettings();
        _dock.AboutRequested += (_, _) => ShowSettings("about");

        _singleInstance.Listen(() => _dock.ActivateFromExternal());

        RegisterGlobalHotkey(viewModel);

        desktop.MainWindow = _dock;
        _dock.Show();

        CreateTrayIcon(viewModel);

        if (viewModel.Settings.CheckUpdates)
        {
            _ = viewModel.CheckUpdateAsync();
        }

        AppLog.Info("Dock 已显示，配置：" + viewModel.ConfigFilePath);
        base.OnFrameworkInitializationCompleted();
    }

    private static ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();

        services.AddSingleton(_ => new ConfigStore(AppContext.BaseDirectory, AppLog.Info));
        services.AddSingleton<ILauncher, ProcessLauncher>();
        services.AddSingleton<IAutoStartService, AutoStartService>();
        services.AddSingleton<IIconProvider>(_ => new IconProvider(AppPaths.IconCacheDirectory(), AppLog.Info));
        services.AddSingleton<IUpdateChecker>(_ => new UpdateChecker(
            new HttpClient { Timeout = TimeSpan.FromSeconds(12) },
            owner: "dotnet9",
            repo: "QuickApp",
            log: AppLog.Info));
        services.AddSingleton(sp => new DockViewModel(
            sp.GetRequiredService<ConfigStore>(),
            sp.GetRequiredService<ILauncher>(),
            sp.GetRequiredService<IIconProvider>(),
            sp.GetRequiredService<IUpdateChecker>(),
            sp.GetRequiredService<IAutoStartService>(),
            appName: "QuickApp"));

        return services.BuildServiceProvider();
    }

    /// <summary>全局唤起热键（默认 Ctrl+Alt+Space）：显隐切换，收起状态下一按即唤出。</summary>
    private void RegisterGlobalHotkey(DockViewModel viewModel)
    {
        try
        {
            _hotkey = new HotkeyService(AppLog.Info);
            if (_hotkey.TryRegister(viewModel.Settings.Hotkey, () => ToggleDockFromHotkey(viewModel), out string? error))
            {
                AppLog.Info("全局热键已注册：" + viewModel.Settings.Hotkey);
            }
            else
            {
                _hotkey.Dispose();
                _hotkey = null;
                AppLog.Info("全局热键注册失败：" + error);
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("注册全局热键失败", ex);
        }
    }

    private void ToggleDockFromHotkey(DockViewModel viewModel)
    {
        // 收起（且未钉住）时唤出；显示时收回，等效一个开关
        viewModel.IsDockVisible = !(viewModel.IsDockVisible && !viewModel.IsPinned);
    }

    private void CreateTrayIcon(DockViewModel viewModel)
    {
        try
        {
            var menu = new NativeMenu();

            var toggle = new NativeMenuItem("显示 / 隐藏 Dock");
            toggle.Click += (_, _) => viewModel.IsDockVisible = !viewModel.IsDockVisible;
            menu.Items.Add(toggle);

            var pin = new NativeMenuItem("钉住（不自动隐藏）");
            pin.Click += (_, _) => viewModel.TogglePinCommand.Execute(null);
            menu.Items.Add(pin);

            var autoStart = new NativeMenuItem("开机启动");
            autoStart.Click += (_, _) => viewModel.ToggleAutoStartCommand.Execute(null);
            menu.Items.Add(autoStart);

            menu.Items.Add(new NativeMenuItemSeparator());

            var check = new NativeMenuItem("检查更新");
            check.Click += (_, _) => _ = viewModel.CheckUpdateAsync();
            menu.Items.Add(check);

            var settings = new NativeMenuItem("设置…");
            settings.Click += (_, _) => ShowSettings();
            menu.Items.Add(settings);

            menu.Items.Add(new NativeMenuItemSeparator());

            var exit = new NativeMenuItem("退出");
            exit.Click += (_, _) => RequestShutdown();
            menu.Items.Add(exit);

            _tray = new TrayIcon
            {
                Icon = CreateAppIcon(),
                ToolTipText = "QuickApp 快捷应用",
                Menu = menu,
                IsVisible = true
            };

            _tray.Clicked += (_, _) => viewModel.IsDockVisible = !viewModel.IsDockVisible;

            var trayIcons = new TrayIcons { _tray };
            TrayIcon.SetIcons(this, trayIcons);
        }
        catch (Exception ex)
        {
            AppLog.Error("创建托盘图标失败", ex);
        }
    }

    /// <summary>
    /// 运行时画应用图标，省得带二进制资源（也避免 AOT 资源加载的坑）。
    /// 对应原型 .tray-icon：145° 蓝紫渐变圆角方块 + 白色「应用」描边图形。
    /// </summary>
    private static WindowIcon CreateAppIcon()
    {
        const int size = 32;
        var bitmap = new RenderTargetBitmap(new PixelSize(size, size), new Vector(96, 96));

        using (DrawingContext context = bitmap.CreateDrawingContext())
        {
            var background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.Parse("#4A6CF7"), 0),
                    new GradientStop(Color.Parse("#2F49C9"), 1)
                }
            };
            context.DrawRectangle(background, null, new RoundedRect(new Rect(0, 0, size, size), 7));

            // 原型 GLYPHS.app：24 网格的圆角矩形 + 顶部横线，等比放到 32px
            Geometry glyph = Geometry.Parse(
                "M10,4.67 L22,4.67 C24.95,4.67 27.33,7.05 27.33,10 L27.33,22 " +
                "C27.33,24.95 24.95,27.33 22,27.33 L10,27.33 C7.05,27.33 4.67,24.95 4.67,22 " +
                "L4.67,10 C4.67,7.05 7.05,4.67 10,4.67 Z M4.67,11.33 L27.33,11.33");
            var stroke = new Pen(new SolidColorBrush(Colors.White), 2.3)
            {
                LineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round
            };
            context.DrawGeometry(null, stroke, glyph);
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
