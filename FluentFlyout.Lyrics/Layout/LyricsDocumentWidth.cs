// Copyright (c) 2026 anync
// SPDX-License-Identifier: MIT
//
// 本文件为本仓库新增，不源自 TaskbarLyrics。

using FluentFlyout.Lyrics.Models;

namespace FluentFlyout.Lyrics.Layout;

/// <summary>
/// 从整首歌词里挑出「最宽的一句」，作为歌词小组件在**整首歌内固定**的宽度。
/// </summary>
/// <remarks>
/// <para>
/// 为什么不按当前行量宽度：小组件是一个带模糊封面背景的胶囊，宽度跟着每一句
/// 伸缩时，胶囊会随换行不停胀缩，用户看到的是一条一直在动的色块。
/// 更难受的是长句：它先按旧宽度开始滚动，一秒后胶囊才变宽、句子不再溢出，
/// 那次滚动白跑一趟，视觉上就是「抖一下」。
/// </para>
/// <para>
/// 所以宽度必须在**整首歌词确定的那一刻**一次算好，歌内不再变。
/// 这里只做纯计算，测量函数由调用方注入（字体、DPI 等 WPF 细节留在外层），
/// 于是这条策略可以被单测锁死。
/// </para>
/// </remarks>
public static class LyricsDocumentWidth
{
    /// <summary>
    /// 最多测量多少句。
    ///
    /// <para>
    /// 正常歌词几十到一百多句，不会触顶。设上限是为了防住异常歌词文件
    /// （几千行）在 UI 线程上一次性量太久——那会掉一帧，而掉帧正好发生在
    /// 换歌的瞬间，最容易被用户看见。
    /// </para>
    /// </summary>
    public const int DefaultMaxMeasuredLines = 600;

    /// <summary>
    /// 量出整首歌词里最宽的一句（DIP）。
    /// </summary>
    /// <param name="lines">整首歌词。null 或空返回 0，调用方据此退回按当前行量。</param>
    /// <param name="measureText">主行文本的测量函数。</param>
    /// <param name="measureTranslation">
    /// 翻译行的测量函数。翻译字号比主行小，不能用同一个函数；
    /// 传 null 表示不把翻译计入宽度（单行模式下翻译根本不显示）。
    /// </param>
    /// <param name="maxMeasuredLines">最多测多少句，超过则等距抽样。</param>
    /// <returns>最大宽度；没有任何可测文本时返回 0。</returns>
    public static double MeasureMaxLineWidth(
        IReadOnlyList<LyricLine>? lines,
        Func<string, double> measureText,
        Func<string, double>? measureTranslation = null,
        int maxMeasuredLines = DefaultMaxMeasuredLines)
    {
        ArgumentNullException.ThrowIfNull(measureText);

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
                max = Math.Max(max, MeasureLine(lines[i], measureText, measureTranslation));
            }

            return Normalize(max);
        }

        // 超上限时按**等距抽样，且必定包含首尾两句**。
        //
        // 不能只取前 N 句：长句常集中在副歌（往往靠后），只看开头会系统性低估，
        // 结果就是副歌一进来胶囊又变宽——正是要修的那个现象。
        // 也不能从 0 开始按固定步长跳：末句永远取不到，而末句常常就是最长的那句。
        for (var k = 0; k < limit; k++)
        {
            var index = limit == 1
                ? 0
                : (int)((long)k * (lines.Count - 1) / (limit - 1));

            max = Math.Max(max, MeasureLine(lines[index], measureText, measureTranslation));
        }

        return Normalize(max);
    }

    /// <summary>量一句（主行 + 翻译），取两者较大值。</summary>
    private static double MeasureLine(
        LyricLine? line,
        Func<string, double> measureText,
        Func<string, double>? measureTranslation)
    {
        if (line is null)
        {
            return 0;
        }

        var width = SafeMeasure(measureText, line.Text);

        if (measureTranslation is not null)
        {
            width = Math.Max(width, SafeMeasure(measureTranslation, line.Translation));
        }

        return width;
    }

    /// <summary>把结果收敛到「有限的正数，否则 0」。</summary>
    private static double Normalize(double width) =>
        double.IsFinite(width) && width > 0 ? width : 0;

    /// <summary>
    /// 调一次测量函数，把空文本、NaN、负数统一归零。
    ///
    /// <para>
    /// 必须挡 NaN：<see cref="Math.Max(double, double)"/> 遇到 NaN 会返回 NaN，
    /// 一个坏值就会污染整条链，最后表现成「胶囊宽度是 NaN」——WPF 会直接抛异常。
    /// </para>
    /// </summary>
    private static double SafeMeasure(Func<string, double> measure, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        var width = measure(text);
        return double.IsFinite(width) && width > 0 ? width : 0;
    }
}
