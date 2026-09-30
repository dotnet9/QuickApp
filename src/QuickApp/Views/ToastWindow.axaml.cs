using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using QuickApp.Core.Services;

namespace QuickApp.Views;

/// <summary>
/// 悬浮 Toast（对应原型 .toast-layer）：屏幕底部居中、任务栏上方 56px 处的浮层提示。
/// 单例复用：DockWindow 在 StatusMessage 变化时更新文案与动作按钮；
/// 下载态带底部 2px 进度条（.toast-progress），入/退场 180ms 淡入上浮 / 160ms 淡出下沉。
/// </summary>
public partial class ToastWindow : Window
{
    public ToastWindow()
    {
        InitializeComponent();
        ToastCard.RenderTransform = _slide;
        ActionButton.Click += (_, _) =>
        {
            if (ActionButton.Command?.CanExecute(null) == true)
            {
                ActionButton.Command.Execute(null);
            }
        };
        // SizeToContent 的尺寸在 Show 之后才稳定，常驻布局回调持续把窗口摆到底部居中
        LayoutUpdated += (_, _) => PositionAtBottomCenterCore();
    }

    private readonly TranslateTransform _slide = new();

    public void ShowToast(string message, string? actionLabel, System.Windows.Input.ICommand? command, double? progress = null)
    {
        MessageText.Text = message;
        if (actionLabel is not null && command is not null)
        {
            ActionButton.Content = actionLabel;
            ActionButton.Command = command;
            ActionButton.IsVisible = true;
        }
        else
        {
            ActionButton.IsVisible = false;
        }

        SetProgress(progress);

        if (!IsVisible)
        {
            Opacity = 0;
            Show();
            PlayEnterAnimation();
        }
    }

    /// <summary>更新下载进度条；null 隐藏（原型 .toast-downloading ↔ 普通 toast 的切换）。</summary>
    public void SetProgress(double? progress)
    {
        if (progress is { } value)
        {
            ProgressBar.Value = Math.Clamp(value, 0, 100);
            ProgressBar.IsVisible = true;
            // 原型 .toast-downloading padding-bottom:13px：进度条区（2px + 上距 5px）+ 底部余量
            ToastCard.Padding = new Thickness(14, 9, 14, 4);
        }
        else
        {
            ProgressBar.IsVisible = false;
            ToastCard.Padding = new Thickness(14, 9);
        }
    }

    public void HideToast()
    {
        if (IsVisible)
        {
            PlayExitAndHide();
        }
        else
        {
            IsVisible = false;
        }
    }

    private async void PlayEnterAnimation()
    {
        // 入场：180ms 淡入 + 上浮 8px（原型 toastIn）
        const int steps = 9;
        for (int i = 1; i <= steps; i++)
        {
            double t = i / (double)steps;
            Opacity = t;
            _slide.Y = 8 * (1 - t);
            await System.Threading.Tasks.Task.Delay(20);
        }

        Opacity = 1;
        _slide.Y = 0;
    }

    private async void PlayExitAndHide()
    {
        // 退场：160ms 淡出 + 下沉 6px（原型 .toast.hide）
        try
        {
            const int steps = 8;
            for (int i = 1; i <= steps; i++)
            {
                double t = i / (double)steps;
                Opacity = 1 - t;
                _slide.Y = 6 * t;
                await System.Threading.Tasks.Task.Delay(20);
            }
        }
        catch
        {
            // 退场动画被打断时直接隐藏
        }

        IsVisible = false;
        Opacity = 1;
        _slide.Y = 0;
    }

    private void PositionAtBottomCenterCore()
    {
        if (ClientSize.Width <= 0)
        {
            return;
        }

        Avalonia.Platform.Screen? screen;
        try
        {
            screen = Screens?.Primary;
        }
        catch
        {
            return;
        }

        if (screen is null)
        {
            return;
        }

        double scaling = screen.Scaling <= 0 ? 1 : screen.Scaling;
        Avalonia.PixelRect work = screen.WorkingArea;
        int width = (int)(ClientSize.Width * scaling);
        int height = (int)(ClientSize.Height * scaling);
        int x = work.X + (work.Width - width) / 2;
        int y = work.Bottom - height - (int)(56 * scaling);
        Position = new PixelPoint(x, y);
    }
}
