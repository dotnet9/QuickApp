using System;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using CodeWF.Avalonia.Controls.Controls;
using QuickApp.ViewModels;

namespace QuickApp.Views;

/// <summary>
/// 发现新版本后的更新说明卡片：已安装版本 / 最新版本 / 更新说明正文，
/// 底部「安装」按当前状态显示为 下载 → 下载中（带进度）→ 安装；
/// 没有匹配本机安装包时按钮变成「打开发布页」，不会下载错平台的文件。
/// </summary>
public partial class UpdateNotesWindow : CodeWFWindow
{
    private DockViewModel? _vm;
    private bool _closing;

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

    /// <summary>由 Dock 或设置窗口在检查结束后弹出；没有可安装的版本时不打开。</summary>
    public static void ShowFor(Window owner, DockViewModel dock)
    {
        if (!dock.UpdateAvailable)
        {
            return;
        }

        var window = new UpdateNotesWindow();
        window.Attach(dock);
        window.ShowDialog(owner);
    }

    private void Attach(DockViewModel dock)
    {
        _vm = dock;
        DataContext = dock;
        NotesText.Text = string.IsNullOrWhiteSpace(dock.UpdateNotesText)
            ? "该版本没有提供更新说明，可点「打开发布页」查看。"
            : dock.UpdateNotesText;
        dock.PropertyChanged += OnDockPropertyChanged;
        UpdateState();
    }

    private void OnDockPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DockViewModel.IsDownloadingUpdate)
            or nameof(DockViewModel.IsUpdateReady)
            or nameof(DockViewModel.DownloadProgress)
            or nameof(DockViewModel.DownloadProgressText)
            or nameof(DockViewModel.CanDownloadUpdate)
            or nameof(DockViewModel.NeedsUpdatePage))
        {
            UpdateState();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_vm is not null)
        {
            _vm.PropertyChanged -= OnDockPropertyChanged;
            _vm = null;
        }

        base.OnClosed(e);
    }

    private void UpdateState()
    {
        if (_vm is null)
        {
            return;
        }

        bool ready = _vm.IsUpdateReady;
        bool unsupported = _vm.NeedsUpdatePage;
        bool downloading = _vm.IsDownloadingUpdate;
        bool canDownload = _vm.CanDownloadUpdate;

        InstallButton.Content = ready ? _vm.UpdateInstallButtonText : unsupported ? "打开发布页" : "下载";
        InstallButton.IsEnabled = !downloading && (ready || canDownload || unsupported);
        InstallBar.IsVisible = downloading || _vm.DownloadProgress > 0 || ready;
        DownloadBar.IsVisible = !ready;
        ProgressText.Text = _vm.DownloadProgressText;
        DownloadBar.Value = _vm.DownloadProgress;
        DownloadError.IsVisible = !downloading && !ready && !unsupported && !canDownload;
        InstallNote.IsVisible = !ready && !unsupported;
        FallbackNote.IsVisible = unsupported;
    }

    private void OnInstallClick(object? sender, RoutedEventArgs e)
    {
        if (_vm is null)
        {
            return;
        }

        if (_vm.IsDownloadingUpdate)
        {
            return;
        }

        if (_vm.IsUpdateReady)
        {
            // 打开安装程序后主程序会退出，先把卡片收掉，避免它跟着一起消失得突兀
            _closing = true;
            Close();
            _vm.InstallUpdateCommand.Execute(null);
            return;
        }

        if (_vm.NeedsUpdatePage)
        {
            Close();
            _vm.OpenUpdateCommand.Execute(null);
            return;
        }

        if (_vm.CanDownloadUpdate)
        {
            _vm.DownloadUpdateCommand.Execute(null);
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // 关掉卡片等于放弃这次更新：停掉下载 / 打开安装程序时不算取消
        if (!_closing)
        {
            _vm?.CancelUpdateDownloadCommand.Execute(null);
        }

        base.OnClosing(e);
    }
}
