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
    private const int EdgeBand = 4;

    private readonly DispatcherTimer _animator;
    private readonly DispatcherTimer _hideTimer;
    private readonly DispatcherTimer _edgeTimer;
    private DockViewModel? _vm;
    private bool _isHidden;
    private double _progress;

    private ItemViewModel? _focused;
    private ItemViewModel? _dragItem;
    private ItemViewModel? _dropTarget;

    public DockWindow()
    {
        InitializeComponent();

        _animator = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _animator.Tick += (_, _) => StepAnimation();

        _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            if (_vm is not null && !_vm.IsPointerOver && !_vm.IsPinned && !_vm.IsSearchOpen && !_vm.IsEditMode)
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
    }

    public event EventHandler? SettingsRequested;

    public void Attach(DockViewModel viewModel)
    {
        _vm = viewModel;
        DataContext = viewModel;
        viewModel.AttachHost(this);
        viewModel.DockVisibilityChanged += (_, visible) => SetDockVisible(visible, animate: true);
        viewModel.PointerOverChanged += (_, over) => OnPointerOverChanged(over);
        viewModel.Items.CollectionChanged += OnItemsChanged;
        viewModel.ItemVisualRequested += ApplyItemVisual;
        viewModel.ContextRequested += ShowContextMenu;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;

        BuildActionIcons();

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

        Opened += (_, _) =>
        {
            ApplyEdge(viewModel.Settings.Edge);
            _edgeTimer.Start();
        };

        Closed += (_, _) => _edgeTimer.Stop();
    }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e) => ScheduleReposition();

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(DockViewModel.IsDockVisible):
            case nameof(DockViewModel.IsSearchOpen):
            case nameof(DockViewModel.IsEditMode):
            case nameof(DockViewModel.UpdateAvailable):
                ScheduleReposition();
                break;

            case nameof(DockViewModel.PanelBrush):
                ApplyTransparency();
                break;
        }
    }

    /// <summary>毛玻璃只在「玻璃」风格时申请，扁平风格直接要透明底。</summary>
    private void ApplyTransparency()
    {
        try
        {
            bool glass = _vm?.Settings.Style != "flat";
            TransparencyLevelHint = glass
                ? new[] { WindowTransparencyLevel.AcrylicBlur, WindowTransparencyLevel.Blur, WindowTransparencyLevel.Transparent }
                : new[] { WindowTransparencyLevel.Transparent };
        }
        catch (Exception ex)
        {
            AppLog.Error("设置窗口透明度失败", ex);
        }
    }

    // ---------------- 图标与占位 ----------------

    private void BuildActionIcons()
    {
        IBrush color = _vm?.TextDimBrush ?? Brushes.Gray;
        SetIcon(MoveButton, Icons.Grip, 14, color);
        SetIcon(SearchButton, Icons.Search, 15, color);
        SetIcon(EditButton, Icons.Pencil, 15, color);
        SetIcon(AddButton, Icons.Plus, 16, color);
        SetIcon(MoreButton, Icons.More, 16, color);
        SetIcon(PinButton, Icons.Pin, 15, color);
        SetIcon(CollapseButton, Icons.ChevronUp, 14, color);

        MoveButton.Cursor = new Cursor(StandardCursorType.SizeAll);
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

        ItemViewModel? item = ItemOf(e.Source);
        bool right = e.GetCurrentPoint(this).Properties.IsRightButtonPressed;

        if (item is null)
        {
            SetFocused(null);
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

        if (_vm.IsEditMode)
        {
            // 编辑模式：左键按住 = 拖动排序
            _dragItem = item;
            e.Pointer.Capture(ItemsHost);
            e.Handled = true;
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

        Panel? panel = ItemsHost.ItemsPanelRoot;
        if (panel is null || panel.Children.Count == 0)
        {
            return;
        }

        Point position = e.GetPosition(panel);
        bool horizontal = _vm.ListOrientation == Orientation.Horizontal;
        double pointer = horizontal ? position.X : position.Y;

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
            }

            _dropTarget = target;
            if (_dropTarget is not null)
            {
                _dropTarget.IsDropTarget = true;
            }
        }

        e.Handled = true;
    }

    private void OnItemsPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_vm is null || _dragItem is null)
        {
            return;
        }

        ItemViewModel dragged = _dragItem;
        ItemViewModel? target = _dropTarget;

        _dragItem = null;
        _dropTarget = null;
        e.Pointer.Capture(null);

        if (target is not null)
        {
            target.IsDropTarget = false;
        }

        if (target is not null && !ReferenceEquals(target, dragged) && !ReferenceEquals(target, dragged))
        {
            _vm.Reorder(dragged.Id, target.Id);
        }
    }

    private void OnItemsPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (_dropTarget is not null)
        {
            _dropTarget.IsDropTarget = false;
        }

        _dragItem = null;
        _dropTarget = null;
    }

    private void OnItemsDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_vm is null || !_vm.IsEditMode)
        {
            return;
        }

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
            menu.Items.Add(MenuEntry("复制路径", () => CopyToClipboard(captured.Model.Target)));
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

        if (_vm is null || _vm.IsPinned)
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
    private void CheckEdgeReveal()
    {
        if (_vm is null || _vm.IsDockVisible || _vm.IsPinned || !_vm.Settings.RevealOnEdgeTouch)
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
        ScheduleReposition();
    }

    /// <summary>内容尺寸变化后重新贴边（布局要等一帧才稳定）。</summary>
    public void ScheduleReposition()
        => Dispatcher.UIThread.Post(() => ApplyPosition(_progress), DispatcherPriority.Background);

    private void ApplyPosition(double progress)
    {
        if (_vm is null)
        {
            return;
        }

        var screen = ResolveScreen();
        if (screen is null)
        {
            return;
        }

        PixelRect work = screen.Value.Work;
        double scaling = screen.Value.Scaling <= 0 ? 1 : screen.Value.Scaling;

        int dockWidth = (int)Math.Ceiling(ClientSize.Width * scaling);
        int dockHeight = (int)Math.Ceiling(ClientSize.Height * scaling);
        if (dockWidth <= 0 || dockHeight <= 0)
        {
            return;
        }

        int hidden = DockPlacement.HiddenOffset(_vm.Settings.Edge, dockWidth, dockHeight);
        int offset = DockPlacement.LerpOffset(0, hidden, progress);

        (int x, int y) = DockPlacement.Anchor(
            work.X, work.Y, work.Width, work.Height,
            dockWidth, dockHeight, _vm.Settings.Edge, DockMargin, offset);

        var target = new PixelPoint(x, y);
        if (Position != target)
        {
            Position = target;
        }
    }

    /// <summary>
    /// 取目标屏幕的工作区与缩放。返回元组而不是 Screen 类型，
    /// 免得依赖该类型落在哪个命名空间（Avalonia 大版本之间挪过位置）。
    /// </summary>
    private (PixelRect Work, double Scaling)? ResolveScreen()
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
                return (byIndex.WorkingArea, byIndex.Scaling);
            }

            if (screens.Primary is { } primary)
            {
                return (primary.WorkingArea, primary.Scaling);
            }

            if (NativeMethods.GetCursorPos(out NativeMethods.POINT point)
                && screens.ScreenFromPoint(new PixelPoint(point.X, point.Y)) is { } fromPoint)
            {
                return (fromPoint.WorkingArea, fromPoint.Scaling);
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
    }, DispatcherPriority.Input);

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

    private async void OnAddClicked(object? sender, RoutedEventArgs e) => await AddFilesAsync();

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

    /// <summary>「更多」菜单：停靠边缘、设置、检查更新、退出。</summary>
    private void OnMoreClicked(object? sender, RoutedEventArgs e)
    {
        if (_vm is null)
        {
            return;
        }

        var flyout = new MenuFlyout();
        flyout.Items.Add(CreateEdgeItem("停靠到上边缘", DockEdge.Top));
        flyout.Items.Add(CreateEdgeItem("停靠到下边缘", DockEdge.Bottom));
        flyout.Items.Add(CreateEdgeItem("停靠到左边缘（竖向）", DockEdge.Left));
        flyout.Items.Add(CreateEdgeItem("停靠到右边缘（竖向）", DockEdge.Right));
        flyout.Items.Add(new Separator());

        var settings = new MenuItem { Header = "设置…" };
        settings.Click += (_, _) => ShowSettings();
        flyout.Items.Add(settings);

        var check = new MenuItem { Header = "检查更新" };
        check.Click += (_, _) => _ = _vm.CheckUpdateAsync();
        flyout.Items.Add(check);

        flyout.Items.Add(new Separator());

        var exit = new MenuItem { Header = "退出" };
        exit.Click += (_, _) => Exit();
        flyout.Items.Add(exit);

        flyout.ShowAt(MoreButton);
    }

    private MenuItem CreateEdgeItem(string header, DockEdge edge)
    {
        var item = new MenuItem { Header = header };
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

    private void OnWindowResized(object? sender, SizeChangedEventArgs e) => ApplyPosition(_progress);
}
