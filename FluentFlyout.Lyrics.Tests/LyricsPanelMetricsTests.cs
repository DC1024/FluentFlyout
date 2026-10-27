// Copyright (c) 2026 anync
// SPDX-License-Identifier: MIT
//
// 本文件为本仓库新增，不源自 TaskbarLyrics。

using FluentFlyout.Lyrics.Layout;
using Xunit;

namespace FluentFlyout.Lyrics.Tests;

/// <summary>
/// 歌词面板度量的测试。
///
/// 重点是「装不下时怎么降级」——这块逻辑如果错了，
/// 用户看到的是文字被裁一半或者整块消失，比字号偏小严重得多。
/// </summary>
public sealed class LyricsPanelMetricsTests
{
    // ---------- 双行基线 ----------

    [Fact]
    public void DefaultTaskbarHeightProducesTwoLines()
    {
        var metrics = LyricsPanelMetrics.Create(availableHeight: 40, preferredFontSize: 13);

        Assert.False(metrics.IsSingleLine);
        Assert.Equal(13, metrics.FontSize);
        Assert.Equal(13 * 1.15, metrics.LineHeight, precision: 6);
        Assert.Equal(2, metrics.VisibleLineCount);

        // 两行 + 间距必须装进 40。
        Assert.True(metrics.TotalHeight <= 40, $"总高 {metrics.TotalHeight} 超过 40");
    }

    [Fact]
    public void TotalHeightIncludesGapInTwoLineMode()
    {
        var metrics = LyricsPanelMetrics.Create(40, 13);

        var expected = (metrics.LineHeight * 2) + LyricsPanelMetrics.LineGap;
        Assert.Equal(expected, metrics.TotalHeight, precision: 6);
        Assert.Equal(LyricsPanelMetrics.LineGap, metrics.Gap);
    }

    [Fact]
    public void TranslationFontIsSmallerThanPrimary()
    {
        var metrics = LyricsPanelMetrics.Create(40, 13);

        Assert.True(metrics.TranslationFontSize < metrics.FontSize);
        Assert.Equal(metrics.FontSize * 0.86, metrics.TranslationFontSize, precision: 6);
    }

    [Fact]
    public void ShowTranslationIsHonouredInTwoLineMode()
    {
        var on = LyricsPanelMetrics.Create(40, 13, showTranslation: true);
        var off = LyricsPanelMetrics.Create(40, 13, showTranslation: false);

        Assert.True(on.ShowTranslation);
        Assert.False(off.ShowTranslation);
        // 关掉翻译不影响行数与字号。
        Assert.Equal(on.FontSize, off.FontSize);
        Assert.Equal(on.VisibleLineCount, off.VisibleLineCount);
    }

    // ---------- 单行降级 ----------

    [Fact]
    public void SmallTaskbarForcesSingleLine()
    {
        var metrics = LyricsPanelMetrics.Create(availableHeight: 28, preferredFontSize: 13);

        Assert.True(metrics.IsSingleLine);
        Assert.Equal(1, metrics.VisibleLineCount);
        Assert.Equal(0, metrics.Gap);
        Assert.Equal(metrics.LineHeight, metrics.TotalHeight, precision: 6);
    }

    [Fact]
    public void SingleLineModeDropsTranslationEvenIfRequested()
    {
        var metrics = LyricsPanelMetrics.Create(28, 13, showTranslation: true);

        Assert.True(metrics.IsSingleLine);
        Assert.False(metrics.ShowTranslation);
    }

    [Fact]
    public void ForceSingleLineOverridesAvailableHeight()
    {
        // 高度够放两行，但调用方明确要求单行（例如用户设置）。
        var metrics = LyricsPanelMetrics.Create(40, 13, forceSingleLine: true);

        Assert.True(metrics.IsSingleLine);
    }

    [Fact]
    public void LargeFontSizeDegradesToSingleLine()
    {
        // 用户把字号调到 20，40px 装不下两行 → 自动降级单行，
        // 而不是把两行都裁掉一半。
        var metrics = LyricsPanelMetrics.Create(40, preferredFontSize: 20);

        Assert.True(metrics.IsSingleLine);
        Assert.True(metrics.TotalHeight <= 40);
    }

    [Fact]
    public void SingleLineFontIsScaledUp()
    {
        // 单行有余量时应放大，否则 28px 任务栏里字太小。
        var metrics = LyricsPanelMetrics.Create(28, preferredFontSize: 13);

        Assert.True(metrics.FontSize > 13, $"单行字号应放大，实际 {metrics.FontSize}");
        Assert.True(metrics.TotalHeight <= 28);
    }

    [Fact]
    public void SingleLineFontIsCappedByAvailableHeight()
    {
        // 高度 14 时放大倍数会撞上限，不能超出。
        var metrics = LyricsPanelMetrics.Create(14, preferredFontSize: 13);

        Assert.True(metrics.IsSingleLine);
        Assert.True(metrics.TotalHeight <= 14 + 1e-9,
            $"总高 {metrics.TotalHeight} 应 ≤ 14");
    }

    // ---------- 边界与脏数据 ----------

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidHeightFallsBackToDefault(double height)
    {
        var metrics = LyricsPanelMetrics.Create(height, 13);

        // 退到 40 的双行布局，而不是抛异常或产生 NaN。
        Assert.False(metrics.IsSingleLine);
        Assert.False(double.IsNaN(metrics.FontSize));
        Assert.True(metrics.FontSize > 0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(double.NaN)]
    public void InvalidFontSizeFallsBackToDefault(double fontSize)
    {
        var metrics = LyricsPanelMetrics.Create(40, fontSize);

        Assert.Equal(13, metrics.FontSize);
    }

    [Fact]
    public void FontSizeHasLowerBound()
    {
        // 高度极小（例如任务栏被压到 10px）时字号不能无限小。
        var metrics = LyricsPanelMetrics.Create(10, 13);

        Assert.True(metrics.FontSize >= 9, $"字号 {metrics.FontSize} 低于下限");
    }

    [Fact]
    public void MetricsNeverProduceNaN()
    {
        foreach (var height in new[] { 10d, 28d, 40d, 56d, 100d })
        {
            foreach (var fontSize in new[] { 8d, 13d, 20d, 32d })
            {
                var metrics = LyricsPanelMetrics.Create(height, fontSize);

                Assert.False(double.IsNaN(metrics.FontSize));
                Assert.False(double.IsNaN(metrics.LineHeight));
                Assert.False(double.IsNaN(metrics.TranslationFontSize));
                Assert.False(double.IsNaN(metrics.TotalHeight));
                Assert.True(metrics.FontSize > 0);
            }
        }
    }

    // ---------- 单调性 ----------

    [Fact]
    public void TallerTaskbarNeverShrinksFont()
    {
        // 高度增加时字号不应变小——这是「布局稳定」的基本要求，
        // 否则用户切任务栏尺寸会看到文字忽大忽小。
        double previous = 0;
        foreach (var height in new[] { 40d, 48d, 56d, 64d, 80d })
        {
            var metrics = LyricsPanelMetrics.Create(height, preferredFontSize: 13);
            Assert.True(metrics.FontSize >= previous,
                $"高度 {height} 时字号 {metrics.FontSize} 小于上一档 {previous}");
            previous = metrics.FontSize;
        }
    }

    [Fact]
    public void TotalHeightNeverExceedsAvailableHeight()
    {
        foreach (var height in new[] { 14d, 20d, 28d, 36d, 40d, 56d, 80d })
        {
            var metrics = LyricsPanelMetrics.Create(height, preferredFontSize: 13);

            // 字号有 9 的下限，极小高度下允许略超，但不超过 20%。
            Assert.True(metrics.TotalHeight <= height * 1.2,
                $"高度 {height} 下总高 {metrics.TotalHeight} 超出过多");
        }
    }
}
