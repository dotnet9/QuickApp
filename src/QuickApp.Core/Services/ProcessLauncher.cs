using System;
using System.Diagnostics;
using QuickApp.Core.Models;

namespace QuickApp.Core.Services;

/// <summary>
/// 用 BCL Process 执行启动计划；命令按用户选择在后台或可见终端执行。
/// 计划里的实例数大于 1 时逐个启动（应用多开），只要有一个起不来就返回失败原因。
/// </summary>
public sealed class ProcessLauncher : ILauncher
{
    public bool TryStart(LauncherItem item, out string? error)
    {
        error = null;
        LaunchPlan plan = LaunchPlanner.Create(item);
        if (string.IsNullOrWhiteSpace(plan.FileName))
        {
            error = AppText.Get(Language.EmptyLaunchTarget);
            return false;
        }

        LaunchPlan single = plan.Single();
        for (int index = 0; index < plan.InstanceCount; index++)
        {
            if (!TryStartOnce(single, out error))
            {
                return false;
            }
        }

        return true;
    }

    private static bool TryStartOnce(LaunchPlan plan, out string? error)
    {
        error = null;
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
