using Avalonia.Media;

namespace QuickApp.Theme;

/// <summary>
/// 图标几何表：把 design/index.html 原型里的内联 SVG 换成 Avalonia 路径数据。
/// 全部走 Geometry.Parse（无反射、无外部资源），AOT 与裁剪下都安全，也不用带任何图片。
/// 统一 24x24 网格、只描边不填充；圆/弧一律用三次贝塞尔手写（只依赖 M/C/L/Z，
/// 避免路径解析器对圆弧指令的支持差异——静态字段解析失败会直接让程序起不来）。
/// </summary>
public static class Icons
{
    public static Geometry Search { get; } = Geometry.Parse(
        "M10,3 C13.87,3 17,6.13 17,10 C17,13.87 13.87,17 10,17 C6.13,17 3,13.87 3,10 C3,6.13 6.13,3 10,3 Z " +
        "M15.2,15.2 L21,21");

    public static Geometry Pencil { get; } = Geometry.Parse(
        "M4,20 L8,19 L18,9 L15,6 L5,16 Z M14.5,5.5 L17.5,8.5");

    public static Geometry Plus { get; } = Geometry.Parse("M12,5 L12,19 M5,12 L19,12");

    /// <summary>「更多」：三个横向圆点，避免和应用网格混淆。</summary>
    public static Geometry More { get; } = Geometry.Parse(
        "M4.8,12 L7.2,12 M10.8,12 L13.2,12 M16.8,12 L19.2,12");

    public static Geometry Pin { get; } = Geometry.Parse(
        "M9.5,3 L14.5,3 L13.7,8.2 L17,11.5 L7,11.5 L10.3,8.2 Z M12,11.5 L12,21");

    /// <summary>拖动把手：2x3 实心圆点（闭合小方块 + Fill）。</summary>
    public static Geometry Grip { get; } = Geometry.Parse(
        "M7.7,4.7 L10.3,4.7 L10.3,7.3 L7.7,7.3 Z M13.7,4.7 L16.3,4.7 L16.3,7.3 L13.7,7.3 Z " +
        "M7.7,10.7 L10.3,10.7 L10.3,13.3 L7.7,13.3 Z M13.7,10.7 L16.3,10.7 L16.3,13.3 L13.7,13.3 Z " +
        "M7.7,16.7 L10.3,16.7 L10.3,19.3 L7.7,19.3 Z M13.7,16.7 L16.3,16.7 L16.3,19.3 L13.7,19.3 Z");

    public static Geometry Close { get; } = Geometry.Parse("M6,6 L18,18 M18,6 L6,18");

    public static Geometry Gear { get; } = Geometry.Parse(
        "M9.8,3.2 L14.2,3.2 L14.8,5.6 C15.3,5.8 15.8,6.1 16.3,6.4 L18.6,5.3 L20.8,7.5 L19.7,9.8 C20,10.3 20.2,10.8 20.4,11.3 L22.8,12 L22.8,16.4 L20.4,17.1 C20.2,17.6 20,18.1 19.7,18.6 L20.8,20.9 L18.6,23.1 L16.3,22 C15.8,22.3 15.3,22.6 14.8,22.8 L14.2,25.2 L9.8,25.2 L9.2,22.8 C8.7,22.6 8.2,22.3 7.7,22 L5.4,23.1 L3.2,20.9 L4.3,18.6 C4,18.1 3.8,17.6 3.6,17.1 L1.2,16.4 L1.2,12 L3.6,11.3 C3.8,10.8 4,10.3 4.3,9.8 L3.2,7.5 L5.4,5.3 L7.7,6.4 C8.2,6.1 8.7,5.8 9.2,5.6 Z M12,9 C13.7,9 15,10.3 15,12 C15,13.7 13.7,15 12,15 C10.3,15 9,13.7 9,12 C9,10.3 10.3,9 12,9 Z");

    public static Geometry Info { get; } = Geometry.Parse(
        "M12,3 C16.97,3 21,7.03 21,12 C21,16.97 16.97,21 12,21 C7.03,21 3,16.97 3,12 C3,7.03 7.03,3 12,3 Z M12,10.5 L12,17 M12,7 L12.01,7");

    public static Geometry Check { get; } = Geometry.Parse("M5,12.5 L9.5,17 L19,7");

    public static Geometry ChevronUp { get; } = Geometry.Parse("M6,14 L12,8 L18,14");

    public static Geometry ChevronDown { get; } = Geometry.Parse("M6,10 L12,16 L18,10");

    public static Geometry ChevronLeft { get; } = Geometry.Parse("M14,6 L8,12 L14,18");

    public static Geometry ChevronRight { get; } = Geometry.Parse("M10,6 L16,12 L10,18");

    /// <summary>命令行占位图标。</summary>
    public static Geometry Terminal { get; } = Geometry.Parse(
        "M3.5,4.5 L20.5,4.5 L20.5,19.5 L3.5,19.5 Z M7.5,9.5 L10,12 L7.5,14.5 M12.5,15 L16.5,15");

    /// <summary>网页占位图标：圆 + 两条纬线 + 一条经线（椭圆用两段贝塞尔拼）。</summary>
    public static Geometry Globe { get; } = Geometry.Parse(
        "M12,3 C16.97,3 21,7.03 21,12 C21,16.97 16.97,21 12,21 C7.03,21 3,16.97 3,12 C3,7.03 7.03,3 12,3 Z " +
        "M3.6,9 L20.4,9 M3.6,15 L20.4,15 " +
        "M12,3 C9.6,5.4 8.4,8.4 8.4,12 C8.4,15.6 9.6,18.6 12,21 " +
        "C14.4,18.6 15.6,15.6 15.6,12 C15.6,8.4 14.4,5.4 12,3 Z");

    /// <summary>通用应用占位图标。</summary>
    public static Geometry Application { get; } = Geometry.Parse("M4,4 L20,4 L20,20 L4,20 Z M4,8.5 L20,8.5");

    // ---------------- 以下图标供「更换图标」选择器使用（对应原型 PICKER_ICONS） ----------------

    /// <summary>对话气泡。</summary>
    public static Geometry Chat { get; } = Geometry.Parse(
        "M20.5,11.5 C20.5,16 16.9,19.5 12,19.5 C10.8,19.5 9.7,19.3 8.7,18.9 L3,21 L4.6,15.6 " +
        "C3.9,14.4 3.5,13 3.5,11.5 C3.5,7 7.1,3.5 12,3.5 C16.9,3.5 20.5,7 20.5,11.5 Z");

    /// <summary>播放：圆 + 三角。</summary>
    public static Geometry Play { get; } = Geometry.Parse(
        Circle(12, 12, 9) + " M10,8.5 L16,12 L10,15.5 Z");

    /// <summary>音乐：两个圆 + 连线。</summary>
    public static Geometry Music { get; } = Geometry.Parse(
        Circle(6.5, 18, 2.5) + " " + Circle(16.5, 16, 2.5) + " M9,18 L9,6 L19,4 L19,16");

    /// <summary>相机：机身 + 镜头。</summary>
    public static Geometry Camera { get; } = Geometry.Parse(
        "M5,6 L14,6 C15.1,6 16,6.9 16,8 L16,16 C16,17.1 15.1,18 14,18 L5,18 C3.9,18 3,17.1 3,16 L3,8 C3,6.9 3.9,6 5,6 Z " +
        "M16,11 L21,8 L21,16 L16,13 Z");

    /// <summary>显示器（含屏幕十字线）。</summary>
    public static Geometry Monitor { get; } = Geometry.Parse(
        RoundedRect(2.5, 4, 19, 13, 2) +
        " M8,20.5 L16,20.5 M12,17 L12,20.5 M9,10.5 L15,10.5 M12,7.5 L12,13.5");

    /// <summary>显示器（无屏幕内容，向日葵类远程工具的区分款）。</summary>
    public static Geometry MonitorAlt { get; } = Geometry.Parse(
        RoundedRect(2.5, 4, 19, 13, 2) + " M8,20.5 L16,20.5 M12,17 L12,20.5");

    /// <summary>文档：折角 + 两行字。</summary>
    public static Geometry Note { get; } = Geometry.Parse(
        "M14,3 L6.5,3 C5.67,3 5,3.67 5,4.5 L5,19.5 C5,20.33 5.67,21 6.5,21 L17.5,21 C18.33,21 19,20.33 19,19.5 L19,8 Z " +
        "M14,3 L14,8 L19,8 M8.5,13 L15.5,13 M8.5,16.5 L13,16.5");

    /// <summary>云。</summary>
    public static Geometry Cloud { get; } = Geometry.Parse(
        "M7.5,18.5 C4.6,18.5 2.8,16.6 2.8,14.2 C2.8,12.1 4.2,10.6 6.2,10.2 " +
        "C6.9,6.9 9.4,5 12.4,5 C15.9,5 18.6,7.4 19,10.6 C20.8,11 22,12.4 22,14.3 " +
        "C22,16.6 20.3,18.5 17.8,18.5 Z");

    /// <summary>代码：左右尖括号。</summary>
    public static Geometry Code { get; } = Geometry.Parse("M9,7 L4,12 L9,17 M15,7 L20,12 L15,17");

    /// <summary>火箭：舱体 + 尾翼（原型路径的相对贝塞尔已转绝对）。</summary>
    public static Geometry Rocket { get; } = Geometry.Parse(
        "M12,2.5 C14.8,4.7 16.2,7.7 16.2,11 L12,15 L7.8,11 C7.8,7.7 9.2,4.7 12,2.5 Z " +
        "M7.8,11 L5,18 L9,16.5 M16.2,11 L19,18 L15,16.5");

    /// <summary>数据库：三层椭圆 + 侧壁。</summary>
    public static Geometry Database { get; } = Geometry.Parse(
        "M5,6 C5,4.34 8.13,3 12,3 C15.87,3 19,4.34 19,6 C19,7.66 15.87,9 12,9 C8.13,9 5,7.66 5,6 Z " +
        "M5,12 C5,13.66 8.13,15 12,15 C15.87,15 19,13.66 19,12 " +
        "M5,6 L5,18 C5,19.66 8.13,21 12,21 C15.87,21 19,19.66 19,18 L19,6");

    /// <summary>盾牌 + 对勾。</summary>
    public static Geometry Shield { get; } = Geometry.Parse(
        "M12,3 L19,6 L19,11.5 C19,15.8 16.1,19.1 12,21 C7.9,19.1 5,15.8 5,11.5 L5,6 Z " +
        "M9.5,12 L11.3,13.8 L14.8,10.2");

    /// <summary>录制：双圆。</summary>
    public static Geometry Record { get; } = Geometry.Parse(
        Circle(12, 12, 9) + " " + Circle(12, 12, 3.5));

    /// <summary>圆（只用 M/C/L/Z 的四段贝塞尔近似，k=0.5523r）。</summary>
    private static string Circle(double cx, double cy, double r)
    {
        double k = r * 0.5523;
        return string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"M{cx - r},{cy} C{cx - r},{cy - k} {cx - k},{cy - r} {cx},{cy - r} " +
            $"C{cx + k},{cy - r} {cx + r},{cy - k} {cx + r},{cy} " +
            $"C{cx + r},{cy + k} {cx + k},{cy + r} {cx},{cy + r} " +
            $"C{cx - k},{cy + r} {cx - r},{cy + k} {cx - r},{cy} Z");
    }

    /// <summary>圆角矩形路径（顺时针，四角用四分之一贝塞尔）。</summary>
    private static string RoundedRect(double x, double y, double width, double height, double radius)
    {
        double right = x + width;
        double bottom = y + height;
        double k = radius * 0.5523;
        return string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $"M{x + radius},{y} L{right - radius},{y} C{right - radius + k},{y} {right},{y + radius - k} {right},{y + radius} " +
            $"L{right},{bottom - radius} C{right},{bottom - radius + k} {right - radius + k},{bottom} {right - radius},{bottom} " +
            $"L{x + radius},{bottom} C{x + radius - k},{bottom} {x},{bottom - radius + k} {x},{bottom - radius} " +
            $"L{x},{y + radius} C{x},{y + radius - k} {x + radius - k},{y} {x + radius},{y} Z");
    }
}
