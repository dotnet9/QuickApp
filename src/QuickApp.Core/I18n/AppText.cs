using System;
using System.Globalization;
using System.IO;
using CodeWF.Toolkit.Lang;
using CodeWF.Toolkit.Lang.Json;

namespace QuickApp.Core.I18n;

/// <summary>从宿主输出的 JSON 读取文案，所有模块共用 Toolkit 的语言单例。</summary>
public static class AppText
{
    private static bool _initialized;

    public static void Initialize()
    {
        if (_initialized) return;
        var direct = Path.Combine(AppContext.BaseDirectory, "I18n");
        var bundled = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "Resources", "I18n"));
        var plugin = new JsonLangPlugin { ResourceFolder = Directory.Exists(direct) ? direct : bundled };
        I18nManager.Instance.Register(plugin, CultureInfo.GetCultureInfo("zh-CN"), out _);
        _initialized = true;
    }

    public static string Get(string key)
    {
        Initialize();
        return I18nManager.Instance.GetResource(key) ?? key;
    }

    public static string Format(string key, params object?[] arguments)
        => string.Format(I18nManager.Instance.Culture, Get(key), arguments);
}
