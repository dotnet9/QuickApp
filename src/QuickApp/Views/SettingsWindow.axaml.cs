using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using QuickApp.ViewModels;

namespace QuickApp.Views;

public partial class SettingsWindow : Window
{
    private SettingsViewModel? _vm;

    public SettingsWindow()
    {
        InitializeComponent();
        AddCmdButton.Click += OnAddCommand;
        AddFileButton.Click += OnAddFiles;
        CloseButton.Click += (_, _) => Close();
        HeaderBar.PointerPressed += OnHeaderPressed;
    }

    /// <summary>自绘标题栏拖动整窗（无系统装饰时用它替代标题栏）。</summary>
    private void OnHeaderPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    public void Attach(DockViewModel dock)
    {
        _vm = new SettingsViewModel(dock);
        DataContext = _vm;
    }

    private void OnAddCommand(object? sender, RoutedEventArgs e)
    {
        if (_vm is null)
        {
            return;
        }

        string name = CmdName.Text ?? string.Empty;
        string command = CmdValue.Text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(command))
        {
            return;
        }

        _vm.AddCommand(name, command);
        CmdName.Text = string.Empty;
        CmdValue.Text = string.Empty;
    }

    private async void OnAddFiles(object? sender, RoutedEventArgs e)
    {
        if (_vm is null)
        {
            return;
        }

        try
        {
            IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "选择要添加的程序或文件",
                AllowMultiple = true
            });

            var paths = new List<string>();
            foreach (IStorageFile file in files)
            {
                if (!string.IsNullOrWhiteSpace(file.Path?.LocalPath))
                {
                    paths.Add(file.Path!.LocalPath);
                }
            }

            if (paths.Count > 0)
            {
                _vm.AddFiles(paths);
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("选择文件失败", ex);
        }
    }
}
