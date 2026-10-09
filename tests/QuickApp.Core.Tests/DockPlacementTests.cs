using System;
using QuickApp.Core.Models;
using QuickApp.Core.Services;
using Xunit;

namespace QuickApp.Core.Tests;

/// <summary>四边停靠的位置计算：这块决定了 Dock 能不能贴对边，必须锁住。</summary>
public sealed class DockPlacementTests
{
    private const int WorkX = 0;
    private const int WorkY = 40;      // 任务栏占掉顶部 40px 的情况
    private const int WorkW = 1920;
    private const int WorkH = 1000;
    private const int DockW = 800;
    private const int DockH = 120;

    [Fact]
    public void Top_is_horizontally_centered_and_above_margin()
    {
        (int x, int y) = DockPlacement.Anchor(WorkX, WorkY, WorkW, WorkH, DockW, DockH, DockEdge.Top);

        Assert.Equal(WorkX + (WorkW - DockW) / 2, x);
        Assert.Equal(WorkY + DockPlacement.DefaultMargin, y);
    }

    [Fact]
    public void Bottom_sits_above_work_area_bottom()
    {
        (int x, int y) = DockPlacement.Anchor(WorkX, WorkY, WorkW, WorkH, DockW, DockH, DockEdge.Bottom);

        Assert.Equal(WorkX + (WorkW - DockW) / 2, x);
        Assert.Equal(WorkY + WorkH - DockH - DockPlacement.DefaultMargin, y);
    }

    [Fact]
    public void Left_and_right_are_vertically_centered()
    {
        int expectedY = WorkY + (WorkH - DockH) / 2;

        (int leftX, int leftY) = DockPlacement.Anchor(WorkX, WorkY, WorkW, WorkH, DockW, DockH, DockEdge.Left);
        (int rightX, int rightY) = DockPlacement.Anchor(WorkX, WorkY, WorkW, WorkH, DockW, DockH, DockEdge.Right);

        Assert.Equal(WorkX + DockPlacement.DefaultMargin, leftX);
        Assert.Equal(expectedY, leftY);

        Assert.Equal(WorkX + WorkW - DockW - DockPlacement.DefaultMargin, rightX);
        Assert.Equal(expectedY, rightY);
    }

    [Theory]
    [InlineData(DockEdge.Top, -1)]      // 上边：y 减小
    [InlineData(DockEdge.Left, -1)]     // 左边：x 减小
    [InlineData(DockEdge.Bottom, 1)]    // 下边：y 增大
    [InlineData(DockEdge.Right, 1)]     // 右边：x 增大
    public void Hidden_offset_sign_matches_its_edge(DockEdge edge, int expectedSign)
    {
        int offset = DockPlacement.HiddenOffset(edge, DockW, DockH);
        Assert.Equal(expectedSign, Math.Sign(offset));
    }

    [Theory]
    [InlineData(DockEdge.Top)]
    [InlineData(DockEdge.Bottom)]
    [InlineData(DockEdge.Left)]
    [InlineData(DockEdge.Right)]
    public void Hiding_leaves_the_work_area_on_every_edge(DockEdge edge)
    {
        int offset = DockPlacement.HiddenOffset(edge, DockW, DockH);
        (int x, int y) = DockPlacement.Anchor(
            WorkX, WorkY, WorkW, WorkH, DockW, DockH, edge, DockPlacement.DefaultMargin, offset);

        Assert.True(
            DockPlacement.IsOutside(edge, x, y, DockW, DockH, WorkX, WorkY, WorkW, WorkH),
            edge + " 收起后必须完全离开工作区");
    }

    [Theory]
    [InlineData(DockEdge.Top)]
    [InlineData(DockEdge.Bottom)]
    [InlineData(DockEdge.Left)]
    [InlineData(DockEdge.Right)]
    public void Shown_position_stays_inside_the_work_area(DockEdge edge)
    {
        (int x, int y) = DockPlacement.Anchor(WorkX, WorkY, WorkW, WorkH, DockW, DockH, edge);

        Assert.False(
            DockPlacement.IsOutside(edge, x, y, DockW, DockH, WorkX, WorkY, WorkW, WorkH),
            edge + " 展开时必须留在工作区内");
    }

    [Fact]
    public void Hidden_offset_is_never_smaller_than_the_dock_itself()
    {
        Assert.True(Math.Abs(DockPlacement.HiddenOffset(DockEdge.Top, DockW, DockH)) >= DockH);
        Assert.True(Math.Abs(DockPlacement.HiddenOffset(DockEdge.Bottom, DockW, DockH)) >= DockH);
        Assert.True(Math.Abs(DockPlacement.HiddenOffset(DockEdge.Left, DockW, DockH)) >= DockW);
        Assert.True(Math.Abs(DockPlacement.HiddenOffset(DockEdge.Right, DockW, DockH)) >= DockW);
    }

    /// <summary>下边缘收起时必须把任务栏高度也算进去，否则窗口被屏幕底边托住、压在任务栏上露出一条。</summary>
    [Fact]
    public void Bottom_edge_clears_the_taskbar_when_hiding()
    {
        const int taskbarHeight = 48;
        int offset = DockPlacement.HiddenOffset(DockEdge.Bottom, DockW, DockH, beyondEdge: taskbarHeight);

        // 可见位置在工作区底部上方 10px；收起位置顶部必须不低于屏幕底边
        (int x, int y) = DockPlacement.Anchor(
            WorkX, WorkY, WorkW, WorkH, DockW, DockH, DockEdge.Bottom, DockPlacement.DefaultMargin, offset);

        int screenBottom = WorkY + WorkH + taskbarHeight;
        Assert.True(y >= screenBottom, $"收起位置 y={y} 应完全离开屏幕（屏幕底边 {screenBottom}）");
    }

    [Theory]
    [InlineData(DockEdge.Left, true)]
    [InlineData(DockEdge.Right, true)]
    [InlineData(DockEdge.Top, false)]
    [InlineData(DockEdge.Bottom, false)]
    public void Only_side_edges_are_vertical(DockEdge edge, bool expected)
        => Assert.Equal(expected, DockPlacement.IsVertical(edge));

    [Theory]
    [InlineData(DockEdge.Top, -999, 45)]
    [InlineData(DockEdge.Bottom, -999, 1051)]
    [InlineData(DockEdge.Left, -1915, 521)]
    [InlineData(DockEdge.Right, -29, 521)]
    public void Handle_is_centered_and_inset_from_the_physical_monitor_edge(DockEdge edge, int expectedX, int expectedY)
    {
        (int x, int y) = DockPlacement.AnchorHandle(-1920, 40, 1920, 1040, 78, 24, edge, 5);
        if (DockPlacement.IsVertical(edge))
        {
            (x, y) = DockPlacement.AnchorHandle(-1920, 40, 1920, 1040, 24, 78, edge, 5);
        }

        Assert.Equal(expectedX, x);
        Assert.Equal(expectedY, y);
    }

    [Theory]
    [InlineData(960, 60, DockEdge.Top)]
    [InlineData(960, 990, DockEdge.Bottom)]
    [InlineData(10, 500, DockEdge.Left)]
    [InlineData(1910, 500, DockEdge.Right)]
    public void Nearest_edge_picks_the_closest_side(int centerX, int centerY, DockEdge expected)
        => Assert.Equal(expected, DockPlacement.NearestEdge(centerX, centerY, WorkX, WorkY, WorkW, WorkH));

    [Fact]
    public void Lerp_offset_is_clamped_and_rounded()
    {
        Assert.Equal(0, DockPlacement.LerpOffset(0, 100, -1));
        Assert.Equal(100, DockPlacement.LerpOffset(0, 100, 2));
        Assert.Equal(50, DockPlacement.LerpOffset(0, 100, 0.5));
        Assert.Equal(-50, DockPlacement.LerpOffset(0, -100, 0.5));
    }

    // ---------------- 沿边偏移记忆（拖动后不回边中间） ----------------

    [Theory]
    [InlineData(0, 0)]      // 居中（默认，兼容旧配置）
    [InlineData(-1, -560)]  // 贴起点：-(1920-800)/2
    [InlineData(1, 560)]    // 贴终点
    [InlineData(0.5, 280)]
    public void Edge_offset_ratio_maps_to_pixels_along_the_edge(double ratio, int expected)
        => Assert.Equal(expected, DockPlacement.EdgeOffsetAlong(ratio, WorkW, DockW));

    [Fact]
    public void Edge_offset_stays_inside_the_screen_when_ratio_is_out_of_range()
    {
        int underflow = DockPlacement.EdgeOffsetAlong(-5, WorkW, DockW);
        int overflow = DockPlacement.EdgeOffsetAlong(5, WorkW, DockW);

        Assert.Equal(-560, underflow);
        Assert.Equal(560, overflow);
    }

    [Fact]
    public void Edge_offset_keeps_an_oversized_dock_as_far_in_as_possible()
    {
        // Dock 比工作区还长：比例 +1 表示贴到终点（右缘对齐工作区右缘），不会再往外推
        int along = DockPlacement.EdgeOffsetAlong(1, workLength: 1000, dockLength: 1200);

        Assert.Equal(-100, along);
        (int x, int _) = DockPlacement.Anchor(0, 0, 1000, 500, 1200, 80, DockEdge.Top, alongOffset: along);
        Assert.Equal(1000 - 1200, x);
    }

    [Theory]
    [InlineData(DockEdge.Top)]
    [InlineData(DockEdge.Bottom)]
    public void Offset_ratio_round_trips_for_horizontal_edges(DockEdge edge)
    {
        int along = DockPlacement.EdgeOffsetAlong(0.6, WorkW, DockW);
        (int x, int y) = DockPlacement.Anchor(WorkX, WorkY, WorkW, WorkH, DockW, DockH, edge, alongOffset: along);
        double ratio = DockPlacement.OffsetRatio(edge, x, y, DockW, DockH, WorkX, WorkY, WorkW, WorkH);

        Assert.Equal(0.6, ratio, precision: 2);
    }

    [Theory]
    [InlineData(DockEdge.Left)]
    [InlineData(DockEdge.Right)]
    public void Offset_ratio_round_trips_for_vertical_edges(DockEdge edge)
    {
        int along = DockPlacement.EdgeOffsetAlong(-0.4, WorkH, DockH);
        (int x, int y) = DockPlacement.Anchor(WorkX, WorkY, WorkW, WorkH, DockW, DockH, edge, alongOffset: along);
        double ratio = DockPlacement.OffsetRatio(edge, x, y, DockW, DockH, WorkX, WorkY, WorkW, WorkH);

        Assert.Equal(-0.4, ratio, precision: 2);
    }

    [Fact]
    public void Offset_ratio_is_zero_when_the_dock_fills_the_edge()
        => Assert.Equal(0, DockPlacement.OffsetRatio(DockEdge.Top, -100, 0, WorkW + 200, DockH, WorkX, WorkY, WorkW, WorkH));

    [Fact]
    public void Offset_ratio_clamps_when_the_window_is_dropped_outside()
        => Assert.Equal(1, DockPlacement.OffsetRatio(DockEdge.Top, 5000, 0, DockW, DockH, WorkX, WorkY, WorkW, WorkH));

    [Theory]
    [InlineData(DockEdge.Top)]
    [InlineData(DockEdge.Bottom)]
    [InlineData(DockEdge.Left)]
    [InlineData(DockEdge.Right)]
    public void Handle_tracks_the_offset_dock_center_with_a_taskbar_and_negative_monitor_origin(DockEdge edge)
    {
        const int screenX = -1920, screenY = -100, screenWidth = 1920, screenHeight = 1080;
        const int workX = screenX + 40, workY = screenY + 30, workWidth = screenWidth - 40, workHeight = screenHeight - 70;
        bool vertical = DockPlacement.IsVertical(edge);
        int handleWidth = vertical ? 24 : 78, handleHeight = vertical ? 78 : 24;
        foreach (double ratio in new[] { -1.0, -0.6, 0, 0.6, 1 })
        {
            int along = DockPlacement.EdgeOffsetAlong(ratio, vertical ? workHeight : workWidth, vertical ? DockH : DockW);
            (int dockX, int dockY) = DockPlacement.Anchor(workX, workY, workWidth, workHeight, DockW, DockH, edge, alongOffset: along);
            int handleAlong = DockPlacement.HandleOffsetAlong(ratio,
                vertical ? workY : workX, vertical ? workHeight : workWidth, vertical ? DockH : DockW,
                vertical ? screenY : screenX, vertical ? screenHeight : screenWidth, vertical ? handleHeight : handleWidth);
            (int handleX, int handleY) = DockPlacement.AnchorHandle(screenX, screenY, screenWidth, screenHeight, handleWidth, handleHeight, edge, 5, handleAlong);
            double dockCenter = vertical ? dockY + DockH / 2.0 : dockX + DockW / 2.0;
            double handleCenter = vertical ? handleY + handleHeight / 2.0 : handleX + handleWidth / 2.0;
            Assert.InRange(Math.Abs(dockCenter - handleCenter), 0, 0.5);
        }
    }
}
