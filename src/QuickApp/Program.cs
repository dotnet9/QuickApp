using System;
using Avalonia;
using ReactiveUI.Avalonia;

namespace QuickApp;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
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
