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

    /// <summary>「更多」：2x2 实心方块（原型的网格图标）。用闭合矩形 + Fill 渲染，
    /// 不用零长线段——那种画法在 Uniform 拉伸下会塌成一个点。</summary>
    public static Geometry More { get; } = Geometry.Parse(
        "M6,6 L11,6 L11,11 L6,11 Z M13,6 L18,6 L18,11 L13,11 Z " +
        "M6,13 L11,13 L11,18 L6,18 Z M13,13 L18,13 L18,18 L13,18 Z");

    public static Geometry Pin { get; } = Geometry.Parse(
        "M9.5,3 L14.5,3 L13.7,8.2 L17,11.5 L7,11.5 L10.3,8.2 Z M12,11.5 L12,21");

    /// <summary>拖动把手：2x3 实心圆点（闭合小方块 + Fill）。</summary>
    public static Geometry Grip { get; } = Geometry.Parse(
        "M7.7,4.7 L10.3,4.7 L10.3,7.3 L7.7,7.3 Z M13.7,4.7 L16.3,4.7 L16.3,7.3 L13.7,7.3 Z " +
        "M7.7,10.7 L10.3,10.7 L10.3,13.3 L7.7,13.3 Z M13.7,10.7 L16.3,10.7 L16.3,13.3 L13.7,13.3 Z " +
        "M7.7,16.7 L10.3,16.7 L10.3,19.3 L7.7,19.3 Z M13.7,16.7 L16.3,16.7 L16.3,19.3 L13.7,19.3 Z");

    public static Geometry Close { get; } = Geometry.Parse("M6,6 L18,18 M18,6 L6,18");

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
}
