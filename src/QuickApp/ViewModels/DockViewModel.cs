using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
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
    private readonly IUpdateChecker _updates;
    private readonly IAutoStartService _autoStart;
    private readonly AppConfig _config;
    private readonly List<ItemViewModel> _allItems = new();

    private IDockHost? _host;
    private string _searchQuery = string.Empty;
    private bool _isSearchOpen;
    private bool _isEditMode;
    private bool _isDockVisible = true;
    private bool _isPointerOver;
    private string? _statusMessage;
    private UpdateInfo? _pendingUpdate;
    private bool _isCheckingUpdate;
    private int _toastToken;

    public DockViewModel(
        ConfigStore store,
        ILauncher launcher,
        IIconProvider icons,
        IUpdateChecker updates,
        IAutoStartService autoStart,
        string appName)
    {
        _store = store;
        _launcher = launcher;
        _icons = icons;
        _updates = updates;
        _autoStart = autoStart;
        AppName = appName;
        _config = store.Load();

        RunCommand = ReactiveCommand.Create<ItemViewModel>(RunItem);
        RemoveCommand = ReactiveCommand.Create<ItemViewModel>(RemoveItem);
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
        DismissUpdateCommand = ReactiveCommand.Create(DismissUpdate);

        LoadItems();
        RefreshPalette();
    }

    public string AppName { get; }

    public string VersionText =>
        "v" + (System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "0.1.0");

    public ObservableCollection<ItemViewModel> Items { get; } = new();

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

    /// <summary>图标区上限：横向 Dock 限制总宽（超出滚动），竖向 Dock 限制高度。</summary>
    public double ListMaxWidth => IsVertical ? double.PositiveInfinity : 1180;

    public double ListMaxHeight => IsVertical ? 640 : double.PositiveInfinity;

    /// <summary>收起按钮的箭头方向随停靠边变化。</summary>
    public string CollapseGlyph => Settings.Edge switch
    {
        DockEdge.Bottom => "▼",
        DockEdge.Left => "◀",
        DockEdge.Right => "▶",
        _ => "▲"
    };

    // ---------------- 更新状态 ----------------

    public bool UpdateAvailable => _pendingUpdate is not null;

    public string UpdateText => _pendingUpdate is null ? string.Empty : "发现新版本 " + _pendingUpdate.Tag;

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

    /// <summary>底部提示条：显示一段时间后自动消失。</summary>
    public void Toast(string message, int durationMs = 2200)
    {
        StatusMessage = message;
        int token = ++_toastToken;
        _ = ClearToastAsync(token, durationMs);
    }

    private async Task ClearToastAsync(int token, int durationMs)
    {
        await Task.Delay(durationMs).ConfigureAwait(true);
        if (token == _toastToken)
        {
            StatusMessage = null;
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

    public string CountText => Items.Count + " / " + _allItems.Count;

    public bool IsSearchEmpty => Items.Count == 0 && SearchQuery.Length > 0;

    // ---------------- 编辑模式 ----------------

    public bool IsEditMode
    {
        get => _isEditMode;
        set
        {
            if (Set(ref _isEditMode, value) && value)
            {
                IsDockVisible = true;
            }
        }
    }

    // ---------------- 命令 ----------------

    public ICommand RunCommand { get; }

    public ICommand RemoveCommand { get; }

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

    public ICommand DismissUpdateCommand { get; }

    // ---------------- 数据 ----------------

    private void LoadItems()
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

    /// <summary>把「随停靠边变化的布局参数」推给每个图标，避免 XAML 里用 $parent 绑定。</summary>
    private void ApplyItemLayout()
    {
        Orientation orientation = IsVertical ? Orientation.Horizontal : Orientation.Vertical;
        foreach (ItemViewModel item in _allItems)
        {
            item.ShowLabel = Settings.ShowLabels;
            item.TileSize = Settings.TileSize;
            item.ItemOrientation = orientation;
        }
    }

    private void RefreshFilter()
    {
        IReadOnlyList<LauncherItem> models = _allItems.Select(v => v.Model).ToList();
        IReadOnlyList<LauncherItem> filtered = ItemQuery.Filter(models, SearchQuery);
        var keep = new HashSet<string>(filtered.Select(i => i.Id), StringComparer.OrdinalIgnoreCase);

        Items.Clear();
        foreach (ItemViewModel vm in _allItems.Where(v => keep.Contains(v.Id)))
        {
            Items.Add(vm);
        }

        this.RaisePropertyChanged(nameof(IsSearchEmpty));
        this.RaisePropertyChanged(nameof(CountText));
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

        if (!_allItems.Remove(vm))
        {
            return;
        }

        _config.Items.Remove(vm.Model);
        RefreshFilter();
        Save();
        Toast("已移除 " + vm.Name + "（可在设置里重新添加）");
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
            this.RaisePropertyChanged(nameof(IsVertical));
            this.RaisePropertyChanged(nameof(BodyOrientation));
            this.RaisePropertyChanged(nameof(ListOrientation));
            this.RaisePropertyChanged(nameof(ListSpacing));
            this.RaisePropertyChanged(nameof(DividerWidth));
            this.RaisePropertyChanged(nameof(DividerHeight));
            this.RaisePropertyChanged(nameof(ListMaxWidth));
            this.RaisePropertyChanged(nameof(ListMaxHeight));
            this.RaisePropertyChanged(nameof(CollapseGlyph));
            ApplyItemLayout();
        }

        this.RaisePropertyChanged(nameof(ShowLabels));
        this.RaisePropertyChanged(nameof(ThemeName));
        this.RaisePropertyChanged(nameof(StyleName));
        this.RaisePropertyChanged(nameof(AutoStartEnabled));
        this.RaisePropertyChanged(nameof(IsPinned));

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

        Palette palette = (dark ? Palette.Dark : Palette.Light)
            .WithOpacity(Settings.PanelOpacity, flat: Settings.Style == "flat");

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
            UpdateInfo? info = await _updates.CheckAsync(current).ConfigureAwait(true);

            if (info is null)
            {
                Toast("已是最新版本 " + VersionText);
                return;
            }

            _pendingUpdate = info;
            this.RaisePropertyChanged(nameof(UpdateAvailable));
            this.RaisePropertyChanged(nameof(UpdateText));
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

    private void OpenUpdatePage()
    {
        string? url = _pendingUpdate?.AssetUrl ?? _pendingUpdate?.PageUrl;
        if (!string.IsNullOrWhiteSpace(url))
        {
            _host?.OpenUrl(url);
        }
    }

    private void DismissUpdate()
    {
        _pendingUpdate = null;
        this.RaisePropertyChanged(nameof(UpdateAvailable));
        this.RaisePropertyChanged(nameof(UpdateText));
    }
}
