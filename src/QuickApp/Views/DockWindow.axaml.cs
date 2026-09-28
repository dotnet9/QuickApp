using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using QuickApp.Core.Models;
using QuickApp.Core.Services;
using QuickApp.Platform.Windows;
using QuickApp.Theme;
using QuickApp.ViewModels;

namespace QuickApp.Views;

/// <summary>
/// Dock 主窗口：无边框、置顶、不占任务栏，贴着所选屏幕边缘。
/// 窗口定位、右键菜单、键盘导航、拖动排序这些属于「视图」职责的都在这里；
/// 位置数学在 Core 的 DockPlacement（可单测），顺序与命名落库在 DockViewModel。
/// </summary>
public partial class DockWindow : Window, IDockHost
{
    private const int DockMargin = 10;
    private const int WindowShadowMargin = 12;
    private const int EdgeBand = 6;
    private const double DragThreshold = 5;

    private readonly DispatcherTimer _animator;
    private readonly DispatcherTimer _hideTimer;
    private readonly DispatcherTimer _edgeTimer;
    private DockViewModel? _vm;
    private bool _isHidden;
    private bool _isMoreMenuOpen;
    private double _progress;

    private ItemViewModel? _focused;
    private ItemViewModel? _dragItem;
    private ItemViewModel? _dropTarget;
    private Border? _pressedTile;
    private Point _dragPressPosition;
    private bool _dragStarted;

    // 面板空白处拖动吸附：窗口跟手，光标最近边显示吸附提示，松手落边
    private bool _dockDragging;
    private bool _dockDragStarted;
    private PixelPoint _dragCursorOrigin;
    private PixelPoint _dragWindowOrigin;
    private DockEdge _dragEdge;
    private int _dragMonitorIndex = -1;
    private Window? _snapChip;
    private TextBlock? _snapChipText;

    public DockWindow()
    {
        InitializeComponent();

        // 透明级别拿不到时退成全透明底，避免出现白色/黑色矩形包住圆角面板
        TransparencyBackgroundFallback = Brushes.Transparent;

        _animator = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _animator.Tick += (_, _) => StepAnimation();

        _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            if (_vm is not null && !_dockDragging && !_vm.IsPointerOver && !_vm.IsPinned && !_vm.IsSearchOpen && !_vm.IsEditMode && !_isMoreMenuOpen)
            {
                _vm.IsDockVisible = false;
            }
        };

        // 「触边滑出」：收起后用 150ms 的光标轮询判断是否贴边，
        // 比再开一个置顶透明热区窗口简单，也不会抢焦点。
        _edgeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _edgeTimer.Tick += (_, _) => CheckEdgeReveal();

        // 内容尺寸变化会经 SizeToContent 反映到窗口尺寸上，SizeChanged 已覆盖重定位
        SizeChanged += OnWindowResized;
        // Avalonia 12 里透明级别变化走依赖属性（旧版的 TransparencyLevelChanged 事件没了）
        PropertyChanged += (_, e) =>
        {
            if (e.Property == ActualTransparencyLevelProperty)
            {
                ReportTransparencyLevel();
            }
        };
    }

    public event EventHandler? SettingsRequested;

    public event EventHandler? AboutRequested;

    public void Attach(DockViewModel viewModel)
    {
        _vm = viewModel;
        DataContext = viewModel;
        viewModel.AttachHost(this);
        viewModel.DockVisibilityChanged += (_, visible) => SetDockVisible(visible, animate: true);
        viewModel.PointerOverChanged += (_, over) => OnPointerOverChanged(over);
        viewModel.Items.CollectionChanged += OnItemsChanged;
        viewModel.InstalledItems.CollectionChanged += OnItemsChanged;
        viewModel.ItemVisualRequested += ApplyItemVisual;
        viewModel.ContextRequested += ShowContextMenu;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;

        BuildActionIcons();

        // 在面板隧道阶段识别空白区域拖动；子控件的交互仍由各自处理器接管。
        Panel.AddHandler(PointerPressedEvent, OnPanelPointerPressed, RoutingStrategies.Tunnel);
        Panel.AddHandler(PointerMovedEvent, OnPanelPointerMoved, RoutingStrategies.Tunnel);
        Panel.AddHandler(PointerReleasedEvent, OnPanelPointerReleased, RoutingStrategies.Tunnel);
        Panel.AddHandler(PointerCaptureLostEvent, OnPanelPointerCaptureLost, RoutingStrategies.Tunnel);

        AddButton.Click += OnAddClicked;
        MoreButton.Click += OnMoreClicked;

        // 图标区交互：左键执行、右键菜单、编辑模式拖动排序、双击改名、键盘导航
        ItemsHost.AddHandler(PointerPressedEvent, OnItemsPointerPressed, RoutingStrategies.Tunnel);
        ItemsHost.AddHandler(PointerMovedEvent, OnItemsPointerMoved, RoutingStrategies.Tunnel);
        ItemsHost.AddHandler(PointerReleasedEvent, OnItemsPointerReleased, RoutingStrategies.Tunnel);
        ItemsHost.AddHandler(PointerCaptureLostEvent, OnItemsPointerCaptureLost, RoutingStrategies.Tunnel);
        ItemsHost.AddHandler(DoubleTappedEvent, OnItemsDoubleTapped, RoutingStrategies.Bubble);
        ItemsHost.AddHandler(KeyDownEvent, OnItemsKeyDown, RoutingStrategies.Tunnel);
        ItemsHost.AddHandler(KeyUpEvent, OnRenameBoxKeyUp, RoutingStrategies.Tunnel);
        ItemsHost.AddHandler(LostFocusEvent, OnRenameBoxLostFocus, RoutingStrategies.Bubble);
        ItemsHost.AddHandler(ContextRequestedEvent, OnContextRequested, RoutingStrategies.Tunnel);

        // 滚动 chrome（原型：滚轮横扫、两端渐隐、指示条）
        ItemsScroll.PointerWheelChanged += OnScrollWheel;
        ItemsScroll.ScrollChanged += OnScrollChanged;

        // 搜索框：回车直接运行第一个匹配项
        SearchBox.KeyDown += OnSearchBoxKeyDown;

        Opened += (_, _) =>
        {
            ApplyEdge(viewModel.Settings.Edge);
            UpdatePinState();
            ReportTransparencyLevel();
            UpdateScrollChromeLayout();
            _edgeTimer.Start();
        };

        Closed += (_, _) => _edgeTimer.Stop();
    }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        ScheduleReposition();
        UpdateScrollChrome();
    }
    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(DockViewModel.IsDockVisible):
            case nameof(DockViewModel.UpdateAvailable):
                ScheduleReposition();
                break;

            case nameof(DockViewModel.IsSearchOpen):
            case nameof(DockViewModel.IsEditMode):
            case nameof(DockViewModel.IsSearchEmpty):
            case nameof(DockViewModel.HasConfiguredResults):
            case nameof(DockViewModel.HasInstalledResults):
            case nameof(DockViewModel.ConfiguredGroupTitle):
            case nameof(DockViewModel.InstalledGroupTitle):
                // 关闭搜索/编辑后若鼠标不在 Dock 上，重新进入自动隐藏倒计时
                ScheduleReposition();
                UpdateScrollChrome();
                ScheduleAutoHide();
                if (e.PropertyName == nameof(DockViewModel.IsSearchOpen) && _vm?.IsSearchOpen == true)
                {
                    FocusSearchBox();
                }
                break;

            case nameof(DockViewModel.IsPinned):
                UpdatePinState();
                ScheduleAutoHide();
                break;

            case nameof(DockViewModel.StatusMessage):
                UpdateToast();
                break;

            case nameof(DockViewModel.PanelBrush):
                ApplyTransparency();
                UpdateFadeBrushes();
                // 图标由 Path 自绘，Stroke 不会继承 Button.Foreground；切换主题时重建，保持颜色同步。
                BuildActionIcons();
                UpdatePinState();
                break;
        }
    }

    // ---------------- 滚动 chrome（原型 .fade / .scroll-indicator） ----------------

    private void OnScrollWheel(object? sender, PointerWheelEventArgs e)
    {
        if (_vm is null)
        {
            return;
        }

        Vector offset = ItemsScroll.Offset;

        if (_vm.IsVertical)
        {
            ItemsScroll.Offset = offset.WithY(offset.Y - e.Delta.Y * 48);
        }
        else
        {
            // 横向 Dock：滚轮（含触摸板横扫）都转成水平滚动
            double horizontalDelta = Math.Abs(e.Delta.X) > Math.Abs(e.Delta.Y) ? e.Delta.X : e.Delta.Y;
            ItemsScroll.Offset = offset.WithX(offset.X - horizontalDelta * 48);
        }

        e.Handled = true;
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e) => UpdateScrollChrome();

    private void UpdateScrollChrome()
    {
        if (_vm is null)
        {
            return;
        }

        Vector offset = ItemsScroll.Offset;
        Size extent = ItemsScroll.Extent;
        Size viewport = ItemsScroll.Viewport;
        bool vertical = _vm.IsVertical;
        bool canScroll = vertical ? extent.Height > viewport.Height + 2 : extent.Width > viewport.Width + 2;
        double maxOffset = canScroll
            ? (vertical ? extent.Height - viewport.Height : extent.Width - viewport.Width)
            : 0;

        ScrollIndicator.IsVisible = canScroll;
        FadeStart.IsVisible = canScroll && (vertical ? offset.Y : offset.X) > 2;
        FadeEnd.IsVisible = canScroll && (vertical ? offset.Y : offset.X) < maxOffset - 2;

        if (!canScroll)
        {
            return;
        }

        double track = vertical ? ScrollArea.Bounds.Height - 48 : ScrollArea.Bounds.Width - 48;
        double ratio = vertical ? viewport.Height / extent.Height : viewport.Width / extent.Width;
        double thumb = Math.Max(14, track * ratio);
        double progress = (vertical ? offset.Y : offset.X) / maxOffset;

        if (vertical)
        {
            ScrollIndicator.Width = 2;
            ScrollIndicator.HorizontalAlignment = HorizontalAlignment.Right;
            ScrollIndicator.VerticalAlignment = VerticalAlignment.Stretch;
            ScrollIndicator.Margin = new Thickness(0, 24, 5, 24);
            ScrollThumb.Width = double.NaN;
            ScrollThumb.HorizontalAlignment = HorizontalAlignment.Stretch;
            ScrollThumb.Height = thumb;
            ScrollThumb.VerticalAlignment = VerticalAlignment.Top;
            ScrollThumb.Margin = new Thickness(0, progress * (track - thumb), 0, 0);
        }
        else
        {
            ScrollIndicator.Height = 2;
            ScrollIndicator.HorizontalAlignment = HorizontalAlignment.Stretch;
            ScrollIndicator.VerticalAlignment = VerticalAlignment.Bottom;
            ScrollIndicator.Margin = new Thickness(24, 0, 24, 4);
            ScrollThumb.Height = double.NaN;
            ScrollThumb.VerticalAlignment = VerticalAlignment.Stretch;
            ScrollThumb.Width = thumb;
            ScrollThumb.HorizontalAlignment = HorizontalAlignment.Left;
            ScrollThumb.Margin = new Thickness(progress * (track - thumb), 0, 0, 0);
        }
    }

    /// <summary>渐隐层的尺寸与朝向随停靠边重算（面板换色时画刷单独更新）。</summary>
    private void UpdateScrollChromeLayout()
    {
        bool vertical = _vm?.IsVertical ?? false;
        const double fadeLength = 22;

        if (vertical)
        {
            FadeStart.HorizontalAlignment = HorizontalAlignment.Stretch;
            FadeStart.VerticalAlignment = VerticalAlignment.Top;
            FadeStart.Height = fadeLength;
            FadeStart.Width = double.NaN;
            FadeEnd.HorizontalAlignment = HorizontalAlignment.Stretch;
            FadeEnd.VerticalAlignment = VerticalAlignment.Bottom;
            FadeEnd.Height = fadeLength;
            FadeEnd.Width = double.NaN;
        }
        else
        {
            FadeStart.VerticalAlignment = VerticalAlignment.Stretch;
            FadeStart.HorizontalAlignment = HorizontalAlignment.Left;
            FadeStart.Width = fadeLength;
            FadeStart.Height = double.NaN;
            FadeEnd.VerticalAlignment = VerticalAlignment.Stretch;
            FadeEnd.HorizontalAlignment = HorizontalAlignment.Right;
            FadeEnd.Width = fadeLength;
            FadeEnd.Height = double.NaN;
        }

        UpdateFadeBrushes();
    }

    /// <summary>渐隐画刷：用低透明度面板色提示两端仍有应用，方向随停靠边。</summary>
    private void UpdateFadeBrushes()
    {
        if (_vm is null || _vm.SettingsBackgroundBrush is not SolidColorBrush surface)
        {
            return;
        }

        Color c = surface.Color;
        Color from = Color.FromArgb(96, c.R, c.G, c.B);
        Color to = Color.FromArgb(0, c.R, c.G, c.B);

        bool vertical = _vm.IsVertical;
        FadeStart.Background = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(vertical ? 0 : 1, vertical ? 1 : 0, RelativeUnit.Relative),
            GradientStops = { new GradientStop(from, 0), new GradientStop(to, 1) }
        };
        FadeEnd.Background = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(vertical ? 0 : 1, vertical ? 1 : 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            GradientStops = { new GradientStop(from, 0), new GradientStop(to, 1) }
        };
    }

    // ---------------- 悬浮 Toast（原型 .toast） ----------------

    private ToastWindow? _toast;

    private void UpdateToast()
    {
        string? message = _vm?.StatusMessage;
        if (string.IsNullOrEmpty(message))
        {
            _toast?.HideToast();
            return;
        }

        _toast ??= new ToastWindow();
        _toast.ShowToast(message, _vm?.ToastActionLabel, _vm?.ToastActionCommand);
    }

    /// <summary>钉住时给图钉按钮一个强调色激活态（对应原型 icon-btn.active）。</summary>
    private void UpdatePinState()
    {
        bool pinned = _vm?.IsPinned ?? false;
        if (pinned)
        {
            PinButton.Classes.Add("active");
        }
        else
        {
            PinButton.Classes.Remove("active");
        }

        if (PinButton.Content is Path path)
        {
            path.Stroke = pinned
                ? _vm?.AccentBrush ?? Brushes.DodgerBlue
                : _vm?.TextDimBrush ?? Brushes.Gray;
        }
    }

    /// <summary>
    /// 窗体恒定逐像素透明：面板圆角外完全透明（真正的异形窗），投影画在透明边距里与桌面混合。
    /// 不再申请系统 acrylic——它是整窗矩形，会在面板外面留下一圈模糊「小边框」，圆角也被吃掉。
    /// </summary>
    private void ApplyTransparency()
    {
        try
        {
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        }
        catch (Exception ex)
        {
            AppLog.Error("设置窗口透明度失败", ex);
        }
    }

    /// <summary>实际拿到的透明级别决定毛玻璃是否退化（对应原型 @supports 回退规则）。</summary>
    private void ReportTransparencyLevel()
    {
        try
        {
            WindowTransparencyLevel actual = ActualTransparencyLevel;
            AppLog.Info("窗口透明级别：" + actual);
            _vm?.SetGlassDegraded(actual == WindowTransparencyLevel.None);
        }
        catch (Exception ex)
        {
            AppLog.Error("读取窗口透明级别失败", ex);
        }
    }

    // ---------------- 图标与占位 ----------------

    private void BuildActionIcons()
    {
        // 原型中的紧凑工具区使用高对比度图标，采用正文色（深色主题下接近白色），避免过暗。
        IBrush color = _vm?.TextBrush ?? Brushes.White;
        SetIcon(SearchButton, Icons.Search, 15, color);
        SetIcon(AddButton, Icons.Plus, 16, color);
        SetIcon(MoreButton, Icons.More, 16, color);
        if (MoreButton.Content is Path morePath)
        {
            morePath.Fill = color;
            morePath.StrokeThickness = 0;
        }
        SetIcon(PinButton, Icons.Pin, 15, color);
        SetIcon(CollapseButton, Icons.ChevronUp, 14, color);

        UpdateCollapseIcon();
    }

    private static void SetIcon(Button button, Geometry geometry, double size, IBrush stroke)
    {
        button.Content = new Path
        {
            Data = geometry,
            Stroke = stroke,
            StrokeThickness = 1.7,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round,
            Stretch = Stretch.Uniform,
            Width = size,
            Height = size
        };
    }

    private void UpdateCollapseIcon()
    {
        Geometry geometry = _vm?.Settings.Edge switch
        {
            DockEdge.Bottom => Icons.ChevronDown,
            DockEdge.Left => Icons.ChevronLeft,
            DockEdge.Right => Icons.ChevronRight,
            _ => Icons.ChevronUp
        };

        if (CollapseButton.Content is Path path)
        {
            path.Data = geometry;
        }
    }

    /// <summary>给一个图标算占位底色与占位图形；已经有真实图标时把图形清掉。</summary>
    private void ApplyItemVisual(ItemViewModel item)
    {
        item.IconBrush = PaletteBrushes.TileBrush(item.Name, item.Model.Kind, item.TileSize);
        item.RingBrush = ReferenceEquals(item, _focused) ? _vm?.AccentBrush : null;
        item.TextBrush = _vm?.TextBrush;

        // 手动挑过的占位图标优先于真实图标（原型「更换图标」的语义）
        if (item.Model.IconKey is { } key)
        {
            IconCatalog.TryResolve(key, out Geometry picked);
            item.Glyph = picked;
            return;
        }

        if (item.HasIcon)
        {
            item.Glyph = null;
            return;
        }

        item.Glyph = item.IsCommand ? Icons.Terminal : item.IsWeb ? Icons.Globe : Icons.Application;
    }

    // ---------------- 图标区交互 ----------------

    private ItemViewModel? ItemOf(object? source)
    {
        Border? border = (source as Visual)?.GetSelfAndVisualAncestors()
            .OfType<Border>()
            .FirstOrDefault(b => b.Classes.Contains("tile"));

        return border?.DataContext as ItemViewModel;
    }

    private void OnInstalledResultPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_vm is null
            || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
            || (e.Source as Visual)?.GetSelfAndVisualAncestors().OfType<Button>().Any() == true)
        {
            return;
        }

        ItemViewModel? item = (e.Source as Visual)?.GetSelfAndVisualAncestors()
            .Select(visual => visual.DataContext)
            .OfType<ItemViewModel>()
            .FirstOrDefault(candidate => candidate.IsSystemResult);

        if (item is null)
        {
            return;
        }

        _vm.RunCommand.Execute(item);
        e.Handled = true;
    }

    private static Border? TileOf(object? source)
        => (source as Visual)?.GetSelfAndVisualAncestors()
            .OfType<Border>()
            .FirstOrDefault(b => b.Classes.Contains("tile"));

    private void SetFocused(ItemViewModel? item)
    {
        if (ReferenceEquals(_focused, item))
        {
            return;
        }

        if (_focused is not null)
        {
            _focused.RingBrush = null;
        }

        _focused = item;

        if (_focused is not null)
        {
            _focused.RingBrush = _vm?.AccentBrush;
        }
    }

    private void OnItemsPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_vm is null)
        {
            return;
        }

        // 点在 × 移除按钮上：放行给按钮，否则隧道里先手会把点击吞成拖动
        if ((e.Source as Visual)?.GetSelfAndVisualAncestors().OfType<Button>().Any() == true)
        {
            return;
        }

        ItemViewModel? item = ItemOf(e.Source);
        bool right = e.GetCurrentPoint(this).Properties.IsRightButtonPressed;

        if (item is null)
        {
            SetFocused(null);
            ClearPressedTile();
            if (right)
            {
                _vm.RequestContext(null);
                e.Handled = true;
            }

            return;
        }

        SetFocused(item);

        if (right)
        {
            _vm.RequestContext(item);
            e.Handled = true;
            return;
        }

        // 按下回缩反馈（原型 tile:active 的 scale .94），松开或拖走后移除
        if (TileOf(e.Source) is { } pressedTile)
        {
            _pressedTile = pressedTile;
            _pressedTile.Classes.Add("pressed");
        }

        if (_vm.IsEditMode)
        {
            // 编辑模式：左键按住 = 准备拖动排序。
            // 不置 Handled、不立即捕获指针——捕获会把后续命中的 Source 重定向到 ItemsHost，
            // 双击手势的 DoubleTapped 就找不到图标了；位移超过阈值才真正进入拖动并捕获。
            _dragItem = item;
            _dragPressPosition = e.GetPosition(ItemsHost);
            _dragStarted = false;
            return;
        }

        _vm.RunCommand.Execute(item);
        e.Handled = true;
    }

    private void OnItemsPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_vm is null || _dragItem is null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        // 位移超过阈值才进入拖动，把「按下-松手」留给双击改名
        if (!_dragStarted)
        {
            Point position = e.GetPosition(ItemsHost);
            double distance = Math.Abs(position.X - _dragPressPosition.X) + Math.Abs(position.Y - _dragPressPosition.Y);
            if (distance < DragThreshold)
            {
                return;
            }

            _dragStarted = true;
            e.Pointer.Capture(ItemsHost);
            ClearPressedTile();
            _dragItem.IsDragged = true;
        }

        Panel? panel = ItemsHost.ItemsPanelRoot;
        if (panel is null || panel.Children.Count == 0)
        {
            return;
        }

        Point dropPosition = e.GetPosition(panel);
        bool horizontal = _vm.ListOrientation == Orientation.Horizontal;
        double pointer = horizontal ? dropPosition.X : dropPosition.Y;

        ItemViewModel? target = null;
        foreach (Control child in panel.Children)
        {
            double start = horizontal ? child.Bounds.X : child.Bounds.Y;
            double middle = start + (horizontal ? child.Bounds.Width : child.Bounds.Height) / 2;
            if (pointer < middle)
            {
                target = child.DataContext as ItemViewModel;
                break;
            }
        }

        if (!ReferenceEquals(_dropTarget, target))
        {
            if (_dropTarget is not null)
            {
                _dropTarget.IsDropTarget = false;
                ApplyItemVisual(_dropTarget);
            }

            _dropTarget = target;
            if (_dropTarget is not null)
            {
                _dropTarget.IsDropTarget = true;
                // 落点用强调色描边提示（对应原型拖拽插入线）
                _dropTarget.RingBrush = _vm.AccentBrush;
            }
        }

        e.Handled = true;
    }

    private void OnItemsPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        ClearPressedTile();

        if (_vm is null || _dragItem is null)
        {
            return;
        }

        ItemViewModel dragged = _dragItem;
        ItemViewModel? target = _dropTarget;

        _dragItem = null;
        _dropTarget = null;
        _dragStarted = false;
        dragged.IsDragged = false;
        e.Pointer.Capture(null);

        if (!ReferenceEquals(target, dragged) && target is not null)
        {
            target.IsDropTarget = false;
            ApplyItemVisual(target);
            _vm.Reorder(dragged.Id, target.Id);
        }
    }

    private void OnItemsPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        ClearPressedTile();

        if (_dropTarget is not null)
        {
            _dropTarget.IsDropTarget = false;
            ApplyItemVisual(_dropTarget);
        }

        _dragItem?.IsDragged = false;
        _dragItem = null;
        _dropTarget = null;
        _dragStarted = false;
    }

    /// <summary>移除按下回缩态（按下另一项、松开、拖动开始时都会调用）。</summary>
    private void ClearPressedTile()
    {
        if (_pressedTile is not null)
        {
            _pressedTile.Classes.Remove("pressed");
            _pressedTile = null;
        }
    }

    private void OnItemsDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_vm is null || !_vm.IsEditMode)
        {
            return;
        }

        // 双击进入改名，同时取消可能已开始的拖动
        _dragItem = null;
        _dropTarget = null;
        _dragStarted = false;

        ItemViewModel? item = ItemOf(e.Source);
        if (item is not null)
        {
            _vm.BeginRename(item);
            FocusRenameBox();
            e.Handled = true;
        }
    }

    private void FocusRenameBox() => Dispatcher.UIThread.Post(() =>
    {
        TextBox? box = ItemsHost.GetVisualDescendants().OfType<TextBox>()
            .FirstOrDefault(t => t.Classes.Contains("rename") && t.IsVisible);
        box?.Focus();
        box?.SelectAll();
    }, DispatcherPriority.Input);

    private void OnRenameBoxKeyUp(object? sender, KeyEventArgs e)
    {
        if (_vm is null || e.Source is not TextBox box || !box.Classes.Contains("rename"))
        {
            return;
        }

        if (box.DataContext is not ItemViewModel item)
        {
            return;
        }

        if (e.Key == Key.Enter)
        {
            _vm.CommitRename(item, save: true);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            _vm.CommitRename(item, save: false);
            e.Handled = true;
        }
    }

    private void OnRenameBoxLostFocus(object? sender, RoutedEventArgs e)
    {
        if (_vm is null || e.Source is not TextBox box || !box.Classes.Contains("rename"))
        {
            return;
        }

        if (box.DataContext is ItemViewModel item)
        {
            _vm.CommitRename(item, save: true);
        }
    }

    private void OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (_vm is null)
        {
            return;
        }

        _vm.RequestContext(ItemOf(e.Source));
        e.Handled = true;
    }

    // ---------------- 键盘导航 ----------------

    private void OnSearchBoxKeyDown(object? sender, KeyEventArgs e)
    {
        if (_vm is null)
        {
            return;
        }

        if (e.Key == Key.Enter && _vm.Items.Count > 0)
        {
            _vm.RunCommand.Execute(_vm.Items[0]);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && _vm.InstalledItems.Count > 0)
        {
            _vm.RunCommand.Execute(_vm.InstalledItems[0]);
            e.Handled = true;
        }
    }

    private void MoveFocus(int step)
    {
        if (_vm is null || _vm.Items.Count == 0)
        {
            return;
        }

        int index = _focused is null ? -1 : _vm.Items.IndexOf(_focused);
        index = index < 0
            ? (step > 0 ? 0 : _vm.Items.Count - 1)
            : (index + step + _vm.Items.Count) % _vm.Items.Count;

        SetFocused(_vm.Items[index]);
    }

    private void OnItemsKeyDown(object? sender, KeyEventArgs e)
    {
        if (_vm is null || e.Source is TextBox)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Left:
            case Key.Up:
                MoveFocus(-1);
                e.Handled = true;
                break;

            case Key.Right:
            case Key.Down:
                MoveFocus(1);
                e.Handled = true;
                break;

            case Key.Enter:
            case Key.Space:
                if (_focused is not null)
                {
                    _vm.RunCommand.Execute(_focused);
                }

                e.Handled = true;
                break;
        }
    }

    // ---------------- 右键菜单 ----------------

    private void ShowContextMenu(ItemViewModel? item)
    {
        if (_vm is null)
        {
            return;
        }

        var menu = new ContextMenu();

        if (item is null)
        {
            menu.Items.Add(MenuEntry("添加文件…", () => _ = AddFilesAsync()));
            menu.Items.Add(MenuEntry("添加命令行…", () => _ = AddCommandAsync()));
            menu.Items.Add(MenuEntry("进入编辑模式", () => _vm.ToggleEditCommand.Execute(null)));
            menu.Items.Add(new Separator());

            foreach (DockEdge edge in new[] { DockEdge.Top, DockEdge.Bottom, DockEdge.Left, DockEdge.Right })
            {
                DockEdge captured = edge;
                bool current = _vm.Settings.Edge == captured;
                menu.Items.Add(MenuEntry(
                    "停靠到" + DockPlacement.Label(captured) + "边缘" + (current ? "（当前）" : string.Empty),
                    () => _vm.SetEdgeCommand.Execute(captured),
                    enabled: !current));
            }

            menu.Items.Add(new Separator());
            menu.Items.Add(MenuEntry("设置…", ShowSettings));
            menu.Items.Add(MenuEntry("退出", Exit));
        }
        else
        {
            ItemViewModel captured = item;
            menu.Items.Add(MenuEntry("运行", () => _vm.RunCommand.Execute(captured)));
            menu.Items.Add(new Separator());
            menu.Items.Add(MenuEntry("编辑名称", () =>
            {
                _vm.BeginRename(captured);
                FocusRenameBox();
            }));
            menu.Items.Add(MenuEntry("更换图标", () => Dispatcher.UIThread.Post(() => OpenIconPicker(captured))));
            menu.Items.Add(MenuEntry("复制路径", () => CopyToClipboard(captured.Model.Target)));
            menu.Items.Add(MenuEntry("复制为命令", () => CopyToClipboard(ItemQuery.ToCommandText(captured.Model))));
            menu.Items.Add(MenuEntry("在资源管理器中显示", () => RevealInExplorer(captured.Model.Target)));
            menu.Items.Add(new Separator());
            menu.Items.Add(MenuEntry("从 Dock 移除", () => _vm.RemoveCommand.Execute(captured)));
        }

        menu.PlacementTarget = ItemsHost;
        menu.Placement = PlacementMode.Pointer;
        menu.Open(ItemsHost);
    }

    private static MenuItem MenuEntry(string header, Action action, bool enabled = true)
    {
        var entry = new MenuItem { Header = header, IsEnabled = enabled };
        entry.Click += (_, _) => action();
        return entry;
    }

    /// <summary>「更换图标」：指针处弹出图标网格，选中即写入 IconKey（原型的 icon-picker）。</summary>
    private void OpenIconPicker(ItemViewModel item)
    {
        if (_vm is null)
        {
            return;
        }

        var grid = new WrapPanel { ItemWidth = 38, ItemHeight = 38 };
        foreach (string key in IconCatalog.PickerKeys)
        {
            string captured = key;
            IconCatalog.TryResolve(captured, out Geometry geometry);
            var glyph = new Path
            {
                Data = geometry,
                Stroke = _vm.TextBrush,
                StrokeThickness = 1.7,
                StrokeLineCap = PenLineCap.Round,
                StrokeJoin = PenLineJoin.Round,
                Stretch = Stretch.Uniform,
                Width = 18,
                Height = 18
            };
            var button = new Button
            {
                Content = glyph,
                Width = 36,
                Height = 36,
                Padding = new Thickness(0),
                Background = Brushes.Transparent,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            ToolTip.SetTip(button, captured);
            button.Click += (_, _) => _vm.ChangeIcon(item, captured);
            grid.Children.Add(button);
        }

        var flyout = new Flyout
        {
            Placement = PlacementMode.Pointer,
            Content = new Border
            {
                Background = _vm.MenuBrush,
                BorderBrush = _vm.PanelBorderBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(8),
                Child = grid
            }
        };
        flyout.ShowAt(ItemsHost);
    }

    private void CopyToClipboard(string text)
    {
        IntPtr handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (ClipboardInterop.TrySetText(text, handle))
        {
            _vm?.Toast("已复制：" + text);
            return;
        }

        _vm?.Toast("复制失败，剪贴板被其它程序占用");
    }

    private void RevealInExplorer(string target)
    {
        try
        {
            if (!System.IO.File.Exists(target))
            {
                _vm?.Toast("找不到该文件：" + target);
                return;
            }

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = "/select,\"" + target + "\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            AppLog.Error("在资源管理器中显示失败", ex);
        }
    }

    // ---------------- 面板空白处拖动吸附四边 ----------------

    private void OnPanelPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_vm is null
            || !e.GetCurrentPoint(Panel).Properties.IsLeftButtonPressed
            || IsInteractiveDockTarget(e.Source))
        {
            return;
        }

        if (!NativeMethods.GetCursorPos(out NativeMethods.POINT cursor))
        {
            return;
        }

        _dragCursorOrigin = new PixelPoint(cursor.X, cursor.Y);
        _dragWindowOrigin = Position;
        _dragEdge = _vm.Settings.Edge;
        _dragMonitorIndex = _vm.Settings.MonitorIndex;
        _dockDragging = true;
        _dockDragStarted = false;
        _hideTimer.Stop();
        e.Pointer.Capture(Panel);
        e.Handled = true;
    }

    private void OnPanelPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_dockDragging || !e.GetCurrentPoint(Panel).Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (!NativeMethods.GetCursorPos(out NativeMethods.POINT cursor))
        {
            return;
        }

        var pointer = new PixelPoint(cursor.X, cursor.Y);
        if (!_dockDragStarted)
        {
            int distance = Math.Abs(pointer.X - _dragCursorOrigin.X) + Math.Abs(pointer.Y - _dragCursorOrigin.Y);
            if (distance < DragThreshold)
            {
                return;
            }

            _dockDragStarted = true;
            ShowSnapChip(_dragCursorOrigin);
        }

        Position = new PixelPoint(
            _dragWindowOrigin.X + pointer.X - _dragCursorOrigin.X,
            _dragWindowOrigin.Y + pointer.Y - _dragCursorOrigin.Y);

        _dragEdge = ResolveNearestEdge(pointer);
        ShowSnapChip(pointer);
        e.Handled = true;
    }

    private void OnPanelPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_dockDragging)
        {
            return;
        }

        bool moved = _dockDragStarted;
        _dockDragging = false;
        _dockDragStarted = false;
        e.Pointer.Capture(null);
        e.Handled = true;
        if (moved)
        {
            FinishDockDrag();
        }
    }

    private void OnPanelPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (!_dockDragging)
        {
            return;
        }

        bool moved = _dockDragStarted;
        _dockDragging = false;
        _dockDragStarted = false;
        if (moved)
        {
            FinishDockDrag();
        }
        else
        {
            HideSnapChip();
        }
    }

    private bool IsInteractiveDockTarget(object? source)
    {
        if (source is not Visual visual)
        {
            return true;
        }

        foreach (Visual ancestor in visual.GetSelfAndVisualAncestors())
        {
            if (ancestor is Button or TextBox or TextBlock or Path or Image or ScrollBar
                || ReferenceEquals(ancestor, ItemsHost)
                || ReferenceEquals(ancestor, InstalledItemsHost))
            {
                return true;
            }

            if (ReferenceEquals(ancestor, Panel))
            {
                break;
            }
        }

        return false;
    }

    /// <summary>松手：吸附到拖动中判定的最近边；与当前边相同则只是把窗口摆回去。</summary>
    private void FinishDockDrag()
    {
        _dockDragging = false;
        _dockDragStarted = false;
        HideSnapChip();

        if (_vm is null)
        {
            return;
        }

        DockEdge edge = _dragEdge;
        bool changed = _vm.Settings.Edge != edge;
        bool monitorChanged = _vm.Settings.MonitorIndex != _dragMonitorIndex;
        // Set the destination screen before SetEdge saves and schedules the final placement.
        _vm.Settings.MonitorIndex = _dragMonitorIndex;
        _vm.SetEdgeCommand.Execute(edge);
        if (!changed)
        {
            if (monitorChanged)
            {
                _vm.Save();
            }

            _vm.Toast("Dock 回到" + DockPlacement.Label(edge) + "边缘");
            ScheduleReposition();
        }
    }

    /// <summary>按窗口中心落在光标所在屏幕工作区的哪条边附近判定吸附边。</summary>
    private DockEdge ResolveNearestEdge(PixelPoint cursor)
    {
        if (_vm is null)
        {
            return DockEdge.Top;
        }

        (PixelRect Work, double Scaling, int MonitorIndex)? screen = null;
        try
        {
            if (Screens.ScreenFromPoint(cursor) is { } byPoint)
            {
                screen = (byPoint.WorkingArea, byPoint.Scaling, ResolveMonitorIndex(byPoint.Bounds));
            }
            else if (_dragMonitorIndex >= 0 && _dragMonitorIndex < Screens.All.Count)
            {
                var previous = Screens.All[_dragMonitorIndex];
                screen = (previous.WorkingArea, previous.Scaling, _dragMonitorIndex);
            }
            else if (Screens.Primary is { } primary)
            {
                screen = (primary.WorkingArea, primary.Scaling, ResolveMonitorIndex(primary.Bounds));
            }
        }
        catch
        {
            screen = null;
        }

        if (screen is null)
        {
            return _vm.Settings.Edge;
        }

        PixelRect work = screen.Value.Work;
        _dragMonitorIndex = screen.Value.MonitorIndex;
        double scaling = screen.Value.Scaling <= 0 ? 1 : screen.Value.Scaling;
        var center = new PixelPoint(
            Position.X + (int)(ClientSize.Width * scaling / 2),
            Position.Y + (int)(ClientSize.Height * scaling / 2));

        return DockPlacement.NearestEdge(center.X, center.Y, work.X, work.Y, work.Width, work.Height);
    }

    private int ResolveMonitorIndex(PixelRect bounds)
    {
        if (Screens.Primary is { } primary && primary.Bounds.Equals(bounds))
        {
            return -1;
        }

        for (int index = 0; index < Screens.All.Count; index++)
        {
            if (Screens.All[index].Bounds.Equals(bounds))
            {
                return index;
            }
        }

        return _vm?.Settings.MonitorIndex ?? -1;
    }

    private void ShowSnapChip(PixelPoint cursor)
    {
        if (_vm is null)
        {
            return;
        }

        if (_snapChip is null)
        {
            _snapChipText = new TextBlock { FontSize = 11, FontWeight = FontWeight.Medium };
            var border = new Border
            {
                Background = _vm.AccentBrush,
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(10, 3),
                Child = _snapChipText
            };

            _snapChip = new Window
            {
                WindowDecorations = WindowDecorations.None,
                ShowInTaskbar = false,
                ShowActivated = false,
                CanResize = false,
                Topmost = true,
                Background = Brushes.Transparent,
                TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent },
                SizeToContent = SizeToContent.WidthAndHeight,
                Content = border
            };
        }

        // 先定位再显示，避免窗口在默认位置闪一帧
        _snapChipText!.Text = "吸附到" + DockPlacement.Label(_dragEdge) + "边缘";
        _snapChip.Position = new PixelPoint(cursor.X + 14, Math.Max(4, cursor.Y - 40));
        if (!_snapChip.IsVisible)
        {
            _snapChip.Show();
        }
    }

    private void HideSnapChip()
    {
        if (_snapChip is not null)
        {
            _snapChip.IsVisible = false;
        }
    }

    // ---------------- 显隐 ----------------

    private void OnPointerOverChanged(bool over)
    {
        if (over)
        {
            _hideTimer.Stop();
            if (_vm is not null && !_vm.IsDockVisible)
            {
                _vm.IsDockVisible = true;
            }

            return;
        }

        ScheduleAutoHide();
    }

    /// <summary>
    /// 未钉住时启动自动隐藏倒计时。
    /// 触边唤出时鼠标停在热区、从没进入过 Dock，不会产生「离开」转换——
    /// 所以唤出时也要开始计时，否则 Dock 会一直挂着不收。
    /// </summary>
    private void ScheduleAutoHide()
    {
        if (_vm is null || _dockDragging || _vm.IsPinned || !_vm.IsDockVisible || _vm.IsPointerOver || _isMoreMenuOpen)
        {
            return;
        }

        _hideTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(0, _vm.Settings.AutoHideDelayMs));
        _hideTimer.Stop();
        _hideTimer.Start();
    }

    private void SetDockVisible(bool visible, bool animate)
    {
        _isHidden = !visible;
        if (visible)
        {
            ScheduleAutoHide();
        }

        if (!animate)
        {
            _progress = visible ? 0 : 1;
            ApplyPosition(_progress);
            return;
        }

        _animator.Start();
    }

    private void StepAnimation()
    {
        double target = _isHidden ? 1 : 0;
        double delta = target - _progress;

        if (Math.Abs(delta) < 0.02)
        {
            _progress = target;
            ApplyPosition(_progress);
            _animator.Stop();
            return;
        }

        _progress += delta * 0.22;
        ApplyPosition(_progress);
    }

    /// <summary>光标贴到停靠边时把 Dock 唤出来。</summary>
    /// <summary>光标贴到停靠边时把 Dock 唤出来。钉住只表示「不自动隐藏」，不影响触边唤出。</summary>
    private void CheckEdgeReveal()
    {
        if (_vm is null || _vm.IsDockVisible || !_vm.Settings.RevealOnEdgeTouch)
        {
            return;
        }

        var screen = ResolveScreen();
        if (screen is null || !NativeMethods.GetCursorPos(out NativeMethods.POINT point))
        {
            return;
        }

        PixelRect work = screen.Value.Work;
        bool near = _vm.Settings.Edge switch
        {
            DockEdge.Top => point.Y <= work.Y + EdgeBand,
            DockEdge.Bottom => point.Y >= work.Y + work.Height - EdgeBand,
            DockEdge.Left => point.X <= work.X + EdgeBand,
            DockEdge.Right => point.X >= work.X + work.Width - EdgeBand,
            _ => false
        };

        if (near)
        {
            _vm.IsDockVisible = true;
        }
    }

    // ---------------- 定位 ----------------

    /// <summary>停靠边变化：重算朝向并重新贴边。</summary>
    public void ApplyEdge(DockEdge edge)
    {
        _vm?.ApplySettings(paletteChanged: false, sizeChanged: true, save: false);
        UpdateCollapseIcon();
        SetFocused(null);
        UpdateScrollChromeLayout();
        ScheduleReposition();
    }

    /// <summary>内容尺寸变化后重新贴边（布局要等一帧才稳定）。</summary>
    public void ScheduleReposition()
        => Dispatcher.UIThread.Post(() => ApplyPosition(_progress), DispatcherPriority.Background);

    private void ApplyPosition(double progress)
    {
        // 拖动期间窗口跟手，别的重定位来源一律让路
        if (_vm is null || _dockDragging)
        {
            return;
        }

        var screen = ResolveScreen();
        if (screen is null)
        {
            return;
        }

        PixelRect work = screen.Value.Work;
        PixelRect bounds = screen.Value.Bounds;
        double scaling = screen.Value.Scaling <= 0 ? 1 : screen.Value.Scaling;

        int dockWidth = (int)Math.Ceiling(ClientSize.Width * scaling);
        int dockHeight = (int)Math.Ceiling(ClientSize.Height * scaling);
        if (dockWidth <= 0 || dockHeight <= 0)
        {
            return;
        }

        // 工作区到屏幕边界的距离（任务栏占掉的部分），收起时要一并推出屏幕
        int beyond = _vm.Settings.Edge switch
        {
            DockEdge.Top => Math.Max(0, work.Y - bounds.Y),
            DockEdge.Bottom => Math.Max(0, bounds.Bottom - work.Bottom),
            DockEdge.Left => Math.Max(0, work.X - bounds.X),
            DockEdge.Right => Math.Max(0, bounds.Right - work.Right),
            _ => 0
        };

        int hidden = DockPlacement.HiddenOffset(_vm.Settings.Edge, dockWidth, dockHeight, beyondEdge: beyond);
        int offset = DockPlacement.LerpOffset(0, hidden, progress);

        (int x, int y) = DockPlacement.Anchor(
            work.X, work.Y, work.Width, work.Height,
            dockWidth, dockHeight, _vm.Settings.Edge,
            (int)Math.Round((DockMargin - WindowShadowMargin) * scaling, MidpointRounding.AwayFromZero),
            offset);

        var target = new PixelPoint(x, y);
        if (Position != target)
        {
            Position = target;
        }
    }

    /// <summary>
    /// 取目标屏幕的工作区、完整边界与缩放。返回元组而不是 Screen 类型，
    /// 免得依赖该类型落在哪个命名空间（Avalonia 大版本之间挪过位置）。
    /// </summary>
    private (PixelRect Work, PixelRect Bounds, double Scaling)? ResolveScreen()
    {
        try
        {
            var screens = Screens;
            if (screens is null)
            {
                return null;
            }

            int index = _vm?.Settings.MonitorIndex ?? -1;
            if (index >= 0 && index < screens.All.Count)
            {
                var byIndex = screens.All[index];
                return (byIndex.WorkingArea, byIndex.Bounds, byIndex.Scaling);
            }

            if (screens.Primary is { } primary)
            {
                return (primary.WorkingArea, primary.Bounds, primary.Scaling);
            }

            if (NativeMethods.GetCursorPos(out NativeMethods.POINT point)
                && screens.ScreenFromPoint(new PixelPoint(point.X, point.Y)) is { } fromPoint)
            {
                return (fromPoint.WorkingArea, fromPoint.Bounds, fromPoint.Scaling);
            }

            return null;
        }
        catch (Exception ex)
        {
            AppLog.Error("解析屏幕失败", ex);
            return null;
        }
    }

    // ---------------- 窗口级输入 ----------------

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        if (_vm is not null)
        {
            _vm.IsPointerOver = true;
        }
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (_vm is not null)
        {
            _vm.IsPointerOver = false;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (_vm is null)
        {
            return;
        }

        // 搜索框 / 改名框里的按键交给控件自己处理
        if (e.Source is TextBox)
        {
            if (e.Key == Key.Escape && _vm.IsSearchOpen)
            {
                _vm.IsSearchOpen = false;
                e.Handled = true;
            }

            return;
        }

        if ((e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.K) || e.Key == Key.S)
        {
            _vm.IsSearchOpen = true;
            FocusSearchBox();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.E)
        {
            _vm.ToggleEditCommand.Execute(null);
            e.Handled = true;
            return;
        }

        // 1~9 快速启动前九项
        if (e.Key >= Key.D1 && e.Key <= Key.D9)
        {
            int index = e.Key - Key.D1;
            if (index < _vm.Items.Count)
            {
                _vm.RunCommand.Execute(_vm.Items[index]);
                e.Handled = true;
            }

            return;
        }

        if (e.Key == Key.Escape)
        {
            if (_vm.IsSearchOpen)
            {
                _vm.IsSearchOpen = false;
            }
            else if (_vm.IsEditMode)
            {
                _vm.IsEditMode = false;
            }
            else if (!_vm.IsPinned)
            {
                _vm.IsDockVisible = false;
            }

            e.Handled = true;
        }
    }

    private void FocusSearchBox() => Dispatcher.UIThread.Post(() =>
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }, DispatcherPriority.Loaded);

    /// <summary>被第二个实例或托盘唤醒：显示并激活。</summary>
    public void ActivateFromExternal()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_vm is not null)
            {
                _vm.IsDockVisible = true;
            }

            ApplyPosition(0);
            Topmost = true;
            Activate();
        });
    }

    private async void OnAddClicked(object? sender, RoutedEventArgs e)
    {
        // 原型：加号弹菜单（添加文件 / 添加命令行），不是直接开文件选择器
        var flyout = new MenuFlyout();

        var files = new MenuItem { Header = "添加文件…" };
        files.Click += (_, _) => _ = AddFilesAsync();
        flyout.Items.Add(files);

        var command = new MenuItem { Header = "添加命令行…" };
        command.Click += (_, _) => _ = AddCommandAsync();
        flyout.Items.Add(command);

        flyout.ShowAt(AddButton);
    }

    private async System.Threading.Tasks.Task AddCommandAsync()
    {
        if (_vm is null)
        {
            return;
        }

        (string Name, string Command)? result = await CommandDialogWindow.ShowAsync(this, _vm);
        if (result is { } entry)
        {
            _vm.AddCommand(entry.Name, entry.Command);
            ScheduleReposition();
        }
    }

    private async System.Threading.Tasks.Task AddFilesAsync()
    {
        if (_vm is null)
        {
            return;
        }

        try
        {
            IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "选择要添加到 Dock 的程序或文件",
                AllowMultiple = true
            });

            var paths = new List<string>();
            foreach (IStorageFile file in files)
            {
                string? path = file.Path?.LocalPath;
                if (!string.IsNullOrWhiteSpace(path))
                {
                    paths.Add(path!);
                }
            }

            if (paths.Count > 0)
            {
                _vm.IsDockVisible = true;
                _vm.AddTargets(paths);
                ScheduleReposition();
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("选择文件失败", ex);
        }
    }

    /// <summary>「更多」菜单：低频管理操作、停靠边缘、设置与关于。</summary>
    private void OnMoreClicked(object? sender, RoutedEventArgs e)
    {
        if (_vm is null)
        {
            return;
        }

        var flyout = new MenuFlyout();

        var addFile = new MenuItem { Header = "添加文件…" };
        addFile.Click += (_, _) => _ = AddFilesAsync();
        flyout.Items.Add(addFile);

        var addCommand = new MenuItem { Header = "添加命令行…" };
        addCommand.Click += (_, _) => _ = AddCommandAsync();
        flyout.Items.Add(addCommand);

        flyout.Items.Add(new Separator());

        var edit = new MenuItem { Header = "进入编辑模式" };
        edit.Click += (_, _) => _vm.ToggleEditCommand.Execute(null);
        flyout.Items.Add(edit);

        var pin = new MenuItem { Header = _vm.IsPinned ? "取消钉住" : "钉住 Dock" };
        pin.Click += (_, _) => _vm.TogglePinCommand.Execute(null);
        flyout.Items.Add(pin);

        flyout.Items.Add(new Separator());

        flyout.Items.Add(CreateEdgeItem("停靠到上边缘", DockEdge.Top));
        flyout.Items.Add(CreateEdgeItem("停靠到下边缘", DockEdge.Bottom));
        flyout.Items.Add(CreateEdgeItem("停靠到左边缘（竖向）", DockEdge.Left));
        flyout.Items.Add(CreateEdgeItem("停靠到右边缘（竖向）", DockEdge.Right));
        flyout.Items.Add(new Separator());

        var settings = new MenuItem { Header = "设置…" };
        settings.Click += (_, _) => ShowSettings();
        flyout.Items.Add(settings);

        var about = new MenuItem { Header = "关于" };
        about.Click += (_, _) => AboutRequested?.Invoke(this, EventArgs.Empty);
        flyout.Items.Add(about);

        flyout.Items.Add(new Separator());

        var exit = new MenuItem { Header = "退出" };
        exit.Click += (_, _) => Exit();
        flyout.Items.Add(exit);

        _isMoreMenuOpen = true;
        _hideTimer.Stop();
        flyout.Closed += (_, _) =>
        {
            _isMoreMenuOpen = false;
            ScheduleAutoHide();
        };
        flyout.ShowAt(MoreButton);
    }

    private MenuItem CreateEdgeItem(string header, DockEdge edge)
    {
        bool current = _vm?.Settings.Edge == edge;
        var item = new MenuItem { Header = current ? header + "（当前）" : header, IsEnabled = !current };
        item.Click += (_, _) => _vm?.SetEdgeCommand.Execute(edge);
        return item;
    }

    // ---------------- IDockHost ----------------

    public void ShowSettings() => SettingsRequested?.Invoke(this, EventArgs.Empty);

    public void Exit()
    {
        _vm?.Save();
        (Application.Current as App)?.RequestShutdown();
    }

    public void OpenUrl(string url)
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
            AppLog.Error("打开链接失败", ex);
        }
    }

    private void OnWindowResized(object? sender, SizeChangedEventArgs e)
    {
        ApplyPosition(_progress);
        UpdateScrollChrome();
    }
}
