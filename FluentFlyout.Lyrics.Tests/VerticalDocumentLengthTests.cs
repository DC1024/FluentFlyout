// Copyright (c) 2026 anync
// SPDX-License-Identifier: MIT

using FluentFlyout.Lyrics.Layout;
using FluentFlyout.Lyrics.Models;
using Xunit;

namespace FluentFlyout.Lyrics.Tests;

/// <summary>
/// <see cref="VerticalDocumentLength"/> 的用例。
///
/// 这一组用例守的是真机反馈的那条缺陷：**竖排小组件的长度会随歌词长度变化**。
/// 长度必须只由「整首歌最长的一列」决定，与当前唱到哪一句无关。
/// 顺带守两条会让真机出问题的边界：
/// 1. 抽样必须覆盖整首（长句常在副歌，只看开头会低估）；
/// 2. 坏测量值不能污染结果（NaN 会让面板宽度变成 NaN，WPF 直接抛异常）。
/// </summary>
public class VerticalDocumentLengthTests
{
    /// <summary>按字符数当步进的假测量函数，1 字符 = 10 DIP。</summary>
    private static double MeasureByLength(string text) => text.Length * 10;

    private static LyricLine Line(string text, string? translation = null) =>
        new(TimeSpan.Zero, text, translation);

    [Fact]
    public void ReturnsZeroWhenDocumentIsNull()
    {
        Assert.Equal(0, VerticalDocumentLength.MeasureMaxColumnLength(null, MeasureByLength));
    }

    [Fact]
    public void ReturnsZeroWhenDocumentIsEmpty()
    {
        Assert.Equal(0, VerticalDocumentLength.MeasureMaxColumnLength([], MeasureByLength));
    }

    [Fact]
    public void ReturnsLongestColumnAcrossWholeDocument()
    {
        // 竖排一列的长度就是各格步进之和，所以「最长的句子」= 最长的列。
        var lines = new[] { Line("短"), Line("中等长度"), Line("这一句是整首最长的") };

        var length = VerticalDocumentLength.MeasureMaxColumnLength(lines, MeasureByLength);

        Assert.Equal(90, length);
    }

    [Fact]
    public void LengthDoesNotDependOnWhichLineIsCurrent()
    {
        // 这正是真机报的现象：同一首歌里换一句，长度不能跟着变。
        // 纯函数层面它只吃整首文档，所以「当前行」根本不在入参里——
        // 这条用例是给未来改动设的护栏：谁想把当前行塞进签名，先看这里。
        var lines = new[] { Line("短句"), Line("这一句明显要长得多得多") };

        var first = VerticalDocumentLength.MeasureMaxColumnLength(lines, MeasureByLength);
        var second = VerticalDocumentLength.MeasureMaxColumnLength(lines, MeasureByLength);

        Assert.Equal(first, second);
        Assert.Equal(110, first);
    }

    [Fact]
    public void CountsTranslationWhenMeasurerProvided()
    {
        // 主行 2 字 = 20；译文 25 个字符 = 250。译文更长时必须按译文算，
        // 否则第二列会被裁掉尾部。
        var lines = new[] { Line("短句", "a much longer translation") };

        var withTranslation = VerticalDocumentLength.MeasureMaxColumnLength(
            lines, MeasureByLength, MeasureByLength);

        Assert.Equal(250, withTranslation);
        Assert.Equal(20, VerticalDocumentLength.MeasureMaxColumnLength(lines, MeasureByLength));
    }

    [Fact]
    public void SplitsIntoLatinWordRunsWithoutChangingTotal()
    {
        // 切分会把 "screen" 合成一格、把空格单独成格，但步进之和必须
        // 与「逐个字素相加」一致——否则测量的长度和排出来的列会差几个字。
        var runLengths = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["screen"] = 42,
            [" "] = 4,
            ["看"] = 13,
        };

        double MeasureRun(string run) => runLengths.TryGetValue(run, out var w) ? w : run.Length * 13;

        var length = VerticalDocumentLength.MeasureColumnLength("看 screen 看", MeasureRun);

        // "看"(13) + 空格(4) + "screen"(42) + 空格(4) + "看"(13)
        Assert.Equal(76, length);
    }

    [Fact]
    public void SamplesEvenlyAndIncludesTheLastLineWhenOverLimit()
    {
        // 600 句里最后一句最长。抽样若漏掉末句，副歌一进来小组件就会变长。
        var lines = new List<LyricLine>();
        for (var i = 0; i < 599; i++)
        {
            lines.Add(Line("短句"));
        }

        lines.Add(Line(new string('长', 200)));

        var length = VerticalDocumentLength.MeasureMaxColumnLength(
            lines, MeasureByLength, maxMeasuredLines: 16);

        Assert.Equal(2000, length);
    }

    [Fact]
    public void IgnoresNonFiniteAndNegativeAdvances()
    {
        // NaN 一旦混进求和，整条链都是 NaN，WPF 写宽度时会抛异常。
        var calls = 0;
        double Measure(string text)
        {
            calls++;
            return calls switch
            {
                1 => double.NaN,
                2 => -5,
                _ => 10,
            };
        }

        var length = VerticalDocumentLength.MeasureColumnLength("三个字", Measure);

        Assert.Equal(10, length);
    }

    [Fact]
    public void ReturnsZeroWhenEveryAdvanceIsUnusable()
    {
        var length = VerticalDocumentLength.MeasureColumnLength("三字", _ => 0);

        Assert.Equal(0, length);
    }

    [Fact]
    public void SkipsEmptyLinesAndTranslations()
    {
        var lines = new[] { Line(string.Empty), Line("  "), Line("有效") };

        var length = VerticalDocumentLength.MeasureMaxColumnLength(lines, MeasureByLength);

        // "  " 也被切成一格空格，仍会被量到；这里只保证空串不参与且结果有限。
        Assert.True(length >= 20);
        Assert.True(double.IsFinite(length));
    }
}
