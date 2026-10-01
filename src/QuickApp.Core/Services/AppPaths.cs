using System;
using System.IO;

namespace QuickApp.Core.Services;

/// <summary>
/// 路径解析。默认落在 %APPDATA%\QuickApp；若 exe 同目录存在 portable.txt，则改为绿色模式（配置就在 exe 旁边）。
/// </summary>
public static class AppPaths
{
    /// <summary>便携模式标记文件：存在于 exe 同目录时，配置跟随程序目录。</summary>
    public const string PortableMarker = "portable.txt";

    /// <summary>滚动备份保留的历史份数（config.1.json 最新；当前文件 + 4 份 = 5 个时间点）。</summary>
    public const int RollingBackupCount = 4;

    /// <summary>配置文件完整路径。</summary>
    public static string ConfigFile(string baseDirectory)
    {
        string dir = IsPortable(baseDirectory)
            ? baseDirectory
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "QuickApp");
        return Path.Combine(dir, "config.json");
    }

    /// <summary>配置备份文件路径（保存前留一份）。</summary>
    public static string ConfigBackupFile(string configFile) => configFile + ".bak";

    /// <summary>第 index 份滚动备份路径（config.1.json 最新的历史版本）。</summary>
    public static string ConfigRollingBackupFile(string configFile, int index)
        => Path.Combine(
            Path.GetDirectoryName(configFile) ?? ".",
            Path.GetFileNameWithoutExtension(configFile) + "." + index + Path.GetExtension(configFile));

    /// <summary>图标缓存目录，始终放 LocalAppData，避免绿色模式污染 U 盘。</summary>
    public static string IconCacheDirectory()
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QuickApp", "icons");

    /// <summary>
    /// 自定义图标库目录：配置文件旁的 icons 子目录。用户选的图标复制进来（按条目 Id 命名），
    /// 原图挪走/删除不影响图标，导入导出与便携模式也天然带上它。
    /// </summary>
    public static string IconLibraryDirectory(string baseDirectory)
    {
        string configFile = ConfigFile(baseDirectory);
        string dir = Path.GetDirectoryName(configFile) ?? ".";
        return Path.Combine(dir, "icons");
    }

    public static bool IsPortable(string baseDirectory)
        => File.Exists(Path.Combine(baseDirectory, PortableMarker));
}
