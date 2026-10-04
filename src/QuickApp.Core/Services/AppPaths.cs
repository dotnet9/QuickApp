using System;
using System.IO;

namespace QuickApp.Core.Services;

/// <summary>
/// 路径解析。默认落在 %LOCALAPPDATA%\QuickApp（Linux ~/.local/share、macOS ~/Library/Application Support，
/// 随 SpecialFolder.LocalApplicationData 各平台取标准值）；若 exe 同目录存在 portable.txt，则改为绿色模式（配置就在 exe 旁边）。
/// </summary>
public static class AppPaths
{
    /// <summary>便携模式标记文件：存在于 exe 同目录时，配置跟随程序目录。</summary>
    public const string PortableMarker = "portable.txt";

    /// <summary>滚动备份保留的历史份数（config.1.json 最新；当前文件 + 4 份 = 5 个时间点）。</summary>
    public const int RollingBackupCount = 4;

    /// <summary>旧版安装模式的配置根（%APPDATA%，Roaming）：仅用于一次性迁移。</summary>
    public const string LegacyConfigRoot = "QuickApp";

    /// <summary>配置目录：便携模式在 exe 旁，安装版在 %LOCALAPPDATA%\QuickApp。</summary>
    public static string ConfigDirectory(string baseDirectory)
        => IsPortable(baseDirectory)
            ? baseDirectory
            : Path.Combine(GetRootDirectory(), "QuickApp");

    /// <summary>配置文件完整路径。</summary>
    public static string ConfigFile(string baseDirectory)
        => Path.Combine(ConfigDirectory(baseDirectory), "config.json");

    /// <summary>配置备份文件路径（保存前留一份）。</summary>
    public static string ConfigBackupFile(string configFile) => configFile + ".bak";

    /// <summary>第 index 份滚动备份路径（config.1.json 最新的历史版本）。</summary>
    public static string ConfigRollingBackupFile(string configFile, int index)
        => Path.Combine(
            Path.GetDirectoryName(configFile) ?? ".",
            Path.GetFileNameWithoutExtension(configFile) + "." + index + Path.GetExtension(configFile));

    /// <summary>图标缓存目录，始终放 LocalAppData，避免绿色模式污染 U 盘。</summary>
    public static string IconCacheDirectory()
        => Path.Combine(GetRootDirectory(), "QuickApp", "icons");

    /// <summary>
    /// 自定义图标库目录：配置文件旁的 icons 子目录。用户选的图标复制进来（按条目 Id 命名），
    /// 原图挪走/删除不影响图标，导入导出与便携模式也天然带上它。
    /// </summary>
    public static string IconLibraryDirectory(string baseDirectory)
        => Path.Combine(ConfigDirectory(baseDirectory), "icons");

    /// <summary>更新检查缓存（ETag + 上次 release）：条件请求 304 不计入 GitHub API 配额。</summary>
    public static string UpdateStateFile(string baseDirectory)
        => Path.Combine(ConfigDirectory(baseDirectory), "update-state.json");

    public static bool IsPortable(string baseDirectory)
        => File.Exists(Path.Combine(baseDirectory, PortableMarker));

    /// <summary>
    /// 一次性迁移：旧版安装版把配置放在 %APPDATA%\QuickApp（Roaming），现统一到
    /// %LOCALAPPDATA%\QuickApp。新位置还没有配置而旧位置有 → 整目录复制过去
    /// （含 icons、update-state.json 与滚动备份），旧目录原样保留作为备份。
    /// 新位置已有配置（已迁移/全新安装）则什么都不做。
    /// </summary>
    public static void MigrateLegacyConfig(
        string baseDirectory,
        Action<string>? log = null,
        string? legacyRootOverride = null,
        string? newRootOverride = null)
    {
        if (IsPortable(baseDirectory))
        {
            return;
        }

        string newRoot = newRootOverride ?? Path.GetDirectoryName(ConfigDirectory(baseDirectory))!;
        string newConfig = Path.Combine(newRoot, "config.json");
        if (File.Exists(newConfig))
        {
            return;
        }

        string legacyAppDataRoot = legacyRootOverride
            ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string legacyRoot = Path.Combine(legacyAppDataRoot, LegacyConfigRoot);
        if (!Directory.Exists(legacyRoot) || !File.Exists(Path.Combine(legacyRoot, "config.json")))
        {
            return;
        }

        try
        {
            CopyDirectory(legacyRoot, newRoot);
            log?.Invoke("已把配置从 " + legacyRoot + " 迁移到 " + newRoot);
        }
        catch (Exception ex)
        {
            // 迁移失败不阻塞启动：旧目录原样保留，下次启动再试
            log?.Invoke("配置迁移失败（将使用新位置继续，可手动从旧位置找回）：" + ex.Message);
        }
    }

    private static string GetRootDirectory()
        => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    private static void CopyDirectory(string sourceDirectory, string targetDirectory)
    {
        Directory.CreateDirectory(targetDirectory);
        foreach (string file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(sourceDirectory, file);
            string target = Path.Combine(targetDirectory, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: false);
        }
    }
}
