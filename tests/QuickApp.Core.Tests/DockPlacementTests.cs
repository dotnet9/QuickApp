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

    [Fact]
    public void Hidden_offset_pushes_the_dock_out_of_its_own_edge()
    {
        int top = DockPlacement.HiddenOffset(DockEdge.Top, DockW, DockH);
        int bottom = DockPlacement.HiddenOffset(DockEdge.Bottom, DockW, DockH);
        int left = DockPlacement.HiddenOffset(DockEdge.Left, DockW, DockH);
        int right = DockPlacement.HiddenOffset(DockEdge.Right, DockW, DockH);

        // 上/左：向负方向移出；下/右：向正方向移出
        Assert.True(top > 0);
        Assert.True(bottom < 0);
        Assert.True(left < 0);
        Assert.True(right > 0);

        // 位移量要够把窗口整个推出可视区
        Assert.True(Math.Abs(top) >= DockH);
        Assert.True(Math.Abs(left) >= DockW);
    }

    [Fact]
    public void Hidden_position_actually_leaves_the_screen()
    {
        int offset = DockPlacement.HiddenOffset(DockEdge.Top, DockW, DockH);
        (_, int y) = DockPlacement.Anchor(WorkX, WorkY, WorkW, WorkH, DockW, DockH, DockEdge.Top, DockPlacement.DefaultMargin, offset);

        Assert.True(y + DockH <= WorkY, "收起后应完全离开工作区上边缘");
    }

    [Theory]
    [InlineData(DockEdge.Left, true)]
    [InlineData(DockEdge.Right, true)]
    [InlineData(DockEdge.Top, false)]
    [InlineData(DockEdge.Bottom, false)]
    public void Only_side_edges_are_vertical(DockEdge edge, bool expected)
        => Assert.Equal(expected, DockPlacement.IsVertical(edge));

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
