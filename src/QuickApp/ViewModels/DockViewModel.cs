using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Layout;
using Avalonia.Media;
using QuickApp.Core.Models;
using QuickApp.Core.Services;
using QuickApp.Theme;
using ReactiveUI;

namespace QuickApp.ViewModels;

/// <summary>
/// 视图侧需要主窗口配合的事情：窗口定位、打开设置、退出、打开链接。
/// 视图模型不直接依赖窗口类型，便于以后拆分。
/// </summary>
public interface IDockHost
{
    /// <summary>停靠边变化后重新摆放窗口（朝向、尺寸、收起方向都会变）。</summary>
    void ApplyEdge(DockEdge edge);

    /// <summary>目标显示器变化后重新定位主 Dock 和收起把手。</summary>
    void MonitorSelectionChanged();

    /// <summary>自动隐藏设置变化后重置 Dock 隐藏计时。</summary>
    void AutoHideDelayChanged();

    void ShowSettings();

    void Exit();

    void OpenUrl(string url);
}

public sealed class DockViewModel : ViewModelBase
{
    private readonly ConfigStore _store;
    private readonly ILauncher _launcher;
    private readonly IIconProvider _icons;
    private readonly IInstalledAppProvider _installedAppProvider;
    private readonly IUpdateChecker _updates;
    private readonly IUpdateDownloader _updateDownloader;
    private readonly IAutoStartService _autoStart;
    private readonly AppConfig _config;
    private readonly List<ItemViewModel> _allItems = new();
    private readonly List<LauncherItem> _installedCatalog = new();
    private readonly Dictionary<string, ItemViewModel> _installedViewModels = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>系统应用来源已变化、等待下次打开搜索时重扫。Changed 事件在后台线程触发，故用 volatile。</summary>
    private volatile bool _installedAppsDirty;
    private List<(LauncherItem Item, int Index)>? _lastRemovedItems;

    /// <summary>更新就绪的成功色（原型 --ok），不随主题切换。</summary>
    private static readonly IBrush UpdateSuccessBrush = new SolidColorBrush(Color.FromRgb(0x3F, 0xB9, 0x6F));

    /// <summary>编辑模式移除可撤销的记忆，与推荐应用「移出 Dock」共用一套撤销栈。</summary>
    private IDockHost? _host;
    private string _searchQuery = string.Empty;
    private bool _isSearchOpen;
    private bool _isEditMode;
    private bool _isDockVisible = true;
    private bool _isPointerOver;
    private bool _glassDegraded;
    private string? _statusMessage;
    private string? _storageModeResult;
    private UpdateInfo? _pendingUpdate;
    private bool _isCheckingUpdate;

    /// <summary>运行中周期检查的间隔（小时）。常驻应用靠它发现新版本。</summary>
    private const int UpdateCheckIntervalHours = 12;
    private DateTime _lastUpdateCheckAt = DateTime.MinValue;
    private int _toastToken;
    private string _updateResultText = "尚未检查";
    private bool _isDownloadingUpdate;
    private double _downloadProgress;
    private string? _downloadedUpdatePath;
    private CancellationTokenSource? _downloadCancellation;

    public DockViewModel(
        ConfigStore store,
        ILauncher launcher,
        IIconProvider icons,
        IInstalledAppProvider installedAppProvider,
        IUpdateChecker updates,
        IUpdateDownloader updateDownloader,
        IAutoStartService autoStart,
        string appName)
    {
        _store = store;
        _launcher = launcher;
        _icons = icons;
        _installedAppProvider = installedAppProvider;
        _updates = updates;
        _updateDownloader = updateDownloader;
        _autoStart = autoStart;
        AppName = appName;
        _config = store.Load();

        RunCommand = ReactiveCommand.Create<ItemViewModel>(RunItem);
        RemoveCommand = ReactiveCommand.Create<ItemViewModel>(RemoveItem);
        AddInstalledCommand = ReactiveCommand.Create<ItemViewModel>(AddInstalledItem);
        ClearAllCommand = ReactiveCommand.Create(ClearAllItems);
        UndoRemoveCommand = ReactiveCommand.Create(UndoRemove);
        ClearSearchCommand = ReactiveCommand.Create(() => SearchQuery = string.Empty);
        ToggleSearchCommand = ReactiveCommand.Create(() => IsSearchOpen = !IsSearchOpen);
        ToggleEditCommand = ReactiveCommand.Create(() => IsEditMode = !IsEditMode);
        TogglePinCommand = ReactiveCommand.Create(() => IsPinned = !IsPinned);
        HideCommand = ReactiveCommand.Create(() => IsDockVisible = false);
        ShowCommand = ReactiveCommand.Create(() => IsDockVisible = true);
        ExitCommand = ReactiveCommand.Create(() => _host?.Exit());
        OpenSettingsCommand = ReactiveCommand.Create(() => _host?.ShowSettings());
        SetEdgeCommand = ReactiveCommand.Create<DockEdge>(SetEdge);
        ToggleAutoStartCommand = ReactiveCommand.Create(ToggleAutoStart);
        CheckUpdateCommand = ReactiveCommand.CreateFromTask(() => CheckUpdateAsync());
        OpenUpdateCommand = ReactiveCommand.Create(OpenUpdatePage);
        DownloadUpdateCommand = ReactiveCommand.CreateFromTask(DownloadUpdateAsync);
        CancelUpdateDownloadCommand = ReactiveCommand.Create(CancelUpdateDownload);
        InstallUpdateCommand = ReactiveCommand.Create(InstallDownloadedUpdate);
        DismissUpdateCommand = ReactiveCommand.Create(DismissUpdate);

        LoadItems();
        // 图标库残留的孤儿（上次会话删除/导入覆盖）此时清掉；会话内不删，保证「移除可撤销」
        _store.CleanupOrphanedIcons(_config.Items);
        LoadInstalledApps();
        _installedAppProvider.Changed += OnInstalledAppsChanged;
        RefreshPalette();
        _ = RunPeriodicUpdateCheckAsync();

        // 主题=跟随系统时，操作系统深浅切换实时生效（原型 matchMedia 监听的等价实现）。
        // RefreshPalette 会写 RequestedThemeVariant，可能在系统变体与显式变体间来回；
        // 用上次应用结果去重，避免刷新环。
        Avalonia.Application.Current!.ActualThemeVariantChanged += OnSystemThemeVariantChanged;
    }

    private string? _appliedFollowTheme;

    private void OnSystemThemeVariantChanged(object? sender, EventArgs e)
    {
        if (Settings.Theme != "system")
        {
            return;
        }

        string key = IsSystemDark() ? "dark" : "light";
        if (_appliedFollowTheme == key)
        {
            return;
        }

        _appliedFollowTheme = key;
        RefreshPalette();
    }

    public string AppName { get; }

    public string VersionText =>
        "v" + (System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.1.0");

    public ObservableCollection<ItemViewModel> Items { get; } = new();

    /// <summary>搜索时显示的系统应用候选，不会混入默认 Dock 列表。</summary>
    public ObservableCollection<ItemViewModel> InstalledItems { get; } = new();

    public AppSettings Settings => _config.Settings;

    public void AttachHost(IDockHost host) => _host = host;

    // ---------------- 面板外观 ----------------

    public IBrush PanelBrush { get; private set; } = Brushes.Transparent;

    /// <summary>设置窗口用不透底面，避免普通窗口配透明画刷出现奇怪的合成。</summary>
    public IBrush SettingsBackgroundBrush { get; private set; } = Brushes.Transparent;

    public IBrush PanelBorderBrush { get; private set; } = Brushes.Transparent;

    public IBrush TextBrush { get; private set; } = Brushes.White;

    public IBrush TextDimBrush { get; private set; } = Brushes.Gray;

    public IBrush HoverBrush { get; private set; } = Brushes.Transparent;

    public IBrush AccentBrush { get; private set; } = Brushes.DodgerBlue;

    public IBrush AccentInkBrush { get; private set; } = Brushes.White;

    public IBrush MenuBrush { get; private set; } = Brushes.Transparent;

    public IBrush MenuHoverBrush { get; private set; } = Brushes.Transparent;

    public IBrush DangerBrush { get; private set; } = Brushes.Red;
    public IBrush PillBrush { get; private set; } = Brushes.Transparent;
    public IBrush PillHoverBrush { get; private set; } = Brushes.Red;

    public double TileSize => Settings.TileSize;

    public double TileRadius => Math.Round(Settings.TileSize * 0.28, 1);

    public double PanelRadius => Settings.CornerRadius;

    /// <summary>Avalonia 的 CornerRadius 没有从 double 的隐式转换，给 XAML 提供明确类型。</summary>
    public CornerRadius PanelCornerRadius => new(Settings.CornerRadius);

    /// <summary>左/右边缘 = 竖向 Dock。</summary>
    public bool IsVertical => DockPlacement.IsVertical(Settings.Edge);

    public string ThemeName => Settings.Theme;

    public string StyleName => Settings.Style;

    public bool ShowLabels => Settings.ShowLabels;

    public string EdgeLabel => DockPlacement.Label(Settings.Edge) + "边缘";

    public bool IsPinned
    {
        get => Settings.Pinned;
        set
        {
            if (Settings.Pinned == value)
            {
                return;
            }

            Settings.Pinned = value;
            if (value)
            {
                IsDockVisible = true;
            }

            this.RaisePropertyChanged();
            Save();
            Toast(value ? "已钉住" : "已取消钉住");
        }
    }

    /// <summary>主区域朝向：上/下边缘是「图标条 + 操作区」横排，左/右边缘竖排。</summary>
    public Orientation BodyOrientation => IsVertical ? Orientation.Vertical : Orientation.Horizontal;

    /// <summary>图标面板朝向。</summary>
    public Orientation ListOrientation => IsVertical ? Orientation.Vertical : Orientation.Horizontal;

    public double ListSpacing => IsVertical ? 4 : 6;

    public double DividerWidth => IsVertical ? 28 : 1;

    public double DividerHeight => IsVertical ? 1 : Math.Max(54, Math.Round(Settings.TileSize * 1.1, 1));

    /// <summary>图标区上限：默认完整容纳 10 项和悬浮安全留白，超出后沿停靠方向滚动。</summary>
    public double ListMaxWidth => IsVertical
        ? double.PositiveInfinity
        : Math.Round(Settings.TileSize * 10 + 90);

    public double ListMaxHeight => IsVertical
        ? Math.Round(Settings.TileSize * 10 + 110)
        : double.PositiveInfinity;

    /// <summary>
    /// 空状态仍保留原型中的图标区空间，避免提示文字把分隔线和操作区挤到一边。
    /// 横向 Dock 预留约四个图标的宽度，竖向 Dock 预留一个图标行的高度。
    /// </summary>
    public double ListMinWidth => IsVertical
        ? 0
        : Math.Round(Settings.TileSize * 4 + 44);

    public double ListMinHeight => IsVertical
        ? Math.Round(Settings.TileSize + 40)
        : 0;

    /// <summary>工具区分隔线到按钮组的间距（原型 .dock-tools gap：横排 4 / 竖排 2）。</summary>
    public double ToolsSpacing => IsVertical ? 2 : 4;

    /// <summary>图标与名称的间距（原型 .dock-item gap：横排 6 / 竖排 8）。</summary>
    public double ItemLabelSpacing => IsVertical ? 8 : 6;

    /// <summary>搜索输入框最小宽度（原型 min-width：横排 190 / 竖排 170）。</summary>
    public double SearchMinWidth => IsVertical ? 170 : 190;
    /// <summary>四个常驻操作按钮始终按两列排列。</summary>
    public double ActionMaxWidth => 60;

    public double ActionMaxHeight => IsVertical ? 88 : double.PositiveInfinity;

    /// <summary>收起按钮的箭头方向随停靠边变化。</summary>
    public string CollapseGlyph => Settings.Edge switch
    {
        DockEdge.Bottom => "▼",
        DockEdge.Left => "◀",
        DockEdge.Right => "▶",
        _ => "▲"
    };

    /// <summary>图标悬停放大时朝桌面内部偏移，避免贴边放大被裁切。</summary>
    public string TileHoverTransform => Settings.Edge switch
    {
        DockEdge.Bottom => "translateY(-5px) scale(1.44)",
        DockEdge.Left => "translateX(5px) scale(1.44)",
        DockEdge.Right => "translateX(-5px) scale(1.44)",
        _ => "translateY(5px) scale(1.44)"
    };

    /// <summary>编辑模式下的恒等变换：图标不放大（原型「编辑态不悬浮放大」规范）。</summary>
    private const string ScaleIdentity = "scale(1)";

    /// <summary>按下回缩（原型 tile:active 的 scale .94），偏移方向同悬停。</summary>
    public string TilePressedTransform => Settings.Edge switch
    {
        DockEdge.Bottom => "translateY(-5px) scale(0.94)",
        DockEdge.Left => "translateX(5px) scale(0.94)",
        DockEdge.Right => "translateX(-5px) scale(0.94)",
        _ => "translateY(5px) scale(0.94)"
    };

    /// <summary>为悬浮放大和两端 peek 留出缓冲（原型定稿 18px），滚动视口只包住应用图标。</summary>
    public Thickness ListPadding => IsVertical
        ? new Thickness(18, 14, 18, 14)
        : new Thickness(14, 18, 14, 18);

    // ---------------- 更新状态 ----------------

    public bool UpdateAvailable => _pendingUpdate is not null;

    public string UpdateText => _pendingUpdate is null ? string.Empty : "发现新版本 " + _pendingUpdate.Tag;

    public string UpdateResultText => _updateResultText;

    public bool IsDownloadingUpdate => _isDownloadingUpdate;

    public bool IsUpdateReady => !string.IsNullOrWhiteSpace(_downloadedUpdatePath) &&
        File.Exists(_downloadedUpdatePath);

    public bool CanDownloadUpdate => UpdateAvailable &&
        !_isDownloadingUpdate &&
        !IsUpdateReady &&
        !string.IsNullOrWhiteSpace(_pendingUpdate?.AssetUrl) &&
        !string.IsNullOrWhiteSpace(_pendingUpdate?.ChecksumUrl);

    public bool NeedsUpdatePage => UpdateAvailable &&
        !_isDownloadingUpdate &&
        !IsUpdateReady &&
        (string.IsNullOrWhiteSpace(_pendingUpdate?.AssetUrl) ||
         string.IsNullOrWhiteSpace(_pendingUpdate?.ChecksumUrl));

    public double DownloadProgress => _downloadProgress;

    public string DownloadProgressText => _isDownloadingUpdate
        ? _downloadProgress > 0 ? $"下载中 {_downloadProgress:0}%" : "正在准备下载…"
        : IsUpdateReady ? "下载完成" : string.Empty;

    public string UpdateInstallButtonText => IsWindowsInstallerAsset ? "安装" : "打开安装包";

    private bool IsWindowsInstallerAsset =>
        OperatingSystem.IsWindows() &&
        (_pendingUpdate?.AssetName?.EndsWith("-setup.exe", StringComparison.OrdinalIgnoreCase) ?? false);

    /// <summary>更新条主文案：发现新版本 / 正在下载 · 百分比 / 下载完成（原型 S1–S3）。</summary>
    public string UpdateDisplayText
    {
        get
        {
            if (_pendingUpdate is null)
            {
                return string.Empty;
            }

            if (_isDownloadingUpdate)
            {
                return _downloadProgress > 0
                    ? $"正在下载 {_pendingUpdate.Tag} · {_downloadProgress:0}%"
                    : $"正在下载 {_pendingUpdate.Tag}";
            }

            if (IsUpdateReady)
            {
                return $"{_pendingUpdate.Tag} 下载完成";
            }

            return UpdateText;
        }
    }

    /// <summary>次要说明：安装包信息不可用时提供发布页入口，空串隐藏。</summary>
    public string UpdateNoteText => NeedsUpdatePage ? "暂无法获取当前系统的安装包" : string.Empty;

    /// <summary>版本号（更新卡片右上角徽标）。</summary>
    public string UpdateTag => _pendingUpdate?.Tag ?? string.Empty;

    /// <summary>更新条左侧图形：就绪转成功对勾，下载中为下箭头，其余为发布箭头。</summary>
    public Geometry UpdateIconGlyph => IsUpdateReady ? Icons.Check : _isDownloadingUpdate ? Icons.Download : Icons.Upload;

    /// <summary>更新条图标颜色：就绪转成功色，其余用强调色。</summary>
    public IBrush UpdateIconBrush => IsUpdateReady ? UpdateSuccessBrush : AccentBrush;

    /// <summary>横排（上/下边缘）：更新条是面板内的一行（原型 .update-bar）。</summary>
    public bool ShowUpdateBar => UpdateAvailable && !IsVertical;

    /// <summary>竖排（左/右边缘）：Dock 上只出现紧凑「新版本」胶囊，点击弹出卡片（原型 .update-pill）。</summary>
    public bool ShowUpdatePill => UpdateAvailable && IsVertical;

    // ---------------- 面板状态 ----------------

    /// <summary>Dock 是否显示（收起时为 false）。</summary>
    public bool IsDockVisible
    {
        get => _isDockVisible;
        set
        {
            if (Set(ref _isDockVisible, value))
            {
                DockVisibilityChanged?.Invoke(this, value);
            }
        }
    }

    /// <summary>鼠标是否停在 Dock 上（由窗口喂进来）。</summary>
    public bool IsPointerOver
    {
        get => _isPointerOver;
        set
        {
            if (Set(ref _isPointerOver, value))
            {
                PointerOverChanged?.Invoke(this, value);
            }
        }
    }

    public event EventHandler<bool>? DockVisibilityChanged;

    public event EventHandler<bool>? PointerOverChanged;

    /// <summary>设置窗口需要它来同步开机启动开关的真实状态。</summary>
    public bool AutoStartEnabled => _autoStart.IsEnabled(AppName);

    public string ConfigFilePath => _store.ConfigFile;

    /// <summary>存储模式说明（安装版 = %LOCALAPPDATA%，便携版 = 程序目录）。</summary>
    public string StorageModeText => AppPaths.IsPortable(AppContext.BaseDirectory)
        ? "便携版 · 配置随程序目录"
        : "安装版 · 配置在 %LOCALAPPDATA%\\QuickApp";

    /// <summary>切换到另一模式的按钮文案。</summary>
    public string StorageModeSwitchText => AppPaths.IsPortable(AppContext.BaseDirectory) ? "切换为安装版" : "切换为便携版";

    /// <summary>切换存储模式的结果说明（设置页展示）。</summary>
    public string? StorageModeResult
    {
        get => _storageModeResult;
        private set => Set(ref _storageModeResult, value);
    }

    public void ToggleStorageMode()
    {
        string? error = _store.SwitchStorageMode(!AppPaths.IsPortable(AppContext.BaseDirectory), AppContext.BaseDirectory, _config);
        StorageModeResult = error is null ? "已切换并迁移配置，立即生效" : "切换失败：" + error;
        Toast(StorageModeResult);
        this.RaisePropertyChanged(nameof(StorageModeText));
        this.RaisePropertyChanged(nameof(StorageModeSwitchText));
        this.RaisePropertyChanged(nameof(ConfigFilePath));
    }

    /// <summary>Toast 下载进度（0–100），null 表示当前 Toast 无进度条（原型 .toast-downloading）。</summary>
    public double? ToastProgress
    {
        get => _toastProgress;
        private set => Set(ref _toastProgress, value);
    }

    private double? _toastProgress;

    public string? StatusMessage
    {
        get => _statusMessage;
        private set => Set(ref _statusMessage, value);
    }

    /// <summary>Toast 上的动作按钮（如移除后的「撤销」），null 表示无动作。</summary>
    public string? ToastActionLabel
    {
        get => _toastActionLabel;
        private set => Set(ref _toastActionLabel, value);
    }

    public ICommand? ToastActionCommand
    {
        get => _toastActionCommand;
        private set => Set(ref _toastActionCommand, value);
    }

    private string? _toastActionLabel;
    private ICommand? _toastActionCommand;

    /// <summary>底部居中的悬浮 Toast（原型 .toast），duration 后自动消失。</summary>
    public void Toast(string message, int durationMs = 2200)
    {
        ShowToast(message, null, null, durationMs);
    }

    public void Toast(string message, string? actionLabel, ICommand? actionCommand, int durationMs = 5000)
        => ShowToast(message, actionLabel, actionCommand, durationMs);

    public void Toast(string message, string? actionLabel, ICommand? actionCommand, int durationMs, double? progress)
        => ShowToast(message, actionLabel, actionCommand, durationMs, progress);

    private void ShowToast(string message, string? actionLabel, ICommand? actionCommand, int durationMs, double? progress = null)
    {
        ToastProgress = progress;
        StatusMessage = message;
        ToastActionLabel = actionLabel;
        ToastActionCommand = actionCommand;
        int token = ++_toastToken;
        _ = ClearToastAsync(token, durationMs);
    }

    private async Task ClearToastAsync(int token, int durationMs)
    {
        await Task.Delay(durationMs).ConfigureAwait(true);
        if (token == _toastToken)
        {
            StatusMessage = null;
            ToastActionLabel = null;
            ToastActionCommand = null;
        }
    }

    public bool IsCheckingUpdate
    {
        get => _isCheckingUpdate;
        private set => Set(ref _isCheckingUpdate, value);
    }

    // ---------------- 搜索 ----------------

    public bool IsSearchOpen
    {
        get => _isSearchOpen;
        set
        {
            if (!Set(ref _isSearchOpen, value))
            {
                return;
            }

            this.RaisePropertyChanged(nameof(IsPanelExpanded));

            if (value)
            {
                // 新装应用多半发生在面板关闭期间，打开时先补扫再出结果
                RefreshInstalledCatalogIfDirty();
                IsDockVisible = true;
            }

            RefreshFilter();
        }
    }

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (Set(ref _searchQuery, value))
            {
                RefreshFilter();
            }
        }
    }

    public bool IsSearchGrouped => IsSearchOpen && SearchQuery.Trim().Length > 0;

    public string ConfiguredGroupTitle => "已配置";

    public int ConfiguredGroupCount => Items.Count;

    public bool HasConfiguredResults => IsSearchOpen && Items.Count > 0;

    public string InstalledGroupTitle => "系统应用";

    public int InstalledGroupCount => InstalledItems.Count;

    public bool HasInstalledResults => IsSearchGrouped && InstalledItems.Count > 0;

    public string CountText => IsSearchGrouped
        ? (Items.Count + InstalledItems.Count) + " 个结果"
        : Items.Count + " / " + _allItems.Count;

    public bool IsSearchEmpty => Items.Count == 0 && InstalledItems.Count == 0 && IsSearchGrouped;

    public string EmptyStateText => IsSearchGrouped
        ? "没有匹配「" + SearchQuery.Trim() + "」的项目"
        : IsEditMode ? "右键空白处添加应用" : "空空如也";

    /// <summary>Dock 一个项都没有（与「搜索无结果」区分开，给出添加引导）。</summary>
    public bool IsDockEmpty => Items.Count == 0 && !IsSearchGrouped;

    // ---------------- 编辑模式 ----------------

    public bool IsEditMode
    {
        get => _isEditMode;
        set
        {
            if (!Set(ref _isEditMode, value))
            {
                return;
            }

            this.RaisePropertyChanged(nameof(IsPanelExpanded));

            if (value)
            {
                IsDockVisible = true;
            }

            this.RaisePropertyChanged(nameof(EmptyStateText));

            // ShowRemove 跟随编辑模式，重新推给每个图标
            ApplyItemLayout();
        }
    }

    /// <summary>搜索或编辑时才显示完整背景面板，普通状态让应用图标悬浮在桌面上。</summary>
    public bool IsPanelExpanded => IsSearchOpen || IsEditMode;

    // ---------------- 命令 ----------------

    public ICommand RunCommand { get; }

    public ICommand RemoveCommand { get; }

    public ICommand AddInstalledCommand { get; }

    public ICommand ClearAllCommand { get; }

    public ICommand UndoRemoveCommand { get; }

    public ICommand ClearSearchCommand { get; }

    public ICommand ToggleSearchCommand { get; }

    public ICommand ToggleEditCommand { get; }

    public ICommand TogglePinCommand { get; }

    public ICommand HideCommand { get; }

    public ICommand ShowCommand { get; }

    public ICommand ExitCommand { get; }

    public ICommand OpenSettingsCommand { get; }

    public ICommand SetEdgeCommand { get; }

    public ICommand ToggleAutoStartCommand { get; }

    public ICommand CheckUpdateCommand { get; }

    public ICommand OpenUpdateCommand { get; }

    public ICommand DownloadUpdateCommand { get; }

    public ICommand CancelUpdateDownloadCommand { get; }

    public ICommand InstallUpdateCommand { get; }

    public ICommand DismissUpdateCommand { get; }

    // ---------------- 数据 ----------------

    private void LoadItems() => RebuildItems();

    private void LoadInstalledApps()
    {
        try
        {
            _installedCatalog.Clear();
            _installedCatalog.AddRange(_installedAppProvider.GetInstalledApps());
        }
        catch (Exception ex)
        {
            AppLog.Error("读取系统已安装应用失败", ex);
        }

        RefreshFilter();
    }

    /// <summary>来源变化只记脏标记（后台线程触发，这里不做任何扫描），把重扫推迟到真正需要结果的时候。</summary>
    private void OnInstalledAppsChanged() => _installedAppsDirty = true;

    /// <summary>打开搜索面板才重扫：平时新装/卸载应用不产生任何扫描开销，打开瞬间至多补扫一次。</summary>
    private void RefreshInstalledCatalogIfDirty()
    {
        if (!_installedAppsDirty)
        {
            return;
        }

        _installedAppsDirty = false;
        LoadInstalledApps();
    }

    /// <summary>用 _config.Items 重建视图列表（首次载入与导入配置共用）。</summary>
    private void RebuildItems()
    {
        _allItems.Clear();
        foreach (LauncherItem item in _config.Items)
        {
            var vm = new ItemViewModel(item, RunItem, RemoveItem);
            _allItems.Add(vm);
            _ = LoadIconAsync(vm);
        }

        ApplyItemLayout();
        RefreshFilter();
    }

    /// <summary>
    /// 有些值只有视图侧能给：线性图标的 Geometry 要在视图层解析、占位底色要按类型与尺寸算。
    /// 视图订阅这个事件，把每个图标的 IconBrush / Glyph 写回 ItemViewModel。
    /// </summary>
    public event Action<ItemViewModel>? ItemVisualRequested;

    /// <summary>把「随停靠边变化的布局参数」推给每个图标，避免 XAML 里用 $parent 绑定。</summary>
    private void ApplyItemLayout()
    {
        Orientation orientation = IsVertical ? Orientation.Horizontal : Orientation.Vertical;
        string hover = TileHoverTransform;
        string pressed = TilePressedTransform;
        foreach (ItemViewModel item in _allItems)
        {
            item.ShowLabel = Settings.ShowLabels;
            item.ShowRemove = IsEditMode;
            item.TileSize = Settings.TileSize;
            item.ItemLabelSpacing = ItemLabelSpacing;
            item.ItemOrientation = orientation;
            item.HoverTransform = hover;
            item.PressedTransform = pressed;
            ItemVisualRequested?.Invoke(item);
        }

        foreach (ItemViewModel item in _installedViewModels.Values)
        {
            item.ShowLabel = Settings.ShowLabels;
            item.ShowRemove = false;
            item.TileSize = Settings.TileSize;
            item.ItemOrientation = orientation;
            item.HoverTransform = hover;
            item.PressedTransform = pressed;
            ItemVisualRequested?.Invoke(item);
        }
    }

    private void RefreshFilter()
    {
        // 显示顺序永远跟随 _config.Items（拖动排序后只改了它，视图列表按它重建）
        var vmById = new Dictionary<string, ItemViewModel>(_allItems.Count, StringComparer.OrdinalIgnoreCase);
        foreach (ItemViewModel vm in _allItems)
        {
            vmById[vm.Id] = vm;
        }

        string activeQuery = IsSearchOpen ? SearchQuery : string.Empty;
        IReadOnlyList<LauncherItem> filtered = ItemQuery.Filter(_config.Items, activeQuery);

        Items.Clear();
        foreach (LauncherItem model in filtered)
        {
            if (vmById.TryGetValue(model.Id, out ItemViewModel? vm))
            {
                Items.Add(vm);
            }
        }

        InstalledItems.Clear();
        if (IsSearchGrouped)
        {
            var configuredTargets = new HashSet<string>(
                _config.Items.Select(item => NormalizeTarget(item.Target)),
                StringComparer.OrdinalIgnoreCase);
            var configuredNames = new HashSet<string>(
                _config.Items.Select(item => item.Name),
                StringComparer.OrdinalIgnoreCase);

            foreach (LauncherItem model in ItemQuery.Filter(_installedCatalog, activeQuery))
            {
                if (configuredTargets.Contains(NormalizeTarget(model.Target))
                    || configuredNames.Contains(model.Name))
                {
                    continue;
                }

                if (!_installedViewModels.TryGetValue(model.Id, out ItemViewModel? vm))
                {
                    vm = new ItemViewModel(model, RunItem, RemoveItem, AddInstalledItem, isSystemResult: true);
                    _installedViewModels[model.Id] = vm;
                    _ = LoadIconAsync(vm);
                    ApplyItemLayoutTo(vm);
                }

                InstalledItems.Add(vm);
            }
        }

        this.RaisePropertyChanged(nameof(IsSearchEmpty));
        this.RaisePropertyChanged(nameof(IsDockEmpty));
        this.RaisePropertyChanged(nameof(CountText));
        this.RaisePropertyChanged(nameof(EmptyStateText));
        this.RaisePropertyChanged(nameof(IsSearchGrouped));
        this.RaisePropertyChanged(nameof(ConfiguredGroupTitle));
        this.RaisePropertyChanged(nameof(ConfiguredGroupCount));
        this.RaisePropertyChanged(nameof(HasConfiguredResults));
        this.RaisePropertyChanged(nameof(InstalledGroupTitle));
        this.RaisePropertyChanged(nameof(InstalledGroupCount));
        this.RaisePropertyChanged(nameof(HasInstalledResults));
    }

    private static string NormalizeTarget(string target)
        => (target ?? string.Empty).Trim().TrimEnd('\\', '/');

    private void ApplyItemLayoutTo(ItemViewModel item)
    {
        item.ShowLabel = Settings.ShowLabels;
        item.ShowRemove = IsEditMode && !item.IsSystemResult;
        item.TileSize = Settings.TileSize;
        item.ItemOrientation = IsVertical ? Orientation.Horizontal : Orientation.Vertical;
        item.HoverTransform = item.ShowRemove ? ScaleIdentity : TileHoverTransform;
        item.PressedTransform = item.ShowRemove ? ScaleIdentity : TilePressedTransform;
        ItemVisualRequested?.Invoke(item);
    }

    private async Task LoadIconAsync(ItemViewModel vm)
    {
        try
        {
            // 用户选择的本地图片优先：直接加载，不走 Shell 提取
            string? custom = vm.Model.CustomIconPath;
            if (!string.IsNullOrWhiteSpace(custom) && File.Exists(custom))
            {
                vm.IconFile = custom;
                return;
            }

            string? path = await _icons.GetIconFileAsync(vm.Model).ConfigureAwait(true);
            if (!string.IsNullOrWhiteSpace(path))
            {
                vm.IconFile = path;
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("加载图标失败：" + vm.Name, ex);
        }
    }

    private void RunItem(ItemViewModel? vm)
    {
        if (vm is null)
        {
            return;
        }

        if (IsEditMode)
        {
            Toast("编辑模式下不运行 · 点「完成」退出");
            return;
        }

        if (!_launcher.TryStart(vm.Model, out string? error))
        {
            Toast("启动失败：" + error);
            return;
        }

        vm.IsRunning = true;
        _ = ResetRunningAsync(vm);
        Toast("正在启动：" + vm.Name);

        if (Settings.CollapseAfterLaunch && !Settings.Pinned)
        {
            IsDockVisible = false;
        }
    }

    /// <summary>把搜索结果里的系统应用复制到自定义配置并立即持久化。</summary>
    private void AddInstalledItem(ItemViewModel? vm)
    {
        if (vm is null || !vm.IsSystemResult)
        {
            return;
        }

        string target = NormalizeTarget(vm.Model.Target);
        if (_config.Items.Any(item => string.Equals(NormalizeTarget(item.Target), target, StringComparison.OrdinalIgnoreCase)))
        {
            Toast(vm.Name + " 已在自定义配置中");
            return;
        }

        LauncherItem model = new()
        {
            Id = "i" + Guid.NewGuid().ToString("N")[..8],
            Name = vm.Model.Name,
            Kind = vm.Model.Kind,
            Target = vm.Model.Target,
            Arguments = vm.Model.Arguments,
            WorkingDirectory = vm.Model.WorkingDirectory,
            CustomIconPath = vm.Model.CustomIconPath
        };

        _config.Items.Add(model);
        var configured = new ItemViewModel(model, RunItem, RemoveItem);
        _allItems.Add(configured);
        _ = LoadIconAsync(configured);
        ApplyItemLayout();
        RefreshFilter();
        Save();
        Toast("已添加 " + model.Name + " 到 Dock");
    }

    private static async Task ResetRunningAsync(ItemViewModel vm)
    {
        await Task.Delay(340).ConfigureAwait(true);
        vm.IsRunning = false;
    }

    private void RemoveItem(ItemViewModel? vm)
    {
        if (vm is null)
        {
            return;
        }

        int index = _allItems.IndexOf(vm);
        if (index < 0 || !_allItems.Remove(vm) || !_config.Items.Remove(vm.Model))
        {
            return;
        }

        _lastRemovedItems = new List<(LauncherItem Item, int Index)> { (vm.Model, index) };
        SyncRecommendedCardsAfterDockChange(vm.Model.RecommendedId);
        RefreshFilter();
        Save();
        Toast("已移除 " + vm.Name, "撤销", UndoRemoveCommand, 5000);
    }

    /// <summary>Dock 条目增删/导入后，把推荐卡片的「已加入 Dock」状态对齐现实。</summary>
    private void SyncRecommendedCardsAfterDockChange(string? recommendedId = null)
    {
        if (RecommendedApps is null)
        {
            return;
        }

        foreach (RecommendedAppCardViewModel card in RecommendedApps.Cards)
        {
            if (recommendedId is null || string.Equals(card.Id, recommendedId, StringComparison.OrdinalIgnoreCase))
            {
                RecommendedApps.SyncDockMembership(card.Id, IsRecommendedInDock(card.Id));
            }
        }
    }

    private void ClearAllItems()
    {
        if (_config.Items.Count == 0)
        {
            Toast("列表已经是空的");
            return;
        }

        _lastRemovedItems = _config.Items
            .Select((item, index) => (Item: item, Index: index))
            .ToList();
        int count = _lastRemovedItems.Count;
        _config.Items.Clear();
        _allItems.Clear();
        RefreshFilter();
        Save();
        Toast("已清空 " + count + " 项", "撤销", UndoRemoveCommand, 5000);
    }

    /// <summary>撤销单项移除或清空列表，按原顺序恢复。</summary>
    private void UndoRemove()
    {
        if (_lastRemovedItems is not { Count: > 0 } removedItems)
        {
            return;
        }

        _lastRemovedItems = null;
        foreach ((LauncherItem item, int originalIndex) in removedItems.OrderBy(entry => entry.Index))
        {
            int index = Math.Clamp(originalIndex, 0, Math.Min(_allItems.Count, _config.Items.Count));
            var vm = new ItemViewModel(item, RunItem, RemoveItem);
            _config.Items.Insert(index, item);
            _allItems.Insert(index, vm);
            _ = LoadIconAsync(vm);
        }

        ApplyItemLayout();
        RefreshFilter();
        SyncRecommendedCardsAfterDockChange();
        Save();
        Toast("已恢复");
    }

    /// <summary>添加目标（设置里选择文件，或将来的拖入）。</summary>
    public void AddTargets(IEnumerable<string> paths)
    {
        int added = 0;
        foreach (string path in paths)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            var model = new LauncherItem
            {
                Id = "i" + Guid.NewGuid().ToString("N")[..8],
                Target = path.Trim(),
                Kind = GuessKind(path)
            };
            model.Name = ItemQuery.ResolveDisplayName(model);

            _config.Items.Add(model);
            var vm = new ItemViewModel(model, RunItem, RemoveItem);
            _allItems.Add(vm);
            _ = LoadIconAsync(vm);
            added++;
        }

        if (added == 0)
        {
            return;
        }

        ApplyItemLayout();
        RefreshFilter();
        Save();
        Toast("已添加 " + added + " 项");
    }

    /// <summary>网址 → Web；其余交给 Shell 打开（.lnk 也能直接启动，不必解析目标）。</summary>
    private static ItemKind GuessKind(string path)
        => Uri.TryCreate(path, UriKind.Absolute, out Uri? uri)
           && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? ItemKind.Web
            : ItemKind.App;

    public void AddCommand(string name, string command)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(command))
        {
            return;
        }

        var model = new LauncherItem
        {
            Id = "i" + Guid.NewGuid().ToString("N")[..8],
            Name = name.Trim(),
            Target = command.Trim(),
            Kind = ItemKind.Command
        };

        _config.Items.Add(model);
        _allItems.Add(new ItemViewModel(model, RunItem, RemoveItem));
        ApplyItemLayout();
        RefreshFilter();
        Save();
        Toast("已添加命令行：" + model.Name);
    }

    /// <summary>「更换图标」选定本地图片后：复制进图标库（配置目录内，按条目 Id 命名）、加载显示并持久化。</summary>
    public void ChangeIcon(ItemViewModel item, string imagePath)
    {
        string? stored = _store.ImportCustomIcon(imagePath, item.Model.Id, out string? copyError);
        if (stored is null)
        {
            // 复制失败（磁盘/权限问题）退回引用原图，至少当下能用
            item.Model.CustomIconPath = imagePath;
        }
        else
        {
            item.Model.CustomIconPath = stored;
        }

        item.IconFile = item.Model.CustomIconPath;
        ItemVisualRequested?.Invoke(item);
        Save();
        Toast(stored is null
            ? "已更换 " + item.Name + " 的图标（复制失败，仍引用原图：" + copyError + "）"
            : "已更换 " + item.Name + " 的图标");
    }

    // ---------------- 导入 / 导出 ----------------

    public bool ExportConfigTo(string filePath) => _store.Export(_config, filePath);

    /// <summary>从文件导入完整配置（设置 + 列表整体替换），返回是否成功。</summary>
    public bool ImportConfigFrom(string filePath)
    {
        AppConfig? imported = _store.Import(filePath);
        if (imported is null)
        {
            Toast("导入失败：不是有效的 QuickApp 配置文件");
            return false;
        }

        bool edgeChanged = imported.Settings.Edge != Settings.Edge;
        bool monitorChanged = imported.Settings.MonitorIndex != Settings.MonitorIndex;
        _config.SchemaVersion = imported.SchemaVersion;
        _config.Settings = imported.Settings;
        _config.Items = imported.Items;

        RebuildItems();
        SyncRecommendedCardsAfterDockChange();
        RefreshPalette();
        ApplySettings(paletteChanged: false, sizeChanged: true, save: false);
        if (edgeChanged)
        {
            _host?.ApplyEdge(Settings.Edge);
        }
        else if (monitorChanged)
        {
            _host?.MonitorSelectionChanged();
        }

        _host?.AutoHideDelayChanged();

        Save();
        // 带进来的图标刚解包到本地图标库，此时清掉被整个替换掉的旧配置残留
        _store.CleanupOrphanedIcons(_config.Items);
        Toast("已导入 " + _config.Items.Count + " 项");
        return true;
    }

    // ---------------- 设置 ----------------

    private void SetEdge(DockEdge edge)
    {
        if (Settings.Edge == edge)
        {
            return;
        }

        Settings.Edge = edge;
        Save();
        this.RaisePropertyChanged(nameof(IsVertical));
        this.RaisePropertyChanged(nameof(EdgeLabel));
        this.RaisePropertyChanged(nameof(CollapseGlyph));
        this.RaisePropertyChanged(nameof(ShowUpdateBar));
        this.RaisePropertyChanged(nameof(ShowUpdatePill));
        _host?.ApplyEdge(edge);
        Toast("已停靠到" + EdgeLabel);
    }

    public void SelectMonitorIndex(int index)
    {
        if (Settings.MonitorIndex == index)
        {
            return;
        }

        Settings.MonitorIndex = index;
        Save();
        _host?.MonitorSelectionChanged();
        Toast("已移动到显示器 " + (index + 1));
    }

    public void AutoHideDelayChanged() => _host?.AutoHideDelayChanged();

    private void ToggleAutoStart()
    {
        bool target = !AutoStartEnabled;
        string exe = Environment.ProcessPath ?? string.Empty;

        if (_autoStart.SetEnabled(AppName, target, exe, out string? error))
        {
            Settings.AutoStart = target;
            Save();
            Toast(target ? "已开启开机启动" : "已关闭开机启动");
        }
        else
        {
            Toast(error ?? "设置开机启动失败");
        }

        this.RaisePropertyChanged(nameof(AutoStartEnabled));
    }

    /// <summary>设置窗口改完值后调用：换配色、改尺寸、必要时存盘。</summary>
    public void ApplySettings(bool paletteChanged = true, bool sizeChanged = true, bool save = true)
    {
        if (paletteChanged)
        {
            RefreshPalette();
        }

        if (sizeChanged)
        {
            this.RaisePropertyChanged(nameof(TileSize));
            this.RaisePropertyChanged(nameof(TileRadius));
            this.RaisePropertyChanged(nameof(PanelRadius));
            this.RaisePropertyChanged(nameof(PanelCornerRadius));
            this.RaisePropertyChanged(nameof(IsVertical));
            this.RaisePropertyChanged(nameof(BodyOrientation));
            this.RaisePropertyChanged(nameof(ListOrientation));
            this.RaisePropertyChanged(nameof(ListSpacing));
            this.RaisePropertyChanged(nameof(ListPadding));
            this.RaisePropertyChanged(nameof(DividerWidth));
            this.RaisePropertyChanged(nameof(DividerHeight));
            this.RaisePropertyChanged(nameof(ListMaxWidth));
            this.RaisePropertyChanged(nameof(ListMaxHeight));
            this.RaisePropertyChanged(nameof(ListMinWidth));
            this.RaisePropertyChanged(nameof(ListMinHeight));
            this.RaisePropertyChanged(nameof(ActionMaxWidth));
            this.RaisePropertyChanged(nameof(ActionMaxHeight));
            this.RaisePropertyChanged(nameof(TileHoverTransform));
            this.RaisePropertyChanged(nameof(CollapseGlyph));
            ApplyItemLayout();
        }

        this.RaisePropertyChanged(nameof(ShowLabels));
        this.RaisePropertyChanged(nameof(ThemeName));
        this.RaisePropertyChanged(nameof(StyleName));
        this.RaisePropertyChanged(nameof(AutoStartEnabled));

        if (save)
        {
            Save();
        }
    }

    private static Color WithAlpha(Color c, double alpha) => Color.FromArgb((byte)Math.Round(alpha * 255), c.R, c.G, c.B);

    private void RefreshPalette()
    {
        bool dark = Settings.Theme switch
        {
            "light" => false,
            "system" => IsSystemDark(),
            _ => true
        };

        // Fluent 控件的浅/深由「应用主题变体」决定，必须跟着我们的设置走：
        // 否则系统是浅色主题时，勾选框/滑块/下拉会被渲染成浅色控件压在深色面板上（设置窗口看着就是两套风格）。
        // 跟随系统时设回 Default——若钉死成具体深/浅，ActualThemeVariant 会被覆盖，
        // 之后操作系统深浅切换将不再触发 ActualThemeVariantChanged（跟随就失效了）。
        ApplyThemeVariant(Settings.Theme == "system" ? null : dark);

        Palette palette = (dark ? Palette.Dark : Palette.Light)
            .WithOpacity(GlassAlpha, flat: Settings.Style == "flat");

        Color opaque = Color.FromArgb(255, palette.Panel.R, palette.Panel.G, palette.Panel.B);
        PanelBrush = PaletteBrushes.Panel(palette);
        SettingsBackgroundBrush = PaletteBrushes.Brush(palette.MenuOpaque);
        PanelBorderBrush = PaletteBrushes.Brush(palette.PanelBorder);
        TextBrush = PaletteBrushes.Text(palette);
        TextDimBrush = PaletteBrushes.TextDim(palette);
        HoverBrush = PaletteBrushes.Brush(palette.Hover);
        AccentBrush = PaletteBrushes.Brush(palette.Accent);
        AccentInkBrush = PaletteBrushes.Brush(palette.AccentInk);
        MenuBrush = PaletteBrushes.Brush(palette.Menu);
        MenuHoverBrush = PaletteBrushes.Brush(palette.MenuHover);
        DangerBrush = PaletteBrushes.Brush(palette.Danger);
        PillBrush = PaletteBrushes.Brush(WithAlpha(palette.Accent, 0.14));
        PillHoverBrush = PaletteBrushes.Brush(WithAlpha(palette.Accent, 0.24));

        foreach (string name in new[]
        {
            nameof(PanelBrush), nameof(SettingsBackgroundBrush), nameof(PanelBorderBrush),
            nameof(TextBrush), nameof(TextDimBrush), nameof(HoverBrush), nameof(AccentBrush),
            nameof(AccentInkBrush), nameof(MenuBrush), nameof(MenuHoverBrush), nameof(DangerBrush), nameof(PillBrush), nameof(PillHoverBrush)
        })
        {
            this.RaisePropertyChanged(name);
        }
    }

    /// <summary>把 Avalonia 的主题变体同步成我们选定的深浅，保证 Fluent 控件与自绘面板同一套配色。</summary>
    private static void ApplyThemeVariant(bool? dark)
    {
        try
        {
            if (Avalonia.Application.Current is { } app)
            {
                // null = 跟随系统：RequestedThemeVariant 设回 Default，ActualThemeVariant 透传系统值
                app.RequestedThemeVariant = dark switch
                {
                    null => Avalonia.Styling.ThemeVariant.Default,
                    true => Avalonia.Styling.ThemeVariant.Dark,
                    false => Avalonia.Styling.ThemeVariant.Light
                };
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("切换应用主题变体失败", ex);
        }
    }

    /// <summary>窗口透明级别不可用时毛玻璃退化成高不透明度纯色（对应原型 @supports 回退）。</summary>
    public void SetGlassDegraded(bool degraded)
    {
        if (_glassDegraded == degraded)
        {
            return;
        }

        _glassDegraded = degraded;
        RefreshPalette();
    }

    /// <summary>面板不透明度已固定 100%（原型定稿：面板颜色不再随配置变化）。</summary>
    private const double GlassAlpha = 1.0;

    private static bool IsSystemDark()
    {
        try
        {
            return Avalonia.Application.Current?.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Dark;
        }
        catch
        {
            return true;
        }
    }

    public void Save() => _store.Save(_config);

    // ---------------- 更新 ----------------

    public async Task CheckUpdateAsync(bool silent = false)
    {
        if (IsCheckingUpdate)
        {
            return;
        }

        // 手动检查防抖：短时间重复点击不重复请求（限流恢复期间尤其不该放大流量）
        if (!silent && DateTime.UtcNow - _lastUpdateCheckAt < TimeSpan.FromSeconds(15))
        {
            Toast("刚刚检查过更新");
            return;
        }

        IsCheckingUpdate = true;
        try
        {
            Version current = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 1, 0);
            UpdateCheckResult result = await _updates.CheckAsync(current).ConfigureAwait(true);
            _lastUpdateCheckAt = DateTime.UtcNow;

            if (!result.Succeeded)
            {
                _updateResultText = string.IsNullOrWhiteSpace(result.Error)
                    ? "检查更新失败，请稍后重试"
                    : "检查更新失败：" + result.Error;
                this.RaisePropertyChanged(nameof(UpdateResultText));
                if (!silent)
                {
                    Toast(_updateResultText);
                }

                return;
            }

            UpdateInfo? info = result.Update;

            if (info is null)
            {
                ClearDownloadedUpdate(cancel: true);
                _pendingUpdate = null;
                this.RaisePropertyChanged(nameof(UpdateAvailable));
                this.RaisePropertyChanged(nameof(UpdateText));
                this.RaisePropertyChanged(nameof(UpdateDisplayText));
                this.RaisePropertyChanged(nameof(UpdateNoteText));
                this.RaisePropertyChanged(nameof(UpdateIconBrush));
                this.RaisePropertyChanged(nameof(ShowUpdateBar));
                this.RaisePropertyChanged(nameof(ShowUpdatePill));
                RaiseDownloadStateChanged();
                _updateResultText = "已是最新版本 " + VersionText;
                this.RaisePropertyChanged(nameof(UpdateResultText));
                if (!silent)
                {
                    Toast(_updateResultText);
                }

                return;
            }

            ClearDownloadedUpdate(cancel: true);
            _pendingUpdate = info;
            this.RaisePropertyChanged(nameof(UpdateAvailable));
            this.RaisePropertyChanged(nameof(UpdateText));
            this.RaisePropertyChanged(nameof(UpdateDisplayText));
            this.RaisePropertyChanged(nameof(UpdateNoteText));
            this.RaisePropertyChanged(nameof(UpdateIconBrush));
            this.RaisePropertyChanged(nameof(ShowUpdateBar));
            this.RaisePropertyChanged(nameof(ShowUpdatePill));
            RaiseDownloadStateChanged();
            _updateResultText = UpdateText;
            this.RaisePropertyChanged(nameof(UpdateResultText));
            Toast("发现新版本 " + info.Tag);
        }
        catch (Exception ex)
        {
            AppLog.Error("检查更新失败", ex);
        }
        finally
        {
            IsCheckingUpdate = false;
        }
    }

    /// <summary>
    /// 运行中周期检查更新：Dock 常驻可能几周不重启，只靠启动检查会漏掉新版本。
    /// 沿用「检查更新」开关；失败静默（不 Toast），发现新版本才提示。
    /// </summary>
    private async Task RunPeriodicUpdateCheckAsync()
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromHours(UpdateCheckIntervalHours));
            while (await timer.WaitForNextTickAsync().ConfigureAwait(false))
            {
                try
                {
                    if (!Settings.CheckUpdates)
                    {
                        continue;
                    }

                    await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(
                        () => CheckUpdateAsync(silent: true)).ConfigureAwait(true);
                }
                catch (Exception ex)
                {
                    AppLog.Error("周期检查更新失败", ex);
                }
            }
        }
        catch
        {
            // PeriodicTimer 释放（进程退出）时结束循环
        }
    }

    private async Task DownloadUpdateAsync()
    {
        if (IsDownloadingUpdate)
        {
            return;
        }

        if (_pendingUpdate?.AssetUrl is null)
        {
            OpenUpdatePage();
            return;
        }

        _downloadCancellation?.Dispose();
        _downloadCancellation = new CancellationTokenSource();
        _downloadProgress = 0;
        _isDownloadingUpdate = true;
        RaiseDownloadStateChanged();

        try
        {
            var progress = new Progress<UpdateDownloadProgress>(value =>
            {
                _downloadProgress = value.Percentage ?? _downloadProgress;
                RaiseDownloadStateChanged();
                Toast("正在下载更新 · " + _downloadProgress.ToString("0") + "%", "取消", CancelUpdateDownloadCommand, 5000, _downloadProgress);
            });
            UpdateDownloadResult result = await _updateDownloader.DownloadAsync(
                _pendingUpdate,
                progress,
                _downloadCancellation.Token).ConfigureAwait(true);

            _downloadedUpdatePath = result.FilePath;
            _downloadProgress = 100;
            Toast("下载完成，请点击" + UpdateInstallButtonText);
        }
        catch (OperationCanceledException)
        {
            Toast("已取消下载");
        }
        catch (Exception ex)
        {
            AppLog.Error("下载更新失败", ex);
            Toast("下载失败，请重试");
        }
        finally
        {
            _isDownloadingUpdate = false;
            _downloadCancellation?.Dispose();
            _downloadCancellation = null;
            RaiseDownloadStateChanged();
        }
    }

    private void CancelUpdateDownload() => _downloadCancellation?.Cancel();

    private void InstallDownloadedUpdate()
    {
        string? path = _downloadedUpdatePath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            ClearDownloadedUpdate(cancel: false);
            Toast("安装包不存在，请重新下载");
            return;
        }

        try
        {
            if (IsWindowsInstallerAsset)
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
                _host?.Exit();
            }
            else
            {
                _host?.OpenUrl(path);
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("打开安装包失败", ex);
            Toast("打开安装包失败");
        }
    }

    private void ClearDownloadedUpdate(bool cancel)
    {
        if (cancel)
        {
            _downloadCancellation?.Cancel();
        }

        _downloadedUpdatePath = null;
        _downloadProgress = 0;
        RaiseDownloadStateChanged();
    }

    private void RaiseDownloadStateChanged()
    {
        this.RaisePropertyChanged(nameof(IsDownloadingUpdate));
        this.RaisePropertyChanged(nameof(IsUpdateReady));
        this.RaisePropertyChanged(nameof(CanDownloadUpdate));
        this.RaisePropertyChanged(nameof(NeedsUpdatePage));
        this.RaisePropertyChanged(nameof(DownloadProgress));
        this.RaisePropertyChanged(nameof(DownloadProgressText));
        this.RaisePropertyChanged(nameof(UpdateInstallButtonText));
        this.RaisePropertyChanged(nameof(UpdateDisplayText));
        this.RaisePropertyChanged(nameof(UpdateNoteText));
        this.RaisePropertyChanged(nameof(UpdateIconBrush));
        this.RaisePropertyChanged(nameof(UpdateIconGlyph));
        this.RaisePropertyChanged(nameof(ShowUpdateBar));
        this.RaisePropertyChanged(nameof(ShowUpdatePill));
    }

    private void OpenUpdatePage()
    {
        string? url = _pendingUpdate?.PageUrl;
        if (!string.IsNullOrWhiteSpace(url))
        {
            _host?.OpenUrl(url);
        }
    }

    // ---------------- 推荐应用 ----------------

    /// <summary>
    /// 推荐应用安装完成后（或用户点「加入 Dock」）把快捷方式放进 Dock。
    /// 同一推荐应用的条目已存在时只刷新图标与路径，不重复添加。
    /// 主程序路径以安装后探测结果优先；没探测到就用条目里已有的（或空，启动时再报错）。
    /// </summary>
    public void AddRecommendedItem(RecommendedApp app, string? launcherPath)
    {
        LauncherItem? existing = _config.Items.FirstOrDefault(item =>
            string.Equals(item.RecommendedId, app.Id, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            if (!string.IsNullOrWhiteSpace(launcherPath))
            {
                existing.Target = launcherPath;
            }

            ItemViewModel? existingVm = _allItems.FirstOrDefault(vm => ReferenceEquals(vm.Model, existing));
            if (existingVm is not null)
            {
                _ = LoadIconAsync(existingVm);
            }

            Save();
            return;
        }

        var model = new LauncherItem
        {
            Id = "i" + Guid.NewGuid().ToString("N")[..8],
            Name = app.Name,
            Kind = ItemKind.App,
            Target = launcherPath ?? string.Empty,
            RecommendedId = app.Id
        };

        _config.Items.Add(model);
        var vm = new ItemViewModel(model, RunItem, RemoveItem);
        _allItems.Add(vm);
        _ = LoadIconAsync(vm);
        ApplyItemLayout();
        RefreshFilter();
        Save();
    }

    /// <summary>推荐卡片「移出 Dock」：只摘快捷方式，保留安装状态，可随时再加回。</summary>
    public void RemoveRecommendedItem(string appId)
    {
        LauncherItem? model = _config.Items.FirstOrDefault(item =>
            string.Equals(item.RecommendedId, appId, StringComparison.OrdinalIgnoreCase));
        if (model is null)
        {
            return;
        }

        ItemViewModel? vm = _allItems.FirstOrDefault(item => ReferenceEquals(item.Model, model));
        if (vm is not null)
        {
            _allItems.Remove(vm);
        }

        _config.Items.Remove(model);
        RefreshFilter();
        Save();
    }

    /// <summary>Dock 条目里某推荐应用是否已有快捷方式（编辑模式移除后变 false）。</summary>
    public bool IsRecommendedInDock(string appId)
        => _config.Items.Any(item =>
            string.Equals(item.RecommendedId, appId, StringComparison.OrdinalIgnoreCase));

    /// <summary>推荐卡片下载安装包：复用自更新的下载器（临时目录 + sha256 校验 + 原子改名）。</summary>
    public Task<UpdateDownloadResult> DownloadRecommendedAssetAsync(
        UpdateInfo update,
        IProgress<UpdateDownloadProgress> progress,
        CancellationToken cancellationToken)
        => _updateDownloader.DownloadAsync(update, progress, cancellationToken);

    /// <summary>
    /// 运行推荐应用的安装包。Windows 上 Inno 安装器带静默参数后台安装；
    /// 其他平台/无参数时交给系统打开安装包，由用户手动完成。
    /// </summary>
    public async Task<bool> InstallRecommendedPackageAsync(RecommendedApp app, string installerPath)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                string arguments = app.InstallArgumentsWindows ?? string.Empty;
                using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = installerPath,
                    Arguments = arguments,
                    UseShellExecute = true
                });
                if (process is not null && !string.IsNullOrWhiteSpace(arguments))
                {
                    await process.WaitForExitAsync().ConfigureAwait(false);
                    return process.ExitCode == 0;
                }

                return true;
            }

            await Task.Run(() =>
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = installerPath,
                    UseShellExecute = true
                })).ConfigureAwait(false);
            // 手动安装：无法确认完成，交由用户「加入 Dock」时重新探测路径
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Error("运行推荐应用安装包失败：" + app.Name, ex);
            return false;
        }
    }

    /// <summary>推荐应用安装完成后刷新一次图标缓存来源（新装的应用第一次提取图标可能还没就绪）。</summary>
    public void RefreshRecommendedIcon(string appId)
    {
        LauncherItem? model = _config.Items.FirstOrDefault(item =>
            string.Equals(item.RecommendedId, appId, StringComparison.OrdinalIgnoreCase));
        ItemViewModel? vm = _allItems.FirstOrDefault(item => item.Model.RecommendedId == appId);
        if (model is not null && vm is not null)
        {
            _ = LoadIconAsync(vm);
        }
    }

    /// <summary>推荐应用控制器（设置 · 推荐页签的数据源）；DI 构造后由 App 装配。</summary>
    public RecommendedAppsController? RecommendedApps { get; set; }

    /// <summary>设置窗口打开推荐页签时刷新卡片版本信息（服务层带 TTL 缓存）。</summary>
    public Task RefreshRecommendedAppsAsync()
        => RecommendedApps?.RefreshAsync() ?? Task.CompletedTask;

    // ---------------- 视图回调：尺寸/透明度/标签变化后由窗口重新应用 ----------------

    /// <summary>拖动排序落定后刷新顺序（顺序本身就是 config.Items 的顺序）。</summary>
    public void ApplySort() => ApplyItemLayout();

    /// <summary>只改透明度时不必重算朝向（窗口会重新应用透明度）。</summary>
    public void ApplyOpacity(bool paletteChanged) => ApplySettings(paletteChanged, sizeChanged: false, save: false);

    /// <summary>切换「仅图标 / 图标 + 名称」时只更新标签，不动尺寸与朝向（搜索态下由窗口自行恢复布局）。</summary>
    public void ApplyLabels()
    {
        if (IsSearchOpen && SearchQuery.Trim().Length > 0)
        {
            return;
        }

        foreach (ItemViewModel item in _allItems)
        {
            item.IsRenaming = false;
            item.ShowLabel = Settings.ShowLabels;
            item.ItemOrientation = IsVertical ? Orientation.Horizontal : Orientation.Vertical;
        }
    }

    // ---------------- 右键菜单请求（菜单由窗口按当前主题建，视图模型不碰 UI 类型） ----------------

    public event Action<ItemViewModel?>? ContextRequested;

    public void RequestContext(ItemViewModel? item) => ContextRequested?.Invoke(item);

    // ---------------- 就地改名 ----------------

    public void BeginRename(ItemViewModel item)
    {
        if (!IsEditMode)
        {
            IsEditMode = true;
        }

        foreach (ItemViewModel other in _allItems)
        {
            if (!ReferenceEquals(other, item))
            {
                other.IsRenaming = false;
            }
        }

        item.EditingName = item.Name;
        item.IsRenaming = true;
    }

    public void CommitRename(ItemViewModel item, bool save)
    {
        if (!item.IsRenaming)
        {
            return;
        }

        item.IsRenaming = false;
        if (!save)
        {
            return;
        }

        string value = item.EditingName.Trim();
        if (value.Length == 0)
        {
            Toast("名称不能为空，已保留原名");
            return;
        }

        if (string.Equals(value, item.Name, StringComparison.Ordinal))
        {
            return;
        }

        item.Name = value;
        Save();
        Toast("已重命名为 " + value);
    }

    // ---------------- 拖动排序 ----------------

    /// <summary>把 draggedId 移到 beforeId 之前；beforeId 为空表示移到最后。</summary>
    public void Reorder(string draggedId, string? beforeId)
    {
        int from = _config.Items.FindIndex(i => string.Equals(i.Id, draggedId, StringComparison.OrdinalIgnoreCase));
        if (from < 0)
        {
            return;
        }

        LauncherItem moved = _config.Items[from];
        _config.Items.RemoveAt(from);

        int insertAt = _config.Items.Count;
        if (!string.IsNullOrEmpty(beforeId))
        {
            int anchor = _config.Items.FindIndex(i => string.Equals(i.Id, beforeId, StringComparison.OrdinalIgnoreCase));
            if (anchor >= 0)
            {
                insertAt = anchor;
            }
        }

        _config.Items.Insert(insertAt, moved);
        ApplySort();
        RefreshFilter();
        Save();
        Toast("顺序已保存");
    }

    private void DismissUpdate()
    {
        ClearDownloadedUpdate(cancel: true);
        _pendingUpdate = null;
        this.RaisePropertyChanged(nameof(UpdateAvailable));
        this.RaisePropertyChanged(nameof(UpdateText));
        RaiseDownloadStateChanged();
    }
}
