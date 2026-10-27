// Copyright (c) 2026 anync
// SPDX-License-Identifier: MIT
//
// 本文件为本仓库新增，不源自 TaskbarLyrics。

using FluentFlyout.Lyrics.Layout;
using Xunit;

namespace FluentFlyout.Lyrics.Tests;

/// <summary>
/// 竖向任务栏主轴长度估算的测试。
///
/// 这类「估算」的 bug 很难靠截图发现——偏小 30% 在渲染图上看起来
/// 只是「字少了点」，容易被当成正常。所以把边界全部锁在单测里。
/// </summary>
public sealed class LyricsVerticalLayoutTests
{
    private const double LineHeight = 14.95;   // 13px × 1.15
    private const double CrossSize = 40;       // 标准任务栏宽度
    private const double MaxLength = 400;

    // ---------- 折行行数 ----------

    [Fact]
    public void ShortContentFitsInOneRow()
    {
        // 内容比可用宽度还短 → 一行。
        var length = LyricsVerticalLayout.EstimateMainAxisLength(
            contentWidth: 30, CrossSize, LineHeight, MaxLength);

        Assert.Equal(LineHeight, length, precision: 6);
    }

    [Fact]
    public void ContentExactlyFitsUsableWidthUsesOneRow()
    {
        var usable = CrossSize - LyricsVerticalLayout.WrapSafetyMargin;

        var length = LyricsVerticalLayout.EstimateMainAxisLength(
            usable, CrossSize, LineHeight, MaxLength);

        Assert.Equal(LineHeight, length, precision: 6);
    }

    [Fact]
    public void OnePixelOverUsableWidthWrapsToTwoRows()
    {
        var usable = CrossSize - LyricsVerticalLayout.WrapSafetyMargin;

        var length = LyricsVerticalLayout.EstimateMainAxisLength(
            usable + 0.1, CrossSize, LineHeight, MaxLength);

        // 多 0.1 也要算第二行——向上取整，否则最后几个字被切掉。
        Assert.Equal(2 * LineHeight, length, precision: 6);
    }

    [Theory]
    [InlineData(30, 1)]    // < 36：一行
    [InlineData(36, 1)]    // = 36：恰好一行
    [InlineData(37, 2)]    // 略超：两行
    [InlineData(72, 2)]    // = 2×36：两行
    [InlineData(73, 3)]    // 略超：三行
    [InlineData(108, 3)]   // = 3×36：三行
    public void RowCountScalesWithContentWidth(double contentWidth, int expectedRows)
    {
        var length = LyricsVerticalLayout.EstimateMainAxisLength(
            contentWidth, CrossSize, LineHeight, MaxLength);

        Assert.Equal(expectedRows * LineHeight, length, precision: 6);
    }

    // ---------- 上限与下限 ----------

    [Fact]
    public void NeverExceedsMaxLength()
    {
        // 一句 2000 DIP 宽的长歌词，主轴只给 100。
        var length = LyricsVerticalLayout.EstimateMainAxisLength(
            contentWidth: 2000, CrossSize, LineHeight, maxLength: 100);

        Assert.True(length <= 100, $"长度 {length} 超过上限 100");
    }

    [Fact]
    public void VeryLongContentDoesNotOverflow()
    {
        // rows 极大时不能溢出成 Infinity。
        var length = LyricsVerticalLayout.EstimateMainAxisLength(
            contentWidth: 1e12, CrossSize, LineHeight, MaxLength);

        Assert.True(double.IsFinite(length));
        Assert.True(length <= MaxLength);
    }

    [Fact]
    public void SmallMaxLengthWinsOverMinimumLength()
    {
        // 主轴只给 30，比 MinimumLength(64) 还小 → 取 30，不能反过来撑大。
        var length = LyricsVerticalLayout.EstimateMainAxisLength(
            contentWidth: 200, CrossSize, LineHeight, maxLength: 30);

        Assert.Equal(30, length);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    [InlineData(double.NaN)]
    public void InvalidMaxLengthReturnsZero(double maxLength)
    {
        // 主轴长度非法说明调用方还没拿到任务栏矩形，
        // 返回 0 让上层走自己的兜底路径，不要瞎猜。
        var length = LyricsVerticalLayout.EstimateMainAxisLength(
            contentWidth: 200, CrossSize, LineHeight, maxLength);

        Assert.Equal(0, length);
    }

    // ---------- 脏输入 ----------

    [Theory]
    [InlineData(0, 40, 15)]
    [InlineData(-5, 40, 15)]
    [InlineData(double.NaN, 40, 15)]
    [InlineData(200, 0, 15)]
    [InlineData(200, -1, 15)]
    [InlineData(200, 40, 0)]
    [InlineData(200, 40, double.NaN)]
    public void InvalidInputsFallBackToBoundedDefault(double contentWidth, double crossSize, double lineHeight)
    {
        var length = LyricsVerticalLayout.EstimateMainAxisLength(
            contentWidth, crossSize, lineHeight, MaxLength);

        // 兜底值必须有限、非负、不超上限。
        Assert.True(double.IsFinite(length), $"长度 {length} 非有限");
        Assert.True(length >= 0);
        Assert.True(length <= MaxLength);
        Assert.Equal(Math.Min(LyricsVerticalLayout.FallbackLength, MaxLength), length);
    }

    [Fact]
    public void FallbackIsClampedByMaxLength()
    {
        var length = LyricsVerticalLayout.EstimateMainAxisLength(
            contentWidth: 0, CrossSize, LineHeight, maxLength: 50);

        // 兜底 120 超过上限 50 → 取 50。
        Assert.Equal(50, length);
    }

    // ---------- 单调性 ----------

    [Fact]
    public void LongerContentNeverShrinksLength()
    {
        double previous = 0;
        foreach (var width in new[] { 20d, 40d, 80d, 160d, 320d })
        {
            var length = LyricsVerticalLayout.EstimateMainAxisLength(
                width, CrossSize, LineHeight, MaxLength);

            Assert.True(length >= previous,
                $"内容宽 {width} 时长度 {length} 小于上一档 {previous}");
            previous = length;
        }
    }

    [Fact]
    public void NarrowerTaskbarNeverShrinksLength()
    {
        // 任务栏越窄，折行越多，需要的纵向空间只会更大。
        double previous = 0;
        foreach (var crossSize in new[] { 60d, 50d, 40d, 32d })
        {
            var length = LyricsVerticalLayout.EstimateMainAxisLength(
                contentWidth: 200, crossSize, LineHeight, MaxLength);

            Assert.True(length >= previous,
                $"任务栏宽 {crossSize} 时长度 {length} 小于上一档 {previous}");
            previous = length;
        }
    }

    // ---------- 真实尺寸 ----------

    [Fact]
    public void TypicalLyricLineOnStandardVerticalTaskbar()
    {
        // 一句 20 字的歌词，13px 字号约 260 DIP 宽；
        // 竖向任务栏 40 DIP 宽，可用 36。
        var length = LyricsVerticalLayout.EstimateMainAxisLength(
            contentWidth: 260, crossSize: 40, lineHeight: LineHeight, maxLength: 800);

        // 260 / 36 = 7.22 → 8 行 → 8 × 14.95 = 119.6
        Assert.Equal(8 * LineHeight, length, precision: 6);

        // 这个量在 1080p 屏（主轴约 1040 DIP）上只占 11%，
        // 不会盖住任务栏中部的应用按钮。
        Assert.True(length < 800 * 0.2);
    }

    [Fact]
    public void EstimateStaysWithinSaneRangeForWideTaskbar()
    {
        // 用户把任务栏调宽到 60 DIP（平板模式），折行变少。
        var length = LyricsVerticalLayout.EstimateMainAxisLength(
            contentWidth: 260, crossSize: 60, lineHeight: LineHeight, maxLength: 800);

        // 260 / 56 = 4.64 → 5 行 → 74.75
        Assert.Equal(5 * LineHeight, length, precision: 6);
    }
}
