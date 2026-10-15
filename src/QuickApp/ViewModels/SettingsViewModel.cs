using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Windows.Input;
using Avalonia.Media;
using QuickApp.Core.Models;
using ReactiveUI;

namespace QuickApp.ViewModels;

public sealed record MonitorOption(int Index, string Label);
public sealed record AppearanceOption(string Key, string Text, IBrush Swatch)
{
    public ICommand? SelectCommand { get; set; }
}

/// <summary>
/// 设置窗口的视图模型：每个 setter 直接改 Dock 的设置并即时生效，关闭窗口时统一存盘。
/// </summary>
public sealed class SettingsViewModel : ViewModelBase
{
    private readonly DockViewModel _dock;
    private int _primaryMonitorIndex;
    private bool _rebuildingPaletteOptions;

    public SettingsViewModel(DockViewModel dock)
    {
        _dock = dock;
        RebuildPaletteOptions();
        foreach (var language in LanguageOptions) language.SelectCommand = ReactiveCommand.Create(() => SelectedLanguage = language);
        CheckUpdateCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            await _dock.CheckUpdateAsync();

            // 有新版本就直接弹出版本说明卡片（已安装版本 / 最新版本 / 更新说明 + 安装、取消），
            // 不再只靠设置页那一行文字提示
            if (_dock.UpdateAvailable)
            {
                _dock.RequestUpdateNotes();
            }

            this.RaisePropertyChanged(nameof(CheckResult));
            this.RaisePropertyChanged(nameof(HasUpdate));
        });
        OpenConfigFolderCommand = ReactiveCommand.Create<string>(OpenConfigFolder);
        ShowUpdateNotesCommand = ReactiveCommand.Create(_dock.RequestUpdateNotes);

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

        SetLabelsCommand = ReactiveCommand.Create<string>(value =>
        {
            Settings.ShowLabels = value == "iconText";
            ApplyChange(palette: false, size: true);
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

    public ObservableCollection<AppearanceOption> PaletteOptions { get; } = new();
    public ObservableCollection<AppearanceOption> LanguageOptions { get; } = new()
    {
        new("zh-CN", AppText.Get(Language.ChineseLanguage), Brushes.Transparent), new("en-US", "English", Brushes.Transparent)
    };
    public AppearanceOption? SelectedPalette
    {
        get => PaletteOptions.FirstOrDefault(option => option.Key == Settings.Palette);
        set
        {
            if (_rebuildingPaletteOptions || value is null || value.Key == Settings.Palette) return;
            Settings.Palette = value.Key;
            ApplyChange(palette: true, size: false);
            _dock.Save();
            this.RaisePropertyChanged();
        }
    }
    public CodeWF.Avalonia.Controls.Themes.StudioThemeMode ThemeMode
    {
        get => Settings.Theme switch
        {
            "dark" => CodeWF.Avalonia.Controls.Themes.StudioThemeMode.Dark,
            "light" => CodeWF.Avalonia.Controls.Themes.StudioThemeMode.Light,
            _ => CodeWF.Avalonia.Controls.Themes.StudioThemeMode.System
        };
        set
        {
            var key = value.ToString().ToLowerInvariant();
            if (Settings.Theme == key) return;
            Settings.Theme = key;
            ApplyChange(palette: true, size: false);
            _dock.Save();
        }
    }
    public AppearanceOption? SelectedLanguage
    {
        get => LanguageOptions.FirstOrDefault(option => option.Key == Settings.Language);
        set
        {
            if (value is null || value.Key == Settings.Language) return;
            Settings.Language = value.Key;
            CodeWF.Toolkit.Lang.I18nManager.Instance.Culture = System.Globalization.CultureInfo.GetCultureInfo(value.Key);
            RebuildPaletteOptions();
            _dock.Save();
            this.RaisePropertyChanged();
        }
    }
    /// <summary>已打开的设置页随全局语言刷新配色名称和语言选择。</summary>
    protected override void OnCultureChanged()
    {
        if (_dock is not null) RebuildPaletteOptions();
        base.OnCultureChanged();
    }

    private void RebuildPaletteOptions()
    {
        _rebuildingPaletteOptions = true;
        PaletteOptions.Clear();
        foreach (var palette in Enum.GetValues<CodeWF.Avalonia.Controls.Themes.StudioPalette>())
        {
            Avalonia.Application.Current!.TryGetResource("StudioSwatch" + palette, Avalonia.Application.Current.ActualThemeVariant, out var swatch);
            var key = palette switch
            {
                CodeWF.Avalonia.Controls.Themes.StudioPalette.Default => Language.DefaultPalette,
                CodeWF.Avalonia.Controls.Themes.StudioPalette.Desert => Language.DesertPalette,
                CodeWF.Avalonia.Controls.Themes.StudioPalette.Aquatic => Language.AquaticPalette,
                CodeWF.Avalonia.Controls.Themes.StudioPalette.Dusk => Language.DuskPalette,
                CodeWF.Avalonia.Controls.Themes.StudioPalette.NightSky => Language.NightSkyPalette,
                CodeWF.Avalonia.Controls.Themes.StudioPalette.DotNet => Language.DotNetPalette,
                CodeWF.Avalonia.Controls.Themes.StudioPalette.Aurora => Language.AuroraPalette,
                _ => Language.EmberPalette
            };
            PaletteOptions.Add(new(palette.ToString(), AppText.Get(key), swatch as IBrush ?? Brushes.Transparent));
        }
        _rebuildingPaletteOptions = false;
        this.RaisePropertyChanged(nameof(SelectedPalette));
    }
    public string PlatformText => $".NET {Environment.Version.Major} · Avalonia {typeof(Avalonia.Application).Assembly.GetName().Version?.ToString(3)} · {System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier}";
    public string BuildTimeText => CodeWF.Toolkit.Core.Extensions.AssemblyExtensions.CompileTime(typeof(DockViewModel).Assembly)?.ToString("yyyy-MM-dd HH:mm:ss") ?? "—";

    public DockViewModel Dock => _dock;

    public ICommand CheckUpdateCommand { get; }

    public ICommand OpenConfigFolderCommand { get; }

    public ICommand SetThemeCommand { get; }

    public ICommand SetStyleCommand { get; }

    public ICommand SetLabelsCommand { get; }

    public ICommand SetEdgeCommand { get; }

    public ObservableCollection<MonitorOption> MonitorOptions { get; } = new();

    public MonitorOption? SelectedMonitor
    {
        get
        {
            int selectedIndex = Settings.MonitorIndex >= 0 ? Settings.MonitorIndex : _primaryMonitorIndex;
            return MonitorOptions.FirstOrDefault(option => option.Index == selectedIndex)
                ?? MonitorOptions.FirstOrDefault();
        }
        set
        {
            if (value is null || SelectedMonitor?.Index == value.Index)
            {
                return;
            }

            _dock.SelectMonitorIndex(value.Index);
            this.RaisePropertyChanged();
        }
    }

    public void SetMonitorOptions(IEnumerable<MonitorOption> options, int primaryMonitorIndex)
    {
        _primaryMonitorIndex = primaryMonitorIndex;
        MonitorOptions.Clear();
        foreach (MonitorOption option in options)
        {
            MonitorOptions.Add(option);
        }

        this.RaisePropertyChanged(nameof(SelectedMonitor));
    }

    /// <summary>卡片外观复用 Dock 的调色板，保证两个窗口同一套配色。</summary>
    public IBrush PanelBorderBrush => _dock.PanelBorderBrush;

    public double PanelRadius => _dock.PanelRadius;

    // ---------------- 分段控件选中态 ----------------

    public bool IsThemeSystem => Settings.Theme is not ("dark" or "light");

    public bool IsThemeDark => Settings.Theme == "dark";

    public bool IsThemeLight => Settings.Theme == "light";

    public bool IsStyleGlass => Settings.Style != "flat";

    public bool IsStyleFlat => Settings.Style == "flat";

    public bool IsLabelsOnly => !Settings.ShowLabels;

    public bool IsLabelsWithText => Settings.ShowLabels;

    public bool IsEdgeTop => Settings.Edge == DockEdge.Top;

    public bool IsEdgeBottom => Settings.Edge == DockEdge.Bottom;

    public bool IsEdgeLeft => Settings.Edge == DockEdge.Left;

    public bool IsEdgeRight => Settings.Edge == DockEdge.Right;

    private AppSettings Settings => _dock.Settings;

    // ---------------- 推荐应用 ----------------

    /// <summary>推荐页签的卡片集合（控制器随 Dock 依赖注入创建）。</summary>
    public RecommendedAppsController? RecommendedApps => _dock.RecommendedApps;

    /// <summary>推荐页签可见时刷新卡片（TTL 内走缓存，过期才出网）。</summary>
    public void RefreshRecommendedApps() => _ = _dock.RefreshRecommendedAppsAsync();

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
            _dock.AutoHideDelayChanged();
            this.RaisePropertyChanged();
            this.RaisePropertyChanged(nameof(AutoHideDelayText));
        }
    }

    public string AutoHideDelayText => Settings.AutoHideDelayMs <= 0
        ? AppText.Get(Language.NoAutoHide)
        : $"{Settings.AutoHideDelayMs} ms";

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

    public string HotkeyText => Settings.Hotkey.Replace("+", " + ");

    public string CheckResult => _dock.UpdateResultText;

    /// <summary>已发现新版本：关于页显示「查看新版本」入口，随时能再打开更新说明卡片。</summary>
    public bool HasUpdate => _dock.UpdateAvailable;

    /// <summary>打开更新说明卡片（Dock 更新条与本次检查弹出的都是同一张）。</summary>
    public ICommand ShowUpdateNotesCommand { get; }

    // ---------------- 数据 ----------------

    public string ConfigFilePath => _dock.ConfigFilePath;
    public bool HasConfigLoadError => _dock.HasConfigLoadError;
    public string ConfigLoadStatus => AppText.Get(Language.ConfigPreservedPrefix) + _dock.ConfigLoadError;
    public bool ReloadConfig() => _dock.ReloadConfig();

    /// <summary>当前快捷项数量（导入确认摘要用）。</summary>
    public int ItemCount => _dock.Items.Count;

    public string StorageModeText => _dock.StorageModeText;

    public string StorageModeSwitchText => _dock.StorageModeSwitchText;

    public string StorageModeResult => _dock.StorageModeResult ?? string.Empty;

    public void ToggleStorageMode()
    {
        _dock.ToggleStorageMode();
        this.RaisePropertyChanged(nameof(StorageModeText));
        this.RaisePropertyChanged(nameof(StorageModeSwitchText));
        this.RaisePropertyChanged(nameof(StorageModeResult));
        this.RaisePropertyChanged(nameof(ConfigFilePath));
    }

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

    public bool ExportConfigTo(string filePath) => _dock.ExportConfigTo(filePath);

    public bool ImportConfigFrom(string filePath) => _dock.ImportConfigFrom(filePath);

    /// <summary>导入完整配置后刷新当前设置窗口的所有绑定值。</summary>
    public void RefreshFromDock()
    {
        this.RaisePropertyChanged(nameof(ThemeMode));
        this.RaisePropertyChanged(nameof(HasConfigLoadError));
        this.RaisePropertyChanged(nameof(ConfigLoadStatus));
        this.RaisePropertyChanged(nameof(HasUpdate));
        this.RaisePropertyChanged(nameof(BackgroundBrush));
        this.RaisePropertyChanged(nameof(TextBrush));
        this.RaisePropertyChanged(nameof(TextDimBrush));
        this.RaisePropertyChanged(nameof(PanelBorderBrush));
        this.RaisePropertyChanged(nameof(AccentBrush));
        this.RaisePropertyChanged(nameof(DangerBrush));
        this.RaisePropertyChanged(nameof(IsThemeSystem));
        this.RaisePropertyChanged(nameof(IsThemeDark));
        this.RaisePropertyChanged(nameof(IsThemeLight));
        this.RaisePropertyChanged(nameof(IsStyleGlass));
        this.RaisePropertyChanged(nameof(IsStyleFlat));
        this.RaisePropertyChanged(nameof(IsLabelsOnly));
        this.RaisePropertyChanged(nameof(IsLabelsWithText));
        this.RaisePropertyChanged(nameof(IsEdgeTop));
        this.RaisePropertyChanged(nameof(IsEdgeBottom));
        this.RaisePropertyChanged(nameof(IsEdgeLeft));
        this.RaisePropertyChanged(nameof(IsEdgeRight));
        this.RaisePropertyChanged(nameof(ShowLabels));
        this.RaisePropertyChanged(nameof(TileSize));
        this.RaisePropertyChanged(nameof(CornerRadius));
        this.RaisePropertyChanged(nameof(RevealOnEdgeTouch));
        this.RaisePropertyChanged(nameof(Pinned));
        this.RaisePropertyChanged(nameof(AutoHideDelayMs));
        this.RaisePropertyChanged(nameof(AutoHideDelayText));
        this.RaisePropertyChanged(nameof(CollapseAfterLaunch));
        this.RaisePropertyChanged(nameof(AutoStartEnabled));
        this.RaisePropertyChanged(nameof(CheckUpdates));
        this.RaisePropertyChanged(nameof(HotkeyText));
        this.RaisePropertyChanged(nameof(ConfigFilePath));
        this.RaisePropertyChanged(nameof(CheckResult));
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
    public IBrush DangerBrush => _dock.DangerBrush;

    private void ApplyChange(bool palette, bool size)
    {
        _dock.ApplySettings(paletteChanged: palette, sizeChanged: size, save: false);
        this.RaisePropertyChanged(nameof(BackgroundBrush));
        this.RaisePropertyChanged(nameof(TextBrush));
        this.RaisePropertyChanged(nameof(TextDimBrush));
        this.RaisePropertyChanged(nameof(AccentBrush));
        this.RaisePropertyChanged(nameof(DangerBrush));

        // 分段按钮的选中态（Classes.active 绑定）依赖这些属性通知，漏发就不变色
        this.RaisePropertyChanged(nameof(IsThemeSystem));
        this.RaisePropertyChanged(nameof(IsThemeDark));
        this.RaisePropertyChanged(nameof(IsThemeLight));
        this.RaisePropertyChanged(nameof(ThemeMode));
        this.RaisePropertyChanged(nameof(IsStyleGlass));
        this.RaisePropertyChanged(nameof(IsStyleFlat));
        this.RaisePropertyChanged(nameof(IsLabelsOnly));
        this.RaisePropertyChanged(nameof(IsLabelsWithText));
        this.RaisePropertyChanged(nameof(IsEdgeTop));
        this.RaisePropertyChanged(nameof(IsEdgeBottom));
        this.RaisePropertyChanged(nameof(IsEdgeLeft));
        this.RaisePropertyChanged(nameof(IsEdgeRight));
    }
}
