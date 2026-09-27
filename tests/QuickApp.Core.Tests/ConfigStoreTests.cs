using System;
using System.IO;
using QuickApp.Core.Models;
using QuickApp.Core.Services;
using Xunit;

namespace QuickApp.Core.Tests;

/// <summary>配置读写：损坏回滚、原子保存、去重与夹取，都是「不能让程序起不来」的关键路径。</summary>
public sealed class ConfigStoreTests : IDisposable
{
    private readonly string _dir;

    public ConfigStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "QuickAppTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_dir))
            {
                Directory.Delete(_dir, recursive: true);
            }
        }
        catch
        {
            // 清理失败不影响断言
        }
    }

    private ConfigStore CreateStore() => new(_dir);

    [Fact]
    public void Missing_file_yields_default_seed_items()
    {
        AppConfig config = CreateStore().Load();

        Assert.NotEmpty(config.Items);
        Assert.Equal(1, config.SchemaVersion);
    }

    [Fact]
    public void Save_then_load_round_trips()
    {
        ConfigStore store = CreateStore();
        AppConfig config = store.Load();
        config.Settings.Edge = DockEdge.Left;
        config.Settings.TileSize = 56;
        config.Items.Add(new LauncherItem { Id = "x1", Name = "自定义", Kind = ItemKind.Command, Target = "echo hi" });

        Assert.True(store.Save(config));

        AppConfig reloaded = CreateStore().Load();
        Assert.Equal(DockEdge.Left, reloaded.Settings.Edge);
        Assert.Equal(56, reloaded.Settings.TileSize);
        Assert.Contains(reloaded.Items, i => i.Id == "x1" && i.Target == "echo hi");
    }

    [Fact]
    public void Corrupt_file_falls_back_and_is_quarantined()
    {
        ConfigStore store = CreateStore();
        File.WriteAllText(store.ConfigFile, "{ 这不是 json ");

        AppConfig config = store.Load();

        Assert.NotEmpty(config.Items);                                        // 回退到默认
        Assert.True(File.Exists(store.ConfigFile + ".broken"));               // 坏文件被留档
    }

    [Fact]
    public void Normalize_fills_ids_drops_empty_and_clamps_numbers()
    {
        var config = new AppConfig
        {
            Items = new System.Collections.Generic.List<LauncherItem>
            {
                new LauncherItem { Target = @"C:\a\b.exe" },                  // 无 Id
                new LauncherItem { Id = "dup", Target = @"C:\c\d.exe" },
                new LauncherItem { Id = "dup", Target = @"C:\e\f.exe" },      // 重复 Id
                new LauncherItem { Id = "empty", Target = "   " }             // 空目标，应丢弃
            }
        };
        config.Settings.TileSize = 500;
        config.Settings.CornerRadius = -10;
        config.Settings.PanelOpacity = 9;

        AppConfig normalized = ConfigStore.Normalize(config);

        Assert.Equal(3, normalized.Items.Count);                          // 空目标被丢掉
        Assert.All(normalized.Items, i => Assert.False(string.IsNullOrWhiteSpace(i.Id)));
        Assert.Equal(3, normalized.Items.ConvertAll(i => i.Id).Count);     // Id 全部唯一
        Assert.Equal(3, new System.Collections.Generic.HashSet<string>(
            normalized.Items.ConvertAll(i => i.Id)).Count);

        Assert.InRange(normalized.Settings.TileSize, 24, 96);
        Assert.InRange(normalized.Settings.CornerRadius, 0, 40);
        Assert.InRange(normalized.Settings.PanelOpacity, 0.2, 1);
        Assert.Equal("b", normalized.Items[0].Name);                       // 名称从文件名推断
    }

    [Fact]
    public void Portable_marker_switches_config_location()
    {
        File.WriteAllText(Path.Combine(_dir, "portable.txt"), "");

        string path = AppPaths.ConfigFile(_dir);

        Assert.Equal(Path.Combine(_dir, "config.json"), path);
        Assert.True(AppPaths.IsPortable(_dir));
    }

    [Fact]
    public void Non_portable_uses_appdata()
    {
        Assert.False(AppPaths.IsPortable(_dir));
        Assert.Contains("QuickApp", AppPaths.ConfigFile(_dir));
    }
}
