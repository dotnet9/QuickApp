namespace QuickApp.Core.Models;

/// <summary>应用设置。全部可持久化，字段名即配置文件字段名（camelCase）。</summary>
public sealed class AppSettings
{
    /// <summary>停靠边缘。原型里四边可拖拽吸附。</summary>
    public DockEdge Edge { get; set; } = DockEdge.Top;

    /// <summary>dark | light | system。</summary>
    public string Theme { get; set; } = "dark";

    /// <summary>glass | flat。</summary>
    public string Style { get; set; } = "glass";

    /// <summary>是否在图标下方显示名称（对应原型的「仅图标 / 图标 + 名称」）。</summary>
    public bool ShowLabels { get; set; }

    public double TileSize { get; set; } = 44;

    public double CornerRadius { get; set; } = 18;

    /// <summary>面板不透明度 0.4 ~ 1.0。</summary>
    public double PanelOpacity { get; set; } = 0.4;

    /// <summary>钉住后不再自动隐藏。</summary>
    public bool Pinned { get; set; }

    /// <summary>鼠标触到所在边缘时滑出。</summary>
    public bool RevealOnEdgeTouch { get; set; } = true;

    public int AutoHideDelayMs { get; set; } = 700;

    /// <summary>执行一个项之后自动收起。</summary>
    public bool CollapseAfterLaunch { get; set; } = true;

    /// <summary>开机启动（写 HKCU\...\Run）。</summary>
    public bool AutoStart { get; set; }

    /// <summary>启动时检查 GitHub Releases 更新。</summary>
    public bool CheckUpdates { get; set; } = true;

    /// <summary>全局唤醒热键，形如 Ctrl+Alt+Space。</summary>
    public string Hotkey { get; set; } = "Ctrl+Alt+Space";

    /// <summary>停靠的屏幕序号；-1 表示主屏。</summary>
    public int MonitorIndex { get; set; } = -1;
}
