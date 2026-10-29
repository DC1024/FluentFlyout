// Copyright (c) 2026 anync
// SPDX-License-Identifier: MIT
//
// 本文件为本仓库新增，不源自 TaskbarLyrics。

namespace FluentFlyout.Lyrics.Layout;

/// <summary>
/// 把「任务栏矩形 + 小组件物理尺寸」映射成画布坐标与屏幕占位矩形。
///
/// 四种任务栏方向（下/上/左/右）在代码里只体现为两个差别：**主轴是哪一条**，
/// 以及**主轴的正方向指向哪里**。整块小组件只有一块画布，所以竖向时要把
/// 主/次轴对调——这一对调如果写错，表现是「歌词跑到任务栏外面」或者
/// 「左侧时方向反了」，两者在截图矩阵里都不一定显眼（图还是一片字），
/// 只有拿到数字才能判。
///
/// 为什么放在 Core：这段是纯算术，输入输出全是整数像素与 DIP，没有任何
/// 句柄、没有 <c>SettingsManager</c>。抽出来以后四种方向 × 三档 DPI 可以
/// 精确断言，而不必先摆出一个竖向任务栏（Win11 摆不出来，见文档 7.2.6）。
/// </summary>
/// <remarks>
/// <para>
/// <b>竖向时轴对调不是可选项</b>：宿主对竖排小组件挂的是
/// <c>RotateTransform(90)</c>，旋转发生在渲染阶段，布局坐标仍是「横向的」。
/// 也就是说画布上的 <c>Left</c> 量的是屏幕上**纵向**的位置，
/// <c>Top</c> 量的是**横向**（离任务栏贴边那一侧的距离）。所以：
/// </para>
/// <list type="bullet">
/// <item>横向：<c>Left = 主轴位置</c>、<c>Top = 次轴居中</c>。</item>
/// <item>竖向：<c>Left = 次轴居中</c>、<c>Top = 主轴位置</c>。</item>
/// </list>
/// <para>
/// 同理，返回的占位矩形在竖向时必须把宽高换回来——它是给
/// <c>UpdateWindowRegion</c> 用的屏幕物理矩形，而屏幕不知道「宿主内部转了一下」。
/// </para>
/// </remarks>
public readonly record struct WidgetPlacement(
    double CanvasLeft,
    double CanvasTop,
    double RectX,
    double RectY,
    double RectWidth,
    double RectHeight)
{
    /// <summary>
    /// 算出小组件在画布上的位置，以及它在屏幕上实际占的那块矩形。
    /// </summary>
    /// <param name="taskbarWidth">任务栏物理宽度（px）。</param>
    /// <param name="taskbarHeight">任务栏物理高度（px）。</param>
    /// <param name="isVertical">
    /// 任务栏是否竖直。生产里的判据是 <c>taskbarHeight &gt; taskbarWidth</c>，
    /// 不读 <c>StuckRects3</c>——那个键在侧栏方案下是错的（文档 8.2 实测）。
    /// </param>
    /// <param name="physicalWidth">小组件沿**主轴**的物理长度（px）。</param>
    /// <param name="physicalHeight">小组件沿**次轴**的物理长度（px）。</param>
    /// <param name="primaryPosition">小组件沿主轴的位置（px，已在任务栏坐标系内）。</param>
    /// <param name="dpiScale">DPI 缩放。画布坐标是 DIP，传进来的位置是 px。</param>
    public static WidgetPlacement Resolve(
        int taskbarWidth,
        int taskbarHeight,
        bool isVertical,
        int physicalWidth,
        int physicalHeight,
        int primaryPosition,
        double dpiScale)
    {
        // 次轴长度：竖向时是任务栏的宽度（40–60 DIP 那一条），横向时是高度。
        var crossSize = isVertical ? taskbarWidth : taskbarHeight;

        // 次轴居中。整数除法沿用原实现：physicalHeight 比 crossSize 还大时
        // 结果向零截断（负数一侧会差 1px），这点差异肉眼不可见，但换算法
        // 会让回归图逐像素对不上，所以保持。
        var crossPosition = (crossSize - physicalHeight) / 2;

        var canvasLeft = (isVertical ? crossPosition : primaryPosition) / dpiScale;
        var canvasTop = (isVertical ? primaryPosition : crossPosition) / dpiScale;

        // 经过 90° 旋转后视觉包围盒的宽高是互换的。
        var rectWidth = isVertical ? physicalHeight : physicalWidth;
        var rectHeight = isVertical ? physicalWidth : physicalHeight;

        return new WidgetPlacement(
            canvasLeft,
            canvasTop,
            canvasLeft * dpiScale,
            canvasTop * dpiScale,
            rectWidth,
            rectHeight);
    }

    /// <summary>
    /// 任务栏贴的是屏幕**左侧**还是右侧。
    ///
    /// 只有竖向任务栏才有意义：贴左侧时主列要贴屏幕边缘，而不是继续贴
    /// 屏幕中间（真机反馈的「左侧方向不对」，见 <c>SetVerticalMirror</c>）。
    /// 判据是「离哪边更近」，不读任务栏自己的坐标符号——多显示器时左侧
    /// 那台屏幕的 x 是负的，按符号判会全反。
    /// </summary>
    /// <param name="taskbarLeft">任务栏左边（px，屏幕坐标）。</param>
    /// <param name="taskbarRight">任务栏右边（px，屏幕坐标）。</param>
    /// <param name="monitorLeft">所在显示器工作区左边（px）。</param>
    /// <param name="monitorRight">所在显示器工作区右边（px）。</param>
    /// <remarks>
    /// 参数用 <see cref="double"/> 而不是 <see cref="int"/>：显示器工作区在
    /// <c>MonitorUtil</c> 里是 DIP 化的 double，调用侧每帧都要取它，
    /// 在这里截断一次就等于把「差 0.4 个 DIP 的两侧」判成贴边。
    /// </remarks>
    public static bool IsFlushLeft(
        double taskbarLeft,
        double taskbarRight,
        double monitorLeft,
        double monitorRight)
    {
        var leftGap = taskbarLeft - monitorLeft;
        var rightGap = monitorRight - taskbarRight;
        return leftGap <= rightGap;
    }
}
