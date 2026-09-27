using System;
using System.Globalization;
using Avalonia.Media;
using QuickApp.Core.Models;

namespace QuickApp.Theme;

/// <summary>
/// 配色表：把 design/index.html 原型里的 CSS 变量搬到这里，深/浅两套。
/// 放代码而不是 XAML 资源，是因为面板不透明度、毛玻璃/扁平这些都要在运行时重算画刷。
/// </summary>
public sealed record Palette(
    Color Panel,
    Color PanelBorder,
    Color Text,
    Color TextDim,
    Color Accent,
    Color AccentInk,
    Color Hover,
    Color Active,
    Color Bubble,
    Color Menu,
    Color MenuHover,
    Color Danger)
{
    public static Palette Dark { get; } = new(
        Panel: Color.Parse("#12151C"),
        PanelBorder: Color.Parse("#26FFFFFF"),
        Text: Color.Parse("#EEF1F6"),
        TextDim: Color.Parse("#9AA4B3"),
        Accent: Color.Parse("#5AA2FF"),
        AccentInk: Color.Parse("#08111F"),
        Hover: Color.Parse("#1AFFFFFF"),
        Active: Color.Parse("#28FFFFFF"),
        Bubble: Color.Parse("#F7181B21"),
        Menu: Color.Parse("#FA181B21"),
        MenuHover: Color.Parse("#14FFFFFF"),
        Danger: Color.Parse("#FF6B6B"));

    public static Palette Light { get; } = new(
        Panel: Color.Parse("#FFFFFF"),
        PanelBorder: Color.Parse("#14111826"),
        Text: Color.Parse("#111826"),
        TextDim: Color.Parse("#5C6675"),
        Accent: Color.Parse("#2563EB"),
        AccentInk: Color.Parse("#FFFFFF"),
        Hover: Color.Parse("#0F111826"),
        Active: Color.Parse("#1F111826"),
        Bubble: Color.Parse("#FBFFFFFF"),
        Menu: Color.Parse("#FDFFFFFF"),
        MenuHover: Color.Parse("#0F111826"),
        Danger: Color.Parse("#D92D20"));

    /// <summary>配色随主题与不透明度重算，扁平风格直接把面板做成不透明。</summary>
    public Palette WithOpacity(double opacity, bool flat)
    {
        double alpha = flat ? 1.0 : Math.Clamp(opacity, 0.2, 1.0);
        byte a = (byte)Math.Round(alpha * 255);
        return this with { Panel = Color.FromArgb(a, Panel.R, Panel.G, Panel.B) };
    }
}

/// <summary>把配色变成可绑定画刷的小工具。</summary>
public static class PaletteBrushes
{
    public static IBrush Brush(Color color) => new SolidColorBrush(color);

    public static IBrush Panel(Palette palette) => Brush(palette.Panel);

    public static IBrush Text(Palette palette) => Brush(palette.Text);

    public static IBrush TextDim(Palette palette) => Brush(palette.TextDim);

    /// <summary>
    /// 图标底色（真实图标提取出来之前的占位）。
    /// 应用按名称哈希取色，网页与命令行用固定色相，方便一眼区分类型。
    /// </summary>
    public static IBrush TileBrush(string name, ItemKind kind, double tileSize)
    {
        double hue = kind switch
        {
            ItemKind.Command => 210,
            ItemKind.Web => 258,
            _ => Hue(name)
        };

        double saturation = kind == ItemKind.App ? 0.52 : 0.5;
        Color from = FromHsl(hue, saturation, 0.46);

        // 小图标用纯色更干净，大图标给一点渐变层次
        if (tileSize < 30)
        {
            return new SolidColorBrush(from);
        }

        Color to = FromHsl((hue + 18) % 360, saturation, 0.34);

        return new LinearGradientBrush
        {
            StartPoint = new Avalonia.RelativePoint(0, 0, Avalonia.RelativeUnit.Relative),
            EndPoint = new Avalonia.RelativePoint(1, 1, Avalonia.RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(from, 0),
                new GradientStop(to, 1)
            }
        };
    }

    private static double Hue(string name)
    {
        int hash = 0;
        foreach (char ch in name ?? string.Empty)
        {
            hash = (hash * 31 + ch) % 360;
        }

        return hash;
    }

    private static Color FromHsl(double h, double s, double l)
    {
        double c = (1 - Math.Abs(2 * l - 1)) * s;
        double x = c * (1 - Math.Abs((h / 60 % 2) - 1));
        double m = l - c / 2;
        (double r, double g, double b) = h switch
        {
            < 60 => (c, x, 0d),
            < 120 => (x, c, 0d),
            < 180 => (0d, c, x),
            < 240 => (0d, x, c),
            < 300 => (x, 0d, c),
            _ => (c, 0d, x)
        };

        return Color.FromRgb(
            (byte)Math.Round((r + m) * 255),
            (byte)Math.Round((g + m) * 255),
            (byte)Math.Round((b + m) * 255));
    }

    public static string ToHex(Color color)
        => string.Format(CultureInfo.InvariantCulture, "#{0:X2}{1:X2}{2:X2}{3:X2}", color.A, color.R, color.G, color.B);
}
