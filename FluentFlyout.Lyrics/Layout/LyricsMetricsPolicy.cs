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
    /// <param name="userSingleLine">
    /// 用户是否**主动**选择单行。
    ///
    /// 与「装不下所以降级」是两件事：
    /// <list type="bullet">
    /// <item>用户选单行 → 无论装不装得下都单行（P4-4 的单/双行开关）；</item>
    /// <item>系统判定装不下 → 降级单行，用户的选择是「自动」。</item>
    /// </list>
    /// 两者最终都走 <see cref="LyricsPanelMetrics.Create"/> 的
    /// <c>forceSingleLine</c>，但来源不同，所以要分开传。
    /// </param>
    public static LyricsPanelMetrics Create(
        bool isVertical,
        bool isSmallTaskbar,
        double preferredFontSize = DefaultPreferredFontSize,
        bool userSingleLine = false)
    {
        var crossSize = isSmallTaskbar ? SmallTaskbarCrossSize : DefaultTaskbarCrossSize;

        // 纵向可用高度：
        //   横向 → 就是任务栏短边，两行装不下必须降级；
        //   竖向 → 不受约束（见 VerticalUnconstrainedHeight）。
        var availableHeight = isVertical ? VerticalUnconstrainedHeight : crossSize;

        // 强制单行的两个来源，任一成立就单行。
        //
        // 1. 用户主动选单行（userSingleLine）——竖向下也生效，
        //    因为这是用户的明确意图，不是系统在替他做取舍。
        // 2. 横向 + 小任务栏——28 DIP 装不下两行 13px 的字，
        //    硬塞会把两行都裁掉一半，所以压成一行。
        //    竖向的约束是宽度，靠换行解决，再压成单行等于把唯一的优势扔掉。
        var forceSingleLine = userSingleLine || (isSmallTaskbar && !isVertical);

        return LyricsPanelMetrics.Create(
            availableHeight,
            preferredFontSize,
            forceSingleLine,
            // 竖向：短边约束的是「一行放几个字」，交给 maxFontSize；
            // 横向：短边已经在 availableHeight 里约束行高，这里不再重复夹。
            maxFontSize: isVertical ? GetVerticalMaxFontSize(isSmallTaskbar) : double.PositiveInfinity);
    }

    /// <summary>
    /// 竖排时一行里至少要放得下几个汉字。
    ///
    /// 两个：一个字的竖条读不成句，两个才能「读」——这是可读性的下限，
    /// 不是排版偏好。截图矩阵实测：28 DIP 短边配 20px 字时一行只站一个
    /// 汉字，30 字的长句变成 30 行独字塔，用户看不出这是歌词。
    /// </summary>
    public const int MinimumGlyphsPerLine = 2;

    /// <summary>
    /// 字宽的安全余量（DIP）。
    ///
    /// 汉字字宽≈字号，但 WPF 的字符步进（advance）有亚像素舍入，
    /// 恰好 N×字号 = 短边时偶尔会多折一个字。留半像素把这种边界抖开。
    /// 取得比 <c>LyricsVerticalLayout.WrapSafetyMargin</c>（4 DIP）小，
    /// 是因为这里直接决定字号：多留 1 DIP 就会让默认 13px 在 28 DIP 短边上
    /// 被压到 12px，白白变小一圈。
    /// </summary>
    public const double VerticalGlyphSafetyMargin = 0.5;

    /// <summary>
    /// 竖向任务栏上允许的最大字号（DIP）——由短边与「每行至少两个字」推出。
    ///
    /// 与 <see cref="LyricsPanelMetrics.MinimumFontSize"/> 的关系：这里可能
    /// 算出比下限还小的值（例如 16 DIP 的极窄任务栏），最终字号的夹取顺序是
    /// 「先夹几何上限，再抬到下限」，所以极端情况下仍然是一个字一行——
    /// 那种宽度本来也放不下两个能看清的字，抬到下限更符合直觉。
    /// </summary>
    public static double GetVerticalMaxFontSize(bool isSmallTaskbar)
    {
        var crossSize = isSmallTaskbar ? SmallTaskbarCrossSize : DefaultTaskbarCrossSize;
        return (crossSize / MinimumGlyphsPerLine) - VerticalGlyphSafetyMargin;
    }
}
