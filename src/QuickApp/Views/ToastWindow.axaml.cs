using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using QuickApp.Core.Services;

namespace QuickApp.Views;

/// <summary>
/// 悬浮 Toast（对应原型 .toast-layer）：屏幕底部居中、任务栏上方 56px 处的浮层提示。
/// 单例复用：DockWindow 在 StatusMessage 变化时更新文案与动作按钮。
/// </summary>
public partial class ToastWindow : Window
{
    public ToastWindow()
    {
        InitializeComponent();
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

    public void ShowToast(string message, string? actionLabel, System.Windows.Input.ICommand? command)
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

        if (!IsVisible)
        {
            Show();
        }
    }

    public void HideToast() => IsVisible = false;

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
