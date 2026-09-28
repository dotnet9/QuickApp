using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using QuickApp.Core.Models;

namespace QuickApp.Core.Services;

/// <summary>搜索、名称推断、气泡副标题等纯逻辑，便于单元测试。</summary>
public static class ItemQuery
{
    private static readonly IReadOnlyDictionary<char, string> Pinyin =
        new Dictionary<char, string>
        {
            ['微'] = "wei", ['信'] = "xin", ['公'] = "gong", ['众'] = "zhong", ['号'] = "hao",
            ['钉'] = "ding", ['腾'] = "teng", ['讯'] = "xun", ['视'] = "shi", ['频'] = "pin",
            ['会'] = "hui", ['议'] = "yi", ['百'] = "bai", ['度'] = "du", ['网'] = "wang",
            ['盘'] = "pan", ['魔'] = "mo", ['音'] = "yin", ['向'] = "xiang", ['日'] = "ri",
            ['葵'] = "kui", ['启'] = "qi", ['动'] = "dong", ['有'] = "you", ['道'] = "dao",
            ['云'] = "yun", ['笔'] = "bi", ['记'] = "ji", ['格'] = "ge", ['式'] = "shi",
            ['化'] = "hua", ['软'] = "ruan", ['件'] = "jian", ['管'] = "guan", ['家'] = "jia",
            ['事'] = "shi", ['本'] = "ben", ['远'] = "yuan", ['程'] = "cheng", ['桌'] = "zhuo",
            ['面'] = "mian", ['应'] = "ying", ['用'] = "yong", ['页'] = "ye",
            ['命'] = "ming", ['令'] = "ling", ['文'] = "wen", ['资'] = "zi", ['源'] = "yuan",
            ['理'] = "li", ['器'] = "qi", ['计'] = "ji", ['算'] = "suan", ['提'] = "ti",
            ['示'] = "shi", ['符'] = "fu", ['路'] = "lu", ['径'] = "jing"
        };

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

        string[] tokens = query.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        foreach (string token in tokens)
        {
            if (!MatchesToken(item, token))
            {
                return false;
            }
        }

        return tokens.Length > 0;
    }

    private static bool MatchesToken(LauncherItem item, string token)
    {
        if (Contains(item.Target, token))
        {
            return true;
        }

        if (ContainsAnyForm(item.Name, token) || ContainsAnyForm(KindLabel(item.Kind), token))
        {
            return true;
        }

        return false;
    }

    private static bool ContainsAnyForm(string? source, string token)
    {
        foreach (string form in SearchForms(source))
        {
            if (Contains(form, token))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>返回原文、全拼和首字母三种形式，保持搜索零依赖。</summary>
    public static IReadOnlyList<string> SearchForms(string? source)
    {
        string text = (source ?? string.Empty).Trim().ToLowerInvariant();
        if (text.Length == 0)
        {
            return Array.Empty<string>();
        }

        var full = new StringBuilder(text.Length * 2);
        var initials = new StringBuilder(text.Length);
        foreach (char character in text)
        {
            if (Pinyin.TryGetValue(character, out string? pinyin))
            {
                full.Append(pinyin);
                initials.Append(pinyin[0]);
            }
            else
            {
                full.Append(character);
                initials.Append(character);
            }
        }

        return new[] { text, full.ToString(), initials.ToString() };
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
