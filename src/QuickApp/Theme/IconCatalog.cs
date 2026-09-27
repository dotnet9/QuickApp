using System;
using System.Collections.Generic;
using Avalonia.Media;

namespace QuickApp.Theme;

/// <summary>
/// 「更换图标」选择器的图标目录。
/// 键名与 LauncherItem.IconKey 对应，持久化到 config.json；为空时按目标自动提取真实图标。
/// </summary>
public static class IconCatalog
{
    /// <summary>选择器里的图标与顺序（对应原型 PICKER_ICONS）。</summary>
    public static readonly IReadOnlyList<string> PickerKeys = new[]
    {
        "globe", "chat", "play", "music", "camera",
        "monitor", "monitor-alt", "note", "cloud", "code",
        "rocket", "database", "shield", "record", "app", "terminal"
    };

    public static bool TryResolve(string? key, out Geometry geometry)
    {
        Geometry? resolved = key switch
        {
            "globe" => Icons.Globe,
            "chat" => Icons.Chat,
            "play" => Icons.Play,
            "music" => Icons.Music,
            "camera" => Icons.Camera,
            "monitor" => Icons.Monitor,
            "monitor-alt" => Icons.MonitorAlt,
            "note" => Icons.Note,
            "cloud" => Icons.Cloud,
            "code" => Icons.Code,
            "rocket" => Icons.Rocket,
            "database" => Icons.Database,
            "shield" => Icons.Shield,
            "record" => Icons.Record,
            "app" => Icons.Application,
            "terminal" => Icons.Terminal,
            _ => null
        };

        if (resolved is null)
        {
            geometry = Icons.Application;
            return false;
        }

        geometry = resolved;
        return true;
    }
}
