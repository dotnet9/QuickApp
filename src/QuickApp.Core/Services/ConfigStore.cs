using System;
using System.Collections.Generic;
using System.IO;
using QuickApp.Core.Json;
using QuickApp.Core.Models;

namespace QuickApp.Core.Services;

/// <summary>
/// 配置读写。写入走「临时文件 + 原子替换」，并在替换前留 .bak；解析失败时把坏文件改名保留，返回默认配置。
/// 任何异常都不向上抛，保证程序永远能启动。
/// </summary>
public sealed class ConfigStore
{
    private readonly string _configFile;
    private readonly Action<string>? _log;

    public ConfigStore(string baseDirectory, Action<string>? log = null)
    {
        _configFile = AppPaths.ConfigFile(baseDirectory);
        _log = log;
    }

    public string ConfigFile => _configFile;

    public AppConfig Load()
    {
        try
        {
            if (!File.Exists(_configFile))
            {
                // 首次运行：把默认配置落盘，设置窗口里显示的路径才真实存在
                AppConfig defaults = CreateDefault();
                Save(defaults);
                return defaults;
            }

            string text = File.ReadAllText(_configFile);
            if (string.IsNullOrWhiteSpace(text))
            {
                return CreateDefault();
            }

            AppConfig? config = System.Text.Json.JsonSerializer.Deserialize(
                text, AppJsonContext.Default.AppConfig);

            return config is null ? CreateDefault() : Normalize(config);
        }
        catch (Exception ex)
        {
            _log?.Invoke($"读取配置失败，已回退默认配置：{ex.Message}");
            TryQuarantineBrokenFile();
            return CreateDefault();
        }
    }

    public bool Save(AppConfig config)
    {
        try
        {
            string? dir = Path.GetDirectoryName(_configFile);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string json = System.Text.Json.JsonSerializer.Serialize(config, AppJsonContext.Default.AppConfig);
            string temp = _configFile + ".tmp";
            File.WriteAllText(temp, json);

            if (File.Exists(_configFile))
            {
                File.Replace(temp, _configFile, AppPaths.ConfigBackupFile(_configFile), ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temp, _configFile);
            }

            return true;
        }
        catch (Exception ex)
        {
            _log?.Invoke($"保存配置失败：{ex.Message}");
            return false;
        }
    }

    /// <summary>补齐字段、去重 Id、清掉非法项，避免坏配置把 UI 带崩。</summary>
    public static AppConfig Normalize(AppConfig config)
    {
        config.Settings ??= new AppSettings();
        config.Items ??= new List<LauncherItem>();

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int index = 0;
        var cleaned = new List<LauncherItem>(config.Items.Count);
        foreach (LauncherItem item in config.Items)
        {
            if (item is null || string.IsNullOrWhiteSpace(item.Target))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(item.Id) || !seen.Add(item.Id))
            {
                item.Id = "i" + index++.ToString("x8");
                seen.Add(item.Id);
            }

            item.Name = string.IsNullOrWhiteSpace(item.Name)
                ? ItemQuery.ResolveDisplayName(item)
                : item.Name.Trim();
            cleaned.Add(item);
        }

        config.Items = cleaned;
        config.Settings.TileSize = Math.Clamp(config.Settings.TileSize, 24, 96);
        config.Settings.CornerRadius = Math.Clamp(config.Settings.CornerRadius, 0, 40);
        config.Settings.PanelOpacity = Math.Clamp(config.Settings.PanelOpacity, 0.2, 1);
        config.Settings.AutoHideDelayMs = Math.Clamp(config.Settings.AutoHideDelayMs, 0, 5000);
        return config;
    }

    /// <summary>首次运行的种子项：几个 Windows 内置程序，方便立刻看到效果。</summary>
    public static AppConfig CreateDefault()
    {
        var config = new AppConfig();
        config.Items.AddRange(new[]
        {
            new LauncherItem { Id = "seed-explorer", Name = "文件资源管理器", Kind = ItemKind.App, Target = "explorer.exe" },
            new LauncherItem { Id = "seed-notepad", Name = "记事本", Kind = ItemKind.App, Target = "notepad.exe" },
            new LauncherItem { Id = "seed-calc", Name = "计算器", Kind = ItemKind.App, Target = "calc.exe" },
            new LauncherItem { Id = "seed-cmd", Name = "命令提示符", Kind = ItemKind.Command, Target = "start cmd.exe" }
        });
        return config;
    }

    private void TryQuarantineBrokenFile()
    {
        try
        {
            if (!File.Exists(_configFile))
            {
                return;
            }

            string broken = _configFile + ".broken";
            if (File.Exists(broken))
            {
                File.Delete(broken);
            }

            File.Move(_configFile, broken);
            _log?.Invoke($"原配置已保留为 {broken}");
        }
        catch
        {
            // 隔离失败不影响启动
        }
    }
}
