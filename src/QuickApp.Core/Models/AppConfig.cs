using System.Collections.Generic;

namespace QuickApp.Core.Models;

/// <summary>配置文件根对象。全新格式，不迁移旧的 menu.json。</summary>
public sealed class AppConfig
{
    /// <summary>配置结构版本，便于将来升级。</summary>
    public int SchemaVersion { get; set; } = 1;

    public AppSettings Settings { get; set; } = new();

    /// <summary>顺序即 Dock 显示顺序。</summary>
    public List<LauncherItem> Items { get; set; } = new();
}
