namespace QuickApp.Core.Models;

/// <summary>Dock 停靠的屏幕边缘。</summary>
public enum DockEdge
{
    Top,
    Bottom,
    Left,
    Right
}

/// <summary>快捷项类型，对应原 WPF 版的 MenuItemType。</summary>
public enum ItemKind
{
    /// <summary>可执行文件、快捷方式、文件夹等交给 Shell 打开的目标。</summary>
    App,

    /// <summary>网址。</summary>
    Web,

    /// <summary>命令行。</summary>
    Command
}
