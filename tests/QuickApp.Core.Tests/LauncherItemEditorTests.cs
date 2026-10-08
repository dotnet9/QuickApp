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
