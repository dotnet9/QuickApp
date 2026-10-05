using System;
using System.IO;
using QuickApp.Core.Services;
using Xunit;

namespace QuickApp.Core.Tests;

/// <summary>
/// 安装版配置根迁移（%APPDATA%\QuickApp → %LOCALAPPDATA%\QuickApp）：
/// 一次性、不动旧目录、便携模式与已迁移场景跳过；
/// 并修复 v0.4.2/0.4.3 的缺陷迁移（内容被拷到 LOCALAPPDATA 根、少了一级 QuickApp，
/// 应用随后自动生成的默认配置需要用旧数据覆盖）。
/// </summary>
public sealed class AppPathsMigrationTests : IDisposable
{
    private readonly string _dir;

    public AppPathsMigrationTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "QuickAppTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    private string LegacyRoot => Path.Combine(_dir, "legacy", "QuickApp");

    /// <summary>模拟真实布局：legacy 顶替 %APPDATA%，新根顶替 %LOCALAPPDATA%\QuickApp。</summary>
    private void SeedLegacy(string content = "{}")
    {
        Directory.CreateDirectory(LegacyRoot);
        File.WriteAllText(Path.Combine(LegacyRoot, "config.json"), content);
        Directory.CreateDirectory(Path.Combine(LegacyRoot, "icons"));
        File.WriteAllText(Path.Combine(LegacyRoot, "icons", "a.png"), "png");
        File.WriteAllText(Path.Combine(LegacyRoot, "update-state.json"), "{}");
    }

    private string Migrate(string? newRootOverride = null)
    {
        var target = newRootOverride ?? Path.Combine(_dir, "local", "QuickApp");
        AppPaths.MigrateLegacyConfig(_dir,
            legacyRootOverride: Path.Combine(_dir, "legacy"),
            newRootOverride: target);
        return target;
    }

    [Fact]
    public void Migrates_legacy_roaming_config_to_new_root()
    {
        SeedLegacy();

        var target = Migrate();

        Assert.True(File.Exists(Path.Combine(target, "config.json")));
        Assert.True(File.Exists(Path.Combine(target, "icons", "a.png")));
        Assert.True(File.Exists(Path.Combine(target, "update-state.json")));
        Assert.True(File.Exists(Path.Combine(LegacyRoot, "config.json")));      // 旧目录保留作备份
    }

    [Fact]
    public void Skips_when_already_migrated()
    {
        SeedLegacy("{\"old\":1}");
        var target = Path.Combine(_dir, "local", "QuickApp");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "config.json"), "{\"already\":\"new\"}");

        Migrate(target);

        Assert.Contains("already", File.ReadAllText(Path.Combine(target, "config.json")));  // 不覆盖新配置
    }

    [Fact]
    public void Skips_in_portable_mode()
    {
        File.WriteAllText(Path.Combine(_dir, AppPaths.PortableMarker), "");
        SeedLegacy();

        Migrate();

        Assert.False(File.Exists(Path.Combine(_dir, "local", "QuickApp", "config.json")));  // 便携模式配置在 exe 旁，不迁移
    }

    [Fact]
    public void Noop_when_legacy_missing()
    {
        Migrate();
        Assert.False(File.Exists(Path.Combine(_dir, "local", "QuickApp", "config.json")));
    }

    [Fact]
    public void Repairs_default_config_created_by_broken_v0_4_2_migration()
    {
        // v0.4.2/0.4.3 的坏迁移把旧配置拷到了 LOCALAPPDATA 根（少了一级 QuickApp），
        // 应用随后在正确位置自动生成了默认配置——旧数据必须覆盖回来
        SeedLegacy("{\"theme\":\"dark\",\"items\":[\"my-apps\"]}");
        var localRoot = Path.Combine(_dir, "local");
        var target = Path.Combine(localRoot, "QuickApp");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "config.json"), "{\"theme\":\"light\",\"items\":[]}");
        File.WriteAllText(Path.Combine(localRoot, "config.json"), "{\"theme\":\"light\"}");   // 坏迁移散落的标记
        Directory.CreateDirectory(Path.Combine(localRoot, "icons"));

        var migrated = Migrate(target);

        var restored = File.ReadAllText(Path.Combine(migrated, "config.json"));
        Assert.Contains("dark", restored);                                      // 旧数据覆盖自动默认配置
        Assert.True(File.Exists(Path.Combine(migrated, "icons", "a.png")));
        Assert.False(File.Exists(Path.Combine(localRoot, "config.json")));      // 散落文件被清理
        Assert.False(Directory.Exists(Path.Combine(localRoot, "icons")));
    }

    [Fact]
    public void Fresh_install_without_strays_is_not_overwritten_by_repair()
    {
        // 全新用户：新位置有自己改过的配置、没有坏迁移散落标记 → 不能被误覆盖
        SeedLegacy("{\"old\":1}");
        var localRoot = Path.Combine(_dir, "local");
        var target = Path.Combine(localRoot, "QuickApp");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "config.json"), "{\"mine\":true}");

        Migrate(target);

        Assert.Contains("mine", File.ReadAllText(Path.Combine(target, "config.json")));
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* 临时目录清理失败可忽略 */ }
    }
}
