using System;
using System.IO;

namespace QuickApp.Core.Services;

/// <summary>
/// 路径解析。默认落在 %APPDATA%\QuickApp；若 exe 同目录存在 portable.txt，则改为绿色模式（配置就在 exe 旁边）。
/// </summary>
public static class AppPaths
{
    private const string PortableMarker = "portable.txt";

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

    /// <summary>图标缓存目录，始终放 LocalAppData，避免绿色模式污染 U 盘。</summary>
    public static string IconCacheDirectory()
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QuickApp", "icons");

    public static bool IsPortable(string baseDirectory)
        => File.Exists(Path.Combine(baseDirectory, PortableMarker));
}
