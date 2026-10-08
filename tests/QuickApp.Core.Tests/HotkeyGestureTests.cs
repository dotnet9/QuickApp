using System;
using QuickApp.Core.Models;
using QuickApp.Core.Services;
using Xunit;

namespace QuickApp.Core.Tests;

/// <summary>全局热键解析：设置里填的 "Ctrl+Alt+Space" 要变成稳定的修饰键 + 虚拟键码。</summary>
public sealed class HotkeyGestureTests
{
    [Fact]
    public void Parses_ctrl_alt_space()
    {
        bool ok = HotkeyGesture.TryParse("Ctrl+Alt+Space", out HotkeyGesture gesture, out string? error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, gesture.Modifiers);
        Assert.Equal(0x20, gesture.VirtualKey);
    }

    [Fact]
    public void Modifier_and_key_names_are_case_insensitive()
    {
        bool ok = HotkeyGesture.TryParse("shift+alt+F5", out HotkeyGesture gesture, out _);

        Assert.True(ok);
        Assert.Equal(HotkeyModifiers.Shift | HotkeyModifiers.Alt, gesture.Modifiers);
        Assert.Equal(0x74, gesture.VirtualKey);
    }

    [Fact]
    public void Accepts_letters_digits_and_win()
    {
        Assert.True(HotkeyGesture.TryParse("Win+D", out HotkeyGesture letter, out _));
        Assert.Equal(HotkeyModifiers.Win, letter.Modifiers);
        Assert.Equal('D', letter.VirtualKey);

        Assert.True(HotkeyGesture.TryParse("Ctrl+Shift+1", out HotkeyGesture digit, out _));
        Assert.Equal('1', digit.VirtualKey);
    }

    [Fact]
    public void Rejects_single_token_without_modifier()
    {
        Assert.False(HotkeyGesture.TryParse("Space", out _, out string? error));
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Ctrl+")]
    [InlineData("Ctrl+Alt+NotAKey")]
    [InlineData("Mouse+Space")]
    public void Rejects_invalid_gestures(string gesture)
    {
        Assert.False(HotkeyGesture.TryParse(gesture, out _, out string? error));
        Assert.NotNull(error);
    }
}

/// <summary>「复制为命令」：能直接粘到 cmd / Win+R 的形式。</summary>
public sealed class ItemQueryCommandTextTests
{
    [Fact]
    public void Web_gets_start_prefix()
    {
        var item = new LauncherItem { Kind = ItemKind.Web, Target = "https://dotnet9.com/" };

        Assert.Equal("start \"\" \"https://dotnet9.com/\"", ItemQuery.ToCommandText(item));
    }

    [Fact]
    public void Web_already_prefixed_is_left_alone()
    {
        var item = new LauncherItem { Kind = ItemKind.Web, Target = "start https://dotnet9.com/" };

        Assert.Equal("start https://dotnet9.com/", ItemQuery.ToCommandText(item));
    }

    [Fact]
    public void Commands_keep_syntax_and_app_paths_are_quoted()
    {
        var command = new LauncherItem { Kind = ItemKind.Command, Target = "mstsc /v:192.168.1.133" };
        var app = new LauncherItem { Kind = ItemKind.App, Target = @"C:\Tools\demo.exe" };

        Assert.Equal("mstsc /v:192.168.1.133", ItemQuery.ToCommandText(command));
        Assert.Equal("\"C:\\Tools\\demo.exe\"", ItemQuery.ToCommandText(app));
    }

    [Fact]
    public void Copied_app_command_includes_arguments_after_the_quoted_path()
        => Assert.Equal("\"C:\\Program Files\\Editor\\editor.exe\" --new-window",
            ItemQuery.ToCommandText(new LauncherItem { Target = @"C:\Program Files\Editor\editor.exe", Arguments = "--new-window" }));

    [Fact]
    public void Empty_target_returns_empty()
    {
        Assert.Equal(string.Empty, ItemQuery.ToCommandText(new LauncherItem()));
        Assert.Equal(string.Empty, ItemQuery.ToCommandText(null!));
    }
}
