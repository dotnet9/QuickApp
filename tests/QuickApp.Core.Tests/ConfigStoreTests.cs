using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
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

    /// <summary>带便携标记的存储：配置与图标库全部落在临时目录，测试永不触碰真实的 %APPDATA%。</summary>
    private ConfigStore CreateStore()
    {
        File.WriteAllText(Path.Combine(_dir, "portable.txt"), "");
        return new(_dir);
    }

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
    public void Export_then_import_round_trips()
    {
        ConfigStore store = CreateStore();
        AppConfig config = store.Load();
        config.Settings.Edge = DockEdge.Right;
        config.Items.Add(new LauncherItem { Id = "exp1", Name = "导出项", Kind = ItemKind.App, Target = @"C:\a.exe" });

        string exportPath = Path.Combine(_dir, "export.qa");
        Assert.True(store.Export(config, exportPath));

        AppConfig? imported = store.Import(exportPath);
        Assert.NotNull(imported);
        Assert.Equal(DockEdge.Right, imported!.Settings.Edge);
        Assert.Contains(imported.Items, i => i.Id == "exp1" && i.Name == "导出项");
    }

    [Fact]
    public void Export_bundles_icons_and_fresh_import_restores_them()
    {
        // 导出方：图标入库后随 .qa 打包（JSON 里只记文件名）
        ConfigStore source = CreateStore();
        string image = Path.Combine(_dir, "img", "cat.png");
        Directory.CreateDirectory(Path.GetDirectoryName(image)!);
        File.WriteAllText(image, "fake png");
        string? stored = source.ImportCustomIcon(image, "ic1", out string? error);

        Assert.Null(error);
        var config = source.Load();
        config.Items.Add(new LauncherItem { Id = "ic1", Name = "带图标", Kind = ItemKind.App, Target = @"C:\a.exe", CustomIconPath = stored });
        string exportPath = Path.Combine(_dir, "export.qa");
        Assert.True(source.Export(config, exportPath));

        using (ZipArchive zip = ZipFile.OpenRead(exportPath))
        {
            Assert.NotNull(zip.GetEntry("config.json"));
            Assert.NotNull(zip.GetEntry("icons/ic1.png"));
        }

        // 导入方：全新「机器」（另一个便携目录），图标解包到本地库并映射回本机路径
        string otherDir = Path.Combine(_dir, "other-machine");
        Directory.CreateDirectory(otherDir);
        File.WriteAllText(Path.Combine(otherDir, "portable.txt"), "");
        var target = new ConfigStore(otherDir);
        AppConfig? imported = target.Import(exportPath);

        Assert.NotNull(imported);
        LauncherItem item = imported!.Items.Find(i => i.Id == "ic1")!;
        Assert.NotNull(item.CustomIconPath);
        Assert.True(File.Exists(item.CustomIconPath));
        Assert.StartsWith(target.IconLibraryDirectory, item!.CustomIconPath);
        Assert.Equal("fake png", File.ReadAllText(item.CustomIconPath!));
    }

    [Fact]
    public void Import_clears_external_icon_paths_that_do_not_exist_locally()
    {
        ConfigStore source = CreateStore();
        AppConfig config = source.Load();
        config.Items.Add(new LauncherItem { Id = "msix1", Name = "打包应用", Kind = ItemKind.App, Target = @"C:\a.exe", CustomIconPath = @"C:\不存在\logo.png" });
        string exportPath = Path.Combine(_dir, "export.qa");
        Assert.True(source.Export(config, exportPath));

        string otherDir = Path.Combine(_dir, "other-machine");
        Directory.CreateDirectory(otherDir);
        File.WriteAllText(Path.Combine(otherDir, "portable.txt"), "");
        AppConfig? imported = new ConfigStore(otherDir).Import(exportPath);

        LauncherItem item = imported!.Items.Find(i => i.Id == "msix1")!;
        Assert.Null(item.CustomIconPath);                        // 死链清掉，走自动图标提取
        Assert.Equal("打包应用", item.Name);
    }

    [Fact]
    public void ImportCustomIcon_copies_into_library_by_item_id()
    {
        ConfigStore store = CreateStore();
        string source = Path.Combine(_dir, "cat.png");
        File.WriteAllText(source, "fake png");

        string? stored = store.ImportCustomIcon(source, "i1", out string? error);

        Assert.Null(error);
        Assert.Equal(Path.Combine(store.IconLibraryDirectory, "i1.png"), stored);
        Assert.True(File.Exists(stored!));

        // 同 Id 换成 .ico：覆盖式入库，旧扩展名不残留
        string second = Path.Combine(_dir, "dog.ico");
        File.WriteAllText(second, "fake ico");
        string? updated = store.ImportCustomIcon(second, "i1", out _);

        Assert.Equal(Path.Combine(store.IconLibraryDirectory, "i1.ico"), updated);
        Assert.True(File.Exists(updated!));
        Assert.False(File.Exists(Path.Combine(store.IconLibraryDirectory, "i1.png")));
    }

    [Fact]
    public void ImportCustomIcon_skips_when_source_already_in_library()
    {
        ConfigStore store = CreateStore();
        Directory.CreateDirectory(store.IconLibraryDirectory);
        string inside = Path.Combine(store.IconLibraryDirectory, "i9.png");
        File.WriteAllText(inside, "fake");

        string? stored = store.ImportCustomIcon(inside, "i9", out _);

        Assert.Equal(inside, stored);
    }

    [Fact]
    public void ImportCustomIcon_reports_missing_source()
    {
        string? stored = CreateStore().ImportCustomIcon(Path.Combine(_dir, "ghost.png"), "i1", out string? error);

        Assert.Null(stored);
        Assert.NotNull(error);
    }

    [Fact]
    public void CleanupOrphanedIcons_removes_only_unreferenced_files()
    {
        ConfigStore store = CreateStore();
        Directory.CreateDirectory(store.IconLibraryDirectory);
        string kept = Path.Combine(store.IconLibraryDirectory, "keep.png");
        string orphan = Path.Combine(store.IconLibraryDirectory, "orphan.png");
        File.WriteAllText(kept, "a");
        File.WriteAllText(orphan, "b");

        store.CleanupOrphanedIcons(new List<LauncherItem>
        {
            new LauncherItem { Id = "keep", CustomIconPath = kept }
        });

        Assert.True(File.Exists(kept));
        Assert.False(File.Exists(orphan));
    }

    [Fact]
    public void SwitchStorageMode_moves_icons_and_remaps_paths()
    {
        // 便携目录 A → 便携目录 B：两端都落在临时目录里，全程不碰真实 AppData
        string sourceDir = Path.Combine(_dir, "portable-a");
        Directory.CreateDirectory(sourceDir);
        File.WriteAllText(Path.Combine(sourceDir, "portable.txt"), "");
        ConfigStore store = new(sourceDir);
        string image = Path.Combine(_dir, "cat.png");
        File.WriteAllText(image, "fake png");
        string? stored = store.ImportCustomIcon(image, "i1", out _);

        string targetDir = Path.Combine(_dir, "portable-b");
        Directory.CreateDirectory(targetDir);
        AppConfig config = store.Load();
        config.Items.Add(new LauncherItem { Id = "i1", Name = "有图标", Kind = ItemKind.App, Target = @"C:\a.exe", CustomIconPath = stored });

        string? error = store.SwitchStorageMode(toPortable: true, targetDir, config);

        Assert.Null(error);
        string newLibrary = AppPaths.IconLibraryDirectory(targetDir);
        Assert.True(File.Exists(Path.Combine(newLibrary, "i1.png")));         // 图标库整体搬过去了
        Assert.True(File.Exists(AppPaths.ConfigFile(targetDir)));             // 配置也搬过去了
        Assert.Equal(Path.Combine(newLibrary, "i1.png"), config.Items.Find(i => i.Id == "i1")!.CustomIconPath);
    }

    [Fact]
    public void Import_rejects_invalid_files()
    {
        string bad = Path.Combine(_dir, "bad.qa");
        File.WriteAllText(bad, "not a zip at all");
        Assert.Null(CreateStore().Import(bad));

        string noConfig = Path.Combine(_dir, "no-config.qa");
        using (FileStream stream = File.Create(noConfig))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            archive.CreateEntry("random.txt");
        }
        Assert.Null(CreateStore().Import(noConfig));             // zip 里没有 config.json

        Assert.Null(CreateStore().Import(Path.Combine(_dir, "missing.qa")));
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
        // 用独立目录测「无便携标记 → 走 AppData」的路径推导，不落任何文件
        string plain = Path.Combine(Path.GetTempPath(), "QuickAppTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(plain);
        try
        {
            Assert.False(AppPaths.IsPortable(plain));
            Assert.Contains("QuickApp", AppPaths.ConfigFile(plain));
        }
        finally
        {
            Directory.Delete(plain, recursive: true);
        }
    }
}
