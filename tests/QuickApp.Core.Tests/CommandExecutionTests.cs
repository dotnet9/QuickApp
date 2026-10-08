using System;
using System.Diagnostics;
using System.IO;
using QuickApp.Core.Models;
using QuickApp.Core.Services;
using Xunit;

namespace QuickApp.Core.Tests;

public sealed class CommandExecutionTests
{
    [Fact]
    public void Cmd_runs_builtins_and_pipelines_in_the_selected_directory()
    {
        if (!OperatingSystem.IsWindows()) return;
        string directory = Path.Combine(Path.GetTempPath(), "QuickApp command " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            LaunchPlan plan = LaunchPlanner.Create(new LauncherItem
            {
                Kind = ItemKind.Command, Target = "(echo first & echo second) | findstr second > \"result file.txt\"", WorkingDirectory = directory
            });
            using Process process = Process.Start(new ProcessStartInfo(plan.FileName, plan.Arguments) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = plan.WorkingDirectory })!;
            Assert.True(process.WaitForExit(10000));
            Assert.Equal(0, process.ExitCode);
            Assert.Equal("second", File.ReadAllText(Path.Combine(directory, "result file.txt")).Trim());
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void PowerShell_preserves_quotes_unicode_and_working_directory()
    {
        if (!OperatingSystem.IsWindows()) return;
        string directory = Path.Combine(Path.GetTempPath(), "QuickApp powershell " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            LaunchPlan plan = LaunchPlanner.Create(new LauncherItem
            {
                Kind = ItemKind.Command, UsePowerShell = true, WorkingDirectory = directory,
                Target = "[IO.File]::WriteAllText((Join-Path (Get-Location) 'result.txt'), '中文 \"引号\" & 符号')"
            });
            using Process process = Process.Start(new ProcessStartInfo(plan.FileName, plan.Arguments) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = plan.WorkingDirectory })!;
            Assert.True(process.WaitForExit(10000));
            Assert.Equal(0, process.ExitCode);
            Assert.Equal("中文 \"引号\" & 符号", File.ReadAllText(Path.Combine(directory, "result.txt")));
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void Terminal_mode_retains_output_and_shell_visibility()
    {
        if (!OperatingSystem.IsWindows()) return;
        LaunchPlan plan = LaunchPlanner.Create(new LauncherItem { Kind = ItemKind.Command, Target = "git status", RunInTerminal = true });
        Assert.True(plan.UseShellExecute);
        Assert.Contains("/k", plan.Arguments);
    }
}
