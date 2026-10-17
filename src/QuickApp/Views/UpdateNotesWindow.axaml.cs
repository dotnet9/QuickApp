using System;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using CodeWF.Avalonia.Controls.Controls;
using QuickApp.ViewModels;

namespace QuickApp.Views;

/// <summary>共用的检查、下载和安装窗口。关闭窗口保留后台下载及已下载的安装包。</summary>
public partial class UpdateNotesWindow : CodeWFWindow
{
    private static UpdateNotesWindow? _current;
    private DockViewModel? _vm;

    public UpdateNotesWindow()
    {
        InitializeComponent();
        CancelButton.Click += (_, _) => Close();
        InstallButton.Click += OnInstallClick;
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
        };
    }

    public static void ShowFor(Window owner, DockViewModel dock)
    {
        var window = _current;
        if (window is null)
        {
            window = new UpdateNotesWindow();
            window._vm = dock;
            window.DataContext = dock;
            dock.PropertyChanged += window.OnDockPropertyChanged;
            window.UpdateState();
            _current = window;
        }

        bool sameOwner = ReferenceEquals(window.Owner, owner);
        if (window.IsVisible && !sameOwner) window.Hide();

        // Windows 会把 Dock 的原生置顶状态传给“关于”，但不会同步其 Topmost 属性，
        // 更深一层的模态窗口必须沿 owner 链显式继承，否则会被已禁用的“关于”挡住。
        window.Topmost = HasTopmostOwner(owner);
        if (!window.IsVisible) _ = window.ShowDialog(owner);
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Activate();
    }

    private static bool HasTopmostOwner(Window owner)
    {
        for (WindowBase? ancestor = owner; ancestor is not null; ancestor = ancestor.Owner)
            if (ancestor.Topmost) return true;
        return false;
    }

    private void OnDockPropertyChanged(object? sender, PropertyChangedEventArgs e) => UpdateState();

    protected override void OnClosed(EventArgs e)
    {
        if (_vm is not null)
        {
            _vm.PropertyChanged -= OnDockPropertyChanged;
            _vm = null;
        }
        if (ReferenceEquals(_current, this)) _current = null;
        base.OnClosed(e);
    }

    private void UpdateState()
    {
        if (_vm is null) return;

        bool checking = _vm.IsCheckingUpdate;
        bool installing = _vm.IsInstallingUpdate;
        bool ready = _vm.IsUpdateReady;
        bool unsupported = _vm.NeedsUpdatePage;
        bool downloading = _vm.IsDownloadingUpdate;

        Title = AppText.Get(Language.CheckUpdates);
        StatusText.Text = installing ? AppText.Get(Language.StartingInstaller) : _vm.UpdateResultText;
        CheckingBar.IsVisible = checking || installing;
        ReleaseDetails.IsVisible = _vm.UpdateAvailable && !checking;
        NotesText.Text = string.IsNullOrWhiteSpace(_vm.UpdateNotesText)
            ? AppText.Get(Language.NoReleaseNotes) : _vm.UpdateNotesText;
        InstallButton.Content = installing ? AppText.Get(Language.StartingInstaller)
            : ready ? _vm.UpdateInstallButtonText
            : unsupported ? AppText.Get(Language.OpenReleasePage)
            : _vm.UpdateAvailable ? AppText.Get(Language.Download) : AppText.Get(Language.CheckNow);
        InstallButton.IsEnabled = !checking && !installing && !downloading;
        CancelButton.IsEnabled = !installing;
        InstallBar.IsVisible = downloading || ready;
        DownloadBar.IsVisible = downloading;
        DownloadBar.IsIndeterminate = downloading && _vm.DownloadProgress <= 0;
        ProgressText.Text = _vm.DownloadProgressText;
        DownloadBar.Value = _vm.DownloadProgress;
        DownloadError.Text = _vm.UpdateActionError;
        DownloadError.IsVisible = !string.IsNullOrWhiteSpace(_vm.UpdateActionError);
        InstallNote.IsVisible = !unsupported;
        FallbackNote.IsVisible = unsupported;
    }

    private void OnInstallClick(object? sender, RoutedEventArgs e)
    {
        // 安装命令执行期间保留窗口及其视图模型，成功启动安装器后由宿主退出应用。
        var dock = _vm;
        if (dock is null || dock.IsCheckingUpdate || dock.IsInstallingUpdate || dock.IsDownloadingUpdate) return;
        if (dock.IsUpdateReady) dock.InstallUpdateCommand.Execute(null);
        else if (dock.NeedsUpdatePage) dock.OpenUpdateCommand.Execute(null);
        else if (dock.CanDownloadUpdate) dock.DownloadUpdateCommand.Execute(null);
        else dock.CheckUpdateCommand.Execute(null);
    }
}
