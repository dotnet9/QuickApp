using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using QuickApp.ViewModels;

namespace QuickApp.Views;

public partial class SettingsWindow : Window
{
    private SettingsViewModel? _vm;

    public SettingsWindow()
    {
        InitializeComponent();
        ExportButton.Click += OnExportConfig;
        ImportButton.Click += OnImportConfig;
        CloseButton.Click += (_, _) => Close();
        HeaderBar.PointerPressed += OnHeaderPressed;
        WebsiteButton.Click += (_, _) => OpenUrl("https://codewf.com");
        RepoButton.Click += (_, _) => OpenUrl("https://github.com/dotnet9/QuickApp");
        LicenseButton.Click += (_, _) => ShowLicense();
        StorageModeButton.Click += OnToggleStorageMode;
        OpenConfigFolderButton.Click += OnOpenConfigFolder;
        Opened += (_, _) => RefreshMonitorOptions();
        // 打开推荐页签时轻量刷新（服务层 TTL 缓存内不出网）
        SettingsTabs.SelectionChanged += (_, _) =>
        {
            if (SettingsTabs.SelectedIndex == 3)
            {
                _vm?.RefreshRecommendedApps();
            }
        };
    }

    /// <summary>切换设置分类；由 Dock 的“关于”入口直接打开对应 Tab。</summary>
    public void ShowTab(string? tab)
    {
        SettingsTabs.SelectedIndex = tab?.ToLowerInvariant() switch
        {
            "appearance" => 1,
            "data" => 2,
            "recommend" => 3,
            "about" => 4,
            _ => 0
        };
    }

    /// <summary>自绘标题栏拖动整窗（无系统装饰时用它替代标题栏）。</summary>
    private void OnHeaderPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    public void Attach(DockViewModel dock)
    {
        _vm = new SettingsViewModel(dock);
        DataContext = _vm;
    }

    private async void OnExportConfig(object? sender, RoutedEventArgs e)
    {
        if (_vm is null)
        {
            return;
        }

        try
        {
            IStorageFile? file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "导出 QuickApp 配置",
                SuggestedFileName = "quickapp-config",
                DefaultExtension = "qa",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("QuickApp 配置") { Patterns = new[] { "*.qa" } }
                }
            });

            if (file is null)
            {
                return;
            }

            string? path = file.Path?.LocalPath;
            if (!string.IsNullOrWhiteSpace(path) && _vm.ExportConfigTo(path))
            {
                AppLog.Info("配置已导出：" + path);
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("导出配置失败", ex);
        }
    }

    private async void OnImportConfig(object? sender, RoutedEventArgs e)
    {
        if (_vm is null)
        {
            return;
        }

        try
        {
            IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "导入 QuickApp 配置",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("QuickApp 配置") { Patterns = new[] { "*.qa" } },
                    FilePickerFileTypes.All
                }
            });

            if (files.Count > 0 && !string.IsNullOrWhiteSpace(files[0].Path?.LocalPath))
            {
                if (await ConfirmImportAsync()
                    && _vm.ImportConfigFrom(files[0].Path!.LocalPath))
                {
                    _vm.RefreshFromDock();
                    RefreshMonitorOptions();
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("导入配置失败", ex);
        }
    }

    private void RefreshMonitorOptions()
    {
        if (_vm is null || Screens is not { } screens || screens.All.Count == 0)
        {
            return;
        }

        int primaryIndex = 0;
        if (screens.Primary is { } primary)
        {
            for (int i = 0; i < screens.All.Count; i++)
            {
                if (screens.All[i].Bounds.Equals(primary.Bounds))
                {
                    primaryIndex = i;
                    break;
                }
            }
        }

        var primaryBounds = screens.All[primaryIndex].Bounds;
        var options = new List<MonitorOption>(screens.All.Count);
        for (int i = 0; i < screens.All.Count; i++)
        {
            var bounds = screens.All[i].Bounds;
            string position = i == primaryIndex ? "主显示器" : RelativeMonitorPosition(bounds, primaryBounds);
            options.Add(new MonitorOption(i, "显示器 " + (i + 1) + " · " + position));
        }

        _vm.SetMonitorOptions(options, primaryIndex);
    }

    private static string RelativeMonitorPosition(Avalonia.PixelRect bounds, Avalonia.PixelRect primary)
    {
        if (bounds.Right <= primary.X) return "左侧";
        if (bounds.X >= primary.Right) return "右侧";
        if (bounds.Bottom <= primary.Y) return "上方";
        if (bounds.Y >= primary.Bottom) return "下方";

        int offsetX = (bounds.X + bounds.Width / 2) - (primary.X + primary.Width / 2);
        int offsetY = (bounds.Y + bounds.Height / 2) - (primary.Y + primary.Height / 2);
        return Math.Abs(offsetX) >= Math.Abs(offsetY)
            ? (offsetX < 0 ? "左侧" : "右侧")
            : (offsetY < 0 ? "上方" : "下方");
    }

    private async Task<bool> ConfirmImportAsync()
    {
        if (_vm is null)
        {
            return false;
        }

        var completion = new TaskCompletionSource<bool>();
        var dialog = new Window
        {
            Title = "替换快捷配置？",
            Width = 390,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            ShowInTaskbar = false,
            WindowDecorations = WindowDecorations.None,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brushes.Transparent,
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent },
            TransparencyBackgroundFallback = Brushes.Transparent
        };

        var cancel = new Button { Content = "取消", MinWidth = 76, Padding = new Avalonia.Thickness(12, 6) };
        var confirm = new Button { Content = "替换当前配置", MinWidth = 110, Padding = new Avalonia.Thickness(12, 6) };
        cancel.Click += (_, _) =>
        {
            completion.TrySetResult(false);
            dialog.Close();
        };
        confirm.Click += (_, _) =>
        {
            completion.TrySetResult(true);
            dialog.Close();
        };
        dialog.Closed += (_, _) => completion.TrySetResult(false);

        var actions = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            Children = { cancel, confirm }
        };
        var content = new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = "导入将替换当前 " + _vm.ItemCount + " 个快捷项及设置。", FontSize = 14, FontWeight = FontWeight.SemiBold, Foreground = _vm.TextBrush },

                actions
            }
        };
        dialog.Content = new Grid
        {
            Margin = new Avalonia.Thickness(14),
            Children =
            {
                new Border
                {
                    Background = _vm.BackgroundBrush,
                    BorderBrush = _vm.PanelBorderBrush,
                    BorderThickness = new Avalonia.Thickness(1),
                    CornerRadius = new Avalonia.CornerRadius(_vm.PanelRadius),
                    Padding = new Avalonia.Thickness(18),
                    Child = content
                }
            }
        };

        await dialog.ShowDialog(this);
        return await completion.Task;
    }

    /// <summary>安装版 ↔ 便携版一键切换（配置与滚动备份一并迁移）。</summary>
    private void OnToggleStorageMode(object? sender, RoutedEventArgs e)
    {
        if (_vm is null)
        {
            return;
        }

        _vm.ToggleStorageMode();
        StorageModeResultText.IsVisible = true;
        StorageModeResultText.Text = _vm.StorageModeResult;
    }

    /// <summary>在资源管理器中打开并选中配置文件。</summary>
    private void OnOpenConfigFolder(object? sender, RoutedEventArgs e)
    {
        string configFile = _vm?.ConfigFilePath ?? string.Empty;
        if (string.IsNullOrEmpty(configFile))
        {
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{configFile}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            AppLog.Error("打开配置目录失败", ex);
        }
    }

    /// <summary>用系统默认浏览器打开项目链接。</summary>
    private void OpenUrl(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            AppLog.Error("打开链接失败：" + url, ex);
        }
    }

    /// <summary>许可证小卡片：对应原型 .modal-card.small（原型为 toast，这里用同级卡片承载完整文本）。</summary>
    private void ShowLicense()
    {
        var dialog = new Window
        {
            Title = "许可证",
            Width = 390,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            ShowInTaskbar = false,
            WindowDecorations = WindowDecorations.None,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Brushes.Transparent,
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent },
            TransparencyBackgroundFallback = Brushes.Transparent
        };

        var close = new Button { Content = "知道了", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };
        if (this.TryFindResource("LinkButton", Avalonia.Styling.ThemeVariant.Default, out var linkTheme)
            && linkTheme is Avalonia.Styling.ControlTheme theme)
        {
            close.Theme = theme;
        }
        close.Click += (_, _) => dialog.Close();

        var content = new StackPanel { Spacing = 10 };
        content.Children.Add(new TextBlock
        {
            Text = "许可证",
            FontSize = 14,
            FontWeight = FontWeight.SemiBold,
            Foreground = _vm?.TextBrush ?? Brushes.White
        });
        content.Children.Add(new TextBlock
        {
            Text = "QuickApp 以 MIT 许可证发布。你可以自由使用、修改和分发本软件，但请保留原始版权声明。",
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Foreground = _vm?.TextDimBrush ?? Brushes.Gray
        });
        content.Children.Add(close);

        dialog.Content = new Grid
        {
            Margin = new Avalonia.Thickness(14),
            Children =
            {
                new Border
                {
                    Background = _vm?.BackgroundBrush ?? Brushes.White,
                    BorderBrush = _vm?.PanelBorderBrush ?? Brushes.Gray,
                    BorderThickness = new Avalonia.Thickness(1),
                    CornerRadius = new Avalonia.CornerRadius(_vm?.PanelRadius ?? 14),
                    Padding = new Avalonia.Thickness(18),
                    Child = content
                }
            }
        };

        dialog.Show(this);
    }
}
