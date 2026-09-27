using System;

namespace QuickApp.Core.Services;

/// <summary>版本号比较。tag 形如 v1.2.3 / 1.2 / 1.2.3.4 都能吃。</summary>
public static class VersionUtil
{
    /// <summary>把 tag 解析成可比较的 Version，失败返回 null。</summary>
    public static Version? Parse(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return null;
        }

        string text = tag.Trim();
        if (text.Length > 0 && (text[0] == 'v' || text[0] == 'V'))
        {
            text = text[1..];
        }

        // 去掉 1.2.3-beta.1 这类后缀
        int dash = text.IndexOfAny(new[] { '-', '+' });
        if (dash > 0)
        {
            text = text[..dash];
        }

        if (!Version.TryParse(text, out Version? version))
        {
            return null;
        }

        // normalize：补齐到三段，避免 1.2 被当成小于 1.2.0
        return new Version(
            Math.Max(version.Major, 0),
            Math.Max(version.Minor, 0),
            Math.Max(version.Build, 0),
            Math.Max(version.Revision, 0));
    }

    /// <summary>candidate 是否比 current 新。</summary>
    public static bool IsNewer(Version? candidate, Version? current)
    {
        if (candidate is null)
        {
            return false;
        }

        if (current is null)
        {
            return true;
        }

        return candidate > current;
    }
}
