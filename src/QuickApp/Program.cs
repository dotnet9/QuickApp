using System;
using Avalonia;
using ReactiveUI.Avalonia;

namespace QuickApp;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (Array.Exists(args, argument => argument == "--selfcheck"))
        {
            AppText.Initialize();
            string? previous = null;
            foreach (var culture in new[] { "en-US", "zh-CN" })
            {
                CodeWF.Toolkit.Lang.I18nManager.Instance.Culture = System.Globalization.CultureInfo.GetCultureInfo(culture);
                var translated = AppText.Get(Language.About);
                if (string.IsNullOrWhiteSpace(translated) || translated == previous
                    || translated.StartsWith("QuickApp.Text.", StringComparison.Ordinal)
                    || AppText.Get(Language.SettingsTitle).StartsWith("QuickApp.Text.", StringComparison.Ordinal)) return 2;
                previous = translated;
            }
            return UpdateVersion.Current(typeof(App).Assembly) > new Version(0, 0, 0) ? 0 : 3;
        }
        try
        {
            AppLog.Info("QuickApp 启动");
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            AppLog.Error("启动失败", ex);
            return 1;
        }
    }

    // Avalonia 设计器需要这个签名的方法
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .UseReactiveUI(_ => { })
            .LogToTrace();
}
