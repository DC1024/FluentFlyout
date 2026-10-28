// Copyright (c) 2026 anync
// SPDX-License-Identifier: MIT
//
// 本文件为本仓库新增，不源自 TaskbarLyrics。

using FluentFlyout.Lyrics.Models;

namespace FluentFlyout.Lyrics.Layout;

/// <summary>
/// 从整首歌词里挑出「竖排最长的一列」，作为歌词小组件在**整首歌内固定**的
/// 主轴长度（屏幕纵向）。
/// </summary>
/// <remarks>
/// <para>
/// 与 <see cref="LyricsDocumentWidth"/> 完全对称，只是把「宽度」换成「长度」：
/// 横向协商宽度、竖向协商长度，各自都要有一个「整首歌一次算好」的量。
/// </para>
/// <para>
/// <b>为什么必须一次算好。</b>竖排的长度原先是按**当前行**算的
/// （<c>LyricsPanel.VerticalContentLength</c> 直接返回本行的逐字步进总和），
/// 于是每换一句小组件就换一个长度：短句缩、长句伸。真机反馈是「长度随歌词
/// 长度变化」，看起来像一条一直在呼吸的色块。横向早就没有这个问题
/// （<see cref="LyricsDocumentWidth"/>），竖向漏了这一层。
/// </para>
/// <para>
/// 这里只做纯计算：切分交给 <see cref="VerticalRunSplitter"/>，
/// 测量函数由调用方注入（字体、DPI 留在 WPF 层），于是这套策略能被单测锁死。
/// </para>
/// </remarks>
public static class VerticalDocumentLength
{
    /// <summary>
    /// 最多测量多少句。与 <see cref="LyricsDocumentWidth.DefaultMaxMeasuredLines"/>
    /// 取同一个值：两条路径在同一时刻、同一份文档上跑，上限不同会让
    /// 「横向已经定了宽、竖向还在量」这种半稳态出现。
    /// </summary>
    public const int DefaultMaxMeasuredLines = LyricsDocumentWidth.DefaultMaxMeasuredLines;

    /// <summary>
    /// 一句文本竖排成一列后占用的主轴长度（DIP）。
    /// </summary>
    /// <param name="text">一句歌词（或一句译文）。</param>
    /// <param name="measureRun">
    /// 量一个排版格的步进。必须是**实测**结果——汉字 1em、空格 0.33em、
    /// 拉丁整词按词宽，一律按字号推算会把空格量成一个豁口。
    /// 零宽字符与缺字由这个委托自行兜底（面板里给 <c>0.2 × 字号</c>）。
    /// </param>
    /// <returns>各格步进之和；无文本时返回 0。</returns>
    public static double MeasureColumnLength(string? text, Func<string, double> measureRun)
    {
        ArgumentNullException.ThrowIfNull(measureRun);

        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        var total = 0.0;

        foreach (var run in VerticalRunSplitter.Split(text))
        {
            var advance = measureRun(run.Text);

            // 只挡 NaN / 负数：0 是合法的（零宽字符），不必特殊处理。
            if (double.IsFinite(advance) && advance > 0)
            {
                total += advance;
            }
        }

        return Normalize(total);
    }

    /// <summary>
    /// 量出整首歌词里最长的一列（DIP）。
    /// </summary>
    /// <param name="lines">整首歌词。null 或空返回 0，调用方据此退回按当前行量。</param>
    /// <param name="measurePrimaryRun">主行每个排版格的步进。</param>
    /// <param name="measureTranslationRun">
    /// 译文每个排版格的步进。翻译字号比主行小，不能用同一个委托；
    /// 传 null 表示不把译文计入长度（单行模式、用户关掉翻译、
    /// 或短边放不下第二列时，译文根本不画）。
    /// </param>
    /// <param name="maxMeasuredLines">最多测多少句，超过则等距抽样。</param>
    /// <returns>最长一列的长度；没有任何可测文本时返回 0。</returns>
    public static double MeasureMaxColumnLength(
        IReadOnlyList<LyricLine>? lines,
        Func<string, double> measurePrimaryRun,
        Func<string, double>? measureTranslationRun = null,
        int maxMeasuredLines = DefaultMaxMeasuredLines)
    {
        ArgumentNullException.ThrowIfNull(measurePrimaryRun);

        if (lines is null || lines.Count == 0)
        {
            return 0;
        }

        var limit = maxMeasuredLines > 0 ? maxMeasuredLines : lines.Count;
        var max = 0.0;

        if (lines.Count <= limit)
        {
            for (var i = 0; i < lines.Count; i++)
            {
                max = Math.Max(max, MeasureLine(lines[i], measurePrimaryRun, measureTranslationRun));
            }

            return Normalize(max);
        }

        // 超上限时等距抽样且必定包含首尾：长句常集中在副歌（靠后），
        // 只取前 N 句会系统性低估，副歌一进来小组件又会变长。
        for (var k = 0; k < limit; k++)
        {
            var index = limit == 1
                ? 0
                : (int)((long)k * (lines.Count - 1) / (limit - 1));

            max = Math.Max(max, MeasureLine(lines[index], measurePrimaryRun, measureTranslationRun));
        }

        return Normalize(max);
    }

    /// <summary>量一句（主行 + 译文），取两者较大值。</summary>
    private static double MeasureLine(
        LyricLine? line,
        Func<string, double> measurePrimaryRun,
        Func<string, double>? measureTranslationRun)
    {
        if (line is null)
        {
            return 0;
        }

        var length = MeasureColumnLength(line.Text, measurePrimaryRun);

        if (measureTranslationRun is not null)
        {
            length = Math.Max(length, MeasureColumnLength(line.Translation, measureTranslationRun));
        }

        return length;
    }

    /// <summary>把结果收敛到「有限的正数，否则 0」。</summary>
    private static double Normalize(double length) =>
        double.IsFinite(length) && length > 0 ? length : 0;
}
