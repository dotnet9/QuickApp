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

    private IDockHost? _host;
    private string _searchQuery = string.Empty;
    private bool _isSearchOpen;
    private bool _isEditMode;
    private bool _isDockVisible = true;
    private bool _isPointerOver;
    private bool _glassDegraded;
    private string? _statusMessage;
    private UpdateInfo? _pendingUpdate;
    private bool _isCheckingUpdate;
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
        LoadInstalledApps();
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
            this.RaisePropertyChanged();
            Save();
            Toast(value ? "已钉住，Dock 常驻显示" : "已取消钉住，鼠标离开后自动隐藏");
        }
    }

    /// <summary>主区域朝向：上/下边缘是「图标条 + 操作区」横排，左/右边缘竖排。</summary>
    public Orientation BodyOrientation => IsVertical ? Orientation.Vertical : Orientation.Horizontal;

    /// <summary>图标面板朝向。</summary>
    public Orientation ListOrientation => IsVertical ? Orientation.Vertical : Orientation.Horizontal;

    public double ListSpacing => IsVertical ? 4 : 6;

    public double DividerWidth => IsVertical ? Math.Round(Settings.TileSize * 1.2, 1) : 1;

    public double DividerHeight => IsVertical ? 1 : Math.Round(Settings.TileSize * 1.2, 1);

    /// <summary>图标区上限：默认完整容纳 10 项和悬浮安全留白，超出后沿停靠方向滚动。</summary>
    public double ListMaxWidth => IsVertical
        ? double.PositiveInfinity
        : Math.Round(Settings.TileSize * 10 + 122);

    public double ListMaxHeight => IsVertical
        ? Math.Round(Settings.TileSize * 10 + 104)
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

    /// <summary>操作区横向停靠为三列，纵向停靠为两列，按钮保持轻量紧凑。</summary>
    public double ActionMaxWidth => IsVertical ? 60 : 88;

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

    /// <summary>按下回缩（原型 tile:active 的 scale .94），偏移方向同悬停。</summary>
    public string TilePressedTransform => Settings.Edge switch
    {
        DockEdge.Bottom => "translateY(-5px) scale(0.94)",
        DockEdge.Left => "translateX(5px) scale(0.94)",
        DockEdge.Right => "translateX(-5px) scale(0.94)",
        _ => "translateY(5px) scale(0.94)"
    };

    /// <summary>为悬浮放大和两端 peek 留出缓冲，滚动视口只包住应用图标。</summary>
    public Thickness ListPadding => new(24);

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
    {
        ShowToast(message, actionLabel, actionCommand, durationMs);
    }

    private void ShowToast(string message, string? actionLabel, ICommand? actionCommand, int durationMs)
    {
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
                IsDockVisible = true;
            }
            else
            {
                SearchQuery = string.Empty;
            }
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

    public bool IsSearchGrouped => SearchQuery.Trim().Length > 0;

    public string ConfiguredGroupTitle => "已配置 · " + Items.Count;

    public bool HasConfiguredResults => IsSearchGrouped && Items.Count > 0;

    public string InstalledGroupTitle => "系统已安装 · " + InstalledItems.Count;

    public bool HasInstalledResults => IsSearchGrouped && InstalledItems.Count > 0;

    public string CountText => SearchQuery.Trim().Length > 0
        ? (Items.Count + InstalledItems.Count) + " 个结果"
        : Items.Count + " / " + _allItems.Count;

    public bool IsSearchEmpty => Items.Count == 0 && InstalledItems.Count == 0 && SearchQuery.Trim().Length > 0;

    public string EmptyStateText => SearchQuery.Trim().Length > 0
        ? "没有匹配「" + SearchQuery.Trim() + "」的项目"
        : "Dock 是空的 · 点右侧 + 添加第一个应用";

    /// <summary>Dock 一个项都没有（与「搜索无结果」区分开，给出添加引导）。</summary>
    public bool IsDockEmpty => Items.Count == 0 && SearchQuery.Trim().Length == 0;

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

        IReadOnlyList<LauncherItem> filtered = ItemQuery.Filter(_config.Items, SearchQuery);

        Items.Clear();
        foreach (LauncherItem model in filtered)
        {
            if (vmById.TryGetValue(model.Id, out ItemViewModel? vm))
            {
                Items.Add(vm);
            }
        }

        InstalledItems.Clear();
        if (SearchQuery.Trim().Length > 0)
        {
            var configuredTargets = new HashSet<string>(
                _config.Items.Select(item => NormalizeTarget(item.Target)),
                StringComparer.OrdinalIgnoreCase);
            var configuredNames = new HashSet<string>(
                _config.Items.Select(item => item.Name),
                StringComparer.OrdinalIgnoreCase);

            foreach (LauncherItem model in ItemQuery.Filter(_installedCatalog, SearchQuery))
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
        this.RaisePropertyChanged(nameof(HasConfiguredResults));
        this.RaisePropertyChanged(nameof(InstalledGroupTitle));
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
        item.HoverTransform = TileHoverTransform;
        item.PressedTransform = TilePressedTransform;
        ItemVisualRequested?.Invoke(item);
    }

    private async Task LoadIconAsync(ItemViewModel vm)
    {
        try
        {
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
            Toast("编辑模式下不启动项目，点「完成」退出编辑");
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
            IconKey = vm.Model.IconKey
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

        _lastRemoved = (vm.Model, index);
        RefreshFilter();
        Save();
        Toast("已移除 " + vm.Name, "撤销", UndoRemoveCommand, 5000);
    }

    /// <summary>移除撤销（原型 toast 的「撤销」动作）：把项插回原位置。</summary>
    private void UndoRemove()
    {
        if (_lastRemoved is not { } removed)
        {
            return;
        }

        _lastRemoved = null;
        int index = Math.Clamp(removed.Index, 0, Math.Min(_allItems.Count, _config.Items.Count));
        var vm = new ItemViewModel(removed.Item, RunItem, RemoveItem);
        _config.Items.Insert(Math.Min(removed.Index, _config.Items.Count), removed.Item);
        _allItems.Insert(index, vm);
        _ = LoadIconAsync(vm);
        ApplyItemLayout();
        RefreshFilter();
        Save();
        Toast("已恢复");
    }

    private (LauncherItem Item, int Index)? _lastRemoved;

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

    /// <summary>图标选择器选定后：写入 IconKey 并刷新该图标的显示。</summary>
    public void ChangeIcon(ItemViewModel item, string iconKey)
    {
        item.Model.IconKey = iconKey;
        item.ClearIcon();
        ItemVisualRequested?.Invoke(item);
        Save();
        Toast("已更换 " + item.Name + " 的图标");
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
        _config.SchemaVersion = imported.SchemaVersion;
        _config.Settings = imported.Settings;
        _config.Items = imported.Items;

        RebuildItems();
        RefreshPalette();
        ApplySettings(paletteChanged: false, sizeChanged: true, save: false);
        if (edgeChanged)
        {
            _host?.ApplyEdge(Settings.Edge);
        }

        Save();
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
        _host?.ApplyEdge(edge);
        Toast("Dock 已停靠到" + EdgeLabel);
    }

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
        ApplyThemeVariant(dark);

        Palette palette = (dark ? Palette.Dark : Palette.Light)
            .WithOpacity(GlassAlpha, flat: Settings.Style == "flat");

        Color opaque = Color.FromArgb(255, palette.Panel.R, palette.Panel.G, palette.Panel.B);
        PanelBrush = PaletteBrushes.Panel(palette);
        SettingsBackgroundBrush = PaletteBrushes.Brush(opaque);
        PanelBorderBrush = PaletteBrushes.Brush(palette.PanelBorder);
        TextBrush = PaletteBrushes.Text(palette);
        TextDimBrush = PaletteBrushes.TextDim(palette);
        HoverBrush = PaletteBrushes.Brush(palette.Hover);
        AccentBrush = PaletteBrushes.Brush(palette.Accent);
        AccentInkBrush = PaletteBrushes.Brush(palette.AccentInk);
        MenuBrush = PaletteBrushes.Brush(palette.Menu);
        MenuHoverBrush = PaletteBrushes.Brush(palette.MenuHover);
        DangerBrush = PaletteBrushes.Brush(palette.Danger);

        foreach (string name in new[]
        {
            nameof(PanelBrush), nameof(SettingsBackgroundBrush), nameof(PanelBorderBrush),
            nameof(TextBrush), nameof(TextDimBrush), nameof(HoverBrush), nameof(AccentBrush),
            nameof(AccentInkBrush), nameof(MenuBrush), nameof(MenuHoverBrush), nameof(DangerBrush)
        })
        {
            this.RaisePropertyChanged(name);
        }
    }

    /// <summary>把 Avalonia 的主题变体同步成我们选定的深浅，保证 Fluent 控件与自绘面板同一套配色。</summary>
    private static void ApplyThemeVariant(bool dark)
    {
        try
        {
            if (Avalonia.Application.Current is { } app)
            {
                app.RequestedThemeVariant = dark
                    ? Avalonia.Styling.ThemeVariant.Dark
                    : Avalonia.Styling.ThemeVariant.Light;
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

    private double GlassAlpha => _glassDegraded ? Math.Max(Settings.PanelOpacity, 0.92) : Settings.PanelOpacity;

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

    public async Task CheckUpdateAsync()
    {
        if (IsCheckingUpdate)
        {
            return;
        }

        IsCheckingUpdate = true;
        try
        {
            Version current = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 1, 0);
            UpdateCheckResult result = await _updates.CheckAsync(current).ConfigureAwait(true);

            if (!result.Succeeded)
            {
                _updateResultText = "检查更新失败，请稍后重试";
                this.RaisePropertyChanged(nameof(UpdateResultText));
                Toast(_updateResultText);
                return;
            }

            UpdateInfo? info = result.Update;

            if (info is null)
            {
                ClearDownloadedUpdate(cancel: true);
                _pendingUpdate = null;
                this.RaisePropertyChanged(nameof(UpdateAvailable));
                this.RaisePropertyChanged(nameof(UpdateText));
                RaiseDownloadStateChanged();
                _updateResultText = "已是最新版本 " + VersionText;
                this.RaisePropertyChanged(nameof(UpdateResultText));
                Toast(_updateResultText);
                return;
            }

            ClearDownloadedUpdate(cancel: true);
            _pendingUpdate = info;
            this.RaisePropertyChanged(nameof(UpdateAvailable));
            this.RaisePropertyChanged(nameof(UpdateText));
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
    }

    private void OpenUpdatePage()
    {
        string? url = _pendingUpdate?.PageUrl;
        if (!string.IsNullOrWhiteSpace(url))
        {
            _host?.OpenUrl(url);
        }
    }

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
