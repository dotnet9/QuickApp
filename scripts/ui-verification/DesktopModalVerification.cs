using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using QuickApp;
using QuickApp.Core.Services;
using QuickApp.ViewModels;
using QuickApp.Views;
using CodeWF.Toolkit.Core.UpdateChecking;
using ReactiveUI.Avalonia;

// 可选的真实 Windows 窗口验证；使用隔离配置和受控更新服务，不启动产品安装器。
internal static class DesktopModalVerification
{
    public static int Run(string output)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
        output = Path.GetFullPath(output);
        Directory.CreateDirectory(output);
        AppBuilder.Configure<App>().UsePlatformDetect().UseReactiveUI(_ => { }).SetupWithoutStarting();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        int result = 1;
        var windows = new List<Window>();
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                string fixture = Path.Combine(output, "fixture");
                Directory.CreateDirectory(fixture);
                File.WriteAllText(Path.Combine(fixture, "portable.txt"), "");
                var store = new ConfigStore(fixture);
                var config = store.Load();
                config.Settings.CheckUpdates = false;
                config.Settings.Pinned = true;
                store.Save(config);
                var model = new DockViewModel(store, new FakeLauncher(), new FakeIcons(), new FakeApps(), new FakeUpdates(fixture),
                    new UpdateDownloader(new HttpClient(), new UpdateServiceOptions { Repository = "QuickApp" }), new FakeAutoStart(), "QuickApp");
                var dock = new DockWindow();
                dock.Attach(model);
                windows.Add(dock);
                dock.Show();
                await Task.Delay(250);
                var about = new AboutWindow { DataContext = new SettingsViewModel(model) };
                windows.Add(about);
                _ = about.ShowDialog(dock);
                await Task.Delay(250);
                UpdateNotesWindow.ShowFor(about, model);
                await Task.Delay(250);
                var update = about.OwnedWindows.OfType<UpdateNotesWindow>().Single();
                windows.Add(update);
                VerifyModal(update, about, "Dock → About → Update");
                Native.PostMessage(Handle(update), 0x0010, 0, 0); // WM_CLOSE，经过原生消息循环关闭。
                await Task.Delay(150);
                Require(!update.IsVisible && Native.IsWindowEnabled(Handle(about)), "Closing update restores About input");
                Native.PostMessage(Handle(about), 0x0010, 0, 0);
                await Task.Delay(150);
                Require(!about.IsVisible && Native.IsWindowEnabled(Handle(dock)), "Closing About restores Dock input");

                var settings = new SettingsWindow();
                settings.Attach(model);
                windows.Add(settings);
                settings.Show();
                about = new AboutWindow { DataContext = new SettingsViewModel(model) };
                windows.Add(about);
                _ = about.ShowDialog(settings);
                await Task.Delay(150);
                UpdateNotesWindow.ShowFor(about, model);
                await Task.Delay(150);
                update = about.OwnedWindows.OfType<UpdateNotesWindow>().Single();
                windows.Add(update);
                VerifyModal(update, about, "Settings → About → Update");
                update.Close();
                about.Close();

                UpdateNotesWindow.ShowFor(settings, model);
                await Task.Delay(150);
                update = settings.OwnedWindows.OfType<UpdateNotesWindow>().Single();
                windows.Add(update);
                about = new AboutWindow { DataContext = new SettingsViewModel(model) };
                windows.Add(about);
                _ = about.ShowDialog(settings);
                await Task.Delay(150);
                UpdateNotesWindow.ShowFor(about, model);
                await Task.Delay(150);
                VerifyModal(update, about, "Existing Update → About → Check again");
                update.Hide();
                UpdateNotesWindow.ShowFor(about, model);
                await Task.Delay(150);
                VerifyModal(update, about, "Hidden Update → Check again");
                update.WindowState = WindowState.Minimized;
                UpdateNotesWindow.ShowFor(about, model);
                await Task.Delay(150);
                Require(update.WindowState == WindowState.Normal, "Repeated entry restores a minimized update window");
                Native.PostMessage(Handle(update), 0x0010, 0, 0);
                await Task.Delay(150);
                Require(!update.IsVisible && Native.IsWindowEnabled(Handle(about)) && !Native.IsWindowEnabled(Handle(settings)), "Reparented update restores About input while Settings remains modal");
                Native.PostMessage(Handle(about), 0x0010, 0, 0);
                await Task.Delay(150);
                Require(!about.IsVisible && Native.IsWindowEnabled(Handle(settings)), "Closing reparented About restores Settings input");
                result = 0;
                Console.WriteLine("PASS: native modal ownership, Z order, input, and close recovery");
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); }
            finally
            {
                foreach (var window in windows.AsEnumerable().Reverse()) if (window.IsVisible) window.Close();
                stop.Cancel();
            }
        });
        Dispatcher.UIThread.MainLoop(stop.Token);
        return result;
    }

    private static nint Handle(Window window) => window.TryGetPlatformHandle()!.Handle;

    private static void VerifyModal(Window child, Window owner, string scenario)
    {
        Console.WriteLine($"{scenario}: owner={Handle(owner)}, native owner={Native.GetWindow(Handle(child), 4)}, child topmost={IsTopmost(child)}, owner topmost={IsTopmost(owner)}, child enabled={Native.IsWindowEnabled(Handle(child))}, owner enabled={Native.IsWindowEnabled(Handle(owner))}");
        Require(ReferenceEquals(child.Owner, owner) && Native.GetWindow(Handle(child), 4) == Handle(owner), scenario + ": managed and native owners match");
        Require(child.IsVisible && Native.IsWindowEnabled(Handle(child)) && !Native.IsWindowEnabled(Handle(owner)), scenario + ": only the dialog receives input");
        var order = new[] { child, owner };
        Window.SortWindowsByZOrder(order);
        Require(ReferenceEquals(order[^1], child), scenario + ": update is above About");
    }

    private static bool IsTopmost(Window window) => (Native.GetWindowLongPtrW(Handle(window), -20).ToInt64() & 8) != 0;
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine("PASS " + message);
    }

    private static class Native
    {
        [DllImport("user32.dll")] public static extern nint GetWindow(nint window, uint command);
        [DllImport("user32.dll")] public static extern bool IsWindowEnabled(nint window);
        [DllImport("user32.dll")] public static extern nint GetWindowLongPtrW(nint window, int index);
        [DllImport("user32.dll")] public static extern bool PostMessage(nint window, uint message, nint wparam, nint lparam);
    }
}
