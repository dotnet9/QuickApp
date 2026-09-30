using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
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
    private const double DragThreshold = 5;

    private readonly DispatcherTimer _animator;
    private readonly DispatcherTimer _hideTimer;
    private readonly DispatcherTimer _revealTimer;
    private DockViewModel? _vm;
    /// <summary>更新就绪的成功色（原型 --ok）。</summary>
    private static readonly IBrush UpdateReadyBrush = new SolidColorBrush(Color.FromRgb(0x3F, 0xB9, 0x6F));

    private Window? _revealHandleWindow;
    private Border? _revealHandleBar;
    private Border? _revealHandleHitTarget;
    private bool _isRevealHandlePointerOver;
    private bool _isHidden;
    private bool _isMoreMenuOpen;
    private bool _isContextMenuOpen;
    private ContextMenu? _contextMenu;
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
            if (_vm is not null && !_dockDragging && !_isRevealHandlePointerOver && !_vm.IsPointerOver && !_vm.IsPinned && !_vm.IsSearchOpen && !_vm.IsEditMode && !_isMoreMenuOpen && !_isContextMenuOpen)
            {
                _vm.IsDockVisible = false;
            }
        };

        _revealTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _revealTimer.Tick += OnRevealTimerTick;

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
        CreateRevealHandle();

        // 在面板隧道阶段识别空白区域拖动；子控件的交互仍由各自处理器接管。
        Panel.AddHandler(PointerPressedEvent, OnPanelPointerPressed, RoutingStrategies.Tunnel);
        Panel.AddHandler(PointerMovedEvent, OnPanelPointerMoved, RoutingStrategies.Tunnel);
        Panel.AddHandler(PointerReleasedEvent, OnPanelPointerReleased, RoutingStrategies.Tunnel);
        Panel.AddHandler(PointerCaptureLostEvent, OnPanelPointerCaptureLost, RoutingStrategies.Tunnel);

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
        InstalledItemsHost.AddHandler(KeyDownEvent, OnItemsKeyDown, RoutingStrategies.Tunnel);

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
            UpdateRevealHandleVisibility();
        };

        Closed += (_, _) =>
        {
            _revealTimer.Stop();
            _revealHandleWindow?.Hide();
        };
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
            case nameof(DockViewModel.IsDownloadingUpdate):
            case nameof(DockViewModel.IsUpdateReady):
            case nameof(DockViewModel.CanDownloadUpdate):
            case nameof(DockViewModel.NeedsUpdatePage):
            case nameof(DockViewModel.DownloadProgressText):
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

            case nameof(DockViewModel.SearchQuery):
                SetFocused(null);
                break;

            case nameof(DockViewModel.IsPinned):
                UpdatePinState();
                UpdateRevealHandleVisibility();
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


        ToolTip.SetTip(PinButton, pinned ? "取消钉住 Dock" : "钉住 Dock");
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
        // 原型 .icon-btn：四键统一淡灰（text-dim），悬停变亮，激活（钉住/搜索打开）转强调色。
        // 图标描边绑定按钮前景色，状态色由 IconButton 主题统一管理，不再各自写死。
        SetIcon(SearchButton, Icons.Search, 15, null);
        SetIcon(MoreButton, Icons.More, 16, null);
        if (MoreButton.Content is Path morePath)
        {
            morePath.Bind(Path.FillProperty, MoreButton.GetObservable(Button.ForegroundProperty));
            morePath.StrokeThickness = 0;
        }
        SetIcon(PinButton, Icons.Pin, 15, null);
        SetIcon(CollapseButton, Icons.ChevronUp, 14, null);

        UpdateCollapseIcon();
    }

    private void CreateRevealHandle()
    {
        var bar = new Border
        {
            Width = 54,
            Height = 5,
            CornerRadius = new CornerRadius(3),
            Background = _vm?.TextDimBrush ?? Brushes.Gray,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            IsHitTestVisible = false
        };
        var hitTarget = new Border
        {
            Width = 78,
            Height = 24,
            Background = Brushes.Transparent,
            Child = bar
        };
        hitTarget.PointerEntered += OnRevealHandlePointerEntered;
        hitTarget.PointerExited += OnRevealHandlePointerExited;
        hitTarget.PointerPressed += OnRevealHandlePointerPressed;
        _revealHandleBar = bar;
        _revealHandleHitTarget = hitTarget;

        _revealHandleWindow = new Window
        {
            Title = "QuickApp Dock handle",
            Width = 78,
            Height = 24,
            ShowInTaskbar = false,
            ShowActivated = false,
            CanResize = false,
            Topmost = true,
            WindowDecorations = WindowDecorations.None,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Background = Brushes.Transparent,
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent },
            TransparencyBackgroundFallback = Brushes.Transparent,
            Content = hitTarget
        };

        UpdateRevealHandleLayout();
    }

    private void UpdateRevealHandleLayout()
    {
        if (_revealHandleWindow is null || _revealHandleBar is null || _revealHandleHitTarget is null)
        {
            return;
        }

        DockEdge edge = _vm?.Settings.Edge ?? DockEdge.Top;
        bool vertical = DockPlacement.IsVertical(edge);
        _revealHandleWindow.Width = vertical ? 24 : 78;
        _revealHandleWindow.Height = vertical ? 78 : 24;
        _revealHandleHitTarget.Width = vertical ? 24 : 78;
        _revealHandleHitTarget.Height = vertical ? 78 : 24;
        _revealHandleBar.Width = vertical ? 5 : 54;
        _revealHandleBar.Height = vertical ? 54 : 5;
        _revealHandleBar.HorizontalAlignment = edge switch
        {
            DockEdge.Left => Avalonia.Layout.HorizontalAlignment.Left,
            DockEdge.Right => Avalonia.Layout.HorizontalAlignment.Right,
            _ => Avalonia.Layout.HorizontalAlignment.Center
        };
        _revealHandleBar.VerticalAlignment = edge switch
        {
            DockEdge.Top => Avalonia.Layout.VerticalAlignment.Top,
            DockEdge.Bottom => Avalonia.Layout.VerticalAlignment.Bottom,
            _ => Avalonia.Layout.VerticalAlignment.Center
        };
        UpdateRevealHandlePosition();
    }

    private void UpdateRevealHandlePosition()
    {
        if (_revealHandleWindow is null || _vm is null)
        {
            return;
        }

        var screen = ResolveScreen();
        if (screen is null)
        {
            return;
        }

        double scaling = screen.Value.Scaling <= 0 ? 1 : screen.Value.Scaling;
        int width = (int)Math.Ceiling(_revealHandleWindow.Width * scaling);
        int height = (int)Math.Ceiling(_revealHandleWindow.Height * scaling);
        int inset = (int)Math.Round(5 * scaling, MidpointRounding.AwayFromZero);
        PixelRect bounds = screen.Value.Bounds;
        (int x, int y) = DockPlacement.AnchorHandle(
            bounds.X, bounds.Y, bounds.Width, bounds.Height, width, height, _vm.Settings.Edge, inset);
        _revealHandleWindow.Position = new PixelPoint(x, y);
    }

    private void UpdateRevealHandleVisibility()
    {
        if (_vm is null || _revealHandleWindow is null)
        {
            return;
        }

        bool show = _isHidden && _progress >= 0.98 && !_vm.IsPinned;
        if (!show)
        {
            _revealTimer.Stop();
            if (!_vm.IsDockVisible)
            {
                _isRevealHandlePointerOver = false;
            }

            if (_revealHandleBar is not null)
            {
                _revealHandleBar.Background = _vm.TextDimBrush;
            }

            _revealHandleWindow.Hide();
            return;
        }

        UpdateRevealHandleLayout();
        if (!_revealHandleWindow.IsVisible)
        {
            _revealHandleWindow.Show();
        }
    }

    private void OnRevealHandlePointerEntered(object? sender, PointerEventArgs e)
    {
        _isRevealHandlePointerOver = true;
        if (_revealHandleBar is not null)
        {
            _revealHandleBar.Background = _vm?.AccentBrush ?? Brushes.White;
        }

        if (_vm?.Settings.RevealOnEdgeTouch == true && _isHidden)
        {
            _revealTimer.Stop();
            _revealTimer.Start();
        }
    }

    private void OnRevealHandlePointerExited(object? sender, PointerEventArgs e)
    {
        _isRevealHandlePointerOver = false;
        _revealTimer.Stop();
        if (_revealHandleBar is not null)
        {
            _revealHandleBar.Background = _vm?.TextDimBrush ?? Brushes.Gray;
        }

        if (_vm?.IsDockVisible == true)
        {
            ScheduleAutoHide();
        }
    }

    private void OnRevealHandlePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        Border? hitTarget = _revealHandleHitTarget;
        if (_vm is null || hitTarget is null
            || !e.GetCurrentPoint(hitTarget).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _revealTimer.Stop();
        _vm.IsDockVisible = true;
        e.Handled = true;
    }

    private void OnRevealTimerTick(object? sender, EventArgs e)
    {
        _revealTimer.Stop();
        if (_isRevealHandlePointerOver
            && _vm is not null
            && _isHidden
            && !_vm.IsPinned
            && _vm.Settings.RevealOnEdgeTouch)
        {
            _vm.IsDockVisible = true;
        }
    }

    private static void SetIcon(Button button, Geometry geometry, double size, IBrush stroke)
    {
        var path = new Path
        {
            Data = geometry,
            StrokeThickness = 1.7,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round,
            Stretch = Stretch.Uniform,
            Width = size,
            Height = size
        };
        // 描边跟随按钮前景色：常态淡灰、悬停变亮、激活（钉住/搜索打开）转强调色（原型 .icon-btn 色彩规则）
        path.Bind(Path.StrokeProperty, button.GetObservable(Button.ForegroundProperty));
        if (stroke is not null)
        {
            path.Stroke = stroke;
        }
        button.Content = path;
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
        item.IsFocused = ReferenceEquals(item, _focused);
        item.TextBrush = _vm?.TextBrush;

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
            _focused.IsFocused = false;
        }

        _focused = item;

        if (_focused is not null)
        {
            _focused.RingBrush = _vm?.AccentBrush;
            _focused.IsFocused = true;
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

        if (e.Key == Key.Down)
        {
            MoveFocus(1);
            e.Handled = true;
        }
        else if (e.Key == Key.Up)
        {
            MoveFocus(-1);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            List<ItemViewModel> results = NavigableItems();
            ItemViewModel? selected = _focused is not null && results.Contains(_focused)
                ? _focused
                : results.FirstOrDefault();
            if (selected is not null)
            {
                _vm.RunCommand.Execute(selected);
                e.Handled = true;
            }
        }
    }

    private void MoveFocus(int step)
    {
        List<ItemViewModel> results = NavigableItems();
        if (results.Count == 0)
        {
            return;
        }

        int index = _focused is null ? -1 : results.IndexOf(_focused);
        index = index < 0
            ? (step > 0 ? 0 : results.Count - 1)
            : (index + step + results.Count) % results.Count;

        ItemViewModel selected = results[index];
        SetFocused(selected);
        Dispatcher.UIThread.Post(() =>
        {
            Control? target = ItemsHost.GetVisualDescendants()
                .Concat(InstalledItemsHost.GetVisualDescendants())
                .OfType<Control>()
                .FirstOrDefault(control => control.Focusable && ReferenceEquals(control.DataContext, selected));
            target?.Focus();
            target?.BringIntoView();
        }, DispatcherPriority.Input);
    }

    private List<ItemViewModel> NavigableItems()
    {
        if (_vm is null)
        {
            return new List<ItemViewModel>();
        }

        var results = _vm.Items.ToList();
        if (_vm.IsSearchGrouped)
        {
            results.AddRange(_vm.InstalledItems);
        }

        return results;
    }

    private void OnItemsKeyDown(object? sender, KeyEventArgs e)
    {
        if (_vm is null || (e.Source as Visual)?.GetSelfAndVisualAncestors().Any(visual => visual is TextBox or Button) == true)
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

        _contextMenu?.Close();
        var menu = new ContextMenu();
        _contextMenu = menu;
        _isContextMenuOpen = true;
        _hideTimer.Stop();
        menu.Closed += (_, _) =>
        {
            if (ReferenceEquals(_contextMenu, menu))
            {
                _contextMenu = null;
                _isContextMenuOpen = false;
                ScheduleAutoHide();
            }
        };

        if (item is null)
        {
            // 与原型 dockMenuSpec 一致：分组标题 + 图标 + danger 红色 + 四向停靠选择器
            AddDockMenuEntries(menu.Items, () => _contextMenu?.Close());
        }
        else
        {
            ItemViewModel captured = item;
            menu.Items.Add(MenuEntry("运行", Icons.Play, () => _vm.RunCommand.Execute(captured)));
            menu.Items.Add(new Separator());
            menu.Items.Add(MenuEntry("重命名", Icons.Pencil, () =>
            {
                _vm.BeginRename(captured);
                FocusRenameBox();
            }));
            menu.Items.Add(MenuEntry("更换图标…", Icons.Upload, () => Dispatcher.UIThread.Post(() => _ = ChangeIconAsync(captured))));
            menu.Items.Add(MenuEntry("复制路径", Icons.Copy, () => CopyToClipboard(captured.Model.Target)));
            menu.Items.Add(MenuEntry("复制命令", Icons.Terminal, () => CopyToClipboard(ItemQuery.ToCommandText(captured.Model))));
            menu.Items.Add(MenuEntry("打开位置", Icons.Folder, () => RevealInExplorer(captured.Model.Target)));
            menu.Items.Add(new Separator());
            menu.Items.Add(MenuEntry("移除", Icons.Trash, () =>
            {
                _vm.RemoveCommand.Execute(captured);
                Dispatcher.UIThread.Post(() =>
                {
                    if (menu.IsOpen)
                    {
                        menu.Close();
                    }
                });
            }, danger: true));
        }

        menu.PlacementTarget = ItemsHost;
        menu.Placement = PlacementMode.Pointer;
        menu.Open(ItemsHost);
    }

    /// <summary>原型 dockMenuSpec 的实现：「更多」菜单与空白处右键菜单共用。</summary>
    private void AddDockMenuEntries(System.Collections.IList items, Action closeMenu)
    {
        items.Add(MenuTitle("添加"));
        items.Add(MenuEntry("文件…", Icons.Upload, () => _ = AddFilesAsync()));
        items.Add(MenuEntry("命令行…", Icons.Terminal, () => _ = AddCommandAsync()));
        items.Add(new Separator());
        items.Add(MenuTitle("管理"));
        items.Add(MenuEntry("编辑模式", Icons.Pencil, () => _vm!.ToggleEditCommand.Execute(null)));
        items.Add(MenuEntry("清空", Icons.Trash, () => _vm!.ClearAllCommand.Execute(null), danger: true));
        items.Add(new Separator());
        items.Add(MenuTitle("停靠"));
        items.Add(BuildEdgePicker(closeMenu));
        items.Add(new Separator());
        items.Add(MenuEntry("设置", Icons.Gear, ShowSettings));
        items.Add(new Separator());
        items.Add(MenuEntry("退出", Icons.Power, Exit, danger: true));
    }

    /// <summary>停靠位置四向选择器：桌面示意框，点哪条边停靠哪条边；当前边强调色高亮（原型 .dock-pos）。</summary>
    private Control BuildEdgePicker(Action closeMenu)
    {
        var grid = new Grid { Width = 168, Height = 96, Margin = new Thickness(2, 2, 2, 6) };
        var frame = new Border { BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(9) };
        if (this.TryFindResource("QAHoverBrush", Avalonia.Styling.ThemeVariant.Default, out var hover) && hover is IBrush hoverBrush)
        {
            frame.Background = hoverBrush;
        }
        if (this.TryFindResource("QALineBrush", Avalonia.Styling.ThemeVariant.Default, out var line) && line is IBrush lineBrush)
        {
            frame.BorderBrush = lineBrush;
        }
        grid.Children.Add(frame);

        var buttons = new Dictionary<DockEdge, Button>();
        void AddEdge(DockEdge edge, Thickness margin, bool horizontal,
            HorizontalAlignment hAlign, VerticalAlignment vAlign)
        {
            var button = new Button
            {
                Classes = { "edge-pick" },
                Margin = margin,
                HorizontalAlignment = hAlign,
                VerticalAlignment = vAlign
            };
            if (horizontal)
            {
                button.Height = 20;
            }
            else
            {
                button.Width = 20;
            }

            // 内部小 Dock 药丸占按钮 58%（原型 .edge::before），随按钮前景色变化
            var bar = horizontal
                ? new Border { Height = 6, Margin = new Thickness(21, 7, 21, 7), HorizontalAlignment = HorizontalAlignment.Stretch, CornerRadius = new CornerRadius(3) }
                : new Border { Width = 6, Margin = new Thickness(7, 7, 7, 7), VerticalAlignment = VerticalAlignment.Stretch, CornerRadius = new CornerRadius(3) };
            bar.Bind(Border.BackgroundProperty, button.GetObservable(Button.ForegroundProperty));
            button.Content = bar;

            if (_vm?.Settings.Edge == edge)
            {
                button.Classes.Add("active");
            }
            buttons[edge] = button;

            button.Click += (_, _) =>
            {
                _vm!.SetEdgeCommand.Execute(edge);
                foreach (var (key, b) in buttons)
                {
                    b.Classes.Set("active", key == edge);
                }
                closeMenu();
            };
            grid.Children.Add(button);
        }

        AddEdge(DockEdge.Top, new Thickness(30, 5, 30, 5), true, HorizontalAlignment.Stretch, VerticalAlignment.Top);
        AddEdge(DockEdge.Bottom, new Thickness(30, 5, 30, 5), true, HorizontalAlignment.Stretch, VerticalAlignment.Bottom);
        AddEdge(DockEdge.Left, new Thickness(5, 30, 5, 30), false, HorizontalAlignment.Left, VerticalAlignment.Stretch);
        AddEdge(DockEdge.Right, new Thickness(5, 30, 5, 30), false, HorizontalAlignment.Right, VerticalAlignment.Stretch);
        return grid;
    }

    /// <summary>菜单分组标题：原型 .menu-title，11px 淡色、不响应悬停。</summary>
    private static MenuItem MenuTitle(string text)
    {
        var title = new MenuItem { Header = text, IsHitTestVisible = false, Focusable = false };
        title.Classes.Add("menu-item");
        title.Classes.Add("title");
        return title;
    }

    /// <summary>
    /// 菜单条目：对应原型 .menu-item——16px 淡色图标 + 文本 + 右侧 11px 徽章（mi-note），
    /// danger 红色文字与图标。
    /// </summary>
    private static MenuItem MenuEntry(string header, Geometry? icon, Action action,
        bool danger = false, string? note = null, bool enabled = true)
    {
        object content = header;
        if (!string.IsNullOrEmpty(note))
        {
            var panel = new DockPanel { LastChildFill = true };
            var badge = new TextBlock { Text = note, VerticalAlignment = VerticalAlignment.Center };
            badge.Classes.Add("menu-note");
            DockPanel.SetDock(badge, Dock.Right);
            panel.Children.Add(badge);
            panel.Children.Add(new TextBlock { Text = header, VerticalAlignment = VerticalAlignment.Center });
            content = panel;
        }

        var entry = new MenuItem
        {
            Header = content,
            Icon = icon is null
                ? null
                : new Path
                {
                    Data = icon,
                    Width = 16,
                    Height = 16,
                    Stretch = Stretch.Uniform,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Classes = { "mi-icon" }
                },
            IsEnabled = enabled
        };
        entry.Classes.Add("menu-item");
        if (danger)
        {
            entry.Classes.Add("danger");
        }
        entry.Click += (_, _) => action();
        return entry;
    }

    /// <summary>「更换图标」：选择本地图片（png/jpg/ico 等），不内置图标库。</summary>
    private async Task ChangeIconAsync(ItemViewModel item)
    {
        if (_vm is null)
        {
            return;
        }

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择图标图片",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("图片")
                {
                    Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.webp", "*.ico" }
                }
            ]
        });

        if (files.Count == 0)
        {
            return;
        }

        string path = files[0].Path.LocalPath;
        if (!string.IsNullOrWhiteSpace(path) && System.IO.File.Exists(path))
        {
            _vm.ChangeIcon(item, path);
        }
    }

    /// <summary>竖排更新胶囊：弹出更新卡片（原型 .update-card），状态与进度变化时原位刷新。</summary>
    private void OnUpdatePillClick(object? sender, RoutedEventArgs e)
    {
        if (_vm is null)
        {
            return;
        }

        var icon = new Path
        {
            Width = 17,
            Height = 17,
            Stretch = Stretch.Uniform,
            StrokeThickness = 1.8,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round
        };
        var title = new TextBlock { FontSize = 13, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        var tag = new TextBlock { FontSize = 11, Opacity = 0.72, VerticalAlignment = VerticalAlignment.Center };
        var tagChip = new Border
        {
            BorderBrush = _vm.PanelBorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(999),
            Padding = new Thickness(8, 2),
            Child = tag,
            VerticalAlignment = VerticalAlignment.Center
        };
        var desc = new TextBlock { FontSize = 11.5, Opacity = 0.78, TextWrapping = TextWrapping.Wrap };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, HorizontalAlignment = HorizontalAlignment.Right };
        var progress = new ProgressBar { Minimum = 0, Maximum = 100, Height = 4, MinHeight = 4 };

        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        head.ColumnSpacing = 8;
        Grid.SetColumn(icon, 0);
        Grid.SetColumn(title, 1);
        Grid.SetColumn(tagChip, 2);
        head.Children.Add(icon);
        head.Children.Add(title);
        head.Children.Add(tagChip);

        var card = new StackPanel { Spacing = 8 };
        card.Children.Add(head);
        card.Children.Add(desc);
        card.Children.Add(actions);
        card.Children.Add(progress);

        Button LinkButton(string text, bool primary, System.Windows.Input.ICommand? command)
        {
            var button = new Button { Content = text, Padding = new Thickness(10, 3) };
            if (TryGetResource("LinkButton", null, out object? theme) && theme is ControlTheme controlTheme)
            {
                button.Theme = controlTheme;
            }

            if (primary)
            {
                button.Classes.Add("primary");
            }

            button.Command = command;
            return button;
        }

        void Refresh()
        {
            bool ready = _vm.IsUpdateReady;
            bool downloading = _vm.IsDownloadingUpdate;
            icon.Data = ready ? Icons.Check : downloading ? Icons.Download : Icons.Upload;
            icon.Stroke = ready ? UpdateReadyBrush : _vm.AccentBrush;
            title.Text = ready ? "更新已就绪" : downloading ? "正在下载更新" : "发现新版本";
            tag.Text = _vm.UpdateTag;
            desc.Text = ready
                ? "安装包已校验存放，点击安装后由系统安装器接管，程序随后退出。"
                : downloading
                    ? "下载在后台进行，可随时取消；完成后 Dock 会提示安装。"
                    : _vm.NeedsUpdatePage
                        ? "没有匹配当前系统的安装包，可以前往 Release 页面手动选择资产。"
                        : "已按当前系统与架构选定安装包，下载完成后需再次点击安装，程序不会静默替换。";
            actions.Children.Clear();
            if (_vm.CanDownloadUpdate)
            {
                actions.Children.Add(LinkButton("下载", true, _vm.DownloadUpdateCommand));
                actions.Children.Add(LinkButton("忽略", false, _vm.DismissUpdateCommand));
            }
            else if (_vm.NeedsUpdatePage)
            {
                actions.Children.Add(LinkButton("打开发布页", true, _vm.OpenUpdateCommand));
                actions.Children.Add(LinkButton("忽略", false, _vm.DismissUpdateCommand));
            }
            else if (downloading)
            {
                actions.Children.Add(LinkButton("取消", false, _vm.CancelUpdateDownloadCommand));
            }
            else if (ready)
            {
                actions.Children.Add(LinkButton(_vm.UpdateInstallButtonText, true, _vm.InstallUpdateCommand));
                actions.Children.Add(LinkButton("忽略", false, _vm.DismissUpdateCommand));
            }

            progress.IsVisible = downloading;
            progress.Value = _vm.DownloadProgress;
        }

        Refresh();
        void OnVmChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (args.PropertyName is nameof(DockViewModel.DownloadProgress)
                or nameof(DockViewModel.IsDownloadingUpdate)
                or nameof(DockViewModel.IsUpdateReady)
                or nameof(DockViewModel.CanDownloadUpdate)
                or nameof(DockViewModel.NeedsUpdatePage))
            {
                Refresh();
            }
        }

        _vm.PropertyChanged += OnVmChanged;
        var flyout = new Flyout
        {
            Placement = _vm.Settings.Edge == DockEdge.Right ? PlacementMode.Left : PlacementMode.Right,
            Content = new Border
            {
                Background = _vm.MenuBrush,
                BorderBrush = _vm.PanelBorderBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(12),
                Width = 290,
                Child = card
            }
        };
        flyout.Closed += (_, _) => _vm.PropertyChanged -= OnVmChanged;
        flyout.ShowAt(UpdatePillButton);
    }

    private void CopyToClipboard(string text)
    {
        if (!OperatingSystem.IsWindows())
        {
            _vm?.Toast("当前平台暂不支持复制到剪贴板");
            return;
        }

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

        if (!TryGetPointerPixel(e, out PixelPoint cursor))
        {
            return;
        }

        _dragCursorOrigin = cursor;
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

        if (!TryGetPointerPixel(e, out PixelPoint pointer))
        {
            return;
        }

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
            _isRevealHandlePointerOver = false;
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
    /// 未钉住时启动自动隐藏倒计时。把手仍悬停时先等待指针离开，再开始计时。
    /// </summary>
    private void ScheduleAutoHide()
    {
        _hideTimer.Stop();
        if (_vm is null
            || _dockDragging
            || _vm.IsPinned
            || !_vm.IsDockVisible
            || _isRevealHandlePointerOver
            || _vm.IsPointerOver
            || _isMoreMenuOpen
            || _isContextMenuOpen
            || _vm.Settings.AutoHideDelayMs <= 0)
        {
            return;
        }

        _hideTimer.Interval = TimeSpan.FromMilliseconds(_vm.Settings.AutoHideDelayMs);
        _hideTimer.Start();
    }

    public void AutoHideDelayChanged() => ScheduleAutoHide();

    private void SetDockVisible(bool visible, bool animate)
    {
        _isHidden = !visible;
        _revealTimer.Stop();
        UpdateRevealHandleVisibility();
        if (visible)
        {
            ScheduleAutoHide();
        }

        if (!animate)
        {
            _progress = visible ? 0 : 1;
            ApplyPosition(_progress);
            UpdateRevealHandleVisibility();
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
            UpdateRevealHandleVisibility();
            return;
        }

        _progress += delta * 0.22;
        ApplyPosition(_progress);
    }

    // ---------------- 定位 ----------------

    /// <summary>停靠边变化：重算朝向并重新贴边。</summary>
    public void ApplyEdge(DockEdge edge)
    {
        _vm?.ApplySettings(paletteChanged: false, sizeChanged: true, save: false);
        UpdateCollapseIcon();
        UpdateRevealHandleLayout();
        SetFocused(null);
        UpdateScrollChromeLayout();
        ScheduleReposition();
    }

    /// <summary>内容尺寸变化后重新贴边（布局要等一帧才稳定）。</summary>
    public void ScheduleReposition()
        => Dispatcher.UIThread.Post(() =>
        {
            ApplyPosition(_progress);
            UpdateRevealHandlePosition();
        }, DispatcherPriority.Background);

    public void MonitorSelectionChanged()
    {
        UpdateRevealHandlePosition();
        ScheduleReposition();
    }

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

            if (OperatingSystem.IsWindows()
                && NativeMethods.GetCursorPos(out NativeMethods.POINT point)
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

    private bool TryGetPointerPixel(PointerEventArgs e, out PixelPoint point)
    {
        if (OperatingSystem.IsWindows()
            && NativeMethods.GetCursorPos(out NativeMethods.POINT cursor))
        {
            point = new PixelPoint(cursor.X, cursor.Y);
            return true;
        }

        Point local = e.GetPosition(this);
        double scaling = RenderScaling;
        point = new PixelPoint(
            Position.X + (int)Math.Round(local.X * scaling, MidpointRounding.AwayFromZero),
            Position.Y + (int)Math.Round(local.Y * scaling, MidpointRounding.AwayFromZero));
        return true;
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

        DockEdge edge = _vm.Settings.Edge;
        var flyout = new MenuFlyout
        {
            Placement = edge switch
            {
                DockEdge.Bottom => PlacementMode.TopEdgeAlignedRight,
                DockEdge.Left => PlacementMode.RightEdgeAlignedTop,
                DockEdge.Right => PlacementMode.LeftEdgeAlignedTop,
                _ => PlacementMode.BottomEdgeAlignedRight
            },
            HorizontalOffset = edge switch
            {
                DockEdge.Left => 8,
                DockEdge.Right => -8,
                _ => 0
            },
            VerticalOffset = edge switch
            {
                DockEdge.Bottom => -8,
                DockEdge.Top => 8,
                _ => 0
            },
            PlacementConstraintAdjustment = PopupPositionerConstraintAdjustment.FlipX
                | PopupPositionerConstraintAdjustment.FlipY
                | PopupPositionerConstraintAdjustment.SlideX
                | PopupPositionerConstraintAdjustment.SlideY
        };

        AddDockMenuEntries(flyout.Items, () => flyout.Hide());

        _isMoreMenuOpen = true;
        _hideTimer.Stop();
        flyout.Closed += (_, _) =>
        {
            _isMoreMenuOpen = false;
            ScheduleAutoHide();
        };
        flyout.ShowAt(MoreButton);
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
        UpdateRevealHandlePosition();
        UpdateScrollChrome();
    }
}
