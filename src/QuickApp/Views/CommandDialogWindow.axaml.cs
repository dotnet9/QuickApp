using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using QuickApp.ViewModels;

namespace QuickApp.Views;

/// <summary>
/// 「添加命令行」小对话框：名称 + 命令，回车在两个输入框间推进。
/// 用 TaskCompletionSource 把结果交给调用方，不引入对话框框架。
/// </summary>
public partial class CommandDialogWindow : Window
{
    private readonly TaskCompletionSource<(string Name, string Command)?> _completion = new();
    private DockViewModel? _dock;

    public CommandDialogWindow()
    {
        InitializeComponent();
        CloseButton.Click += (_, _) => Close();
        CancelButton.Click += (_, _) => Close();
        SubmitButton.Click += OnSubmit;
        HeaderBar.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                BeginMoveDrag(e);
            }
        };
        NameBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                CommandBox.Focus();
                e.Handled = true;
            }
        };
        CommandBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                OnSubmit(this, new RoutedEventArgs());
                e.Handled = true;
            }
        };
        Opened += (_, _) => NameBox.Focus();
    }

    /// <summary>配色与设置窗口一致（直接用 Dock 的画刷与圆角）。</summary>
    public void Attach(DockViewModel dock)
    {
        _dock = dock;
        DataContext = dock;
    }

    /// <summary>展示对话框；返回 (名称, 命令)，取消返回 null。</summary>
    public static async Task<(string Name, string Command)?> ShowAsync(Window owner, DockViewModel dock)
    {
        var dialog = new CommandDialogWindow();
        dialog.Attach(dock);
        await dialog.ShowDialog(owner);
        return dialog._completion.Task.IsCompleted ? dialog._completion.Task.Result : null;
    }

    protected override void OnClosed(EventArgs e)
    {
        // 关闭即视为取消，保证 await 一定能返回
        _completion.TrySetResult(null);
        base.OnClosed(e);
    }

    private void OnSubmit(object? sender, RoutedEventArgs e)
    {
        string name = (NameBox.Text ?? string.Empty).Trim();
        string command = (CommandBox.Text ?? string.Empty).Trim();
        if (name.Length == 0 || command.Length == 0)
        {
            _dock?.Toast("名称和命令都要填写");
            return;
        }

        _completion.TrySetResult((name, command));
        Close();
    }
}
