// Copyright (c) 2026 anync
// SPDX-License-Identifier: MIT
//
// 本文件为本仓库新增，不源自 TaskbarLyrics。

using FluentFlyout.Lyrics.Layout;
using Xunit;

namespace FluentFlyout.Lyrics.Tests;

/// <summary>
/// 长句滚动几何的测试。
///
/// 核心关注两点：
/// 1. 不该滚的时候绝不能滚（短句滚动会让整句一直在动，比截断更难读）；
/// 2. 滚动偏移必须单调、不跳变（跳变表现为「突然闪一下」）。
/// </summary>
public sealed class ScrollGeometryTests
{
    // ---------- 不需要滚动的情形 ----------

    [Fact]
    public void ShortTextDoesNotScroll()
    {
        var geo = ScrollGeometry.Create(0.5, textWidth: 100, viewportWidth: 200, lineDurationSeconds: 5);

        Assert.False(geo.NeedsScroll);
        Assert.Equal(0, geo.OffsetX);
        Assert.Equal(0, geo.ScrollDistance);
    }

    [Fact]
    public void TextExactlyFittingViewportDoesNotScroll()
    {
        var geo = ScrollGeometry.Create(0.5, 200, 200, 5);

        Assert.False(geo.NeedsScroll);
    }

    [Fact]
    public void DisabledNeverScrolls()
    {
        var geo = ScrollGeometry.Create(0.9, textWidth: 1000, viewportWidth: 200,
            lineDurationSeconds: 10, enabled: false);

        Assert.False(geo.NeedsScroll);
        Assert.Equal(0, geo.OffsetX);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    [InlineData(double.NaN)]
    public void InvalidTextWidthDoesNotScroll(double width)
    {
        var geo = ScrollGeometry.Create(0.5, width, 200, 5);

        Assert.False(geo.NeedsScroll);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-50)]
    [InlineData(double.NaN)]
    public void InvalidViewportWidthDoesNotScroll(double viewport)
    {
        var geo = ScrollGeometry.Create(0.5, 500, viewport, 5);

        Assert.False(geo.NeedsScroll);
    }

    // ---------- 滚动距离 ----------

    [Fact]
    public void ScrollDistanceIncludesTrailingMargin()
    {
        var geo = ScrollGeometry.Create(0.5, textWidth: 500, viewportWidth: 200, lineDurationSeconds: 20);

        Assert.True(geo.NeedsScroll);
        // 500 - 200 + 10 余量
        Assert.Equal(310, geo.ScrollDistance);
    }

    [Fact]
    public void ScrollDurationIsDistanceOverSpeed()
    {
        var geo = ScrollGeometry.Create(0.5, 500, 200, 20);

        Assert.Equal(310 / ScrollGeometry.ScrollSpeedDipPerSecond, geo.ScrollDurationSeconds, precision: 6);
    }

    // ---------- 三段式节奏 ----------

    [Fact]
    public void LeadInKeepsTextStillAtStart()
    {
        // 进度 0 → elapsed 0，应在静止期。
        var geo = ScrollGeometry.Create(0, 500, 200, lineDurationSeconds: 10);

        Assert.True(geo.NeedsScroll);
        Assert.Equal(0, geo.OffsetX);
    }

    [Fact]
    public void StillWithinLeadInWindow()
    {
        // 时长 10s，进度 0.05 → elapsed 0.5s < LeadIn 0.8s。
        var geo = ScrollGeometry.Create(0.05, 500, 200, 10);

        Assert.Equal(0, geo.OffsetX);
    }

    [Fact]
    public void ScrollsAfterLeadIn()
    {
        // 时长 10s，进度 0.2 → elapsed 2s；已过 LeadIn 0.8s，
        // 滚动 (2 - 0.8) * 40 = 48。
        var geo = ScrollGeometry.Create(0.2, 500, 200, 10);

        Assert.True(geo.NeedsScroll);
        Assert.Equal(-48, geo.OffsetX, precision: 6);
    }

    [Fact]
    public void StopsAtEndOfScroll()
    {
        // 滚动一程 310/40 = 7.75s，加 LeadIn 0.8 → 8.55s。
        // 时长 10s、进度 1.0 → elapsed 10s，早就滚完了。
        var geo = ScrollGeometry.Create(1.0, 500, 200, 10);

        Assert.Equal(-310, geo.OffsetX, precision: 6);
    }

    [Fact]
    public void OffsetNeverExceedsScrollDistance()
    {
        // 逐进度检查，偏移不能超过 -scrollDistance（会滚出文字范围）。
        for (var progress = 0.0; progress <= 1.0; progress += 0.02)
        {
            var geo = ScrollGeometry.Create(progress, 500, 200, 10);

            Assert.True(geo.OffsetX >= -geo.ScrollDistance - 1e-9,
                $"progress={progress} 偏移 {geo.OffsetX} 超过滚动距离 {geo.ScrollDistance}");
            Assert.True(geo.OffsetX <= 1e-9,
                $"progress={progress} 偏移 {geo.OffsetX} 为正（不该向右移）");
        }
    }

    [Fact]
    public void OffsetIsMonotonicInProgress()
    {
        // 偏移必须单调递减（越来越负），不能往回跳。
        double previous = 1;
        for (var progress = 0.0; progress <= 1.0; progress += 0.02)
        {
            var geo = ScrollGeometry.Create(progress, 500, 200, 10);

            Assert.True(geo.OffsetX <= previous + 1e-9,
                $"progress={progress} 偏移 {geo.OffsetX} 大于上一档 {previous}");
            previous = geo.OffsetX;
        }
    }

    // ---------- 时长未知的退化路径 ----------

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(double.NaN)]
    public void UnknownDurationFallsBackToLinearScroll(double duration)
    {
        var geo = ScrollGeometry.Create(0.5, 500, 200, duration);

        Assert.True(geo.NeedsScroll);
        // 线性退化：-scrollDistance * 0.5
        Assert.Equal(-155, geo.OffsetX, precision: 6);
    }

    [Fact]
    public void UnknownDurationStillStaysWithinBounds()
    {
        foreach (var progress in new[] { 0.0, 0.25, 0.5, 0.75, 1.0 })
        {
            var geo = ScrollGeometry.Create(progress, 500, 200, 0);

            Assert.True(geo.OffsetX >= -geo.ScrollDistance - 1e-9);
            Assert.True(geo.OffsetX <= 1e-9);
        }
    }

    // ---------- 进度越界 ----------

    [Theory]
    [InlineData(-1)]
    [InlineData(-0.5)]
    public void NegativeProgressClampsToStart(double progress)
    {
        var geo = ScrollGeometry.Create(progress, 500, 200, 10);

        Assert.Equal(0, geo.OffsetX);
    }

    [Theory]
    [InlineData(1.5)]
    [InlineData(99)]
    public void ProgressAboveOneClampsToEnd(double progress)
    {
        var geo = ScrollGeometry.Create(progress, 500, 200, 10);

        Assert.Equal(-geo.ScrollDistance, geo.OffsetX, precision: 6);
    }

    [Fact]
    public void NaNProgressFallsBackToStart()
    {
        var geo = ScrollGeometry.Create(double.NaN, 500, 200, 10);

        Assert.Equal(0, geo.OffsetX);
    }

    // ---------- FitsWithin ----------

    [Fact]
    public void FitsWithinReturnsTrueForShortText()
    {
        var geo = ScrollGeometry.Create(0.5, 100, 200, 5);

        Assert.True(geo.FitsWithin(0.1));
    }

    [Fact]
    public void FitsWithinReturnsTrueWhenDurationIsSufficient()
    {
        // 需要 0.8 + 7.75 + 0.8 = 9.35s，给 15s 够。
        var geo = ScrollGeometry.Create(0.5, 500, 200, 15);

        Assert.True(geo.FitsWithin(15));
    }

    [Fact]
    public void FitsWithinReturnsFalseWhenDurationIsTooShort()
    {
        // 需要 9.35s，只给 5s 不够。
        var geo = ScrollGeometry.Create(0.5, 500, 200, 5);

        Assert.False(geo.FitsWithin(5));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    public void FitsWithinReturnsFalseForUnknownDuration(double duration)
    {
        var geo = ScrollGeometry.Create(0.5, 500, 200, duration);

        Assert.False(geo.FitsWithin(duration));
    }

    // ---------- 真实场景 ----------

    [Fact]
    public void TypicalLongLyricScrollsToEndWithinLineDuration()
    {
        // 一句 5 秒、文本 600 宽、视口 200：距离 410，需 10.25s。
        // 5 秒不够滚完——这种句子应该截断而不是滚到一半跳回。
        var geo = ScrollGeometry.Create(0.5, 600, 200, 5);

        Assert.True(geo.NeedsScroll);
        Assert.False(geo.FitsWithin(5));
    }

    [Fact]
    public void SlightlyOverflowingTextScrollsQuickly()
    {
        // 文本 260、视口 200：距离 70，滚动 1.75s。
        // 加上首尾静止 0.8 + 0.8 = 3.35s，所以 4 秒够、3 秒不够。
        var geo = ScrollGeometry.Create(0.5, 260, 200, 4);

        Assert.True(geo.NeedsScroll);
        Assert.Equal(70, geo.ScrollDistance);
        Assert.True(geo.FitsWithin(4));

        // 同一句只给 3 秒就滚不完——这是「短句不该滚」的边界附近。
        Assert.False(geo.FitsWithin(3));
    }
}
