// Copyright (c) 2026 anync
// SPDX-License-Identifier: MIT
//
// 本文件为本仓库新增，不源自 TaskbarLyrics。

using FluentFlyout.Lyrics.Layout;
using Xunit;

namespace FluentFlyout.Lyrics.Tests;

/// <summary>
/// 逐字扫描几何的测试。
///
/// 这块算错的表现很隐蔽：羽化超出扫描点两侧时，短句上看到的是
/// 「整行半透明」而不是明显的位置错误，很容易被当成配色问题。
/// </summary>
public sealed class ScanGeometryTests
{
    // ---------- 基本位置 ----------

    [Fact]
    public void ProgressZeroPutsScanAtLineStart()
    {
        var geo = ScanGeometry.Create(0, 200);

        Assert.Equal(0, geo.ScanX);
        Assert.Equal(0, geo.SolidEnd);
        // 进度 0 时羽化必须被压到 0，否则未唱部分会先亮一截。
        Assert.Equal(0, geo.Feather);
        Assert.Equal(0, geo.FadeStart);
    }

    [Fact]
    public void ProgressOnePutsScanAtLineEnd()
    {
        var geo = ScanGeometry.Create(1, 200);

        Assert.Equal(200, geo.ScanX);
        // 进度 1 时羽化同样被压到 0，否则已唱部分尾部会缺一块。
        Assert.Equal(0, geo.Feather);
        Assert.Equal(200, geo.SolidEnd);
        Assert.Equal(200, geo.FadeStart);
    }

    [Fact]
    public void MidProgressSplitsLineSymmetrically()
    {
        var geo = ScanGeometry.Create(0.5, 200);

        Assert.Equal(100, geo.ScanX);
        Assert.True(geo.Feather > 0, "中段应有羽化");
        Assert.Equal(geo.ScanX - geo.Feather, geo.SolidEnd);
        Assert.Equal(geo.ScanX + geo.Feather, geo.FadeStart);
    }

    [Theory]
    [InlineData(0.1)]
    [InlineData(0.25)]
    [InlineData(0.5)]
    [InlineData(0.75)]
    [InlineData(0.9)]
    public void SolidEndNeverExceedsScanX(double progress)
    {
        var geo = ScanGeometry.Create(progress, 300);

        Assert.True(geo.SolidEnd <= geo.ScanX + 1e-9);
        Assert.True(geo.FadeStart >= geo.ScanX - 1e-9);
    }

    // ---------- 羽化约束 ----------

    [Fact]
    public void FeatherIsCappedByLineWidthRatio()
    {
        // 行宽 1000 → 比例羽化 120，但被 MaxFeather=24 夹住。
        var geo = ScanGeometry.Create(0.5, 1000);

        Assert.Equal(ScanGeometry.MaxFeather, geo.Feather);
    }

    [Fact]
    public void FeatherHasMinimumWidth()
    {
        // 行宽 10 → 比例羽化 1.2，但被 MinFeather=2 抬到 2。
        // 中段时两侧各 5 的空间够放 2。
        var geo = ScanGeometry.Create(0.5, 10);

        Assert.Equal(ScanGeometry.MinFeather, geo.Feather);
    }

    [Fact]
    public void FeatherNeverExceedsAvailableSpaceOnShortLines()
    {
        // 行宽 10、进度 0.1 → scanX=1，左侧只有 1 的空间，
        // 羽化必须压到 1 而不是 MinFeather=2，否则 SolidEnd 会被
        // Math.Max 夹成 0，看起来像「还没开始就整行渐变」。
        var geo = ScanGeometry.Create(0.1, 10);

        Assert.Equal(1, geo.ScanX);
        Assert.Equal(1, geo.Feather);
        Assert.Equal(0, geo.SolidEnd);
        Assert.Equal(2, geo.FadeStart);
    }

    [Fact]
    public void FeatherNeverExceedsTrailingSpace()
    {
        // 进度 0.95、行宽 200 → scanX=190，右侧只有 10。
        var geo = ScanGeometry.Create(0.95, 200);

        Assert.Equal(190, geo.ScanX);
        Assert.True(geo.Feather <= 10 + 1e-9);
        Assert.True(geo.FadeStart <= 200 + 1e-9);
    }

    [Fact]
    public void SolidEndIsNeverNegative()
    {
        foreach (var progress in new[] { 0.0, 0.01, 0.05, 0.5, 1.0 })
        {
            foreach (var width in new[] { 1.0, 5.0, 50.0, 500.0 })
            {
                var geo = ScanGeometry.Create(progress, width);
                Assert.True(geo.SolidEnd >= 0, $"progress={progress} width={width}");
                Assert.True(geo.Feather >= 0, $"progress={progress} width={width}");
            }
        }
    }

    [Fact]
    public void FadeStartNeverExceedsLineWidth()
    {
        foreach (var progress in new[] { 0.0, 0.5, 0.99, 1.0 })
        {
            foreach (var width in new[] { 1.0, 5.0, 50.0, 500.0 })
            {
                var geo = ScanGeometry.Create(progress, width);
                Assert.True(geo.FadeStart <= width + 1e-9,
                    $"progress={progress} width={width} fadeStart={geo.FadeStart}");
            }
        }
    }

    // ---------- 越界与脏数据 ----------

    [Theory]
    [InlineData(-1)]
    [InlineData(-0.5)]
    public void NegativeProgressIsClampedToZero(double progress)
    {
        var geo = ScanGeometry.Create(progress, 200);

        Assert.Equal(0, geo.ScanX);
    }

    [Theory]
    [InlineData(1.5)]
    [InlineData(99)]
    public void ProgressAboveOneIsClamped(double progress)
    {
        var geo = ScanGeometry.Create(progress, 200);

        Assert.Equal(200, geo.ScanX);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidLineWidthFallsBackToOne(double width)
    {
        var geo = ScanGeometry.Create(0.5, width);

        // 行宽退到 1，scanX 应为 0.5。
        Assert.Equal(0.5, geo.ScanX, precision: 6);
        Assert.True(geo.Feather >= 0);
    }

    [Fact]
    public void NaNProgressFallsBackToZero()
    {
        var geo = ScanGeometry.Create(double.NaN, 200);

        Assert.Equal(0, geo.ScanX);
    }

    // ---------- 单调性 ----------

    [Fact]
    public void ScanXIsMonotonicInProgress()
    {
        double previous = -1;
        for (var progress = 0.0; progress <= 1.0; progress += 0.05)
        {
            var geo = ScanGeometry.Create(progress, 200);
            Assert.True(geo.ScanX >= previous,
                $"progress={progress} 时 scanX={geo.ScanX} 小于上一档 {previous}");
            previous = geo.ScanX;
        }
    }

    [Fact]
    public void SolidEndIsMonotonicInProgress()
    {
        // 已唱部分的终点必须单调递增，否则用户会看到高亮往回缩。
        double previous = -1;
        for (var progress = 0.0; progress <= 1.0; progress += 0.05)
        {
            var geo = ScanGeometry.Create(progress, 200);
            Assert.True(geo.SolidEnd >= previous - 1e-9,
                $"progress={progress} 时 solidEnd={geo.SolidEnd} 小于上一档 {previous}");
            previous = geo.SolidEnd;
        }
    }
}
