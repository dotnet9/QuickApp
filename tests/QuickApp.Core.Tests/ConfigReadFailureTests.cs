using System;
using System.IO;
using QuickApp.Core.Models;
using QuickApp.Core.Services;
using Xunit;

namespace QuickApp.Core.Tests;

public sealed class ConfigReadFailureTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "QuickAppReadFailure", Guid.NewGuid().ToString("N"));
    private readonly ConfigStore _store;

    public ConfigReadFailureTests()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, AppPaths.PortableMarker), "");
        _store = new ConfigStore(_directory);
    }

    [Fact]
    public void Sharing_failure_preserves_valid_file_and_retries_without_data_loss()
    {
        if (!OperatingSystem.IsWindows()) return;
        var config = new AppConfig();
        config.Items.Add(new LauncherItem { Id = "saved", Name = "My command", Kind = ItemKind.Command, Target = "echo hello", Hotkey = "Ctrl+Alt+D", WorkingDirectory = _directory, RunInTerminal = true });
        Assert.True(_store.Save(config));
        string original = File.ReadAllText(_store.ConfigFile);
        using (var locked = new FileStream(_store.ConfigFile, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.Empty(_store.Load().Items);
            Assert.NotNull(_store.LastLoadError);
            Assert.False(_store.Save(new AppConfig()));
            Assert.False(File.Exists(_store.ConfigFile + ".broken"));
        }
        Assert.Equal(original, File.ReadAllText(_store.ConfigFile));
        AppConfig loaded = _store.Load();
        Assert.Null(_store.LastLoadError);
        LauncherItem item = Assert.Single(loaded.Items);
        Assert.Equal("saved", item.Id);
        Assert.Equal("Ctrl+Alt+D", item.Hotkey);
        Assert.True(item.RunInTerminal);
        Assert.Equal(_directory, item.WorkingDirectory);
    }

    [Fact]
    public void Malformed_json_is_preserved_and_default_save_is_blocked()
    {
        File.WriteAllText(_store.ConfigFile, "{ invalid json");
        Assert.Empty(_store.Load().Items);
        Assert.NotNull(_store.LastLoadError);
        Assert.False(_store.Save(new AppConfig()));
        Assert.Equal("{ invalid json", File.ReadAllText(_store.ConfigFile + ".broken"));
        Assert.False(File.Exists(_store.ConfigFile));
        Assert.Empty(_store.Load().Items);
        Assert.NotNull(_store.LastLoadError);
        Assert.False(File.Exists(_store.ConfigFile));
        Assert.NotNull(_store.SwitchStorageMode(false, _directory));
        Assert.True(File.Exists(Path.Combine(_directory, AppPaths.PortableMarker)));
        Assert.True(_store.Save(new AppConfig(), overwriteAfterLoadFailure: true));
        Assert.Null(_store.LastLoadError);
    }

    [Fact]
    public void Export_and_import_preserve_command_options_shortcut_and_recommendation()
    {
        var original = new LauncherItem
        {
            Id = "saved", Name = "Build", Kind = ItemKind.Command, Target = "dotnet build",
            Arguments = "--ignored", WorkingDirectory = _directory, Hotkey = "Ctrl+Alt+D",
            UsePowerShell = true, RunInTerminal = true, RecommendedId = "vex"
        };
        var config = new AppConfig();
        config.Items.Add(original);
        string archive = Path.Combine(_directory, "backup.qa");
        Assert.True(_store.Export(config, archive));
        AppConfig imported = Assert.IsType<AppConfig>(_store.Import(archive));
        LauncherItem restored = Assert.Single(imported.Items);
        Assert.Equal(original.Id, restored.Id);
        Assert.Equal(original.Target, restored.Target);
        Assert.Equal(original.WorkingDirectory, restored.WorkingDirectory);
        Assert.Equal(original.Hotkey, restored.Hotkey);
        Assert.True(restored.UsePowerShell);
        Assert.True(restored.RunInTerminal);
        Assert.Equal(original.RecommendedId, restored.RecommendedId);
        Assert.Equal(original.Arguments, restored.Arguments);
    }

    public void Dispose() => Directory.Delete(_directory, true);
}
