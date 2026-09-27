using System;
using System.Collections.Generic;
using System.IO;
using QuickApp.Core.Models;
using QuickApp.Core.Services;
using Xunit;

namespace QuickApp.Core.Tests;

public sealed class ItemQueryTests
{
    private static List<LauncherItem> Sample() => new()
    {
        new LauncherItem { Id = "a", Name = "记事本", Kind = ItemKind.App, Target = @"C:\Windows\notepad.exe" },
        new LauncherItem { Id = "b", Name = "Dotnet9", Kind = ItemKind.Web, Target = "https://dotnet9.com/" },
        new LauncherItem { Id = "c", Name = "远程桌面", Kind = ItemKind.Command, Target = "mstsc /v:192.168.1.133" }
    };

    [Fact]
    public void Empty_query_returns_everything_in_order()
    {
        IReadOnlyList<LauncherItem> result = ItemQuery.Filter(Sample(), "  ");

        Assert.Equal(3, result.Count);
        Assert.Equal("a", result[0].Id);
        Assert.Equal("c", result[2].Id);
    }

    [Theory]
    [InlineData("记")]          // 名称
    [InlineData("notepad")]     // 路径
    [InlineData("192.168")]     // 命令参数
    [InlineData("网页")]        // 类型名
    public void Query_matches_name_target_or_kind(string query)
    {
        Assert.NotEmpty(ItemQuery.Filter(Sample(), query));
    }

    [Fact]
    public void Query_is_case_insensitive()
    {
        Assert.Single(ItemQuery.Filter(Sample(), "DOTNET9"));
    }

    [Fact]
    public void Unknown_query_returns_empty()
        => Assert.Empty(ItemQuery.Filter(Sample(), "不存在的关键字"));

    [Fact]
    public void Display_name_falls_back_to_file_name_or_host()
    {
        Assert.Equal("notepad", ItemQuery.ResolveDisplayName(
            new LauncherItem { Kind = ItemKind.App, Target = @"C:\Windows\notepad.exe" }));

        Assert.Equal("dotnet9.com", ItemQuery.ResolveDisplayName(
            new LauncherItem { Kind = ItemKind.Web, Target = "https://dotnet9.com/a/b" }));
    }

    [Fact]
    public void Describe_target_summarizes_by_kind()
    {
        Assert.Equal("dotnet9.com", ItemQuery.DescribeTarget(
            new LauncherItem { Kind = ItemKind.Web, Target = "https://dotnet9.com/x" }));

        Assert.Equal("mstsc /v:1.2.3.4", ItemQuery.DescribeTarget(
            new LauncherItem { Kind = ItemKind.Command, Target = "mstsc /v:1.2.3.4" }));

        Assert.Equal(@"…\Notepad++\notepad++.exe", ItemQuery.DescribeTarget(
            new LauncherItem { Kind = ItemKind.App, Target = @"C:\Program Files\Notepad++\notepad++.exe" }));
    }

    [Fact]
    public void Move_reorders_and_ignores_bad_indexes()
    {
        var items = Sample();
        ItemQuery.Move(items, 0, 2);
        Assert.Equal(new[] { "b", "c", "a" }, items.ConvertAll(i => i.Id));

        ItemQuery.Move(items, -1, 0);
        ItemQuery.Move(items, 99, 0);
        Assert.Equal(new[] { "b", "c", "a" }, items.ConvertAll(i => i.Id));
    }

    [Fact]
    public void At_returns_null_out_of_range()
    {
        var items = Sample();
        Assert.Equal("a", ItemQuery.At(items, 0)!.Id);
        Assert.Null(ItemQuery.At(items, 5));
        Assert.Null(ItemQuery.At(items, -1));
    }
}

public sealed class LaunchPlannerTests
{
    [Fact]
    public void Web_target_goes_through_shell()
    {
        LaunchPlan plan = LaunchPlanner.Create(
            new LauncherItem { Kind = ItemKind.Web, Target = "https://dotnet9.com" });

        Assert.Equal("https://dotnet9.com", plan.FileName);
        Assert.True(plan.UseShellExecute);
    }

    [Fact]
    public void Command_target_is_wrapped_in_cmd_start()
    {
        LaunchPlan plan = LaunchPlanner.Create(
            new LauncherItem { Kind = ItemKind.Command, Target = "mstsc /v:192.168.1.133" });

        Assert.Equal("cmd.exe", plan.FileName);
        Assert.Equal("/c start \"\" mstsc /v:192.168.1.133", plan.Arguments);
        Assert.False(plan.UseShellExecute);
    }

    [Fact]
    public void App_target_keeps_arguments_and_working_directory()
    {
        LaunchPlan plan = LaunchPlanner.Create(new LauncherItem
        {
            Kind = ItemKind.App,
            Target = @"C:\Tools\demo\demo.exe",
            Arguments = "--fast"
        });

        Assert.Equal(@"C:\Tools\demo\demo.exe", plan.FileName);
        Assert.Equal("--fast", plan.Arguments);
        Assert.Equal(@"C:\Tools\demo", plan.WorkingDirectory);
        Assert.True(plan.UseShellExecute);
    }

    [Fact]
    public void Empty_target_yields_empty_plan()
        => Assert.Equal(LaunchPlan.Empty, LaunchPlanner.Create(new LauncherItem { Target = "  " }));
}

public sealed class VersionUtilTests
{
    [Theory]
    [InlineData("v1.2.3", "1.2.3.0")]
    [InlineData("1.2.3", "1.2.3.0")]
    [InlineData("V2.0", "2.0.0.0")]
    [InlineData("1.2.3-beta.1", "1.2.3.0")]
    public void Parse_normalizes_tags(string tag, string expected)
        => Assert.Equal(Version.Parse(expected), VersionUtil.Parse(tag));

    [Fact]
    public void Parse_returns_null_for_garbage()
    {
        Assert.Null(VersionUtil.Parse("latest"));
        Assert.Null(VersionUtil.Parse(""));
        Assert.Null(VersionUtil.Parse(null));
    }

    [Fact]
    public void IsNewer_compares_properly()
    {
        Assert.True(VersionUtil.IsNewer(new Version(0, 2, 0), new Version(0, 1, 0)));
        Assert.True(VersionUtil.IsNewer(new Version(0, 1, 1), new Version(0, 1, 0)));
        Assert.False(VersionUtil.IsNewer(new Version(0, 1, 0), new Version(0, 1, 0)));
        Assert.False(VersionUtil.IsNewer(null, new Version(0, 1, 0)));
    }
}
