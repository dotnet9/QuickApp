using System;
using System.Globalization;
using QuickApp.Core.Services;

namespace QuickApp.Platform.Windows;

/// <summary>
/// 开机启动：直接写 HKCU\Software\Microsoft\Windows\CurrentVersion\Run。
/// 不用 Microsoft.Win32.Registry（net10.0-windows 非桌面框架下要额外引包），几个 P/Invoke 更干净。
/// </summary>
public sealed class AutoStartService : IAutoStartService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public bool IsEnabled(string appName)
    {
        if (!OpenRunKey(NativeMethods.KeyQueryValue, out IntPtr key))
        {
            return false;
        }

        try
        {
            // 读值长度：先试读空值，键不存在时返回 ERROR_FILE_NOT_FOUND
            int result = QueryValueExists(key, appName);
            return result == NativeMethods.ErrorSuccess;
        }
        finally
        {
            NativeMethods.RegCloseKey(key);
        }
    }

    public bool SetEnabled(string appName, bool enabled, string executablePath, out string? error)
    {
        error = null;
        if (!OpenRunKey(NativeMethods.KeySetValue | NativeMethods.KeyQueryValue, out IntPtr key))
        {
            error = "无法打开注册表 Run 键（可能被安全软件拦截）。";
            return false;
        }

        try
        {
            if (!enabled)
            {
                int deleted = NativeMethods.RegDeleteValue(key, appName);
                if (deleted != NativeMethods.ErrorSuccess && deleted != NativeMethods.ErrorFileNotFound)
                {
                    error = "删除开机启动项失败，错误码 " + deleted.ToString(CultureInfo.InvariantCulture);
                    return false;
                }

                return true;
            }

            string value = "\"" + executablePath + "\"";
            byte[] bytes = System.Text.Encoding.Unicode.GetBytes(value + "\0");
            int written = NativeMethods.RegSetValueEx(
                key, appName, 0, NativeMethods.RegSz, ref bytes[0], bytes.Length);

            if (written != NativeMethods.ErrorSuccess)
            {
                error = "写入开机启动项失败，错误码 " + written.ToString(CultureInfo.InvariantCulture);
                return false;
            }

            return true;
        }
        finally
        {
            NativeMethods.RegCloseKey(key);
        }
    }

    private static bool OpenRunKey(int access, out IntPtr key)
        => NativeMethods.RegOpenKeyEx(NativeMethods.HKeyCurrentUserHandle, RunKey, 0, access, out key)
           == NativeMethods.ErrorSuccess;

    /// <summary>值是否存在：lpData 传 NULL 只探测，键值不存在时返回 ERROR_FILE_NOT_FOUND。</summary>
    private static int QueryValueExists(IntPtr key, string appName)
    {
        int size = 0;
        return NativeMethods.RegQueryValueEx(key, appName, IntPtr.Zero, out _, IntPtr.Zero, ref size);
    }
}
