using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace QuickApp;

/// <summary>
/// 极简日志：调试器 + %LOCALAPPDATA%\QuickApp\logs\app.log。
/// 不引第三方日志库，避免给 AOT 增加反射面。
/// </summary>
public static class AppLog
{
    private static readonly object Gate = new();
    private static string? _logFile;

    public static void Info(string message) => Write("INFO", message, null);

    public static void Error(string message, Exception? ex = null) => Write("ERROR", message, ex);

    private static void Write(string level, string message, Exception? ex)
    {
        string line = string.Format(
            CultureInfo.InvariantCulture,
            "{0:yyyy-MM-dd HH:mm:ss.fff} [{1}] {2}{3}",
            DateTime.Now,
            level,
            message,
            ex is null ? string.Empty : Environment.NewLine + ex);

        Debug.WriteLine(line);

        try
        {
            _logFile ??= EnsureLogFile();
            if (_logFile is not null)
            {
                lock (Gate)
                {
                    File.AppendAllText(_logFile, line + Environment.NewLine);
                }
            }
        }
        catch
        {
            // 日志失败不能影响主流程
        }
    }

    private static string? EnsureLogFile()
    {
        try
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "QuickApp",
                "logs");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "app.log");
        }
        catch
        {
            return null;
        }
    }
}
