using System;
using System.Windows.Input;
using Avalonia.Layout;
using Avalonia.Media;
using QuickApp.Core.Models;
using QuickApp.Core.Services;
using QuickApp.Theme;
using ReactiveUI;

namespace QuickApp.ViewModels;

/// <summary>
/// Dock 上的一个图标。
/// 尺寸/朝向这类「随停靠边与设置变化」的值由 DockViewModel 统一写入（见 ApplyItemLayout），
/// 这样 XAML 里的绑定都指向元素自己的 DataContext，不用 $parent 语法，编译期绑定最稳。
/// </summary>
public sealed class ItemViewModel : ViewModelBase
{
    private string? _iconFile;
    private IImage? _icon;
    private bool _isRunning;
    private bool _showLabel = true;
    private double _tileSize = 44;
    private Orientation _itemOrientation = Orientation.Vertical;

    public ItemViewModel(LauncherItem model, Action<ItemViewModel>? activate = null, Action<ItemViewModel>? remove = null)
    {
        Model = model;
        Placeholder = PaletteBrushes.TilePlaceholder(model.Name);
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

    /// <summary>图标没提取出来时显示的占位底色（按名称哈希，和原型一致）。</summary>
    public IBrush Placeholder { get; }

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
