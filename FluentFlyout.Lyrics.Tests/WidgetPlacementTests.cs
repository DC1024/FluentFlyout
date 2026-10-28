// Copyright (c) 2026 anync
// SPDX-License-Identifier: MIT
//
// 本文件为本仓库新增，不源自 TaskbarLyrics。

using FluentFlyout.Lyrics.Layout;
using Xunit;

namespace FluentFlyout.Lyrics.Tests;

/// <summary>
/// 四种任务栏方向下的**定位几何**：<see cref="WidgetPlacement"/>。
///
/// <para>
/// 为什么要有这一层：截图矩阵里竖排的尺寸是夹具直接写进面板的，装配链探针
/// 只摆出一种任务栏，而「左/右」两列在 Win11 上根本摆不出来（文档 7.2.6）。
/// 结果是「主轴与次轴对调」这段算术——也是「左侧方向反了」那类真机反馈的
/// 唯一来源——长期没有任何断言。抽成纯函数后，四方向 × 三档 DPI 可以精确判。
/// </para>
///
/// <para>
/// 判据分三类，刻意不用「再算一遍同样的公式」去验证：
/// </para>
/// <list type="bullet">
/// <item><b>结构</b>：主轴位置只能落在它该落的那一个轴上（横向是 Left，
/// 竖向是 Top），另一个轴只由次轴居中决定。</item>
/// <item><b>交换</b>：竖向的屏幕矩形宽高必须互换——旋转只发生在渲染层，
/// 屏幕不知道这回事。</item>
/// <item><b>等价</b>：顶部与底部同构（同一套坐标），这正是当前实现
/// 没有「顶部分支」却仍能在顶部工作的原因。</item>
/// </list>
/// </remarks>
public class WidgetPlacementTests
{
    /// <summary>任务栏主轴的 DIP 长度，取 1080p 横屏的宽度。</summary>
    private const double TaskbarLengthDip = 1920;

    /// <summary>任务栏次轴的 DIP 长度（48 DIP 是 Win11 默认任务栏高度）。</summary>
    private const double TaskbarCrossDip = 48;

    /// <summary>小组件沿主轴的 DIP 长度。</summary>
    private const double WidgetPrimaryDip = 400;

    /// <summary>小组件沿次轴的 DIP 长度（竖向时就是那条 40 DIP 宽的竖排文字带）。</summary>
    private const double WidgetCrossDip = 40;

    /// <summary>三档 DPI，与截图矩阵同一组。</summary>
    public static TheoryData<double> DpiScales => new() { 1.0, 1.5, 2.0 };

    /// <summary>
    /// 横向（底部/顶部）：主轴走画布 <c>Left</c>，次轴居中走 <c>Top</c>，
    /// 屏幕矩形不换宽高。
    /// </summary>
    [Theory]
    [MemberData(nameof(DpiScales))]
    public void HorizontalPutsPrimaryPositionOnTheLeftAxis(double dpiScale)
    {
        var taskbarWidth = (int)(TaskbarLengthDip * dpiScale);
        var taskbarHeight = (int)(TaskbarCrossDip * dpiScale);
        var physicalWidth = (int)(WidgetPrimaryDip * dpiScale);
        var physicalHeight = (int)(WidgetCrossDip * dpiScale);
        const int primaryPosition = 300;

        var placement = WidgetPlacement.Resolve(
            taskbarWidth,
            taskbarHeight,
            isVertical: false,
            physicalWidth,
            physicalHeight,
            primaryPosition,
            dpiScale);

        // 主轴位置原样落到 Left，换算成 DIP。
        Assert.Equal(primaryPosition / dpiScale, placement.CanvasLeft, 9);

        // 次轴居中：48 DIP 的任务栏里放 40 DIP 的组件，上下各留 4 DIP。
        Assert.Equal(4.0, placement.CanvasTop, 9);

        // 屏幕矩形不换宽高，且原点就是画布坐标换回物理像素。
        Assert.Equal(placement.CanvasLeft * dpiScale, placement.RectX, 9);
        Assert.Equal(placement.CanvasTop * dpiScale, placement.RectY, 9);
        Assert.Equal(physicalWidth, placement.RectWidth, 9);
        Assert.Equal(physicalHeight, placement.RectHeight, 9);
    }

    /// <summary>
    /// 竖向（左/右）：主轴走画布 <c>Top</c>，次轴居中走 <c>Left</c>，
    /// 屏幕矩形宽高互换。
    /// </summary>
    [Theory]
    [MemberData(nameof(DpiScales))]
    public void VerticalSwapsTheAxesAndTheRect(double dpiScale)
    {
        // 竖向任务栏是「窄而高」，宽高与横向恰好对调。
        var taskbarWidth = (int)(TaskbarCrossDip * dpiScale);
        var taskbarHeight = (int)(TaskbarLengthDip * dpiScale);
        var physicalWidth = (int)(WidgetPrimaryDip * dpiScale);
        var physicalHeight = (int)(WidgetCrossDip * dpiScale);
        const int primaryPosition = 300;

        var placement = WidgetPlacement.Resolve(
            taskbarWidth,
            taskbarHeight,
            isVertical: true,
            physicalWidth,
            physicalHeight,
            primaryPosition,
            dpiScale);

        // 主轴位置这次落在 Top 上（旋转发生在渲染层，画布仍然是「横向」的）。
        Assert.Equal(primaryPosition / dpiScale, placement.CanvasTop, 9);

        // 次轴居中变成 Left：48 DIP 宽的竖条里放 40 DIP 宽的组件，左右各留 4 DIP。
        Assert.Equal(4.0, placement.CanvasLeft, 9);

        // 屏幕矩形宽高互换，否则窗口区域会把歌词裁掉一半。
        Assert.Equal(physicalHeight, placement.RectWidth, 9);
        Assert.Equal(physicalWidth, placement.RectHeight, 9);
    }

    /// <summary>
    /// 主轴位置**只能**影响它该影响的那一个轴。
    ///
    /// 这条是防御性的：把 <c>isVertical</c> 的三元表达式写反时，上面两个用例
    /// 仍可能因为数值巧合通过（例如次轴恰好居中的时候），但「动主轴、
    /// 另一个轴不动」这条一定塌。
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PrimaryPositionOnlyMovesThePrimaryAxis(bool isVertical)
    {
        var taskbarWidth = isVertical ? 72 : 2880;
        var taskbarHeight = isVertical ? 2880 : 72;

        var a = WidgetPlacement.Resolve(
            taskbarWidth, taskbarHeight, isVertical, 1200, 120, 300, 1.5);
        var b = WidgetPlacement.Resolve(
            taskbarWidth, taskbarHeight, isVertical, 1200, 120, 900, 1.5);

        Assert.Equal(isVertical ? a.CanvasLeft : a.CanvasTop, isVertical ? b.CanvasLeft : b.CanvasTop, 9);

        if (isVertical)
        {
            Assert.NotEqual(a.CanvasTop, b.CanvasTop);
            Assert.Equal(a.CanvasLeft, b.CanvasLeft, 9);
        }
        else
        {
            Assert.NotEqual(a.CanvasLeft, b.CanvasLeft);
            Assert.Equal(a.CanvasTop, b.CanvasTop, 9);
        }
    }

    /// <summary>
    /// 顶部任务栏与底部任务栏**同构**：坐标只相对于任务栏自己的左上角，
    /// 与它在屏幕上的绝对位置无关。
    ///
    /// 这不是巧合，而是当前实现的真实行为——代码里没有「顶部」分支，
    /// 顶部能用就是因为这条等价。哪天有人给顶部加特殊处理，这个用例会先醒。
    /// </summary>
    [Fact]
    public void TopAndBottomAreStructurallyIdentical()
    {
        // 底部：任务栏贴在 y=1392 上；顶部：贴在 y=0 上。
        // Resolve 只吃宽高，不含原点，所以两次调用必然一致。
        var bottom = WidgetPlacement.Resolve(2560, 48, false, 800, 40, 120, 1.0);
        var top = WidgetPlacement.Resolve(2560, 48, false, 800, 40, 120, 1.0);

        Assert.Equal(bottom, top);
        Assert.Equal(120, bottom.CanvasLeft);
        Assert.Equal(4, bottom.CanvasTop);
    }

    /// <summary>
    /// 组件比任务栏还宽/高时，次轴居中会变成负数。
    ///
    /// 这里锁的是**整数除法的截断方向**：C# 的 <c>int / 2</c> 向零截断，
    /// 所以 -5/2 = -2 而不是 -3。与原实现逐像素一致，改算法会让回归图对不上。
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OversizedWidgetOverflowsBothSidesOfTheCrossAxis(bool isVertical)
    {
        // 次轴只有 40px，组件要 45px → 差 5px，居中后应各溢出 2px（截断）。
        var taskbarWidth = isVertical ? 40 : 1920;
        var taskbarHeight = isVertical ? 1920 : 40;

        var placement = WidgetPlacement.Resolve(
            taskbarWidth, taskbarHeight, isVertical, 400, 45, 0, 1.0);

        var crossPosition = isVertical ? placement.CanvasLeft : placement.CanvasTop;
        Assert.Equal(-2, crossPosition);
    }

    /// <summary>
    /// 贴左侧判定：只看「离哪边更近」，不看坐标符号。
    /// 多显示器时左侧那台屏幕的 x 是负的，按符号判会全反。
    /// </summary>
    [Theory]
    // 单屏：左贴边 / 右贴边 / 底部整条
    [InlineData(0, 48, 0, 1920, true)]
    [InlineData(1872, 1920, 0, 1920, false)]
    // 底部整条任务栏两边一样近，判 true——但只有竖向才会用到这个值，
    // 横向时它是无害的（见 WidgetPlacement.IsFlushLeft 的 remarks）。
    [InlineData(0, 1920, 0, 1920, true)]
    // 副屏在主屏左侧（x 为负）：贴外侧是左，贴内侧是右
    [InlineData(-2560, -2512, -2560, 0, true)]
    [InlineData(-48, 0, -2560, 0, false)]
    public void FlushLeftIsDecidedByDistanceNotSign(
        int taskbarLeft,
        int taskbarRight,
        int monitorLeft,
        int monitorRight,
        bool expected)
    {
        Assert.Equal(expected, WidgetPlacement.IsFlushLeft(
            taskbarLeft, taskbarRight, monitorLeft, monitorRight));
    }

    /// <summary>
    /// 竖向的左右两侧必须给出**相反**的镜像判定，否则「左侧方向不对」会以
    /// 「左右都对」的形式回归（两侧都贴屏幕中间）。
    /// </summary>
    [Fact]
    public void LeftAndRightVerticalTaskbarsMirrorEachOther()
    {
        const int monitorLeft = 0;
        const int monitorRight = 2560;

        Assert.True(WidgetPlacement.IsFlushLeft(0, 48, monitorLeft, monitorRight));
        Assert.False(WidgetPlacement.IsFlushLeft(2512, 2560, monitorLeft, monitorRight));
    }
}
