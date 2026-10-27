// Copyright (c) 2026 anync
// SPDX-License-Identifier: MIT
//
// 本文件为本仓库新增，不源自 TaskbarLyrics。

namespace FluentFlyout.Lyrics.Layout;

/// <summary>
/// 竖向任务栏上歌词面板沿主轴（屏幕纵向）要占多长。
///
/// 竖向任务栏的物理约束是**宽度窄**（通常 40–60 DIP），一句歌词必然折成多行，
/// 所以「想要多长」≈ 「折成 N 行需要多少高度」。这个估算决定了小组件在
/// 主轴上占多大一块——报太小会让歌词只剩两三行可见，报太大会把任务栏
/// 中部的应用按钮盖住。
///
/// 为什么放在 Core：它是纯算术，可以用假字号/假宽度精确断言边界。
/// 放在 WPF 层就只能靠截图目视，而这类「估算偏小 30%」的错误在截图上
/// 看不出来（看起来只是「字少了点」）。
/// </summary>
public static class LyricsVerticalLayout
{
    /// <summary>
    /// 估算失败或内容为空时的兜底长度（DIP）。
    ///
    /// 取 120：按 40 DIP 宽、13px 字号算约 7–8 行，够显示一句短歌词。
    /// 报 0 会让避让算法认为「什么都不需要」，把小组件塞进最窄的缝里。
    /// </summary>
    public const double FallbackLength = 120;

    /// <summary>
    /// 允许的最小长度（DIP）。再短就只能显示一两行，不如不给。
    ///
    /// 只在**兜底路径**（内容宽度未知）与「估算结果异常小」时生效。
    /// 不能无条件夹下限：一句很短的两行歌词算出来约 30 DIP，硬夹到 64
    /// 会让小组件白占一倍空间，主轴上留下一条空白。
    /// </summary>
    public const double MinimumLength = 64;

    /// <summary>
    /// 折行宽度的安全余量（DIP）。与 <c>_extraMarginForText</c> 同源：
    /// 文本按容器宽度精确折行时，最后一行常常差一两个像素就被挤下去，
    /// 留 4 DIP 让「刚好放得下」的情况稳定落在同一行。
    /// </summary>
    public const double WrapSafetyMargin = 4;

    /// <summary>
    /// 估算沿主轴需要的长度（DIP）。
    /// </summary>
    /// <param name="contentWidth">
    /// 歌词单行的**不折行**宽度（DIP），来自
    /// <c>LyricsPanel.DesiredContentWidth</c>。
    /// </param>
    /// <param name="crossSize">
    /// 任务栏短边（DIP），也就是折行后的可用宽度。
    /// </param>
    /// <param name="lineHeight">单行高度（DIP）。</param>
    /// <param name="maxLength">
    /// 任务栏主轴方向能给的最大长度（DIP）。结果不会超过它。
    /// </param>
    /// <returns>主轴长度（DIP），已夹在 [<see cref="MinimumLength"/>, maxLength] 内。</returns>
    public static double EstimateMainAxisLength(
        double contentWidth,
        double crossSize,
        double lineHeight,
        double maxLength)
    {
        if (!double.IsFinite(maxLength) || maxLength <= 0)
        {
            return 0;
        }

        // 内容宽度或行高不可用时给兜底值。这类输入来自首帧之前的
        // FormattedText 测量失败，属于「暂时不知道」而不是「不需要空间」。
        if (!double.IsFinite(contentWidth) || contentWidth <= 0 ||
            !double.IsFinite(crossSize) || crossSize <= 0 ||
            !double.IsFinite(lineHeight) || lineHeight <= 0)
        {
            return Math.Min(FallbackLength, maxLength);
        }

        var usableWidth = crossSize - WrapSafetyMargin;
        if (usableWidth <= 0)
        {
            // 任务栏窄到连安全余量都吃掉了，按整宽折行。
            usableWidth = crossSize;
        }

        // 向上取整：差 0.1 个像素也算一行，否则最后几个字会被切掉。
        var rows = Math.Ceiling(contentWidth / usableWidth);

        // rows 可能极大（用户拖来一句 200 字的歌词），先夹住再乘，
        // 避免 rows * lineHeight 溢出到 Infinity。
        var maxRows = Math.Max(1, Math.Ceiling(maxLength / lineHeight));
        rows = Math.Min(rows, maxRows);

        var length = rows * lineHeight;

        // 这里**不夹 MinimumLength**：真实估算出多少就是多少。
        // 一句短歌词折成两行约 30 DIP，硬夹到 64 会让小组件白占空间。
        // 下限只用于「输入不可用」的兜底路径（见上方 return）。
        return Math.Min(length, maxLength);
    }
}
