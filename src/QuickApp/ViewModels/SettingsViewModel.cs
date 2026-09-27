using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows.Input;
using Avalonia.Media;
using QuickApp.Core.Models;
using ReactiveUI;

namespace QuickApp.ViewModels;

/// <summary>
/// 设置窗口的视图模型：每个 setter 直接改 Dock 的设置并即时生效，关闭窗口时统一存盘。
/// </summary>
public sealed class SettingsViewModel : ViewModelBase
{
    private readonly DockViewModel _dock;

    public SettingsViewModel(DockViewModel dock)
    {
        _dock = dock;
        CheckUpdateCommand = ReactiveCommand.CreateFromTask(() => _dock.CheckUpdateAsync());
        OpenConfigFolderCommand = ReactiveCommand.Create<string>(OpenConfigFolder);

        SetThemeCommand = ReactiveCommand.Create<string>(value =>
        {
            Settings.Theme = value;
            ApplyChange(palette: true, size: false);
        });

        SetStyleCommand = ReactiveCommand.Create<string>(value =>
        {
            Settings.Style = value;
            ApplyChange(palette: true, size: false);
        });

        SetEdgeCommand = ReactiveCommand.Create<string>(value =>
        {
            DockEdge edge = value switch
            {
                "bottom" => DockEdge.Bottom,
                "left" => DockEdge.Left,
                "right" => DockEdge.Right,
                _ => DockEdge.Top
            };

            if (Settings.Edge != edge)
            {
                _dock.SetEdgeCommand.Execute(edge);
            }

            ApplyChange(palette: false, size: true);
        });
    }

    public ICommand CheckUpdateCommand { get; }

    public ICommand OpenConfigFolderCommand { get; }

    public ICommand SetThemeCommand { get; }

    public ICommand SetStyleCommand { get; }

    public ICommand SetEdgeCommand { get; }

    /// <summary>卡片外观复用 Dock 的调色板，保证两个窗口同一套配色。</summary>
    public IBrush PanelBorderBrush => _dock.PanelBorderBrush;

    public double PanelRadius => _dock.PanelRadius;

    // ---------------- 分段控件选中态 ----------------

    public bool IsThemeSystem => Settings.Theme is not ("dark" or "light");

    public bool IsThemeDark => Settings.Theme == "dark";

    public bool IsThemeLight => Settings.Theme == "light";

    public bool IsStyleGlass => Settings.Style != "flat";

    public bool IsStyleFlat => Settings.Style == "flat";

    public bool IsEdgeTop => Settings.Edge == DockEdge.Top;

    public bool IsEdgeBottom => Settings.Edge == DockEdge.Bottom;

    public bool IsEdgeLeft => Settings.Edge == DockEdge.Left;

    public bool IsEdgeRight => Settings.Edge == DockEdge.Right;

    private AppSettings Settings => _dock.Settings;

    // ---------------- 外观 ----------------

    public int ThemeIndex
    {
        get => Settings.Theme switch { "dark" => 1, "light" => 2, _ => 0 };
        set
        {
            Settings.Theme = value switch { 1 => "dark", 2 => "light", _ => "system" };
            ApplyChange(palette: true, size: false);
        }
    }

    public int StyleIndex
    {
        get => Settings.Style == "flat" ? 1 : 0;
        set
        {
            Settings.Style = value == 1 ? "flat" : "glass";
            ApplyChange(palette: true, size: false);
        }
    }

    public bool ShowLabels
    {
        get => Settings.ShowLabels;
        set
        {
            Settings.ShowLabels = value;
            ApplyChange(palette: false, size: true);
        }
    }

    public double TileSize
    {
        get => Settings.TileSize;
        set
        {
            Settings.TileSize = Math.Clamp(Math.Round(value), 28, 72);
            ApplyChange(palette: false, size: true);
            this.RaisePropertyChanged();
        }
    }

    public double CornerRadius
    {
        get => Settings.CornerRadius;
        set
        {
            Settings.CornerRadius = Math.Clamp(Math.Round(value), 0, 32);
            ApplyChange(palette: false, size: true);
            this.RaisePropertyChanged();
        }
    }

    public double OpacityPercent
    {
        get => Math.Round(Settings.PanelOpacity * 100);
        set
        {
            Settings.PanelOpacity = Math.Clamp(value / 100, 0.2, 1);
            ApplyChange(palette: true, size: false);
            this.RaisePropertyChanged();
        }
    }

    // ---------------- 停靠 ----------------

    public int EdgeIndex
    {
        get => Settings.Edge switch
        {
            DockEdge.Bottom => 1,
            DockEdge.Left => 2,
            DockEdge.Right => 3,
            _ => 0
        };
        set
        {
            DockEdge edge = value switch
            {
                1 => DockEdge.Bottom,
                2 => DockEdge.Left,
                3 => DockEdge.Right,
                _ => DockEdge.Top
            };

            if (Settings.Edge != edge)
            {
                _dock.SetEdgeCommand.Execute(edge);
            }

            this.RaisePropertyChanged();
        }
    }

    // ---------------- 常规 ----------------

    public bool RevealOnEdgeTouch
    {
        get => Settings.RevealOnEdgeTouch;
        set
        {
            Settings.RevealOnEdgeTouch = value;
            ApplyChange(palette: false, size: false);
        }
    }

    public bool Pinned
    {
        get => Settings.Pinned;
        set
        {
            if (value != Settings.Pinned)
            {
                _dock.TogglePinCommand.Execute(null);
            }

            this.RaisePropertyChanged();
        }
    }

    public double AutoHideDelayMs
    {
        get => Settings.AutoHideDelayMs;
        set
        {
            Settings.AutoHideDelayMs = (int)Math.Clamp(Math.Round(value / 100) * 100, 0, 3000);
            ApplyChange(palette: false, size: false);
            this.RaisePropertyChanged();
        }
    }

    public bool CollapseAfterLaunch
    {
        get => Settings.CollapseAfterLaunch;
        set
        {
            Settings.CollapseAfterLaunch = value;
            ApplyChange(palette: false, size: false);
        }
    }

    public bool AutoStartEnabled
    {
        get => _dock.AutoStartEnabled;
        set
        {
            if (value != _dock.AutoStartEnabled)
            {
                _dock.ToggleAutoStartCommand.Execute(null);
            }

            this.RaisePropertyChanged();
        }
    }

    // ---------------- 更新 ----------------

    public bool CheckUpdates
    {
        get => Settings.CheckUpdates;
        set
        {
            Settings.CheckUpdates = value;
            ApplyChange(palette: false, size: false);
        }
    }

    public string VersionText => _dock.VersionText;

    public string CheckResult => _dock.UpdateText.Length > 0 ? _dock.UpdateText : "已是最新版本";

    // ---------------- 数据 ----------------

    public string ConfigFilePath => _dock.ConfigFilePath;

    public void AddCommand(string name, string command)
    {
        _dock.AddCommand(name, command);
        this.RaisePropertyChanged(nameof(ConfigFilePath));
    }

    public void AddFiles(IEnumerable<string> paths)
    {
        _dock.AddTargets(paths);
        this.RaisePropertyChanged(nameof(ConfigFilePath));
    }

    private static void OpenConfigFolder(string? path)
    {
        try
        {
            string? folder = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder))
            {
                Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("打开配置目录失败", ex);
        }
    }

    // ---------------- 画刷（委派给 Dock，保证两个窗口配色一致） ----------------

    public IBrush BackgroundBrush => _dock.SettingsBackgroundBrush;

    public IBrush TextBrush => _dock.TextBrush;

    public IBrush TextDimBrush => _dock.TextDimBrush;

    public IBrush AccentBrush => _dock.AccentBrush;

    private void ApplyChange(bool palette, bool size)
    {
        _dock.ApplySettings(paletteChanged: palette, sizeChanged: size, save: false);
        this.RaisePropertyChanged(nameof(BackgroundBrush));
        this.RaisePropertyChanged(nameof(TextBrush));
        this.RaisePropertyChanged(nameof(TextDimBrush));
        this.RaisePropertyChanged(nameof(AccentBrush));
    }
}
