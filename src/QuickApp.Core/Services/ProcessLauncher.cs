using System;
using System.Diagnostics;
using QuickApp.Core.Models;

namespace QuickApp.Core.Services;

/// <summary>用 BCL 的 Process 执行启动计划。Web 与应用走 ShellExecute，命令行走 cmd 且不弹窗口。</summary>
public sealed class ProcessLauncher : ILauncher
{
    public bool TryStart(LauncherItem item, out string? error)
    {
        error = null;
        LaunchPlan plan = LaunchPlanner.Create(item);
        if (string.IsNullOrWhiteSpace(plan.FileName))
        {
            error = "该项目没有可执行的目标。";
            return false;
        }

        try
        {
            var info = new ProcessStartInfo
            {
                FileName = plan.FileName,
                Arguments = plan.Arguments,
                UseShellExecute = plan.UseShellExecute,
                CreateNoWindow = !plan.UseShellExecute
            };

            if (!string.IsNullOrWhiteSpace(plan.WorkingDirectory))
            {
                info.WorkingDirectory = plan.WorkingDirectory;
            }

            using Process? process = Process.Start(info);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
