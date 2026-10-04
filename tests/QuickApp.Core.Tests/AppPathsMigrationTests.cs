using System;
using System.IO;
using QuickApp.Core.Services;
using Xunit;

namespace QuickApp.Core.Tests;

/// <summary>安装版配置根迁移（%APPDATA% → %LOCALAPPDATA%）：一次性、不动旧目录、便携模式与已迁移场景跳过。</summary>
public sealed class AppPathsMigrationTests : IDisposable
{
    private readonly string _dir;

    public AppPathsMigrationTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "QuickAppTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [Fact]
    public void Migrates_legacy_roaming_config_to_new_root()
    {
        string legacyRoot = Path.Combine(_dir, "legacy", "QuickApp");
        Directory.CreateDirectory(legacyRoot);
        File.WriteAllText(Path.Combine(legacyRoot, "config.json"), "{}");
        Directory.CreateDirectory(Path.Combine(legacyRoot, "icons"));
        File.WriteAllText(Path.Combine(legacyRoot, "icons", "a.png"), "png");
        File.WriteAllText(Path.Combine(legacyRoot, "update-state.json"), "{}");

        // 两个 override 都顶替真实环境（%APPDATA% 与 %LOCALAPPDATA%），测试完全落在临时目录
        string legacyOverride = Path.Combine(_dir, "legacy");
        string newRoot = Path.Combine(_dir, "local");
        AppPaths.MigrateLegacyConfig(_dir, legacyRootOverride: legacyOverride, newRootOverride: newRoot);

        Assert.True(File.Exists(Path.Combine(newRoot, "config.json")));
        Assert.True(File.Exists(Path.Combine(newRoot, "icons", "a.png")));
        Assert.True(File.Exists(Path.Combine(newRoot, "update-state.json")));
        Assert.True(File.Exists(Path.Combine(legacyRoot, "config.json")));      // 旧目录保留作备份
    }

    [Fact]
    public void Skips_when_already_migrated()
    {
        string legacyOverride = Path.Combine(_dir, "legacy");
        string newRoot = Path.Combine(_dir, "local");
        string newConfig = Path.Combine(newRoot, "QuickApp", "config.json");
        Directory.CreateDirectory(Path.GetDirectoryName(newConfig)!);
        File.WriteAllText(newConfig, "{\"already\":\"new\"}");

        string legacyRoot = Path.Combine(legacyOverride, "QuickApp");
        Directory.CreateDirectory(legacyRoot);
        File.WriteAllText(Path.Combine(legacyRoot, "config.json"), "{\"old\":1}");

        AppPaths.MigrateLegacyConfig(_dir, legacyRootOverride: legacyOverride, newRootOverride: newRoot);

        Assert.Contains("already", File.ReadAllText(newConfig));                // 不覆盖新配置
    }

    [Fact]
    public void Skips_in_portable_mode()
    {
        File.WriteAllText(Path.Combine(_dir, AppPaths.PortableMarker), "");
        string legacyRoot = Path.Combine(_dir, "legacy", "QuickApp");
        Directory.CreateDirectory(legacyRoot);
        File.WriteAllText(Path.Combine(legacyRoot, "config.json"), "{}");
        string newRoot = Path.Combine(_dir, "local");

        AppPaths.MigrateLegacyConfig(_dir, legacyRootOverride: Path.Combine(_dir, "legacy"), newRootOverride: newRoot);

        Assert.False(File.Exists(Path.Combine(newRoot, "config.json")));  // 便携模式配置在 exe 旁，不迁移
    }

    [Fact]
    public void Noop_when_legacy_missing()
    {
        string newRoot = Path.Combine(_dir, "local");
        AppPaths.MigrateLegacyConfig(_dir, legacyRootOverride: Path.Combine(_dir, "nothing"), newRootOverride: newRoot);
        Assert.False(File.Exists(Path.Combine(newRoot, "config.json")));
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* 临时目录清理失败可忽略 */ }
    }
}
