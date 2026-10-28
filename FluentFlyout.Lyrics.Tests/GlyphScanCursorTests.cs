// Copyright (c) 2026 anync
// SPDX-License-Identifier: MIT
//
// 本文件为本仓库新增，不源自 TaskbarLyrics。

using FluentFlyout.Lyrics.Layout;
using Xunit;

namespace FluentFlyout.Lyrics.Tests;

/// <summary>
/// 竖排逐字点亮游标的测试。
///
/// 这块算错的表现同样隐蔽：整列偏暗、长句唱到末尾还剩两三格不亮、
/// 拉丁词独占半句的时间——都像「配色不对」而不是「映射算错了」。
/// </summary>
public sealed class GlyphScanCursorTests
{
    // ---------- 边界 ----------

    [Fact]
    public void EmptyRunListMeansNothingToLight()
    {
        var cursor = GlyphScanCursor.Create(0.5, Array.Empty<int>());

        Assert.Equal(0, cursor.TotalCount);
        Assert.Equal(0, cursor.LitCount);
        Assert.Equal(0, cursor.CrossingFraction);
        Assert.False(cursor.IsComplete);
    }

    [Fact]
    public void NullRunListIsTreatedAsEmpty()
    {
        var cursor = GlyphScanCursor.Create(1, null);

        Assert.Equal(0, cursor.TotalCount);
        Assert.False(cursor.IsComplete);
    }

    [Fact]
    public void ProgressZeroLeavesEveryGlyphUnlit()
    {
        var cursor = GlyphScanCursor.Create(0, new[] { 1, 1, 1 });

        Assert.Equal(3, cursor.TotalCount);
        Assert.Equal(0, cursor.LitCount);
        Assert.Equal(0, cursor.CrossingFraction);
        Assert.False(cursor.IsComplete);
    }

    [Fact]
    public void ProgressOneLightsEveryGlyph()
    {
        var cursor = GlyphScanCursor.Create(1, new[] { 1, 1, 1 });

        Assert.Equal(3, cursor.LitCount);
        Assert.True(cursor.IsComplete);
    }

    [Fact]
    public void ProgressBeyondRangeIsClamped()
    {
        Assert.True(GlyphScanCursor.Create(2.5, new[] { 1, 1 }).IsComplete);
        Assert.Equal(0, GlyphScanCursor.Create(-3, new[] { 1, 1 }).LitCount);
    }

    /// <summary>
    /// NaN / 无穷来自上游算错（除零、缺时长）。此时不能整列乱亮，
    /// 必须退化成「一格都没唱」。
    /// </summary>
    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void NonFiniteProgressFallsBackToTheStart(double progress)
    {
        var cursor = GlyphScanCursor.Create(progress, new[] { 1, 1 });

        Assert.Equal(0, cursor.LitCount);
        Assert.Equal(0, cursor.CrossingFraction);
    }

    // ---------- 逐格推进 ----------

    [Fact]
    public void HalfwayThroughTheSecondGlyphLightsOneAndAHalf()
    {
        // 四个单字格，进度 0.375 → 已唱 1.5 个字。
        var cursor = GlyphScanCursor.Create(0.375, new[] { 1, 1, 1, 1 });

        Assert.Equal(1, cursor.LitCount);
        Assert.Equal(0.5, cursor.CrossingFraction, precision: 6);
    }

    /// <summary>
    /// 拉丁整词是一格（见 <c>VerticalRunSplitter</c>）：进度按**字数**分配，
    /// 所以六字母的词要走完六个字的时间，而不是按它三倍于汉字的宽度
    /// 独占更久，也不是一次翻牌。
    /// </summary>
    [Fact]
    public void WideLatinRunAbsorbsProgressInProportionToItsCharacterCount()
    {
        // 一格汉字 + 一格六字母的词，共 7 个字。进度 4/7 → 已唱 4 个字，
        // 即「汉字」唱完 + 词里唱掉 3 个字母 → 该格点亮一半。
        var cursor = GlyphScanCursor.Create(4d / 7d, new[] { 1, 6 });

        Assert.Equal(1, cursor.LitCount);
        Assert.Equal(0.5, cursor.CrossingFraction, precision: 6);
    }

    /// <summary>
    /// 零长的格不占时间，应当直接算作已点亮，不能让游标卡在它身上
    /// （否则它后面的格永远等不到点亮）。
    /// </summary>
    [Fact]
    public void ZeroLengthRunsAreSkippedWithoutStallingTheCursor()
    {
        var cursor = GlyphScanCursor.Create(0, new[] { 0, 1 });

        Assert.Equal(1, cursor.LitCount);
        Assert.Equal(0, cursor.CrossingFraction);
    }

    [Fact]
    public void ZeroLengthRunsDoNotProduceProgress()
    {
        var cursor = GlyphScanCursor.Create(0.5, new[] { 0, 0 });

        Assert.Equal(2, cursor.TotalCount);
        Assert.Equal(0, cursor.LitCount);
        Assert.False(cursor.IsComplete);
    }

    /// <summary>
    /// emoji 是代理对，UTF-16 长度算 2——与 <c>CalculateWordScanProgress</c>
    /// 的 <c>line.Text.Length</c> 同口径，于是「唱到一半」真的是这一格的一半。
    /// </summary>
    [Fact]
    public void SurrogatePairsCountAsTwoUnitsLikeTheProgressSide()
    {
        var cursor = GlyphScanCursor.Create(0.5, new[] { 2 });

        Assert.Equal(0, cursor.LitCount);
        Assert.Equal(0.5, cursor.CrossingFraction, precision: 6);
    }

    // ---------- 单调性 ----------

    /// <summary>
    /// 进度单调增时已点亮的格数只能增不能减，且跨界比例恒在 0..1。
    /// 这条是「唱到一半往回跳」这类 bug 的总闸。
    /// </summary>
    [Fact]
    public void LitCountNeverGoesBackwards()
    {
        int[] lengths = { 2, 5, 1, 3, 1 };
        var previous = -1;

        for (var step = 0; step <= 100; step++)
        {
            var cursor = GlyphScanCursor.Create(step / 100d, lengths);

            Assert.True(
                cursor.LitCount >= previous,
                $"progress {step / 100d}: LitCount {cursor.LitCount} < previous {previous}");
            Assert.InRange(cursor.CrossingFraction, 0, 1);

            previous = cursor.LitCount;
        }

        Assert.Equal(5, previous);
    }
}
