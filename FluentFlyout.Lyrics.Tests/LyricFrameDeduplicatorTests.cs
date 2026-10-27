// Copyright (c) 2026 anync
// SPDX-License-Identifier: MIT
//
// 本文件为本仓库新增，不源自 TaskbarLyrics。

using FluentFlyout.Lyrics.Frames;
using FluentFlyout.Lyrics.Models;
using Xunit;

namespace FluentFlyout.Lyrics.Tests;

/// <summary>
/// 帧去重的测试。
///
/// 这个类的存在理由就是「不能整帧比较」：<c>LineProgress</c> 每帧都在变，
/// 整帧比较等于永远有变化。所以测试的重点是两类信号被区别对待——
/// 离散信号立即推送，连续信号按桶推送。
/// </summary>
public sealed class LyricFrameDeduplicatorTests
{
    [Fact]
    public void FirstFrameIsAlwaysEmitted()
    {
        var deduplicator = new LyricFrameDeduplicator();

        Assert.True(deduplicator.ShouldEmit(Frame("line one")));
        Assert.Equal(1, deduplicator.EmittedCount);
        Assert.Equal(0, deduplicator.SuppressedCount);
    }

    [Fact]
    public void NullFrameIsNeverEmitted()
    {
        var deduplicator = new LyricFrameDeduplicator();

        Assert.False(deduplicator.ShouldEmit(null));
        Assert.Equal(0, deduplicator.EmittedCount);
        Assert.Equal(0, deduplicator.SuppressedCount);
    }

    [Fact]
    public void IdenticalFrameIsSuppressed()
    {
        var deduplicator = new LyricFrameDeduplicator();
        var frame = Frame("line one", progress: 0.5);

        Assert.True(deduplicator.ShouldEmit(frame));
        Assert.False(deduplicator.ShouldEmit(frame));
        Assert.Equal(1, deduplicator.EmittedCount);
        Assert.Equal(1, deduplicator.SuppressedCount);
    }

    [Fact]
    public void SubQuantumProgressChangeIsSuppressed()
    {
        // 0.500 → 0.505 落在同一个 1% 桶里，视觉上不可分辨，不该推。
        var deduplicator = new LyricFrameDeduplicator();

        Assert.True(deduplicator.ShouldEmit(Frame("line one", progress: 0.500)));
        Assert.False(deduplicator.ShouldEmit(Frame("line one", progress: 0.505)));
        Assert.False(deduplicator.ShouldEmit(Frame("line one", progress: 0.509)));
    }

    [Fact]
    public void ProgressCrossingAQuantumBoundaryIsEmitted()
    {
        var deduplicator = new LyricFrameDeduplicator();

        Assert.True(deduplicator.ShouldEmit(Frame("line one", progress: 0.500)));
        Assert.True(deduplicator.ShouldEmit(Frame("line one", progress: 0.510)));
        Assert.Equal(2, deduplicator.EmittedCount);
    }

    [Fact]
    public void LineChangeIsEmittedImmediatelyEvenWithTinyProgressDelta()
    {
        // 换行是离散信号，哪怕进度只动了 0.001 也必须立刻推，
        // 否则 UI 会晚一帧才换词。
        var deduplicator = new LyricFrameDeduplicator();

        Assert.True(deduplicator.ShouldEmit(Frame("line one", progress: 0.9)));
        Assert.True(deduplicator.ShouldEmit(Frame("line two", progress: 0.901)));
    }

    [Fact]
    public void NextLineChangeIsEmittedImmediately()
    {
        var deduplicator = new LyricFrameDeduplicator();

        Assert.True(deduplicator.ShouldEmit(Frame("current", next: "a")));
        Assert.True(deduplicator.ShouldEmit(Frame("current", next: "b")));
    }

    [Fact]
    public void TitleChangeIsEmittedImmediately()
    {
        var deduplicator = new LyricFrameDeduplicator();

        Assert.True(deduplicator.ShouldEmit(Frame("line", title: "Song A")));
        Assert.True(deduplicator.ShouldEmit(Frame("line", title: "Song B")));
    }

    [Fact]
    public void TranslationChangesAreEmittedImmediately()
    {
        var deduplicator = new LyricFrameDeduplicator();

        Assert.True(deduplicator.ShouldEmit(Frame("line", translation: "译一")));
        Assert.True(deduplicator.ShouldEmit(Frame("line", translation: "译二")));
        Assert.True(deduplicator.ShouldEmit(Frame("line", translation: "译二", nextTranslation: "译三")));
    }

    [Fact]
    public void LineIndexChangeIsEmittedImmediately()
    {
        // 重复歌词（同一句出现两次）文本相同但索引不同，仍要推。
        var deduplicator = new LyricFrameDeduplicator();

        Assert.True(deduplicator.ShouldEmit(Frame("same text", lineIndex: 3)));
        Assert.True(deduplicator.ShouldEmit(Frame("same text", lineIndex: 7)));
    }

    [Fact]
    public void PureMusicFlagChangeIsEmittedImmediately()
    {
        var deduplicator = new LyricFrameDeduplicator();

        Assert.True(deduplicator.ShouldEmit(Frame("纯音乐，请欣赏", pureMusic: false)));
        Assert.True(deduplicator.ShouldEmit(Frame("纯音乐，请欣赏", pureMusic: true)));
    }

    [Fact]
    public void WordScanProgressUsesItsOwnQuantum()
    {
        var deduplicator = new LyricFrameDeduplicator();

        Assert.True(deduplicator.ShouldEmit(Frame("line", wordScan: 0.20)));
        Assert.False(deduplicator.ShouldEmit(Frame("line", wordScan: 0.204)));
        Assert.True(deduplicator.ShouldEmit(Frame("line", wordScan: 0.21)));
    }

    [Fact]
    public void TransitionFromNoWordScanToWordScanIsEmitted()
    {
        // null → 有值 是离散变化（开始逐字），必须推。
        var deduplicator = new LyricFrameDeduplicator();

        Assert.True(deduplicator.ShouldEmit(Frame("line", wordScan: null)));
        Assert.True(deduplicator.ShouldEmit(Frame("line", wordScan: 0.0)));
    }

    [Fact]
    public void TransitionFromWordScanToNoneIsEmitted()
    {
        var deduplicator = new LyricFrameDeduplicator();

        Assert.True(deduplicator.ShouldEmit(Frame("line", wordScan: 0.5)));
        Assert.True(deduplicator.ShouldEmit(Frame("line", wordScan: null)));
    }

    [Fact]
    public void NegativeProgressIsTreatedAsInvalidAndSuppressedRepeatedly()
    {
        // 异常输入不能被当成「一直在变」而反复推送。
        var deduplicator = new LyricFrameDeduplicator();

        Assert.True(deduplicator.ShouldEmit(Frame("line", progress: -0.5)));
        Assert.False(deduplicator.ShouldEmit(Frame("line", progress: -0.4)));
        Assert.False(deduplicator.ShouldEmit(Frame("line", progress: -0.9)));
    }

    [Fact]
    public void NaNNProgressIsTreatedAsInvalidAndSuppressedRepeatedly()
    {
        var deduplicator = new LyricFrameDeduplicator();

        Assert.True(deduplicator.ShouldEmit(Frame("line", progress: double.NaN)));
        Assert.False(deduplicator.ShouldEmit(Frame("line", progress: double.NaN)));
    }

    [Fact]
    public void ResetForcesTheNextFrameToBeEmitted()
    {
        var deduplicator = new LyricFrameDeduplicator();
        var frame = Frame("line one", progress: 0.5);

        Assert.True(deduplicator.ShouldEmit(frame));
        Assert.False(deduplicator.ShouldEmit(frame));

        deduplicator.Reset();

        Assert.True(deduplicator.ShouldEmit(frame));
    }

    [Fact]
    public void ResetDoesNotClearDiagnosticCounters()
    {
        // 计数器是诊断用途，跨换歌累计才有意义。
        var deduplicator = new LyricFrameDeduplicator();

        deduplicator.ShouldEmit(Frame("line"));
        deduplicator.ShouldEmit(Frame("line"));
        deduplicator.Reset();

        Assert.Equal(1, deduplicator.EmittedCount);
        Assert.Equal(1, deduplicator.SuppressedCount);
    }

    [Fact]
    public void ProgressExactlyOnBucketBoundaryBelongsToTheNewBucket()
    {
        // 0.99 属于桶 99，1.00 属于桶 100，必须跨桶推送。
        var deduplicator = new LyricFrameDeduplicator();

        Assert.True(deduplicator.ShouldEmit(Frame("line", progress: 0.99)));
        Assert.True(deduplicator.ShouldEmit(Frame("line", progress: 1.00)));
    }

    [Fact]
    public void FullPlaybackSweepEmitsAboutOneHundredFrames()
    {
        // 一句歌词从 0 走到 1，按 1% 粒度最多推 100 次。
        // 这个上界决定了长句的 CPU 预算，超出说明量化粒度写错了。
        var deduplicator = new LyricFrameDeduplicator();
        var emitted = 0;

        for (var step = 0; step <= 1000; step++)
        {
            if (deduplicator.ShouldEmit(Frame("line", progress: step / 1000.0)))
            {
                emitted++;
            }
        }

        Assert.InRange(emitted, 100, 102);
    }

    // ── 辅助 ───────────────────────────────────────────────────────────────

    private static LyricDisplayFrame Frame(
        string current,
        string next = "",
        string title = "Midnight City",
        double progress = 0,
        int lineIndex = 0,
        bool pureMusic = false,
        double? wordScan = null,
        string? translation = null,
        string? nextTranslation = null,
        bool hasTrackTranslation = false) =>
        new(
            current,
            next,
            title,
            progress,
            lineIndex,
            pureMusic,
            wordScan,
            translation,
            nextTranslation,
            hasTrackTranslation);
}
