using System;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using QuickApp.Core.Models;
using QuickApp.Core.Services;

namespace QuickApp.Platform.Windows;

/// <summary>
/// Shell 图标提取 + 磁盘缓存。
/// 流程：SHGetFileInfo(ICONLOCATION) 取图标文件与索引 → SHDefExtractIcon 取指定尺寸 HICON
///       → CreateDIBSection + DrawIconEx 拿到 BGRA 像素 → Avalonia 存成 PNG 缓存。
/// 全程 P/Invoke，不用 COM（AOT 下 COM 互操作被关闭）。
/// </summary>
public sealed class IconProvider : IIconProvider
{
    private const int IconSize = 128;

    private readonly string _cacheDirectory;
    private readonly ConcurrentDictionary<string, Task<string?>> _inflight = new(StringComparer.OrdinalIgnoreCase);
    private readonly Action<string>? _log;

    public IconProvider(string cacheDirectory, Action<string>? log = null)
    {
        _cacheDirectory = cacheDirectory;
        _log = log;
    }

    public Task<string?> GetIconFileAsync(LauncherItem item, CancellationToken cancellationToken = default)
    {
        if (item is null || string.IsNullOrWhiteSpace(item.Target) || item.Kind != ItemKind.App)
        {
            return Task.FromResult<string?>(null);
        }

        string key = CacheKey(item);
        string cached = Path.Combine(_cacheDirectory, key + ".png");
        if (File.Exists(cached))
        {
            return Task.FromResult<string?>(cached);
        }

        return _inflight.GetOrAdd(cached, path => ExtractAsync(item, path, cancellationToken));
    }

    private async Task<string?> ExtractAsync(LauncherItem item, string cachePath, CancellationToken cancellationToken)
    {
        try
        {
            IntPtr pixels = await Task.Run(() => ExtractPixels(item, IconSize), cancellationToken).ConfigureAwait(false);
            if (pixels == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                // 存 PNG 交给 UI 线程做：WriteableBitmap 的创建/保存不保证线程安全
                return await Dispatcher.UIThread.InvokeAsync(() => SavePng(pixels, cachePath));
            }
            finally
            {
                Marshal.FreeHGlobal(pixels);
            }
        }
        catch (Exception ex)
        {
            _log?.Invoke($"提取图标失败（{item.Name}）：{ex.Message}");
            return null;
        }
        finally
        {
            _inflight.TryRemove(cachePath, out _);
        }
    }

    /// <summary>在后台线程做的 GDI 部分：返回 size*size*4 字节的 BGRA 非预乘像素缓冲。</summary>
    private static IntPtr ExtractPixels(LauncherItem item, int size)
    {
        string iconFile = item.IconKey ?? item.Target;
        int iconIndex = 0;

        // 1) 问 Shell：这个文件的图标在哪个文件/哪个索引
        var info = new NativeMethods.SHFILEINFO();
        uint flags = NativeMethods.ShgfiIconLocation | NativeMethods.ShgfiLargIcon;
        IntPtr result = NativeMethods.SHGetFileInfo(
            item.Target, 0, ref info, (uint)Marshal.SizeOf<NativeMethods.SHFILEINFO>(), flags);

        if (result != IntPtr.Zero)
        {
            iconIndex = info.iIcon;
            unsafe
            {
                string located = new string(info.szDisplayName);
                if (!string.IsNullOrWhiteSpace(located) && File.Exists(located))
                {
                    iconFile = located;
                }
            }
        }

        bool isShortcut = Path.GetExtension(item.Target).Equals(".lnk", StringComparison.OrdinalIgnoreCase);
        if (isShortcut && Path.GetExtension(iconFile).Equals(".lnk", StringComparison.OrdinalIgnoreCase)
            && ShellLinkIconResolver.TryGetIconResource(item.Target, out string shortcutIconFile, out int shortcutIconIndex))
        {
            iconFile = shortcutIconFile;
            iconIndex = shortcutIconIndex;
        }

        // 2) 取大尺寸 HICON
        uint iconSize = (uint)(size | (size << 16));
        int hr = NativeMethods.SHDefExtractIcon(iconFile, iconIndex, 0, out IntPtr hIcon, IntPtr.Zero, iconSize);
        if (hr != 0 || hIcon == IntPtr.Zero)
        {
            // 退化：快捷方式从解析出的目标程序取图标，避免 Shell 合成快捷方式箭头。
            if (isShortcut && Path.GetExtension(iconFile).Equals(".lnk", StringComparison.OrdinalIgnoreCase))
            {
                return IntPtr.Zero;
            }

            var fallback = new NativeMethods.SHFILEINFO();
            IntPtr ok = NativeMethods.SHGetFileInfo(
                isShortcut ? iconFile : item.Target, 0, ref fallback,
                (uint)Marshal.SizeOf<NativeMethods.SHFILEINFO>(),
                NativeMethods.ShgfiIcon | NativeMethods.ShgfiLargIcon);
            if (ok == IntPtr.Zero || fallback.hIcon == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            hIcon = fallback.hIcon;
        }

        IntPtr hdc = IntPtr.Zero;
        IntPtr hBitmap = IntPtr.Zero;
        try
        {
            hdc = NativeMethods.CreateCompatibleDC(IntPtr.Zero);
            if (hdc == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            var bmi = new NativeMethods.BITMAPINFO
            {
                bmiHeader = new NativeMethods.BITMAPINFOHEADER
                {
                    biSize = (uint)Marshal.SizeOf<NativeMethods.BITMAPINFOHEADER>(),
                    biWidth = size,
                    biHeight = -size,           // 负数 = 自上而下
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = NativeMethods.BiRgb
                }
            };

            hBitmap = NativeMethods.CreateDIBSection(hdc, ref bmi, NativeMethods.DibRgbColors, out IntPtr bits, IntPtr.Zero, 0);
            if (hBitmap == IntPtr.Zero || bits == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            IntPtr previous = NativeMethods.SelectObject(hdc, hBitmap);
            try
            {
                if (!NativeMethods.DrawIconEx(hdc, 0, 0, hIcon, size, size, 0, IntPtr.Zero, NativeMethods.DiNormal))
                {
                    return IntPtr.Zero;
                }
            }
            finally
            {
                NativeMethods.SelectObject(hdc, previous);
            }

            int length = size * size * 4;
            IntPtr copy = Marshal.AllocHGlobal(length);
            unsafe
            {
                Buffer.MemoryCopy((void*)bits, (void*)copy, length, length);
            }

            FixAlphaChannel(copy, size);
            return copy;
        }
        finally
        {
            if (hBitmap != IntPtr.Zero)
            {
                NativeMethods.DeleteObject(hBitmap);
            }

            if (hdc != IntPtr.Zero)
            {
                NativeMethods.DeleteDC(hdc);
            }

            if (hIcon != IntPtr.Zero)
            {
                NativeMethods.DestroyIcon(hIcon);
            }
        }
    }

    /// <summary>部分图标没有 alpha 通道，DrawIconEx 后会整片透明；全 0 时补成不透明。</summary>
    private static unsafe void FixAlphaChannel(IntPtr pixels, int size)
    {
        byte* p = (byte*)pixels;
        int count = size * size;
        bool anyAlpha = false;
        for (int i = 0; i < count; i++)
        {
            if (p[i * 4 + 3] != 0)
            {
                anyAlpha = true;
                break;
            }
        }

        if (anyAlpha)
        {
            return;
        }

        for (int i = 0; i < count; i++)
        {
            p[i * 4 + 3] = 255;
        }
    }

    private static string? SavePng(IntPtr pixels, string cachePath)
    {
        try
        {
            string? dir = Path.GetDirectoryName(cachePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var bitmap = new WriteableBitmap(
                new PixelSize(IconSize, IconSize),
                new Vector(96, 96),
                PixelFormat.Bgra8888,
                AlphaFormat.Unpremul);

            using (ILockedFramebuffer buffer = bitmap.Lock())
            {
                int rowBytes = IconSize * 4;
                for (int y = 0; y < IconSize; y++)
                {
                    IntPtr source = IntPtr.Add(pixels, y * rowBytes);
                    IntPtr destination = IntPtr.Add(buffer.Address, y * buffer.RowBytes);
                    unsafe
                    {
                        Buffer.MemoryCopy((void*)source, (void*)destination, rowBytes, rowBytes);
                    }
                }
            }

            bitmap.Save(cachePath);
            bitmap.Dispose();
            return cachePath;
        }
        catch
        {
            return null;
        }
    }

    private static string CacheKey(LauncherItem item)
    {
        string raw = string.Join('|', "icon-v2", item.Target, item.IconKey ?? string.Empty, IconSize.ToString());
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw.ToUpperInvariant()));
        return Convert.ToHexString(hash.AsSpan(0, 12)).ToLowerInvariant();
    }
}
