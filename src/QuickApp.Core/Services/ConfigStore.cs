using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
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
        // 旧版安装版配置在 %APPDATA%，先一次性迁到 %LOCALAPPDATA% 再解析路径
        AppPaths.MigrateLegacyConfig(baseDirectory, log);
        _configFile = AppPaths.ConfigFile(baseDirectory);
        _log = log;
    }

    public string ConfigFile => _configFile;

    /// <summary>读取失败时保留原因，禁止默认空配置覆盖原数据；重新加载或明确导入后解除。</summary>
    public string? LastLoadError { get; private set; }

    public AppConfig Load()
    {
        LastLoadError = null;
        try
        {
            string text = File.ReadAllText(_configFile);
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new System.Text.Json.JsonException("配置文件为空。");
            }

            AppConfig? config = System.Text.Json.JsonSerializer.Deserialize(
                text, AppJsonContext.Default.AppConfig);

            if (config is null)
            {
                throw new System.Text.Json.JsonException("配置内容不是有效的对象。");
            }

            AppConfig normalized = Normalize(config);
            if (DropForeignSeedItems(normalized))
            {
                // 清掉了历史版本预置的内置程序条目，回写一次避免每次启动重复处理
                Save(normalized);
            }

            return normalized;
        }
        catch (FileNotFoundException)
        {
            return CreateFirstRunConfig();
        }
        catch (DirectoryNotFoundException)
        {
            return CreateFirstRunConfig();
        }
        catch (Exception ex)
        {
            LastLoadError = ex.Message;
            _log?.Invoke($"读取配置失败（{_configFile}），已阻止空配置覆盖原文件：{ex.Message}");
            // 只有确实无法解析的 JSON 才隔离；权限、共享锁等 I/O 问题不代表文件损坏。
            if (ex is System.Text.Json.JsonException) TryQuarantineBrokenFile();
            return CreateDefault();
        }
    }

    private AppConfig CreateFirstRunConfig()
    {
        AppConfig defaults = CreateDefault();
        if (File.Exists(_configFile + ".broken"))
        {
            LastLoadError = "配置文件缺失，原内容保留在 " + _configFile + ".broken，请恢复有效配置或导入备份。";
            _log?.Invoke(LastLoadError);
            return defaults;
        }
        if (!Save(defaults)) LastLoadError = "无法创建配置文件，请检查目录权限后重试。";
        return defaults;
    }

    public bool Save(AppConfig config, bool overwriteAfterLoadFailure = false)
    {
        if (LastLoadError is not null && !overwriteAfterLoadFailure)
        {
            _log?.Invoke("配置读取尚未恢复，取消保存以保护已有数据。" );
            return false;
        }
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

            LastLoadError = null;
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
    /// 切换安装版/便携版：写入或移除 portable.txt，把当前配置（含滚动备份与图标库）复制到新模式的位置，
    /// 并把条目里指向旧图标库的路径映射到新位置。
    /// 成功返回 null，失败返回错误信息（标记与配置路径回滚到切换前）。
    /// </summary>
    public string? SwitchStorageMode(bool toPortable, string baseDirectory, AppConfig? config = null)
    {
        if (LastLoadError is not null) return "配置读取失败，请先重新加载或导入有效配置，再切换存储位置。";
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

            CopyIconLibrary(oldFile, _configFile);
            RemapIconLibraryPaths(config, oldFile, _configFile);

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

    /// <summary>自定义图标库目录（配置文件旁的 icons 子目录）。</summary>
    public string IconLibraryDirectory
        => AppPaths.IconLibraryDirectory(Path.GetDirectoryName(_configFile) ?? ".");

    /// <summary>
    /// 把用户选的图片复制进图标库（按条目 Id 命名，重复更换自动覆盖）。
    /// 返回入库后的路径；来源已在库内时原样返回；失败返回 null 并把原因写入 error。
    /// </summary>
    public string? ImportCustomIcon(string sourcePath, string itemId, out string? error)
    {
        error = null;
        try
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            {
                error = "图片不存在";
                return null;
            }

            string library = IconLibraryDirectory;
            if (IsUnderDirectory(sourcePath, library))
            {
                return sourcePath;
            }

            Directory.CreateDirectory(library);
            string destination = Path.Combine(library, itemId + Path.GetExtension(sourcePath));
            File.Copy(sourcePath, destination, overwrite: true);

            // 同 Id 换过扩展名时清掉旧版本，库里一个条目只留一份图标
            foreach (string stale in Directory.GetFiles(library, itemId + ".*"))
            {
                if (!string.Equals(stale, destination, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(stale);
                }
            }

            return destination;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            _log?.Invoke($"图标入库失败：{ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 删除图标库里没有被任何条目引用的文件。只在启动和导入后调用：
    /// 会话内的「移除可撤销」不删文件，撤销后图标还在。
    /// </summary>
    public void CleanupOrphanedIcons(IReadOnlyList<LauncherItem> items)
    {
        try
        {
            string library = IconLibraryDirectory;
            if (!Directory.Exists(library))
            {
                return;
            }

            var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (LauncherItem item in items)
            {
                if (!string.IsNullOrWhiteSpace(item.CustomIconPath))
                {
                    referenced.Add(Path.GetFileName(item.CustomIconPath));
                }
            }

            foreach (string file in Directory.GetFiles(library))
            {
                if (!referenced.Contains(Path.GetFileName(file)))
                {
                    File.Delete(file);
                }
            }
        }
        catch (Exception ex)
        {
            _log?.Invoke($"清理图标库失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 导出配置到 .qa 文件（zip 容器：config.json + 图标库），用于设置窗口的「导出」。
    /// 库内图标在 JSON 里只记文件名，跨机器导入时映射回本地图标库；
    /// 库外路径（如 MSIX 包 logo）原样保留，跨机器失效时由导入清理。
    /// </summary>
    public bool Export(AppConfig config, string filePath)
    {
        try
        {
            string library = IconLibraryDirectory;
            var bundled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var exportItems = new List<LauncherItem>(config.Items.Count);
            foreach (LauncherItem item in config.Items)
            {
                string? custom = item.CustomIconPath;
                if (custom is not null && IsUnderDirectory(custom, library) && File.Exists(custom))
                {
                    string fileName = Path.GetFileName(custom);
                    custom = fileName;
                    bundled.Add(fileName);
                }

                LauncherItem exported = LauncherItemEditor.Copy(item);
                exported.CustomIconPath = custom;
                exportItems.Add(exported);
            }

            var exportConfig = new AppConfig
            {
                SchemaVersion = config.SchemaVersion,
                Settings = config.Settings,
                Items = exportItems
            };

            using FileStream stream = File.Create(filePath);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
            WriteTextEntry(archive, "config.json",
                System.Text.Json.JsonSerializer.Serialize(exportConfig, AppJsonContext.Default.AppConfig));

            foreach (string fileName in bundled)
            {
                using Stream source = File.OpenRead(Path.Combine(library, fileName));
                using Stream target = archive.CreateEntry("icons/" + fileName, CompressionLevel.Optimal).Open();
                source.CopyTo(target);
            }

            return true;
        }
        catch (Exception ex)
        {
            _log?.Invoke($"导出配置失败：{ex.Message}");
            return false;
        }
    }

    /// <summary>从 .qa 文件（zip 容器）读入配置：还原图标库并映射回本机路径；文件无效返回 null。</summary>
    public AppConfig? Import(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
            {
                return null;
            }

            using FileStream stream = File.OpenRead(filePath);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

            ZipArchiveEntry? configEntry = archive.GetEntry("config.json");
            if (configEntry is null)
            {
                return null;
            }

            string json;
            using (var reader = new StreamReader(configEntry.Open()))
            {
                json = reader.ReadToEnd();
            }

            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            AppConfig? config = System.Text.Json.JsonSerializer.Deserialize(json, AppJsonContext.Default.AppConfig);
            if (config is null)
            {
                return null;
            }

            ExtractIconLibrary(archive);
            RemapImportedIcons(config);
            return Normalize(config);
        }
        catch (Exception ex)
        {
            _log?.Invoke($"导入配置失败：{ex.Message}");
            return null;
        }
    }

    /// <summary>解包 icons/ 下的图标到本地图标库；只取文件名，防 zip 路径穿越。</summary>
    private void ExtractIconLibrary(ZipArchive archive)
    {
        string library = IconLibraryDirectory;
        Directory.CreateDirectory(library);
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            if (!entry.FullName.StartsWith("icons/", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string fileName = Path.GetFileName(entry.FullName);
            if (fileName.Length == 0)
            {
                continue;
            }

            entry.ExtractToFile(Path.Combine(library, fileName), overwrite: true);
        }
    }

    /// <summary>导入配置里的图标路径映射回本机：库内文件名还原成本地路径；库外路径本机已失效的清掉，走自动图标提取。</summary>
    private void RemapImportedIcons(AppConfig config)
    {
        string library = IconLibraryDirectory;
        foreach (LauncherItem item in config.Items)
        {
            string? custom = item.CustomIconPath;
            if (string.IsNullOrWhiteSpace(custom))
            {
                continue;
            }

            if (Path.IsPathRooted(custom))
            {
                item.CustomIconPath = File.Exists(custom) ? custom : null;
            }
            else
            {
                string candidate = Path.Combine(library, Path.GetFileName(custom));
                item.CustomIconPath = File.Exists(candidate) ? candidate : null;
            }
        }
    }

    private static void WriteTextEntry(ZipArchive archive, string name, string content)
    {
        using var writer = new StreamWriter(archive.CreateEntry(name, CompressionLevel.Optimal).Open());
        writer.Write(content);
    }

    /// <summary>存储模式切换时把图标库整体搬到新配置目录（尽力而为，缺文件不阻断切换）。</summary>
    private static void CopyIconLibrary(string oldConfigFile, string newConfigFile)
    {
        string oldLibrary = Path.Combine(Path.GetDirectoryName(oldConfigFile) ?? ".", "icons");
        string newLibrary = Path.Combine(Path.GetDirectoryName(newConfigFile) ?? ".", "icons");
        if (!Directory.Exists(oldLibrary))
        {
            return;
        }

        Directory.CreateDirectory(newLibrary);
        foreach (string file in Directory.GetFiles(oldLibrary))
        {
            File.Copy(file, Path.Combine(newLibrary, Path.GetFileName(file)), overwrite: true);
        }
    }

    /// <summary>存储模式切换后，把条目里指向旧图标库的绝对路径改指新库；库外路径不动。</summary>
    private static void RemapIconLibraryPaths(AppConfig? config, string oldConfigFile, string newConfigFile)
    {
        if (config is null)
        {
            return;
        }

        string oldLibrary = Path.Combine(Path.GetDirectoryName(oldConfigFile) ?? ".", "icons");
        string newLibrary = Path.Combine(Path.GetDirectoryName(newConfigFile) ?? ".", "icons");
        foreach (LauncherItem item in config.Items)
        {
            string? custom = item.CustomIconPath;
            if (custom is null || !IsUnderDirectory(custom, oldLibrary))
            {
                continue;
            }

            string candidate = Path.Combine(newLibrary, Path.GetFileName(custom));
            item.CustomIconPath = File.Exists(candidate) ? candidate : custom;
        }
    }

    private static bool IsUnderDirectory(string path, string directory)
    {
        string full = Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string root = Path.GetFullPath(directory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase);
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

    /// <summary>
    /// 清掉历史版本注入的 Windows 内置程序种子项。
    /// 这些内置程序名（explorer.exe 等）只存在于 Windows，在 macOS / Linux 上
    /// 会变成几个点不开的 exe 条目，得用户手工删——所以任何平台都不保留。
    /// 只按 Target 精确匹配 CreateDefault 写过的四个值，用户自己加的同名命令不动。
    /// 返回 true 表示有改动。
    /// </summary>
    private bool DropForeignSeedItems(AppConfig config)
    {
        if (config.Items.Count == 0)
        {
            return false;
        }

        var kept = new List<LauncherItem>(config.Items.Count);
        bool changed = false;
        foreach (LauncherItem item in config.Items)
        {
            if (WindowsSeedTargets.Contains(item.Target ?? string.Empty))
            {
                changed = true;
                _log?.Invoke($"已移除预置的内置程序条目：{item.Name}");
                continue;
            }

            kept.Add(item);
        }

        if (changed)
        {
            config.Items = kept;
        }

        return changed;
    }

    /// <summary>旧版本 CreateDefault 预置的 Windows 内置程序目标，一律视为无效。</summary>
    private static readonly HashSet<string> WindowsSeedTargets = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer.exe",
        "notepad.exe",
        "calc.exe",
        "start cmd.exe"
    };

    /// <summary>
    /// 首次运行的默认配置：条目列表为空。
    /// 曾按平台注入过 Windows 内置程序（explorer.exe / notepad.exe / calc.exe / cmd.exe），
    /// 但预置条目在非 Windows 平台全是点不开的死链，在 Windows 上也只是四个可有可无的默认项，
    /// 却要用户自己一个个删干净——不值得，空列表让用户按自己的习惯添加。
    /// </summary>
    public static AppConfig CreateDefault()
    {
        return new AppConfig();
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
