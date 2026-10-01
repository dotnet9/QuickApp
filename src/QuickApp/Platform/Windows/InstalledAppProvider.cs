using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using QuickApp.Core.Models;
using QuickApp.Core.Services;

namespace QuickApp.Platform.Windows;

/// <summary>
/// 从 Windows 开始菜单的快捷方式目录建立轻量应用索引。
/// 不依赖 COM：快捷方式本身就是可由 Shell 启动的目标，后续启动和图标提取仍由现有管线处理。
/// </summary>
public sealed class InstalledAppProvider : IInstalledAppProvider, IDisposable
{
    private static readonly string[] Extensions = { ".lnk", ".url", ".exe" };

    private readonly Action<string>? _log;
    private readonly List<FileSystemWatcher> _watchers = new();

    public InstalledAppProvider(Action<string>? log = null)
    {
        _log = log;
        StartWatching();
    }

    /// <summary>开始菜单内容变化时触发（后台线程）。只作失效通知，重扫时机由调用方决定。</summary>
    public event Action? Changed;

    public void Dispose()
    {
        foreach (FileSystemWatcher watcher in _watchers)
        {
            watcher.Dispose();
        }

        _watchers.Clear();
    }

    public IReadOnlyList<LauncherItem> GetInstalledApps()
    {
        var result = new List<LauncherItem>();
        var seenTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string root in GetScanRoots().Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                continue;
            }

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(root, "*.*", new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = true,
                    ReturnSpecialDirectories = false
                });
            }
            catch (Exception ex)
            {
                _log?.Invoke("读取开始菜单失败：" + ex.Message);
                continue;
            }

            try
            {
                foreach (string file in files)
                {
                    string extension;
                    try
                    {
                        extension = Path.GetExtension(file);
                    }
                    catch (ArgumentException)
                    {
                        continue;
                    }

                    if (!Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string name = Path.GetFileNameWithoutExtension(file).Trim();
                    if (name.Length == 0 || name.Equals("Uninstall", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string target;
                    ItemKind kind = ItemKind.App;
                    if (extension.Equals(".url", StringComparison.OrdinalIgnoreCase))
                    {
                        target = ReadUrlTarget(file) ?? file;
                        kind = target.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                            || target.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                            ? ItemKind.Web
                            : ItemKind.App;
                    }
                    else
                    {
                        target = file;
                    }

                    string targetKey = target.Trim();
                    if (!seenTargets.Add(targetKey) || !seenNames.Add(name))
                    {
                        continue;
                    }

                    result.Add(new LauncherItem
                    {
                        Id = StableId(targetKey),
                        Name = name,
                        Kind = kind,
                        Target = targetKey
                    });
                }
            }
            catch (Exception ex)
            {
                _log?.Invoke("读取开始菜单失败：" + ex.Message);
            }
        }

        // MSIX/商店应用（ChatGPT 这类打包应用没有 .lnk，开始菜单目录扫描天然扫不到）
        try
        {
            result.AddRange(MsixAppScanner.Scan(_log));
        }
        catch (Exception ex)
        {
            _log?.Invoke("枚举商店应用失败：" + ex.Message);
        }

        return result
            .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string? ReadUrlTarget(string file)
    {
        try
        {
            foreach (string line in File.ReadLines(file))
            {
                if (line.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))
                {
                    return line[4..].Trim();
                }
            }
        }
        catch
        {
            // 快捷方式不可读时仍返回 .url 本身，让 Shell 决定是否可启动。
        }

        return null;
    }

    private static string[] GetScanRoots()
    {
        return new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
            Environment.GetFolderPath(Environment.SpecialFolder.Programs)
        };
    }

    /// <summary>
    /// 挂 FileSystemWatcher 监听开始菜单目录（事件驱动，平时零开销）：
    /// 事件只用来标脏，真正的重扫发生在调用方认为需要的时候。
    /// </summary>
    private void StartWatching()
    {
        string[] existing = GetScanRoots()
            .Where(root => !string.IsNullOrWhiteSpace(root) && Directory.Exists(root))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        // CommonPrograms / Programs 嵌套在对应 Start Menu 根下，子目录监听已覆盖，避免重复挂
        string[] roots = existing
            .Where(root => !existing.Any(other =>
                !string.Equals(root, other, StringComparison.OrdinalIgnoreCase)
                && root.StartsWith(other.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        foreach (string root in roots)
        {
            try
            {
                var watcher = new FileSystemWatcher(root)
                {
                    IncludeSubdirectories = true,
                    // 默认缓冲区太小，安装程序批量写快捷方式时容易溢出；溢出走 Error 同样触发标脏，这里只是少打几次日志
                    InternalBufferSize = 16 * 1024
                };
                watcher.Created += (_, _) => Changed?.Invoke();
                watcher.Deleted += (_, _) => Changed?.Invoke();
                watcher.Changed += (_, _) => Changed?.Invoke();
                watcher.Renamed += (_, _) => Changed?.Invoke();
                watcher.Error += (_, _) => Changed?.Invoke();
                watcher.EnableRaisingEvents = true;
                _watchers.Add(watcher);
            }
            catch (Exception ex)
            {
                _log?.Invoke("监听开始菜单变化失败：" + ex.Message);
            }
        }
    }

    private static string StableId(string target)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(target.ToUpperInvariant()));
        return "installed-" + Convert.ToHexString(bytes.AsSpan(0, 8)).ToLowerInvariant();
    }
}
