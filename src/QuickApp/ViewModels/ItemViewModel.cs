using System;
using System.Windows.Input;
using Avalonia;
using Avalonia.Layout;
using Avalonia.Media;
using QuickApp.Core.Models;
using QuickApp.Core.Services;
using ReactiveUI;

namespace QuickApp.ViewModels;

/// <summary>
/// Dock 上的一个图标。
/// 尺寸/朝向/占位底色/占位图形这类「随停靠边、设置与图标加载状态变化」的值，
/// 由 DockViewModel 统一算好写进来（见 ApplyItemLayout 与 ItemVisualRequested）：
/// XAML 因此只绑定元素自己的 DataContext，不需要 $parent 语法，也不用在 XAML 里内联图标资源。
/// </summary>
public sealed class ItemViewModel : ViewModelBase
{
    private string? _iconFile;
    private IImage? _icon;
    private IBrush? _iconBrush;
    private IBrush? _ringBrush;
    private IBrush? _textBrush;
    private Geometry? _glyph;
    private bool _isRunning;
    private bool _showLabel = true;
    private bool _showRemove;
    private bool _isRenaming;
    private bool _isDropTarget;
    private bool _isDragged;
    private string _editingName = string.Empty;
    private double _tileSize = 44;
    private Orientation _itemOrientation = Orientation.Vertical;

    public ItemViewModel(
        LauncherItem model,
        Action<ItemViewModel>? activate = null,
        Action<ItemViewModel>? remove = null,
        Action<ItemViewModel>? addToDock = null,
        bool isSystemResult = false)
    {
        Model = model;
        IsSystemResult = isSystemResult;
        ActivateCommand = ReactiveCommand.Create(() => activate?.Invoke(this));
        RemoveCommand = ReactiveCommand.Create(() => remove?.Invoke(this));
        AddToDockCommand = ReactiveCommand.Create(() => addToDock?.Invoke(this));
    }

    public LauncherItem Model { get; }

    /// <summary>搜索结果中的系统应用候选，尚未加入用户配置。</summary>
    public bool IsSystemResult { get; }

    public bool ShowSystemAdd => IsSystemResult;

    public string ResultStatus => IsSystemResult ? "系统已安装 · 未配置" : string.Empty;

    public string Id => Model.Id;

    public ICommand ActivateCommand { get; }

    public ICommand RemoveCommand { get; }

    public ICommand AddToDockCommand { get; }

    public string Name
    {
        get => Model.Name;
        set
        {
            if (string.Equals(Model.Name, value, StringComparison.Ordinal))
            {
                return;
            }

            Model.Name = value;
            this.RaisePropertyChanged();
            this.RaisePropertyChanged(nameof(Tooltip));
            this.RaisePropertyChanged(nameof(Initial));
        }
    }

    public string KindLabel => ItemQuery.KindLabel(Model.Kind);

    public bool IsCommand => Model.Kind == ItemKind.Command;

    public bool IsWeb => Model.Kind == ItemKind.Web;

    /// <summary>占位状态显示的首字（图标还没提取出来时）。</summary>
    public string Initial => string.IsNullOrEmpty(Name) ? "?" : Name[..1].ToUpperInvariant();

    public string TargetSummary => ItemQuery.DescribeTarget(Model);

    public string Tooltip => string.IsNullOrWhiteSpace(TargetSummary) ? Name : Name + Environment.NewLine + TargetSummary;

    /// <summary>图标缓存文件的绝对路径。</summary>
    public string? IconFile
    {
        get => _iconFile;
        set
        {
            if (Set(ref _iconFile, value))
            {
                Icon = LoadBitmap(value);
            }
        }
    }

    /// <summary>已解码的图标位图，直接给 Image.Source 用。</summary>
    public IImage? Icon
    {
        get => _icon;
        private set
        {
            if (Set(ref _icon, value))
            {
                this.RaisePropertyChanged(nameof(HasIcon));
            }
        }
    }

    public bool HasIcon => _icon is not null && string.IsNullOrEmpty(Model.IconKey);

    /// <summary>用户指定的占位图标键（图标选择器写入 Model.IconKey）；为空时显示真实图标。</summary>
    public string? IconKey => Model.IconKey;

    /// <summary>「更换图标」选中占位图标后调用：真实位图让位给所选几何图形。</summary>
    public void ClearIcon()
    {
        Icon = null;
        this.RaisePropertyChanged(nameof(IconKey));
    }

    /// <summary>图标底色（真实图标提取出来之前的占位底），由视图按类型与尺寸算好写入。</summary>
    public IBrush? IconBrush
    {
        get => _iconBrush;
        set => Set(ref _iconBrush, value);
    }

    /// <summary>键盘选中时的描边色，由视图写入（跟随主题）。</summary>
    public IBrush? RingBrush
    {
        get => _ringBrush;
        set => Set(ref _ringBrush, value);
    }

    /// <summary>名称文字颜色，由视图写入。必须显式给：不设会继承 Fluent 主题前景色，系统浅色主题下就是黑字压深底。</summary>
    public IBrush? TextBrush
    {
        get => _textBrush;
        set => Set(ref _textBrush, value);
    }

    /// <summary>没有真实图标时显示的线性图标（按类型给不同图形），由视图写入。</summary>
    public Geometry? Glyph
    {
        get => _glyph;
        set => Set(ref _glyph, value);
    }

    /// <summary>点击后的短暂反馈。</summary>
    public bool IsRunning
    {
        get => _isRunning;
        set => Set(ref _isRunning, value);
    }

    /// <summary>「仅图标 / 图标 + 名称」。切换时 ShowNameText 的绑定也要跟着刷新。</summary>
    public bool ShowLabel
    {
        get => _showLabel;
        set
        {
            if (Set(ref _showLabel, value))
            {
                this.RaisePropertyChanged(nameof(ShowNameText));
            }
        }
    }

    /// <summary>悬停时的位移放大组合（方向背离停靠边），由 DockViewModel 按当前边算好写入。</summary>
    public string HoverTransform { get; set; } = "scale(1.18)";

    /// <summary>按下时的回缩组合（原型 tile:active 的 scale .94），偏移方向与悬停一致。</summary>
    public string PressedTransform { get; set; } = "scale(0.94)";

    /// <summary>拖动排序中：本项是不是正被拖动的那一个（原型 .dragging 的 35% 透明）。</summary>
    public bool IsDragged
    {
        get => _isDragged;
        set => Set(ref _isDragged, value);
    }

    /// <summary>图标排布是否竖向（横排在名称上方）：决定移除按钮放左上还是右上。</summary>
    public bool IsItemVertical => ItemOrientation == Orientation.Vertical;

    /// <summary>上/下 Dock 为了显示 10 项按瓦片宽度截断名称，左/右 Dock 给名称更多空间。</summary>
    public double LabelMaxWidth => IsItemVertical ? TileSize : 108;

    /// <summary>编辑模式下才显示右上角的移除按钮。</summary>
    public bool ShowRemove
    {
        get => _showRemove;
        set => Set(ref _showRemove, value);
    }

    /// <summary>正在就地改名：名称位置换成输入框。</summary>
    public bool IsRenaming
    {
        get => _isRenaming;
        set
        {
            if (Set(ref _isRenaming, value))
            {
                this.RaisePropertyChanged(nameof(ShowNameText));
            }
        }
    }

    /// <summary>名称文本的显隐（改名时隐藏，让位给输入框）。</summary>
    public bool ShowNameText => _showLabel && !_isRenaming;

    /// <summary>改名输入框里的草稿，取消时不会污染 Name。</summary>
    public string EditingName
    {
        get => _editingName;
        set => Set(ref _editingName, value);
    }

    /// <summary>拖动排序时作为落点的插入位置高亮。</summary>
    public bool IsDropTarget
    {
        get => _isDropTarget;
        set => Set(ref _isDropTarget, value);
    }

    public double TileSize
    {
        get => _tileSize;
        set
        {
            if (Set(ref _tileSize, value))
            {
                this.RaisePropertyChanged(nameof(TileRadius));
                this.RaisePropertyChanged(nameof(TileCornerRadius));
                this.RaisePropertyChanged(nameof(IconSize));
                this.RaisePropertyChanged(nameof(LabelMaxWidth));
            }
        }
    }

    public double TileRadius => Math.Round(TileSize * 0.28, 1);

    /// <summary>明确提供 CornerRadius 类型，确保图标四角圆角在 Avalonia 绑定中生效。</summary>
    public CornerRadius TileCornerRadius => new(TileRadius);

    public double IconSize => Math.Round(TileSize * 0.5, 1);

    /// <summary>改名框宽度：瓦片宽 + 70（原型 startRename 的 width 规则，最小 120）。</summary>
    public double RenameWidth => Math.Max(120, Math.Round(TileSize + 70));

    /// <summary>上/下边缘图标在上、名称在下；左/右边缘图标在左、名称在右。</summary>
    public Orientation ItemOrientation
    {
        get => _itemOrientation;
        set
        {
            if (Set(ref _itemOrientation, value))
            {
                this.RaisePropertyChanged(nameof(IsItemVertical));
                this.RaisePropertyChanged(nameof(LabelMaxWidth));
            }
        }
    }

    private static IImage? LoadBitmap(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            return new Avalonia.Media.Imaging.Bitmap(path);
        }
        catch (Exception ex)
        {
            AppLog.Error("解码图标失败：" + path, ex);
            return null;
        }
    }
}
