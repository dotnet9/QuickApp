namespace QuickApp.Core.Models;

using System;

/// <summary>
/// 推荐应用目录条目：只描述「是什么软件、从哪个仓库取发布包、装完后去哪找主程序」。
/// 安装包的下载地址永远不写在这里——每次都按操作系统实时拉取该仓库最新 Release 的资产，
/// 软件自动发新版后无需改这份目录。
/// </summary>
public sealed class RecommendedApp
{
    /// <summary>稳定标识，用于安装状态记录与 Dock 条目关联。</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>加入 Dock 时的显示名称（尽量短）。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>推荐卡片上的展示名；为空时回退 <see cref="Name"/>。</summary>
    public string? DisplayName { get; set; }

    /// <summary>一句话介绍。</summary>
    public string? Description { get; set; }

    /// <summary>GitHub 仓库拥有者。</summary>
    public string Owner { get; set; } = string.Empty;

    /// <summary>GitHub 仓库名。</summary>
    public string Repo { get; set; } = string.Empty;

    /// <summary>
    /// Windows 安装器的静默安装参数（如 Inno Setup 的 /VERYSILENT）；
    /// 为空表示直接启动安装器，由用户手动完成。
    /// </summary>
    public string? InstallArgumentsWindows { get; set; }

    /// <summary>安装完成后主程序候选路径（Windows，支持 %环境变量% 展开），按优先级排列。</summary>
    public string[] LauncherPathsWindows { get; set; } = System.Array.Empty<string>();

    /// <summary>安装完成后主程序候选路径（Linux）。</summary>
    public string[] LauncherPathsLinux { get; set; } = System.Array.Empty<string>();

    /// <summary>安装完成后主程序候选路径（macOS，可以是 .app 包）。</summary>
    public string[] LauncherPathsMacOS { get; set; } = System.Array.Empty<string>();

    /// <summary>当前系统的主程序候选路径。</summary>
    public string[] LauncherPathsForCurrentPlatform()
        => OperatingSystem.IsWindows() ? LauncherPathsWindows
            : OperatingSystem.IsMacOS() ? LauncherPathsMacOS
            : OperatingSystem.IsLinux() ? LauncherPathsLinux
            : System.Array.Empty<string>();
}
