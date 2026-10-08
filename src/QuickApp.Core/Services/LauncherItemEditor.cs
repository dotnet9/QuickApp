using System;
using System.Collections.Generic;
using System.IO;
using QuickApp.Core.Models;

namespace QuickApp.Core.Services;

/// <summary>编辑表单的校验与规范化，不修改正在运行的原条目。</summary>
public static class LauncherItemEditor
{
    public static LauncherItem Copy(LauncherItem item) => new()
    {
        Id = item.Id, Name = item.Name, Kind = item.Kind, Target = item.Target,
        Arguments = item.Arguments, WorkingDirectory = item.WorkingDirectory,
        Hotkey = item.Hotkey, UsePowerShell = item.UsePowerShell, RunInTerminal = item.RunInTerminal,
        CustomIconPath = item.CustomIconPath, RecommendedId = item.RecommendedId
    };

    public static string? NormalizeAndValidate(LauncherItem draft, IReadOnlyList<LauncherItem> existing, string dockHotkey)
    {
        draft.Target = draft.Target.Trim();
        if (draft.Target.Length == 0) return "请填写目标。";
        if (!Enum.IsDefined(draft.Kind)) return "请选择有效的快捷项类型。";
        if (draft.Kind == ItemKind.Web)
        {
            if (!draft.Target.Contains("://", StringComparison.Ordinal)) draft.Target = "https://" + draft.Target;
            if (!Uri.TryCreate(draft.Target, UriKind.Absolute, out Uri? uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                || string.IsNullOrWhiteSpace(uri.Host)) return "请填写有效的 HTTP 或 HTTPS 网址。";
            draft.Arguments = draft.WorkingDirectory = null;
        }
        else if (draft.Kind == ItemKind.App)
        {
            draft.Target = ExpandPath(draft.Target);
            draft.Arguments = EmptyToNull(draft.Arguments);
        }
        else draft.Arguments = null;

        if (draft.Kind != ItemKind.Command) draft.RunInTerminal = draft.UsePowerShell = false;
        draft.WorkingDirectory = EmptyToNull(draft.WorkingDirectory);
        if (draft.WorkingDirectory is not null)
        {
            draft.WorkingDirectory = ExpandPath(draft.WorkingDirectory);
            if (!Directory.Exists(draft.WorkingDirectory)) return "工作目录不存在，请选择有效目录。";
        }
        draft.Name = ItemQuery.ResolveDisplayName(draft);
        draft.Hotkey = EmptyToNull(draft.Hotkey);
        if (draft.Hotkey is not null)
        {
            if (!HotkeyGesture.TryParse(draft.Hotkey, out HotkeyGesture parsed, out string? error)) return error;
            if (HotkeyGesture.TryParse(dockHotkey, out HotkeyGesture dock, out _) && parsed == dock)
                return "该快捷键已用于唤出 QuickApp。";
            foreach (LauncherItem item in existing)
            {
                if (item.Id != draft.Id && HotkeyGesture.TryParse(item.Hotkey ?? string.Empty, out HotkeyGesture other, out _) && parsed == other)
                    return "该快捷键已用于「" + item.Name + "」。";
            }
        }
        return null;
    }

    public static string ExpandPath(string path) => Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
    private static string? EmptyToNull(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
