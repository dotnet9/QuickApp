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
    private string _configFile;
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
                // 滚动备份：config.1 最新……config.4 最旧，加上当前文件共 5 个时间点
                RotateRollingBackups();
                File.Replace(temp, _configFile, AppPaths.ConfigRollingBackupFile(_configFile, 1), ignoreMetadataErrors: true);
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

    private void RotateRollingBackups()
    {
        for (int i = AppPaths.RollingBackupCount; i >= 2; i--)
        {
            string src = AppPaths.ConfigRollingBackupFile(_configFile, i - 1);
            if (File.Exists(src))
            {
                File.Move(src, AppPaths.ConfigRollingBackupFile(_configFile, i), overwrite: true);
            }
        }
    }

    /// <summary>
    /// 切换安装版/便携版：写入或移除 portable.txt，把当前配置（含滚动备份）复制到新模式的位置，
    /// 存储器随即指向新路径。成功返回 null，失败返回错误信息（标记与配置路径回滚到切换前）。
    /// </summary>
    public string? SwitchStorageMode(bool toPortable, string baseDirectory)
    {
        string oldFile = _configFile;
        string marker = Path.Combine(baseDirectory, AppPaths.PortableMarker);
        bool wasPortable = File.Exists(marker);
        try
        {
            if (toPortable)
            {
                File.WriteAllText(marker, string.Empty);
            }
            else if (wasPortable)
            {
                File.Delete(marker);
            }

            _configFile = AppPaths.ConfigFile(baseDirectory);
            if (_configFile == oldFile)
            {
                return null;
            }

            string? dir = Path.GetDirectoryName(_configFile);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            if (!File.Exists(_configFile) && File.Exists(oldFile))
            {
                File.Copy(oldFile, _configFile);
            }

            for (int i = 1; i <= AppPaths.RollingBackupCount; i++)
            {
                string src = AppPaths.ConfigRollingBackupFile(oldFile, i);
                string dst = AppPaths.ConfigRollingBackupFile(_configFile, i);
                if (File.Exists(src) && !File.Exists(dst))
                {
                    File.Copy(src, dst);
                }
            }

            _log?.Invoke("存储模式已切换：" + _configFile);
            return null;
        }
        catch (Exception ex)
        {
            // 回滚：标记与配置路径恢复到切换前
            try
            {
                bool nowPortable = File.Exists(marker);
                if (nowPortable != wasPortable)
                {
                    if (wasPortable)
                    {
                        File.WriteAllText(marker, string.Empty);
                    }
                    else
                    {
                        File.Delete(marker);
                    }
                }

                _configFile = AppPaths.ConfigFile(baseDirectory);
            }
            catch
            {
                // 回滚失败时保留原路径，至少当前会话还能读写
            }

            return ex.Message;
        }
    }

    /// <summary>导出配置到指定文件（同样的 JSON 格式），用于设置窗口的「导出」。</summary>
    public bool Export(AppConfig config, string filePath)
    {
        try
        {
            string json = System.Text.Json.JsonSerializer.Serialize(config, AppJsonContext.Default.AppConfig);
            File.WriteAllText(filePath, json);
            return true;
        }
        catch (Exception ex)
        {
            _log?.Invoke($"导出配置失败：{ex.Message}");
            return false;
        }
    }

    /// <summary>从文件读入一份配置并做规范化；文件无效返回 null。</summary>
    public AppConfig? Import(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return null;
            }

            string text = File.ReadAllText(filePath);
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            AppConfig? config = System.Text.Json.JsonSerializer.Deserialize(text, AppJsonContext.Default.AppConfig);
            return config is null ? null : Normalize(config);
        }
        catch (Exception ex)
        {
            _log?.Invoke($"导入配置失败：{ex.Message}");
            return null;
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
        if (string.IsNullOrWhiteSpace(config.Settings.Hotkey))
        {
            config.Settings.Hotkey = "Ctrl+Alt+Space";
        }

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
