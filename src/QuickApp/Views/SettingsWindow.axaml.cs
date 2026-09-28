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
        ExportButton.Click += OnExportConfig;
        ImportButton.Click += OnImportConfig;
        CloseButton.Click += (_, _) => Close();
        HeaderBar.PointerPressed += OnHeaderPressed;
    }

    /// <summary>切换设置分类；由 Dock 的“关于”入口直接打开对应 Tab。</summary>
    public void ShowTab(string? tab)
    {
        SettingsTabs.SelectedIndex = tab?.ToLowerInvariant() switch
        {
            "appearance" => 1,
            "data" => 2,
            "about" => 3,
            _ => 0
        };
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

    private async void OnExportConfig(object? sender, RoutedEventArgs e)
    {
        if (_vm is null)
        {
            return;
        }

        try
        {
            IStorageFile? file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "导出 QuickApp 配置",
                SuggestedFileName = "quickapp-config",
                DefaultExtension = "json",
                FileTypeChoices = new[]
                {
                    new FilePickerFileType("QuickApp 配置") { Patterns = new[] { "*.json" } }
                }
            });

            if (file is null)
            {
                return;
            }

            string? path = file.Path?.LocalPath;
            if (!string.IsNullOrWhiteSpace(path) && _vm.ExportConfigTo(path))
            {
                AppLog.Info("配置已导出：" + path);
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("导出配置失败", ex);
        }
    }

    private async void OnImportConfig(object? sender, RoutedEventArgs e)
    {
        if (_vm is null)
        {
            return;
        }

        try
        {
            IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "导入 QuickApp 配置",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("QuickApp 配置") { Patterns = new[] { "*.json" } },
                    FilePickerFileTypes.All
                }
            });

            if (files.Count > 0 && !string.IsNullOrWhiteSpace(files[0].Path?.LocalPath))
            {
                if (_vm.ImportConfigFrom(files[0].Path!.LocalPath))
                {
                    _vm.RefreshFromDock();
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Error("导入配置失败", ex);
        }
    }

}
