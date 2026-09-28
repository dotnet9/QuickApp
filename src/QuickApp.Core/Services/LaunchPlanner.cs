using System;
using System.IO;
using QuickApp.Core.Models;

namespace QuickApp.Core.Services;

/// <summary>一次启动所需的参数，与具体启动 API 解耦（便于测试与实际执行共用一套逻辑）。</summary>
public sealed record LaunchPlan(
    string FileName,
    string Arguments,
    bool UseShellExecute,
    string? WorkingDirectory)
{
    public static LaunchPlan Empty { get; } = new(string.Empty, string.Empty, false, null);
}

/// <summary>把快捷项翻译成启动参数。</summary>
public static class LaunchPlanner
{
    public static LaunchPlan Create(LauncherItem item)
    {
        if (item is null || string.IsNullOrWhiteSpace(item.Target))
        {
            return LaunchPlan.Empty;
        }

        string target = item.Target.Trim();

        switch (item.Kind)
        {
            case ItemKind.Web when Uri.TryCreate(target, UriKind.Absolute, out _):
                // 交给 Shell 打开默认浏览器
                return new LaunchPlan(target, string.Empty, UseShellExecute: true, null);

            case ItemKind.Command:
                if (OperatingSystem.IsWindows())
                {
                    // start "" <命令>：立即返回，不留隐藏的 cmd 进程
                    return new LaunchPlan("cmd.exe", "/c start \"\" " + target, UseShellExecute: false, null);
                }

                // Linux/macOS 没有 cmd.exe；交给系统 shell 执行用户配置的命令。
                string shellCommand = target.Replace("\\", "\\\\", StringComparison.Ordinal)
                    .Replace("\"", "\\\"", StringComparison.Ordinal);
                return new LaunchPlan("/bin/sh", "-c \"" + shellCommand + "\"", UseShellExecute: false, null);

            default:
                string? workingDirectory = string.IsNullOrWhiteSpace(item.WorkingDirectory)
                    ? SafeDirectoryOf(target)
                    : item.WorkingDirectory;
                return new LaunchPlan(
                    target,
                    item.Arguments ?? string.Empty,
                    UseShellExecute: true,
                    workingDirectory);
        }
    }

    private static string? SafeDirectoryOf(string target)
    {
        try
        {
            return Path.GetDirectoryName(target);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
