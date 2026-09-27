using System;
using System.Runtime.InteropServices;

namespace QuickApp.Platform.Windows;

/// <summary>
/// 剪贴板写入（CF_UNICODETEXT）。
/// Avalonia 12 的 IClipboard 换成了新的 DataTransfer API，这里只做「复制一段文本」这一件事，
/// 用 Win32 写反而更短更稳，也和本项目其它平台互操作保持一致。
/// </summary>
internal static partial class ClipboardInterop
{
    private const uint CfUnicodeText = 13;
    private const uint GmemMoveable = 0x0002;

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenClipboard(IntPtr hWndNewOwner);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EmptyClipboard();

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseClipboard();

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial IntPtr GlobalAlloc(uint uFlags, nuint dwBytes);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial IntPtr GlobalLock(IntPtr hMem);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalUnlock(IntPtr hMem);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial IntPtr GlobalFree(IntPtr hMem);

    /// <summary>把文本放进剪贴板。失败返回 false（不抛异常）。</summary>
    public static bool TrySetText(string text, IntPtr ownerWindow)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        IntPtr memory = IntPtr.Zero;
        bool opened = false;

        try
        {
            int bytes = (text.Length + 1) * sizeof(char);
            memory = GlobalAlloc(GmemMoveable, (nuint)bytes);
            if (memory == IntPtr.Zero)
            {
                return false;
            }

            IntPtr target = GlobalLock(memory);
            if (target == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                unsafe
                {
                    Span<char> destination = new((void*)target, text.Length + 1);
                    text.AsSpan().CopyTo(destination);
                    destination[text.Length] = '\0';
                }
            }
            finally
            {
                GlobalUnlock(memory);
            }

            if (!OpenClipboard(ownerWindow))
            {
                return false;
            }

            opened = true;
            if (!EmptyClipboard())
            {
                return false;
            }

            if (SetClipboardData(CfUnicodeText, memory) == IntPtr.Zero)
            {
                return false;
            }

            // 所有权已交给剪贴板，不能自己释放
            memory = IntPtr.Zero;
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (opened)
            {
                CloseClipboard();
            }

            if (memory != IntPtr.Zero)
            {
                GlobalFree(memory);
            }
        }
    }
}
