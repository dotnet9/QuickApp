using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CodeWF.Avalonia.Controls.Themes;

// 验证实际模板的交互颜色，避免只检查控件根节点而漏掉模板覆盖。
internal static class ButtonPaletteVerification
{
    public static void Run(StudioThemeController controller, string output,
        params (string Name, Button Button)[] cases)
    {
        var initialPalette = controller.Palette;
        var initialMode = controller.Mode;
        var rows = new List<string> { "palette,mode,button,state,foreground,background,contrast" };
        var host = new Border();
        var window = new Window { Width = 320, Height = 160, Content = host };
        var minimum = double.MaxValue;
        var samples = 0;
        window.Show();
        try
        {
            foreach (var palette in Enum.GetValues<StudioPalette>())
            foreach (var dark in new[] { false, true })
            {
                controller.Palette = palette;
                controller.Mode = dark ? StudioThemeMode.Dark : StudioThemeMode.Light;
                var app = Application.Current!;
                if (!app.TryGetResource("StudioSurface", app.ActualThemeVariant, out var surface))
                    throw new InvalidOperationException("Missing StudioSurface");
                host.Background = (IBrush)surface!;
                foreach (var (name, button) in cases)
                {
                    button.Content = new TextBlock { Text = "Confirm" };
                    button.Width = 190;
                    button.HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center;
                    button.VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center;
                    host.Child = button;
                    window.MouseMove(new Point(2, 2));
                    Pump();
                    var normal = Measure("normal");
                    var center = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
                    window.MouseMove(center); Pump(); Measure("hover");
                    window.MouseDown(center, MouseButton.Left); Pump(); Measure("pressed");
                    window.MouseMove(new Point(2, 2));
                    window.MouseUp(new Point(2, 2), MouseButton.Left);
                    button.IsEnabled = false; Pump();
                    var clicks = 0;
                    void CountClick(object? sender, global::Avalonia.Interactivity.RoutedEventArgs args) => clicks++;
                    button.Click += CountClick;
                    window.MouseMove(center); window.MouseDown(center, MouseButton.Left);
                    window.MouseUp(center, MouseButton.Left); Pump();
                    button.Click -= CountClick;
                    if (button.IsEffectivelyEnabled || clicks != 0 || button.IsPressed)
                        throw new InvalidOperationException("Disabled button accepted input: " + name);
                    window.MouseMove(new Point(2, 2));
                    button.IsEnabled = true; Pump();
                    var restored = Measure("restored");
                    if (normal != restored)
                        throw new InvalidOperationException("Button colors did not recover: " + name);
                    host.Child = null;

                    (Color Foreground, Color Background) Measure(string state)
                    {
                        var text = button.GetVisualDescendants().OfType<TextBlock>().Single(item => item.Text == "Confirm");
                        var background = ColorOf(host.Background!);
                        var brushes = text.GetVisualAncestors().Select(ancestor => ancestor switch
                        {
                            Border border => border.Background,
                            ContentPresenter presenter => presenter.Background,
                            Panel panel => panel.Background,
                            Window root => root.Background,
                            _ => null
                        }).Where(brush => brush is not null).Reverse();
                        foreach (var brush in brushes) background = Over(ColorOf(brush!), background);
                        var foreground = Over(ColorOf(text.Foreground!), background);
                        var ratio = Contrast(foreground, background);
                        minimum = Math.Min(minimum, ratio); samples++;
                        rows.Add(string.Join(',', palette, dark ? "dark" : "light", name, state,
                            foreground, background, ratio.ToString("F6", CultureInfo.InvariantCulture)));
                        if (ratio < 4.5)
                            throw new InvalidOperationException($"Button contrast {ratio:F4}: {palette}/{dark}/{name}/{state}, {foreground} on {background}");
                        return (foreground, background);
                    }
                }
            }
        }
        finally
        {
            host.Child = null;
            window.Close();
            controller.Palette = initialPalette;
            controller.Mode = initialMode;
            File.WriteAllLines(Path.Combine(output, "button-palette.csv"), rows);
        }
        Console.WriteLine($"Actual button palette verification passed: {samples} states, minimum={minimum:F4}; disabled input and recovery passed.");
    }

    private static void Pump()
    {
        var timer = Stopwatch.StartNew();
        do
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Thread.Sleep(10);
        } while (timer.ElapsedMilliseconds < 280);
    }

    private static Color ColorOf(IBrush brush)
    {
        if (brush is not ISolidColorBrush solid)
            throw new InvalidOperationException("Expected a solid button brush: " + brush.GetType().Name);
        return Color.FromArgb((byte)Math.Round(solid.Color.A * brush.Opacity), solid.Color.R, solid.Color.G, solid.Color.B);
    }

    private static Color Over(Color foreground, Color background)
    {
        var alpha = foreground.A / 255d;
        byte Mix(byte front, byte back) => (byte)Math.Round(front * alpha + back * (1 - alpha));
        return Color.FromRgb(Mix(foreground.R, background.R), Mix(foreground.G, background.G), Mix(foreground.B, background.B));
    }

    private static double Contrast(Color foreground, Color background)
    {
        static double Luminance(Color color)
        {
            static double Channel(byte value)
            {
                var fraction = value / 255d;
                return fraction <= .04045 ? fraction / 12.92 : Math.Pow((fraction + .055) / 1.055, 2.4);
            }
            return .2126 * Channel(color.R) + .7152 * Channel(color.G) + .0722 * Channel(color.B);
        }
        var one = Luminance(foreground); var two = Luminance(background);
        return (Math.Max(one, two) + .05) / (Math.Min(one, two) + .05);
    }
}
