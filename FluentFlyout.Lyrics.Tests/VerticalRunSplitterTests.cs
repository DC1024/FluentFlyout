// Copyright (c) 2026 anync
// SPDX-License-Identifier: MIT
//
// 本文件为本仓库新增，不源自 TaskbarLyrics。

using FluentFlyout.Lyrics.Layout;
using Xunit;

namespace FluentFlyout.Lyrics.Tests;

/// <summary>
/// 竖排切分的测试。
///
/// 这一层的错误在屏幕上很隐蔽：切错了不会报错，只会「少一个字」或
/// 「一个词变成六行」。所以每条规则都单独钉一个用例。
/// </summary>
public sealed class VerticalRunSplitterTests
{
    [Fact]
    public void ChineseLineSplitsOneGlyphPerRunAndAllUpright()
    {
        var runs = VerticalRunSplitter.Split("被爱的深夜");

        Assert.Equal(5, runs.Count);
        Assert.All(runs, r => Assert.True(r.Upright));
        Assert.Equal("被", runs[0].Text);
        Assert.Equal("夜", runs[4].Text);
    }

    [Fact]
    public void LatinWordStaysOneRunAndIsNotUpright()
    {
        // 逐字母竖排会把 screen 变成六行，可读性归零。
        var runs = VerticalRunSplitter.Split("screen");

        var run = Assert.Single(runs);
        Assert.Equal("screen", run.Text);
        Assert.False(run.Upright);
    }

    [Fact]
    public void EnglishSentenceKeepsWordBoundaries()
    {
        var runs = VerticalRunSplitter.Split("My screen is glowing");

        Assert.Equal(
            new[] { "My", " ", "screen", " ", "is", " ", "glowing" },
            runs.Select(r => r.Text));
        Assert.All(runs, r => Assert.False(r.Upright));
    }

    [Fact]
    public void MixedLineAlternatesUprightGlyphsAndSidewaysWords()
    {
        var runs = VerticalRunSplitter.Split("我爱 R&B 音乐");

        // 我爱 / 空格 / R&B / 空格 / 音 / 乐
        Assert.Equal(new[] { "我", "爱", " ", "R&B", " ", "音", "乐" }, runs.Select(r => r.Text));
        Assert.True(runs[0].Upright);
        Assert.False(runs[3].Upright);
        Assert.True(runs[5].Upright);
    }

    [Fact]
    public void CombiningAccentStaysOneRun()
    {
        // é = e + U+0301。按 char 切会得到两个格，其中一格是悬空的组合符。
        var runs = VerticalRunSplitter.Split("e\u0301");

        var run = Assert.Single(runs);
        Assert.Equal("e\u0301", run.Text);
    }

    [Fact]
    public void EmojiSurrogatePairStaysOneRunAndUpright()
    {
        // 😀 = U+1F600，代理对。按 char 切会变成两个孤立代理码位。
        var runs = VerticalRunSplitter.Split("😀");

        var run = Assert.Single(runs);
        Assert.Equal("😀", run.Text);
        Assert.True(run.Upright);
    }

    [Fact]
    public void ChinesePunctuationIsUprightAndSeparate()
    {
        var runs = VerticalRunSplitter.Split("爱你，再见。");

        Assert.Equal(new[] { "爱", "你", "，", "再", "见", "。" }, runs.Select(r => r.Text));
        Assert.All(runs, r => Assert.True(r.Upright));
    }

    [Fact]
    public void NumbersStayWithTheirWord()
    {
        var runs = VerticalRunSplitter.Split("第 24 个夏天");

        Assert.Equal(new[] { "第", " ", "24", " ", "个", "夏", "天" }, runs.Select(r => r.Text));
        Assert.False(runs[2].Upright);
    }

    [Fact]
    public void ApostropheDoesNotBreakTheWord()
    {
        var runs = VerticalRunSplitter.Split("don't");

        var run = Assert.Single(runs);
        Assert.Equal("don't", run.Text);
    }

    [Fact]
    public void EmptyOrNullYieldsNoRuns()
    {
        Assert.Empty(VerticalRunSplitter.Split(null));
        Assert.Empty(VerticalRunSplitter.Split(string.Empty));
    }
}
