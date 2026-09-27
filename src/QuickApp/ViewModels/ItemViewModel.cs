using System;
using System.Windows.Input;
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
    private string _editingName = string.Empty;
    private double _tileSize = 44;
    private Orientation _itemOrientation = Orientation.Vertical;

    public ItemViewModel(LauncherItem model, Action<ItemViewModel>? activate = null, Action<ItemViewModel>? remove = null)
    {
        Model = model;
        ActivateCommand = ReactiveCommand.Create(() => activate?.Invoke(this));
        RemoveCommand = ReactiveCommand.Create(() => remove?.Invoke(this));
    }

    public LauncherItem Model { get; }

    public string Id => Model.Id;

    public ICommand ActivateCommand { get; }

    public ICommand RemoveCommand { get; }

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

    public bool HasIcon => _icon is not null;

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

    /// <summary>「仅图标 / 图标 + 名称」。</summary>
    public bool ShowLabel
    {
        get => _showLabel;
        set => Set(ref _showLabel, value);
    }

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
                this.RaisePropertyChanged(nameof(IconSize));
            }
        }
    }

    public double TileRadius => Math.Round(TileSize * 0.28, 1);

    public double IconSize => Math.Round(TileSize * 0.5, 1);

    /// <summary>上/下边缘图标在上、名称在下；左/右边缘图标在左、名称在右。</summary>
    public Orientation ItemOrientation
    {
        get => _itemOrientation;
        set => Set(ref _itemOrientation, value);
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
