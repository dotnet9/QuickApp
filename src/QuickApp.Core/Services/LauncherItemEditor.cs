using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using QuickApp.Core.Models;

namespace QuickApp.Core.Services;

/// <summary>编辑表单的校验与规范化，不修改正在运行的原条目。</summary>
public static class LauncherItemEditor
{
    /// <summary>一次允许启动的实例数上限，避免手改配置或误输入把机器拖垮。</summary>
    public const int MaxInstanceCount = 99;

    public static LauncherItem Copy(LauncherItem item) => new()
    {
        Id = item.Id, Name = item.Name, Kind = item.Kind, Target = item.Target,
        Arguments = item.Arguments, InstanceCount = NormalizeInstanceCount(item.InstanceCount),
        WorkingDirectory = item.WorkingDirectory,
        Hotkey = item.Hotkey, UsePowerShell = item.UsePowerShell, RunInTerminal = item.RunInTerminal,
        CustomIconPath = item.CustomIconPath, RecommendedId = item.RecommendedId
    };

    public static string? NormalizeAndValidate(LauncherItem draft, IReadOnlyList<LauncherItem> existing, string dockHotkey)
    {
        draft.Target = draft.Target.Trim();
        if (draft.Target.Length == 0) return AppText.Get(Language.TargetRequired);
        if (!Enum.IsDefined(draft.Kind)) return AppText.Get(Language.InvalidItemKind);
        if (draft.Kind == ItemKind.Web)
        {
            if (!draft.Target.Contains("://", StringComparison.Ordinal)) draft.Target = "https://" + draft.Target;
            if (!Uri.TryCreate(draft.Target, UriKind.Absolute, out Uri? uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                || string.IsNullOrWhiteSpace(uri.Host)) return AppText.Get(Language.InvalidUrl);
            draft.Arguments = draft.WorkingDirectory = null;
            draft.InstanceCount = 1;
        }
        else if (draft.Kind == ItemKind.App)
        {
            draft.Target = ExpandPath(draft.Target);
            draft.Arguments = EmptyToNull(draft.Arguments);
            draft.InstanceCount = NormalizeInstanceCount(draft.InstanceCount);
        }
        else
        {
            draft.Arguments = null;
            draft.InstanceCount = 1;
        }

        if (draft.Kind != ItemKind.Command) draft.RunInTerminal = draft.UsePowerShell = false;
        draft.WorkingDirectory = EmptyToNull(draft.WorkingDirectory);
        if (draft.WorkingDirectory is not null)
        {
            draft.WorkingDirectory = ExpandPath(draft.WorkingDirectory);
            if (!Directory.Exists(draft.WorkingDirectory)) return AppText.Get(Language.InvalidWorkingDirectory);
        }
        draft.Name = ItemQuery.ResolveDisplayName(draft);
        draft.Hotkey = EmptyToNull(draft.Hotkey);
        if (draft.Hotkey is not null)
        {
            if (!HotkeyGesture.TryParse(draft.Hotkey, out HotkeyGesture parsed, out string? error)) return error;
            if (HotkeyGesture.TryParse(dockHotkey, out HotkeyGesture dock, out _) && parsed == dock)
                return AppText.Get(Language.ReservedHotkey);
            foreach (LauncherItem item in existing)
            {
                if (item.Id != draft.Id && HotkeyGesture.TryParse(item.Hotkey ?? string.Empty, out HotkeyGesture other, out _) && parsed == other)
                    return AppText.Get(Language.UsedHotkeyPrefix) + item.Name + "」。";
            }
        }
        return null;
    }

    public static string ExpandPath(string path) => Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));

    /// <summary>旧配置没有该字段（反序列化为 0）与手改越界值统一兜底到 1 ~ <see cref="MaxInstanceCount"/>。</summary>
    public static int NormalizeInstanceCount(int count) => Math.Clamp(count, 1, MaxInstanceCount);

    /// <summary>解析编辑框里的启动数量：留空按 1 处理，非法或越界返回错误信息。</summary>
    public static string? TryParseInstanceCount(string? text, out int count)
    {
        count = 1;
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (!int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int parsed)
            || parsed < 1 || parsed > MaxInstanceCount)
        {
            return AppText.Get(Language.InstanceRangePrefix) + MaxInstanceCount + AppText.Get(Language.InstanceRangeSuffix);
        }

        count = parsed;
        return null;
    }

    private static string? EmptyToNull(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
