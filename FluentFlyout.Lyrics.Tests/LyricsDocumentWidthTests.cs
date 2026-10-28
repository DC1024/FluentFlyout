// Copyright (c) 2026 anync
// SPDX-License-Identifier: MIT

using FluentFlyout.Lyrics.Layout;
using FluentFlyout.Lyrics.Models;
using Xunit;

namespace FluentFlyout.Lyrics.Tests;

/// <summary>
/// <see cref="LyricsDocumentWidth"/> 的用例。
///
/// 覆盖重点是两件会让真机出问题的事：
/// 1. **抽样必须覆盖整首**——长句常在副歌，只看开头会低估，副歌一进来胶囊又变宽；
/// 2. **坏测量值不能污染结果**——NaN 会让胶囊宽度变成 NaN，WPF 直接抛异常。
/// </summary>
public class LyricsDocumentWidthTests
{
    /// <summary>按字符数当宽度的假测量函数，1 字符 = 10 DIP。</summary>
    private static double MeasureByLength(string text) => text.Length * 10;

    private static LyricLine Line(string text, string? translation = null) =>
        new(TimeSpan.Zero, text, translation);

    [Fact]
    public void ReturnsZeroWhenDocumentIsNull()
    {
        Assert.Equal(0, LyricsDocumentWidth.MeasureMaxLineWidth(null, MeasureByLength));
    }

    [Fact]
    public void ReturnsZeroWhenDocumentIsEmpty()
    {
        Assert.Equal(0, LyricsDocumentWidth.MeasureMaxLineWidth([], MeasureByLength));
    }

    [Fact]
    public void ReturnsWidestLineAcrossWholeDocument()
    {
        var lines = new[] { Line("短"), Line("中等长度"), Line("这一句是整首最长的") };

        var width = LyricsDocumentWidth.MeasureMaxLineWidth(lines, MeasureByLength);

        Assert.Equal(90, width);
    }

    [Fact]
    public void CountsTranslationWhenMeasurerProvided()
    {
        // 主行 2 字 = 20；翻译 10 字按每字 8 = 80。翻译更长时必须按翻译算，
        // 否则翻译行会溢出视口，滚起来比主行还早。
        var lines = new[] { Line("短句", "0123456789") };

        var width = LyricsDocumentWidth.MeasureMaxLineWidth(
            lines,
            MeasureByLength,
            text => text.Length * 8);

        Assert.Equal(80, width);
    }

    [Fact]
    public void IgnoresTranslationWhenMeasurerOmitted()
    {
        // 单行模式不显示翻译，算进去只会让胶囊白白变宽。
        var lines = new[] { Line("短句", "0123456789") };

        var width = LyricsDocumentWidth.MeasureMaxLineWidth(lines, MeasureByLength);

        Assert.Equal(20, width);
    }

    [Fact]
    public void SkipsBlankAndNullText()
    {
        var lines = new[] { Line(""), Line("   "), Line("有字") };

        var width = LyricsDocumentWidth.MeasureMaxLineWidth(lines, MeasureByLength);

        Assert.Equal(20, width);
    }

    [Fact]
    public void SamplesAcrossWholeDocumentInsteadOfFirstLines()
    {
        // 1000 句，只有最后一句很长。上限 10 句时按等距抽样仍应命中它——
        // 若实现只看前 10 句，这里会返回 10，副歌一到胶囊就会突然变宽。
        var lines = Enumerable.Range(0, 1000)
            .Select(i => Line(i == 999 ? new string('长', 50) : "短"))
            .ToArray();

        var width = LyricsDocumentWidth.MeasureMaxLineWidth(
            lines,
            MeasureByLength,
            maxMeasuredLines: 10);

        Assert.Equal(500, width);
    }

    [Fact]
    public void MeasuresEveryLineWhenUnderLimit()
    {
        var lines = Enumerable.Range(0, 5)
            .Select(i => Line(new string('字', i + 1)))
            .ToArray();

        var width = LyricsDocumentWidth.MeasureMaxLineWidth(lines, MeasureByLength);

        Assert.Equal(50, width);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-1)]
    public void IgnoresNonFiniteOrNegativeMeasurement(double bad)
    {
        var lines = new[] { Line("坏值"), Line("好值好值") };

        var width = LyricsDocumentWidth.MeasureMaxLineWidth(
            lines,
            text => text == "坏值" ? bad : MeasureByLength(text));

        Assert.Equal(40, width);
    }

    [Fact]
    public void ReturnsZeroWhenEveryMeasurementIsBad()
    {
        var lines = new[] { Line("一"), Line("二") };

        var width = LyricsDocumentWidth.MeasureMaxLineWidth(lines, _ => double.NaN);

        Assert.Equal(0, width);
    }

    [Fact]
    public void ZeroLimitMeansMeasureAll()
    {
        // 传 0 / 负数表示「不限制」，不能退化成「一句都不量」。
        var lines = new[] { Line("一"), Line("一二三四五") };

        var width = LyricsDocumentWidth.MeasureMaxLineWidth(
            lines,
            MeasureByLength,
            maxMeasuredLines: 0);

        Assert.Equal(50, width);
    }
}
