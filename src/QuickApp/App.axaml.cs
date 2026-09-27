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

        _dock = new DockWindow();
        _dock.Attach(viewModel);
        _dock.SettingsRequested += (_, _) => ShowSettings();

        _singleInstance.Listen(() => _dock.ActivateFromExternal());

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
                Icon = CreateTrayBitmap(),
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

    /// <summary>托盘图标在运行时画出来，省得带一个二进制资源（也避免 AOT 资源加载的坑）。</summary>
    private static WindowIcon CreateTrayBitmap()
    {
        const int size = 32;
        var bitmap = new RenderTargetBitmap(new PixelSize(size, size), new Vector(96, 96));

        using (DrawingContext context = bitmap.CreateDrawingContext())
        {
            var background = new SolidColorBrush(Color.Parse("#3B82F6"));
            context.DrawRectangle(background, null, new RoundedRect(new Rect(0, 0, size, size), 8));

            var foreground = new SolidColorBrush(Colors.White);
            context.DrawRectangle(foreground, null, new RoundedRect(new Rect(7, 9, 18, 4), 2));
            context.DrawRectangle(foreground, null, new RoundedRect(new Rect(7, 16, 12, 4), 2));
            context.DrawRectangle(foreground, null, new RoundedRect(new Rect(7, 23, 18, 4), 2));
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream);
        stream.Position = 0;
        return new WindowIcon(stream);
    }

    private void ShowSettings()
    {
        if (_services is null)
        {
            return;
        }

        DockViewModel viewModel = _services.GetRequiredService<DockViewModel>();
        if (_settings is not null)
        {
            _settings.Activate();
            return;
        }

        _settings = new SettingsWindow();
        _settings.Attach(viewModel);
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
