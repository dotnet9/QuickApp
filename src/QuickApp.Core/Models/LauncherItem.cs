namespace QuickApp.Core.Models;

/// <summary>一个快捷项。</summary>
public sealed class LauncherItem
{
    /// <summary>稳定标识，用于拖拽排序、图标缓存与撤销。</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>显示名称。为空时由 <see cref="Services.ItemQuery.ResolveDisplayName"/> 从目标推断。</summary>
    public string Name { get; set; } = string.Empty;

    public ItemKind Kind { get; set; } = ItemKind.App;

    /// <summary>文件路径 / 网址 / 命令行原文。</summary>
    public string Target { get; set; } = string.Empty;

    /// <summary>启动参数，仅 <see cref="ItemKind.App"/> 使用。</summary>
    public string? Arguments { get; set; }

    /// <summary>工作目录，仅 <see cref="ItemKind.App"/> 使用。</summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>用户选择的本地图片图标（png/jpg/ico 等）；为空时按目标自动提取真实图标。</summary>
    public string? CustomIconPath { get; set; }

    /// <summary>来源为推荐应用时的目录条目 Id（设置 · 推荐页用它识别已安装的快捷方式）。</summary>
    public string? RecommendedId { get; set; }
}
