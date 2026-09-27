using System;
using System.Globalization;

namespace QuickApp.Core.Services;

/// <summary>
/// Dock 停靠位置计算（纯数学，物理像素，可单测）。
/// 上/下边缘水平居中，左/右边缘垂直居中；<paramref name="hiddenOffset"/> 用于收起时把窗口推出屏幕。
/// </summary>
public static class DockPlacement
{
    public const int DefaultMargin = 10;

    /// <summary>收起时窗口移出屏幕的距离（一般传窗口自身尺寸 + 一点余量）。</summary>
    public static (int X, int Y) Anchor(
        int workX,
        int workY,
        int workWidth,
        int workHeight,
        int dockWidth,
        int dockHeight,
        Models.DockEdge edge,
        int margin = DefaultMargin,
        int hiddenOffset = 0)
    {
        int x;
        int y;

        switch (edge)
        {
            case Models.DockEdge.Bottom:
                x = workX + (workWidth - dockWidth) / 2;
                y = workY + workHeight - dockHeight - margin + hiddenOffset;
                break;

            case Models.DockEdge.Left:
                x = workX + margin - hiddenOffset;
                y = workY + (workHeight - dockHeight) / 2;
                break;

            case Models.DockEdge.Right:
                x = workX + workWidth - dockWidth - margin + hiddenOffset;
                y = workY + (workHeight - dockHeight) / 2;
                break;

            default:
                x = workX + (workWidth - dockWidth) / 2;
                y = workY + margin - hiddenOffset;
                break;
        }

        return (x, y);
    }

    /// <summary>把收起位移换算成像素：上/左为负方向，下/右为正方向。</summary>
    public static int HiddenOffset(Models.DockEdge edge, int dockWidth, int dockHeight, int gap = 22)
        => edge switch
        {
            Models.DockEdge.Top => dockHeight + gap,
            Models.DockEdge.Bottom => -(dockHeight + gap),
            Models.DockEdge.Left => -(dockWidth + gap),
            Models.DockEdge.Right => dockWidth + gap,
            _ => dockHeight + gap
        };

    /// <summary>左/右边缘是竖向 Dock。</summary>
    public static bool IsVertical(Models.DockEdge edge)
        => edge is Models.DockEdge.Left or Models.DockEdge.Right;

    /// <summary>收起进度 0~1 对应的位移（带缓出的插值由调用方决定，这里只做线性映射）。</summary>
    public static int LerpOffset(int from, int to, double progress)
    {
        double p = Math.Clamp(progress, 0, 1);
        return (int)Math.Round(from + (to - from) * p, MidpointRounding.AwayFromZero);
    }

    /// <summary>「拖到哪条边」：按窗口中心到四条边的距离取最近的一条。</summary>
    public static Models.DockEdge NearestEdge(
        int centerX, int centerY, int workX, int workY, int workWidth, int workHeight)
    {
        int top = centerY - workY;
        int bottom = workY + workHeight - centerY;
        int left = centerX - workX;
        int right = workX + workWidth - centerX;

        Models.DockEdge best = Models.DockEdge.Top;
        int bestDistance = top;

        if (bottom < bestDistance)
        {
            best = Models.DockEdge.Bottom;
            bestDistance = bottom;
        }

        if (left < bestDistance)
        {
            best = Models.DockEdge.Left;
            bestDistance = left;
        }

        if (right < bestDistance)
        {
            best = Models.DockEdge.Right;
        }

        return best;
    }

    /// <summary>日志/提示用的边缘名称。</summary>
    public static string Label(Models.DockEdge edge) => edge switch
    {
        Models.DockEdge.Bottom => "下",
        Models.DockEdge.Left => "左",
        Models.DockEdge.Right => "右",
        _ => "上"
    };

    internal static string Invariant(int value) => value.ToString(CultureInfo.InvariantCulture);
}
