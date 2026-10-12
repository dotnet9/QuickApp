using System;
using System.IO;
using QuickApp.Core.Models;
using QuickApp.Core.Services;
using Xunit;

namespace QuickApp.Core.Tests;

public sealed class LauncherItemEditorTests
{
    [Fact]
    public void Editing_a_copy_does_not_change_original_or_drop_metadata()
    {
        var original = new LauncherItem { Id = "stable", Name = "Original", Target = "old.exe", CustomIconPath = "icon.png", RecommendedId = "vex", Hotkey = "Ctrl+Alt+D" };
        LauncherItem draft = LauncherItemEditor.Copy(original);
        draft.Target = "new.exe";
        Assert.Equal("old.exe", original.Target);
        Assert.Equal(original.Id, draft.Id);
        Assert.Equal(original.CustomIconPath, draft.CustomIconPath);
        Assert.Equal(original.RecommendedId, draft.RecommendedId);
        Assert.Equal(original.Hotkey, draft.Hotkey);
    }

    [Theory]
    [InlineData("Alt+Ctrl+Space")]
    [InlineData("control+alt+space")]
    public void Dock_shortcut_conflict_is_detected_independently_of_spelling(string gesture)
    {
        var draft = new LauncherItem { Target = "explorer.exe", Hotkey = gesture };
        Assert.Contains("唤出 QuickApp", LauncherItemEditor.NormalizeAndValidate(draft, Array.Empty<LauncherItem>(), "Ctrl+Alt+Space"));
    }

    [Fact]
    public void Item_shortcut_conflict_names_the_other_item()
    {
        var original = new LauncherItem { Id = "one", Name = "Work", Target = "explorer.exe", Hotkey = "Ctrl+Alt+D" };
        var draft = new LauncherItem { Id = "two", Target = "notepad.exe", Hotkey = "Alt+Ctrl+D" };
        Assert.Contains("Work", LauncherItemEditor.NormalizeAndValidate(draft, new[] { original }, "Ctrl+Alt+Space"));
        draft.Id = original.Id;
        Assert.Null(LauncherItemEditor.NormalizeAndValidate(draft, new[] { original }, "Ctrl+Alt+Space"));
    }

    [Theory]
    [InlineData("", "请填写目标")]
    [InlineData("   ", "请填写目标")]
    public void Empty_target_is_rejected(string target, string expected)
        => Assert.Contains(expected, LauncherItemEditor.NormalizeAndValidate(new LauncherItem { Target = target }, Array.Empty<LauncherItem>(), "Ctrl+Alt+Space"));

    [Fact]
    public void Web_target_gains_https_and_clears_inapplicable_fields()
    {
        var draft = new LauncherItem { Kind = ItemKind.Web, Target = "dotnet9.com", Arguments = "--test", WorkingDirectory = "invalid", RunInTerminal = true };
        Assert.Null(LauncherItemEditor.NormalizeAndValidate(draft, Array.Empty<LauncherItem>(), "Ctrl+Alt+Space"));
        Assert.Equal("https://dotnet9.com", draft.Target);
        Assert.Equal("dotnet9.com", draft.Name);
        Assert.Null(draft.Arguments);
        Assert.Null(draft.WorkingDirectory);
        Assert.False(draft.RunInTerminal);
    }

    [Fact]
    public void Nonweb_scheme_is_rejected()
    {
        var draft = new LauncherItem { Kind = ItemKind.Web, Target = "file:///C:/test.txt" };
        Assert.NotNull(LauncherItemEditor.NormalizeAndValidate(draft, Array.Empty<LauncherItem>(), "Ctrl+Alt+Space"));
    }

    [Fact]
    public void Command_syntax_is_preserved_and_bad_working_directory_is_rejected()
    {
        var draft = new LauncherItem { Kind = ItemKind.Command, Target = "  echo \"a & b\" | findstr a  ", WorkingDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")) };
        Assert.Contains("工作目录不存在", LauncherItemEditor.NormalizeAndValidate(draft, Array.Empty<LauncherItem>(), "Ctrl+Alt+Space"));
        Assert.Equal("echo \"a & b\" | findstr a", draft.Target);
    }

    [Fact]
    public void Instance_count_is_kept_for_apps_and_cleared_for_other_kinds()
    {
        var app = new LauncherItem { Kind = ItemKind.App, Target = @"C:\Tools\wechat.exe", InstanceCount = 4 };
        Assert.Null(LauncherItemEditor.NormalizeAndValidate(app, Array.Empty<LauncherItem>(), "Ctrl+Alt+Space"));
        Assert.Equal(4, app.InstanceCount);

        // 旧配置没有该字段（反序列化为 0）与手改越界值都兜底到 1 / 上限
        var legacy = new LauncherItem { Kind = ItemKind.App, Target = @"C:\Tools\wechat.exe", InstanceCount = 0 };
        Assert.Null(LauncherItemEditor.NormalizeAndValidate(legacy, Array.Empty<LauncherItem>(), "Ctrl+Alt+Space"));
        Assert.Equal(1, legacy.InstanceCount);

        var tooMany = new LauncherItem { Kind = ItemKind.App, Target = @"C:\Tools\wechat.exe", InstanceCount = 1000 };
        Assert.Null(LauncherItemEditor.NormalizeAndValidate(tooMany, Array.Empty<LauncherItem>(), "Ctrl+Alt+Space"));
        Assert.Equal(LauncherItemEditor.MaxInstanceCount, tooMany.InstanceCount);

        var web = new LauncherItem { Kind = ItemKind.Web, Target = "dotnet9.com", InstanceCount = 5 };
        var command = new LauncherItem { Kind = ItemKind.Command, Target = "echo hi", InstanceCount = 5 };
        Assert.Null(LauncherItemEditor.NormalizeAndValidate(web, Array.Empty<LauncherItem>(), "Ctrl+Alt+Space"));
        Assert.Null(LauncherItemEditor.NormalizeAndValidate(command, Array.Empty<LauncherItem>(), "Ctrl+Alt+Space"));
        Assert.Equal(1, web.InstanceCount);
        Assert.Equal(1, command.InstanceCount);
    }

    [Fact]
    public void Copy_keeps_instance_count()
        => Assert.Equal(3, LauncherItemEditor.Copy(new LauncherItem { Target = "a.exe", InstanceCount = 3 }).InstanceCount);

    [Theory]
    [InlineData(null, 1)]
    [InlineData("", 1)]
    [InlineData("   ", 1)]
    [InlineData("3", 3)]
    [InlineData(" 12 ", 12)]
    [InlineData("99", 99)]
    public void Instance_count_text_is_parsed(string? text, int expected)
    {
        Assert.Null(LauncherItemEditor.TryParseInstanceCount(text, out int count));
        Assert.Equal(expected, count);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("100")]
    [InlineData("2.5")]
    [InlineData("两个")]
    public void Invalid_instance_count_text_is_rejected(string text)
        => Assert.NotNull(LauncherItemEditor.TryParseInstanceCount(text, out _));

    [Fact]
    public void Folder_name_keeps_dots_and_trailing_separator()
    {
        string folder = Path.Combine(Path.GetTempPath(), "QuickApp.Tests." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var draft = new LauncherItem { Target = folder + Path.DirectorySeparatorChar };
            Assert.Null(LauncherItemEditor.NormalizeAndValidate(draft, Array.Empty<LauncherItem>(), "Ctrl+Alt+Space"));
            Assert.Equal(new DirectoryInfo(folder).Name, draft.Name);
        }
        finally { Directory.Delete(folder); }
    }
}
