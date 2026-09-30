using System;
using System.Reactive;
using System.Windows.Input;
using ReactiveUI;

namespace QuickApp.ViewModels;

/// <summary>
/// 托盘菜单的视图模型：菜单文案、开关状态与命令都收敛在这里，
/// App 只负责创建 NativeMenu 视图并把 Header / Command 绑定上去（原型 08 · 托盘菜单）。
/// </summary>
public sealed class TrayViewModel : ViewModelBase
{
    private readonly DockViewModel _dock;
    private readonly Action _showSettings;
    private readonly Action _exit;

    public TrayViewModel(DockViewModel dock, Action showSettings, Action exit)
    {
        _dock = dock;
        _showSettings = showSettings;
        _exit = exit;

        ToggleDockCommand = ReactiveCommand.Create(() => _dock.IsDockVisible = !_dock.IsDockVisible);
        ShowSettingsCommand = ReactiveCommand.Create(_showSettings);
        ExitCommand = ReactiveCommand.Create(_exit);
        ToggleAutoStartCommand = ReactiveCommand.Create(() =>
        {
            _dock.ToggleAutoStartCommand.Execute(null);
            this.RaisePropertyChanged(nameof(AutoStartText));
        });

        // Dock 显隐与钉住也可能在托盘之外变化（Dock 按钮/热键），文案跟随刷新
        _dock.WhenAnyValue(x => x.IsDockVisible, x => x.IsPinned)
            .Subscribe(_ =>
            {
                this.RaisePropertyChanged(nameof(ToggleDockText));
                this.RaisePropertyChanged(nameof(PinText));
            });
    }

    public ICommand ToggleDockCommand { get; }

    public ICommand ToggleAutoStartCommand { get; }

    public ICommand TogglePinCommand => _dock.TogglePinCommand;

    public ICommand ShowSettingsCommand { get; }

    public ICommand ExitCommand { get; }

    public string ToggleDockText => _dock.IsDockVisible ? "隐藏" : "显示";

    public string AutoStartText => "开机启动（" + (_dock.Settings.AutoStart ? "开" : "关") + "）";

    public string PinText => "钉住（" + (_dock.IsPinned ? "开" : "关") + "）";
}
