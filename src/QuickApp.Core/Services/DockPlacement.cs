using System;
using System.Globalization;

namespace QuickApp.Core.Services;

/// <summary>
/// Dock 停靠位置计算（纯数学，物理像素，可单测）。
/// 上/下边缘水平居中，左/右边缘垂直居中。
///
/// <paramref name="hiddenOffset"/> 的约定：**一个带符号的位移，Anchor 一律加上它**，
/// 正负号保证窗口被推出自己所在的那条边——上/左为负，下/右为正。
/// 之前这里 Anchor 对不同边用了不同的加减法，导致下边与左边收起时反而往屏幕里跑，故统一。
/// </summary>
public static class DockPlacement
{
    public const int DefaultMargin = 10;

    public static (int X, int Y) Anchor(
        int workX,
        int workY,
        int workWidth,
        int workHeight,
        int dockWidth,
        int dockHeight,
        Models.DockEdge edge,
        int margin = DefaultMargin,
        int hiddenOffset = 0,
        int alongOffset = 0)
    {
        int x;
        int y;

        switch (edge)
        {
            case Models.DockEdge.Bottom:
                x = workX + (workWidth - dockWidth) / 2 + alongOffset;
                y = workY + workHeight - dockHeight - margin + hiddenOffset;
                break;

            case Models.DockEdge.Left:
                x = workX + margin + hiddenOffset;
                y = workY + (workHeight - dockHeight) / 2 + alongOffset;
                break;

            case Models.DockEdge.Right:
                x = workX + workWidth - dockWidth - margin + hiddenOffset;
                y = workY + (workHeight - dockHeight) / 2 + alongOffset;
                break;

            default:
                x = workX + (workWidth - dockWidth) / 2 + alongOffset;
                y = workY + margin + hiddenOffset;
                break;
        }

        return (x, y);
    }

    /// <summary>
    /// 收起时的位移量（带符号，直接交给 Anchor 相加）：
    /// 上边向上移出、下边向下移出、左边向左、右边向右。
    /// <paramref name="beyondEdge"/> 是工作区边界到屏幕边界的距离（任务栏高度）——
    /// 只推出工作区还不够，窗口被屏幕边「托住」的部分会压在任务栏上显示出来，必须一并推出屏幕。
    /// </summary>
    public static int HiddenOffset(Models.DockEdge edge, int dockWidth, int dockHeight, int gap = 22, int beyondEdge = 0)
        => edge switch
        {
            Models.DockEdge.Top => -(dockHeight + gap + beyondEdge),
            Models.DockEdge.Bottom => dockHeight + gap + beyondEdge,
            Models.DockEdge.Left => -(dockWidth + gap + beyondEdge),
            Models.DockEdge.Right => dockWidth + gap + beyondEdge,
            _ => -(dockHeight + gap + beyondEdge)
        };

    /// <summary>
    /// 偏移比例（-1 贴起点、0 居中、+1 贴终点）换算成沿边像素位移，叠加到 Anchor 的居中坐标上。
    /// 结果夹在「贴起点 ~ 贴终点」之间，屏幕变小或比例越界时也不会把 Dock 推出所在边。
    /// </summary>
    public static int EdgeOffsetAlong(double ratio, int workLength, int dockLength)
    {
        int free = workLength - dockLength;
        int baseOffset = free / 2;
        double r = Math.Clamp(ratio, -1, 1);
        int along = (int)Math.Round(free / 2.0 * r, MidpointRounding.AwayFromZero);
        int min = Math.Min(0, free) - baseOffset;
        int max = Math.Max(0, free) - baseOffset;
        return Math.Clamp(along, min, max);
    }

    /// <summary>
    /// 把窗口当前的沿边位置换算回偏移比例，拖动结束时用它记住「松手处在边的哪个位置」。
    /// 比例（而非像素）落盘，换分辨率、换主屏后仍按同一相对位置还原。
    /// </summary>
    public static double OffsetRatio(
        Models.DockEdge edge,
        int dockX, int dockY, int dockWidth, int dockHeight,
        int workX, int workY, int workWidth, int workHeight)
    {
        bool horizontal = edge is Models.DockEdge.Top or Models.DockEdge.Bottom;
        int workLength = horizontal ? workWidth : workHeight;
        int dockLength = horizontal ? dockWidth : dockHeight;
        int workCenter = (horizontal ? workX : workY) + workLength / 2;
        int dockCenter = (horizontal ? dockX : dockY) + dockLength / 2;
        int freeHalf = (workLength - dockLength) / 2;
        if (freeHalf <= 0)
        {
            return 0;
        }

        return Math.Clamp((dockCenter - workCenter) / (double)freeHalf, -1, 1);
    }

    /// <summary>把手按原型内缩显示在显示器边界内，坐标使用完整边界而非工作区；沿边偏移跟随 Dock 记忆位置。</summary>
    public static (int X, int Y) AnchorHandle(
        int screenX,
        int screenY,
        int screenWidth,
        int screenHeight,
        int handleWidth,
        int handleHeight,
        Models.DockEdge edge,
        int inset,
        int alongOffset = 0)
    {
        return edge switch
        {
            Models.DockEdge.Bottom => (screenX + (screenWidth - handleWidth) / 2 + alongOffset, screenY + screenHeight - handleHeight - inset),
            Models.DockEdge.Left => (screenX + inset, screenY + (screenHeight - handleHeight) / 2 + alongOffset),
            Models.DockEdge.Right => (screenX + screenWidth - handleWidth - inset, screenY + (screenHeight - handleHeight) / 2 + alongOffset),
            _ => (screenX + (screenWidth - handleWidth) / 2 + alongOffset, screenY + inset)
        };
    }

    /// <summary>左/右边缘是竖向 Dock。</summary>
    public static bool IsVertical(Models.DockEdge edge)
        => edge is Models.DockEdge.Left or Models.DockEdge.Right;

    /// <summary>收起进度 0~1 对应的位移（缓动由调用方决定，这里只做线性映射）。</summary>
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

    /// <summary>窗口是否已经完全离开工作区（收起完成的判定，也用于单测）。</summary>
    public static bool IsOutside(
        Models.DockEdge edge,
        int x,
        int y,
        int dockWidth,
        int dockHeight,
        int workX,
        int workY,
        int workWidth,
        int workHeight)
        => edge switch
        {
            Models.DockEdge.Top => y + dockHeight <= workY,
            Models.DockEdge.Bottom => y >= workY + workHeight,
            Models.DockEdge.Left => x + dockWidth <= workX,
            Models.DockEdge.Right => x >= workX + workWidth,
            _ => y + dockHeight <= workY
        };

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
