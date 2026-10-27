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
    /// 估算沿主轴需要的长度（DIP）——<b>只算一行</b>的简化入口。
    /// </summary>
    /// <remarks>
    /// 保留它是为了不让既有单测与调用方大改，但**生产路径必须用
    /// <see cref="EstimateMainAxisLength(LyricsVerticalContent, double, double)"/>**：
    /// 竖向歌词面板有当前行 + 翻译/下一行两行独立折行的文本，
    /// 只按一行估会漏掉次要行整块（截图矩阵实测少算一半）。
    /// </remarks>
    public static double EstimateMainAxisLength(
        double contentWidth,
        double crossSize,
        double lineHeight,
        double maxLength)
    {
        return EstimateMainAxisLength(
            new LyricsVerticalContent(contentWidth, lineHeight),
            crossSize,
            maxLength);
    }

    /// <summary>
    /// 估算沿主轴需要的长度（DIP）。
    ///
    /// 两行**各自独立折行**，所以分别算行数再相加：
    /// 主行 8 个字折 2 行、翻译 8 个字也折 2 行，加起来就是 4 行，
    /// 而不是「取两行较宽者的行数」。
    /// </summary>
    /// <param name="content">两行的实测宽度与行高。</param>
    /// <param name="crossSize">
    /// 任务栏短边（DIP），也就是折行后的可用宽度。
    /// </param>
    /// <param name="maxLength">
    /// 任务栏主轴方向能给的最大长度（DIP）。结果不会超过它。
    /// </param>
    /// <returns>主轴长度（DIP），不超过 <paramref name="maxLength"/>。</returns>
    public static double EstimateMainAxisLength(
        LyricsVerticalContent content,
        double crossSize,
        double maxLength)
    {
        if (!double.IsFinite(maxLength) || maxLength <= 0)
        {
            return 0;
        }

        // 主行宽度/行高不可用 → 内容还不可知（首帧之前）。
        // 次要行只在「声明有次要行」时才要求其数值可用。
        if (!IsUsable(content.PrimaryWidth, content.PrimaryLineHeight) ||
            (content.HasSecondary && !IsUsable(content.SecondaryWidth, content.SecondaryLineHeight)))
        {
            return Math.Min(FallbackLength, maxLength);
        }

        if (!double.IsFinite(crossSize) || crossSize <= 0)
        {
            return Math.Min(FallbackLength, maxLength);
        }

        var usableWidth = crossSize - WrapSafetyMargin;
        if (usableWidth <= 0)
        {
            // 任务栏窄到连安全余量都吃掉了，按整宽折行。
            usableWidth = crossSize;
        }

        var length = CountRows(content.PrimaryWidth, usableWidth) * content.PrimaryLineHeight;

        if (content.HasSecondary)
        {
            length += content.Gap +
                (CountRows(content.SecondaryWidth, usableWidth) * content.SecondaryLineHeight);
        }

        // 上限用「最大行数 × 主行行高」会把两行行高不同的情况算歪，
        // 直接用 maxLength 夹即可：它是调用方给的硬预算，
        // 而 length 由两段行数×行高相加，本身就是有限值
        //（rows 是 contentWidth/usableWidth 的向上取整，受 contentWidth 有限性约束）。
        return Math.Min(length, maxLength);
    }

    /// <summary>
    /// 折成几行。向上取整：差 0.1 个像素也算一行，否则最后几个字会被切掉。
    ///
    /// <b>这里刻意不做中文禁则（kinsoku）修正。</b> 标点不能出现在行首，
    /// 实际折行会比纯宽度除法多出约一行——这个偏差由
    /// <c>LyricsPanel.EnsureVerticalContentFits</c> 在布局后用**实测高度**
    /// 兜住，而不是在这里猜。
    ///
    /// 为什么不在这里猜：禁则的损失取决于标点出现在哪、每行放几个字，
    /// 是文本内容的函数，不是宽度的函数。本文件是纯算术层，拿不到文本；
    /// 硬塞一个「每行减一个字」的系数会在短句上凭空多算一行
    ///（实测 single 状态从 33.0 涨到 49.5，白占一行高度），
    /// 而在长句上又可能仍然不够。交给实测更可靠。
    /// </summary>
    private static double CountRows(double contentWidth, double usableWidth)
    {
        if (!double.IsFinite(contentWidth) || contentWidth <= 0)
        {
            return 0;
        }

        return Math.Ceiling(contentWidth / usableWidth);
    }

    private static bool IsUsable(double width, double lineHeight) =>
        double.IsFinite(width) && width > 0 &&
        double.IsFinite(lineHeight) && lineHeight > 0;
}
