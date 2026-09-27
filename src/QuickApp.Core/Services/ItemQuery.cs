using System;
using System.Collections.Generic;
using System.IO;
using QuickApp.Core.Models;

namespace QuickApp.Core.Services;

/// <summary>搜索、名称推断、气泡副标题等纯逻辑，便于单元测试。</summary>
public static class ItemQuery
{
    /// <summary>按当前搜索词过滤，顺序保持不变。</summary>
    public static IReadOnlyList<LauncherItem> Filter(IReadOnlyList<LauncherItem> items, string? query)
    {
        if (items is null || items.Count == 0)
        {
            return Array.Empty<LauncherItem>();
        }

        string q = (query ?? string.Empty).Trim();
        if (q.Length == 0)
        {
            return items;
        }

        var result = new List<LauncherItem>();
        foreach (LauncherItem item in items)
        {
            if (Matches(item, q))
            {
                result.Add(item);
            }
        }

        return result;
    }

    public static bool Matches(LauncherItem item, string query)
    {
        if (item is null || query.Length == 0)
        {
            return false;
        }

        return Contains(item.Name, query)
            || Contains(item.Target, query)
            || Contains(KindLabel(item.Kind), query);
    }

    private static bool Contains(string? source, string query)
        => !string.IsNullOrEmpty(source) && source.Contains(query, StringComparison.OrdinalIgnoreCase);

    public static string KindLabel(ItemKind kind) => kind switch
    {
        ItemKind.Web => "网页",
        ItemKind.Command => "命令",
        _ => "应用"
    };

    /// <summary>名称为空时从目标推断：文件名去扩展名，网址取主机名。</summary>
    public static string ResolveDisplayName(LauncherItem item)
    {
        if (item is null || string.IsNullOrWhiteSpace(item.Target))
        {
            return "未命名";
        }

        if (!string.IsNullOrWhiteSpace(item.Name))
        {
            return item.Name.Trim();
        }

        if (item.Kind == ItemKind.Web && Uri.TryCreate(item.Target, UriKind.Absolute, out Uri? uri))
        {
            return uri.Host;
        }

        if (item.Kind == ItemKind.Command)
        {
            return item.Target.Length <= 24 ? item.Target : item.Target[..24] + "…";
        }

        try
        {
            string name = Path.GetFileNameWithoutExtension(item.Target);
            return string.IsNullOrWhiteSpace(name) ? item.Target : name;
        }
        catch (ArgumentException)
        {
            return item.Target;
        }
    }

    /// <summary>气泡/提示里的第二行：网址给域名，命令给原文，应用给路径尾段。</summary>
    public static string DescribeTarget(LauncherItem item)
    {
        if (item is null || string.IsNullOrWhiteSpace(item.Target))
        {
            return string.Empty;
        }

        if (item.Kind == ItemKind.Web)
        {
            return Uri.TryCreate(item.Target, UriKind.Absolute, out Uri? uri) ? uri.Host : item.Target;
        }

        if (item.Kind == ItemKind.Command)
        {
            return item.Target;
        }

        string[] parts = item.Target.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return item.Target;
        }

        return parts.Length == 1
            ? parts[0]
            : "…\\" + parts[^2] + "\\" + parts[^1];
    }

    /// <summary>按下标取项，越界返回 null（数字键 1~9 快启用）。</summary>
    public static LauncherItem? At(IReadOnlyList<LauncherItem> items, int index)
        => items is not null && index >= 0 && index < items.Count ? items[index] : null;

    /// <summary>「复制为命令」：能直接粘到 cmd / Win+R 里用的形式，网页补 start 前缀。</summary>
    public static string ToCommandText(LauncherItem item)
    {
        if (item is null || string.IsNullOrWhiteSpace(item.Target))
        {
            return string.Empty;
        }

        return item.Kind == ItemKind.Web && !item.Target.StartsWith("start ", StringComparison.OrdinalIgnoreCase)
            ? "start " + item.Target
            : item.Target;
    }

    /// <summary>把 from 位置的项移动到 to 位置（拖拽排序，纯列表操作）。</summary>
    public static void Move(IList<LauncherItem> items, int from, int to)
    {
        if (items is null || from < 0 || from >= items.Count)
        {
            return;
        }

        to = Math.Clamp(to, 0, items.Count - 1);
        if (from == to)
        {
            return;
        }

        LauncherItem item = items[from];
        items.RemoveAt(from);
        items.Insert(to, item);
    }
}
