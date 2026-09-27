using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using QuickApp.Core.Models;
using QuickApp.Core.Services;
using QuickApp.Platform.Windows;
using QuickApp.ViewModels;

namespace QuickApp.Views;

/// <summary>
/// Dock 主窗口：无边框、置顶、不占任务栏，贴着所选屏幕边缘。
/// 窗口定位与收起动画是「视图」职责，放在这里；数学部分在 Core 的 DockPlacement（可单测）。
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

        AddButton.Click += OnAddClicked;
        MoreButton.Click += OnMoreClicked;

        Opened += (_, _) =>
        {
            ApplyEdge(viewModel.Settings.Edge);
            _edgeTimer.Start();
        };

        Closed += (_, _) => _edgeTimer.Stop();
    }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e) => ScheduleReposition();

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

    // ---------------- 交互 ----------------

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
