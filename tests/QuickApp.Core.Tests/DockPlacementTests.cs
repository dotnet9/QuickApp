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
}
