// Copyright (c) 2026 anync
// SPDX-License-Identifier: MIT
//
// 本文件为本仓库新增，不源自 TaskbarLyrics。

namespace FluentFlyout.Lyrics.Layout;

/// <summary>
/// 把「任务栏长什么样」翻译成「歌词面板用什么度量」。
///
/// 为什么单独抽出来：这段决策原本写在 <c>TaskbarWidgetControl.BuildLyricsMetrics</c> 里，
/// 只有一行三元表达式，看起来不值得测。但它同时依赖**方向**与**是否小任务栏**，
/// 两个输入各自独立变化（用户改任务栏位置、改图标大小、换到另一块屏），
/// 组合错了用户看到的是「竖任务栏里歌词被压成一行、只剩三四个字」——
/// 而这恰恰是 P3 最容易被忽略的一条。放在 Core 里就能用 2×2 矩阵锁死。
///
/// 单位全部是 DIP。调用方负责乘 DPI——这里不需要知道 DPI 是多少，
/// 因为度量的每一项（字号、行高、间距）都是 DIP，物理像素由 WPF 自己换算。
/// 也正因为如此，<b>混合 DPI 切换时度量不需要重算</b>：
/// 同一块任务栏在 100% 与 150% 下算出来的度量完全一致。
/// </summary>
public static class LyricsMetricsPolicy
{
    /// <summary>标准任务栏的短边长度（DIP）。</summary>
    public const double DefaultTaskbarCrossSize = 40;

    /// <summary>小任务栏的短边长度（DIP）。</summary>
    public const double SmallTaskbarCrossSize = 28;

    /// <summary>用户默认字号（DIP）。</summary>
    public const double DefaultPreferredFontSize = 13;

    /// <summary>
    /// 竖向任务栏上「高度方向不构成约束」的有限替身。
    ///
    /// 为什么需要它：<see cref="LyricsPanelMetrics.Create"/> 的 <c>availableHeight</c>
    /// 表示「面板在**纵向**能占多少」。横向任务栏上这个量就是任务栏的短边（40/28），
    /// 所以两行装不下时要降级单行。但竖向任务栏不旋转排版（文本横排、沿主轴换行），
    /// 面板的纵向延伸是整条屏幕高度——两行永远装得下，短边约束的是**宽度**而非高度。
    ///
    /// 把短边当 availableHeight 传给竖向是错的：28 DIP 的竖向任务栏会被判成
    /// 「装不下两行」而压成单行，白扔掉换行这个唯一的优势。
    ///
    /// 取 4096 而不是 <see cref="double.PositiveInfinity"/>：后者会被
    /// <see cref="LyricsPanelMetrics.Create"/> 的合法性检查当成脏数据回落成 40，
    /// 效果与不传一样。4096 DIP 远超任何单屏高度（8K 屏也只有 4320 DIP），
    /// 语义上就是「不约束」。
    /// </summary>
    private const double VerticalUnconstrainedHeight = 4096;

    /// <summary>
    /// 按任务栏形态推导歌词度量。
    /// </summary>
    /// <param name="isVertical">
    /// 任务栏是否竖向（高 &gt; 宽）。竖向时**不旋转排版**，文本沿主轴换行，
    /// 见 <c>LyricsPanel.SetVerticalMode</c> 里的实测说明。
    /// </param>
    /// <param name="isSmallTaskbar">
    /// 任务栏短边是否小于 40 DIP。注意判定用的是 DIP 而不是物理像素：
    /// 150% DPI 下物理 60px 的任务栏仍是 40 DIP，不会被误判成小任务栏。
    /// </param>
    /// <param name="preferredFontSize">用户设置的字号（DIP）。</param>
    public static LyricsPanelMetrics Create(
        bool isVertical,
        bool isSmallTaskbar,
        double preferredFontSize = DefaultPreferredFontSize)
    {
        var crossSize = isSmallTaskbar ? SmallTaskbarCrossSize : DefaultTaskbarCrossSize;

        // 纵向可用高度：
        //   横向 → 就是任务栏短边，两行装不下必须降级；
        //   竖向 → 不受约束（见 VerticalUnconstrainedHeight）。
        var availableHeight = isVertical ? VerticalUnconstrainedHeight : crossSize;

        // 只有「横向 + 小任务栏」需要强制单行。
        //
        // 横向小任务栏的约束是高度：28 DIP 装不下两行 13px 的字，
        // 硬塞会把两行都裁掉一半，所以压成一行。
        //
        // 竖向的约束是宽度，靠换行解决——再压成单行等于把唯一的优势扔掉。
        // TaskbarLyrics 在竖向任务栏上也是换行而非单行。
        var forceSingleLine = isSmallTaskbar && !isVertical;

        return LyricsPanelMetrics.Create(availableHeight, preferredFontSize, forceSingleLine);
    }

    /// <summary>
    /// 竖向任务栏上「一行」能占的高度上限（DIP）——也就是任务栏的宽度。
    ///
    /// 与 <see cref="LyricsPanelMetrics.LineHeight"/> 比较即可判断
    /// 「这个宽度的任务栏还放得下正常字号吗」。目前 60 DIP 的竖向任务栏
    /// 配 13px 字（行高 14.95）绰绰有余，所以没有额外的降级路径；
    /// 留着这个方法是为了让「宽度是不是约束」这件事在调用点显式可见，
    /// 而不是埋在注释里。
    /// </summary>
    public static double GetVerticalLineBudget(bool isSmallTaskbar)
    {
        return isSmallTaskbar ? SmallTaskbarCrossSize : DefaultTaskbarCrossSize;
    }
}
