using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using QuickApp.Core.Models;
using QuickApp.Core.Services;

namespace QuickApp.Platform.Windows;

/// <summary>
/// MSIX/商店应用（Appx 包）的轻量枚举：直接读 %LOCALAPPDATA%\Packages 的注册信息与
/// WindowsApps 目录清单，与「开始菜单搜索」同源（Get-StartApps 的 AppxRepository）。
/// 不走 COM / AppxPackage 命令（NativeAOT 下 PowerShell 子进程可靠且无互操作负担）。
/// </summary>
internal static class MsixAppScanner
{
    private sealed record MsixApp(string DisplayName, string AppId, string LogoPath);

    /// <summary>枚举当前用户可用的 MSIX 应用（含开始菜单可见的商店/打包应用）。</summary>
    public static IReadOnlyList<LauncherItem> Scan(Action<string>? log)
    {
        var result = new List<LauncherItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Get-StartApps 的 AppID 形如「Family!AppId」；一次性取全量再本地过滤，比逐包查询快得多
        string stdout;
        try
        {
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -NonInteractive -Command " +
                    "\"[Console]::OutputEncoding=[Text.Encoding]::UTF8; " +
                    "Get-StartApps | ForEach-Object { \\\"$($_.Name)`t$($_.AppID)\\\" }\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            });
            if (process is null)
            {
                return result;
            }

            stdout = process.StandardOutput.ReadToEnd();
            process.WaitForExit(15000);
            if (process.ExitCode != 0)
            {
                log?.Invoke("Get-StartApps 退出码 " + process.ExitCode);
            }
        }
        catch (Exception ex)
        {
            log?.Invoke("枚举 MSIX 应用失败：" + ex.Message);
            return result;
        }

        foreach (string line in stdout.Split('\n'))
        {
            string trimmed = line.TrimEnd('\r');
            int tab = trimmed.IndexOf('\t');
            if (tab <= 0)
            {
                continue;
            }

            string name = trimmed[..tab].Trim();
            string appId = trimmed[(tab + 1)..].Trim();

            // 只收 UWP/打包应用（AppID 含 Family!AppId 形态）；桌面 Win32 项已由 .lnk 扫描覆盖
            if (name.Length == 0 || !appId.Contains('!'))
            {
                continue;
            }

            string key = "msix-" + appId;
            if (!seen.Add(appId))
            {
                continue;
            }

            string logo = ResolveLogo(appId);
            result.Add(new LauncherItem
            {
                Id = StableId(key),
                Name = name,
                Kind = ItemKind.App,
                Target = "shell:AppsFolder\\" + appId,
                CustomIconPath = string.IsNullOrEmpty(logo) ? null : logo
            });
        }

        return result;
    }

    /// <summary>从包清单解析方形 logo 的绝对路径（优先最大尺寸）。</summary>
    private static string ResolveLogo(string appId)
    {
        try
        {
            string family = appId.Split('!')[0];
            string installDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft", "WindowsApps", family);
            if (!Directory.Exists(installDir))
            {
                return string.Empty;
            }

            // AppxManifest.xml 里的 Logo 是相对路径；选带 scale 最大倍数的方形图
            string manifest = Directory.GetFiles(installDir, "AppxManifest.xml", SearchOption.TopDirectoryOnly)
                .FirstOrDefault();
            if (manifest is null)
            {
                return string.Empty;
            }

            var doc = XDocument.Load(manifest);
            XNamespace ns = doc.Root?.Name.Namespace ?? XNamespace.None;
            string? relative = doc.Descendants(ns + "Application")
                .Elements(ns + "VisualElements")
                .Select(e => (string?)e.Attribute("Square44x44Logo") ?? (string?)e.Attribute("Square150x150Logo"))
                .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
            if (relative is null)
            {
                return string.Empty;
            }

            string baseName = Path.Combine(installDir, relative.Replace('/', Path.DirectorySeparatorChar));
            string dir = Path.GetDirectoryName(baseName) ?? installDir;
            string stem = Path.GetFileNameWithoutExtension(baseName);
            return Directory.GetFiles(dir, stem + "*.png")
                .OrderByDescending(f =>
                {
                    string file = Path.GetFileNameWithoutExtension(f);
                    int scale = file.LastIndexOf('.') is { } dot && int.TryParse(file[(dot + 1)..].Replace("scale-", ""), out int s) ? s : 100;
                    return scale;
                })
                .FirstOrDefault() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string StableId(string key)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(key.ToUpperInvariant()));
        return "installed-" + Convert.ToHexString(bytes.AsSpan(0, 8)).ToLowerInvariant();
    }
}
