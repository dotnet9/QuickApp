using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using QuickApp.Core.Models;
using QuickApp.Core.Services;
using QuickApp.ViewModels;

namespace QuickApp.Views;

/// <summary>统一新增与编辑快捷项；所有输入先写草稿，取消不改原配置。</summary>
public partial class ItemDialogWindow : Window
{
    private readonly TaskCompletionSource<LauncherItem?> _completion = new();
    private DockViewModel? _dock;
    private LauncherItem _draft = new();

    public ItemDialogWindow()
    {
        InitializeComponent();
        CloseButton.Click += (_, _) => Close();
        CancelButton.Click += (_, _) => Close();
        SubmitButton.Click += OnSubmit;
        KindBox.SelectionChanged += (_, _) => UpdateFields();
        ClearHotkeyButton.Click += (_, _) => HotkeyBox.Text = string.Empty;
        HotkeyBox.KeyDown += CaptureHotkey;
        BrowseFileButton.Click += async (_, _) => await PickFileAsync();
        BrowseFolderButton.Click += async (_, _) => await PickFolderAsync(TargetBox);
        BrowseWorkingDirectoryButton.Click += async (_, _) => await PickFolderAsync(WorkingDirectoryBox);
        HeaderBar.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e);
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { Close(); e.Handled = true; }
            else if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.Control)
            { OnSubmit(this, new RoutedEventArgs()); e.Handled = true; }
        };
        NameBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { TargetBox.Focus(); e.Handled = true; }
        };
        Opened += (_, _) => NameBox.Focus();
    }

    public void Attach(DockViewModel dock, LauncherItem item, bool editing)
    {
        _dock = dock;
        DataContext = dock;
        _draft = LauncherItemEditor.Copy(item);
        Title = TitleText.Text = editing ? "编辑快捷项" : "添加快捷项";
        SubmitText.Text = editing ? "保存" : "添加";
        NameBox.Text = item.Name;
        TargetBox.Text = item.Target;
        ArgumentsBox.Text = item.Arguments;
        WorkingDirectoryBox.Text = item.WorkingDirectory;
        HotkeyBox.Text = item.Hotkey;
        ShellBox.SelectedIndex = item.UsePowerShell ? 1 : 0;
        TerminalBox.IsChecked = item.RunInTerminal;
        KindBox.SelectedIndex = (int)item.Kind;
        HotkeyBox.IsEnabled = ClearHotkeyButton.IsEnabled = OperatingSystem.IsWindows();
        if (!OperatingSystem.IsWindows()) HotkeyNote.Text = "当前平台暂不支持全局快捷键；已有配置会保留。";
        UpdateFields();
    }

    public static async Task<LauncherItem?> ShowAsync(Window owner, DockViewModel dock, LauncherItem item, bool editing = false)
    {
        var dialog = new ItemDialogWindow();
        dialog.Attach(dock, item, editing);
        var screen = owner.Screens.ScreenFromWindow(owner);
        if (screen is not null) dialog.FormScroll.MaxHeight = Math.Max(180, Math.Min(440, screen.WorkingArea.Height / screen.Scaling - 190));
        await dialog.ShowDialog(owner);
        return dialog._completion.Task.IsCompleted ? dialog._completion.Task.Result : null;
    }

    protected override void OnClosed(EventArgs e)
    {
        _completion.TrySetResult(null);
        base.OnClosed(e);
    }

    private void UpdateFields()
    {
        ItemKind kind = (ItemKind)Math.Max(0, KindBox.SelectedIndex);
        bool app = kind == ItemKind.App, command = kind == ItemKind.Command;
        TargetLabel.Text = command ? "命令" : kind == ItemKind.Web ? "网址" : "路径或程序名";
        TargetBox.PlaceholderText = command ? "例如：git status" : kind == ItemKind.Web ? "例如：https://dotnet9.com" : @"例如：C:\Work 或 explorer.exe";
        ArgumentsPanel.IsVisible = BrowseFileButton.IsVisible = BrowseFolderButton.IsVisible = app;
        WorkingDirectoryPanel.IsVisible = kind != ItemKind.Web;
        CommandOptions.IsVisible = command && OperatingSystem.IsWindows();
        PlatformNote.IsVisible = command && !OperatingSystem.IsWindows();
    }

    private void CaptureHotkey(object? sender, KeyEventArgs e)
    {
        e.Handled = true;
        if (e.Key == Key.Escape) { Close(); return; }
        if (e.KeyModifiers == KeyModifiers.None && e.Key is Key.Back or Key.Delete) { HotkeyBox.Text = string.Empty; return; }
        if (e.Key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return;
        if (e.KeyModifiers == KeyModifiers.None) { ShowError("快捷键需要 Ctrl、Alt、Shift 或 Win 修饰键。"); return; }
        string key = e.Key is >= Key.D0 and <= Key.D9 ? ((int)e.Key - (int)Key.D0).ToString() : e.Key.ToString();
        string gesture = (e.KeyModifiers.HasFlag(KeyModifiers.Control) ? "Ctrl+" : string.Empty)
            + (e.KeyModifiers.HasFlag(KeyModifiers.Alt) ? "Alt+" : string.Empty)
            + (e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? "Shift+" : string.Empty)
            + (e.KeyModifiers.HasFlag(KeyModifiers.Meta) ? "Win+" : string.Empty) + key;
        if (!HotkeyGesture.TryParse(gesture, out _, out string? error)) { ShowError(error!); return; }
        HotkeyBox.Text = gesture;
        ErrorText.IsVisible = false;
    }

    private async Task PickFileAsync()
    {
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "选择程序或文件" });
            if (files.Count > 0) TargetBox.Text = files[0].TryGetLocalPath();
        }
        catch (Exception ex) { ShowError("选择文件失败：" + ex.Message); }
    }

    private async Task PickFolderAsync(TextBox destination)
    {
        try
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "选择目录" });
            if (folders.Count > 0) destination.Text = folders[0].TryGetLocalPath();
        }
        catch (Exception ex) { ShowError("选择目录失败：" + ex.Message); }
    }

    private void OnSubmit(object? sender, RoutedEventArgs e)
    {
        var draft = LauncherItemEditor.Copy(_draft);
        draft.Kind = (ItemKind)Math.Max(0, KindBox.SelectedIndex);
        draft.Name = NameBox.Text ?? string.Empty;
        draft.Target = TargetBox.Text ?? string.Empty;
        draft.Arguments = ArgumentsBox.Text;
        draft.WorkingDirectory = WorkingDirectoryBox.Text;
        draft.Hotkey = HotkeyBox.Text;
        draft.UsePowerShell = ShellBox.SelectedIndex == 1;
        draft.RunInTerminal = TerminalBox.IsChecked == true;
        string? error = _dock?.ValidateItemDraft(draft);
        if (error is not null) { ShowError(error); return; }
        _completion.TrySetResult(draft);
        Close();
    }

    private void ShowError(string error) { ErrorText.Text = error; ErrorText.IsVisible = true; }
}
