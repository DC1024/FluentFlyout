// Copyright (c) 2026 anync
// SPDX-License-Identifier: MIT
//
// 本文件为本仓库新增，不源自 TaskbarLyrics。

using FluentFlyout.Lyrics.Timeline;
using Xunit;

namespace FluentFlyout.Lyrics.Tests;

/// <summary>
/// 本地时钟外推策略，以及它与注册表保位层的相互作用。
///
/// <para>
/// 背景是实测出来的：网易云音乐（<c>cloudmusic.exe</c>）的 SMTC 时间轴
/// 位置恒 <c>00:00.000</c>、<c>EndTime</c> 恒 0、<c>LastUpdatedTime</c> 恒为
/// <c>1601-01-01</c>（<c>FILETIME</c> 零值）。标题与艺术家却完全正常。
/// 于是「有没有歌」可知、「播到第几秒」不可知，歌词只能停在第 0 行。
/// </para>
///
/// <para>
/// 这里的断言全部用精确值，不写容差：位置会直接驱动歌词行切换，
/// 「差不多对」会掩盖边界错误。时间推进一律走 <see cref="ManualTimeProvider"/>。
/// </para>
/// </summary>
public sealed class LocalClockTimelineStrategyTests
{
    /// <summary>正常的时间戳基准，用于构造「播放器正常报时间轴」的对照样本。</summary>
    private static readonly DateTimeOffset FreshOrigin = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>网易云实际报出来的时间戳：<c>FILETIME</c> 的零值。</summary>
    private static readonly DateTimeOffset FileTimeZero = new(1601, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private const string Netease = "cloudmusic.exe";

    // ── 判据 ───────────────────────────────────────────────────────────────

    [Fact]
    public void CanApply_TrueForNeteaseBrokenTimeline()
    {
        var strategy = new LocalClockTimelinePositionStrategy(new ManualTimeProvider());
        var sample = Sample(Netease, playing: true, raw: 0, updated: FileTimeZero);

        Assert.True(strategy.CanApply(in sample));
    }

    [Fact]
    public void CanApply_FalseWhenPositionIsAdvancing()
    {
        // 位置能正常前进就说明时间轴可用，不该被接管。
        var strategy = new LocalClockTimelinePositionStrategy(new ManualTimeProvider());
        var sample = Sample(Netease, playing: true, raw: 42, updated: FileTimeZero);

        Assert.False(strategy.CanApply(in sample));
    }

    [Fact]
    public void CanApply_FalseWhenTimestampIsPlausible()
    {
        // 位置是 0 但时间戳新鲜 = 刚开播，属于正常情况，交给原策略外推。
        var strategy = new LocalClockTimelinePositionStrategy(new ManualTimeProvider());
        var sample = Sample(Netease, playing: true, raw: 0, updated: FreshOrigin);

        Assert.False(strategy.CanApply(in sample));
    }

    // ── 位置累计 ───────────────────────────────────────────────────────────

    [Fact]
    public void SelectPosition_AdvancesWithLocalClock()
    {
        var clock = new ManualTimeProvider();
        var strategy = new LocalClockTimelinePositionStrategy(clock);
        var sample = Sample(Netease, playing: true, raw: 0, updated: FileTimeZero);

        Assert.Equal(TimeSpan.Zero, strategy.SelectPosition(in sample));

        clock.AdvanceSeconds(1);
        Assert.Equal(TimeSpan.FromSeconds(1), strategy.SelectPosition(in sample));

        clock.AdvanceSeconds(2.5);
        Assert.Equal(TimeSpan.FromSeconds(3.5), strategy.SelectPosition(in sample));
    }

    [Fact]
    public void SelectPosition_FreezesWhilePaused()
    {
        var clock = new ManualTimeProvider();
        var strategy = new LocalClockTimelinePositionStrategy(clock);

        var playing = Sample(Netease, playing: true, raw: 0, updated: FileTimeZero);
        strategy.SelectPosition(in playing);

        clock.AdvanceSeconds(10);
        Assert.Equal(TimeSpan.FromSeconds(10), strategy.SelectPosition(in playing));

        // 暂停：无论过去多久，位置都不该再涨。
        var paused = Sample(Netease, playing: false, raw: 0, updated: FileTimeZero);
        Assert.Equal(TimeSpan.FromSeconds(10), strategy.SelectPosition(in paused));

        clock.AdvanceSeconds(30);
        Assert.Equal(TimeSpan.FromSeconds(10), strategy.SelectPosition(in paused));
    }

    [Fact]
    public void SelectPosition_ResumesFromFrozenPosition()
    {
        var clock = new ManualTimeProvider();
        var strategy = new LocalClockTimelinePositionStrategy(clock);

        var playing = Sample(Netease, playing: true, raw: 0, updated: FileTimeZero);
        var paused = Sample(Netease, playing: false, raw: 0, updated: FileTimeZero);

        strategy.SelectPosition(in playing);
        clock.AdvanceSeconds(5);
        strategy.SelectPosition(in playing);

        strategy.SelectPosition(in paused);
        clock.AdvanceSeconds(60); // 暂停一分钟，这段时间不能算进播放进度
        strategy.SelectPosition(in paused);

        // 恢复播放的那一帧：_playing 仍是 false，不累加，位置保持 5 秒。
        Assert.Equal(TimeSpan.FromSeconds(5), strategy.SelectPosition(in playing));

        clock.AdvanceSeconds(2);
        Assert.Equal(TimeSpan.FromSeconds(7), strategy.SelectPosition(in playing));
    }

    [Fact]
    public void SelectPosition_RestartsAtZeroOnTrackChange()
    {
        var clock = new ManualTimeProvider();
        var strategy = new LocalClockTimelinePositionStrategy(clock);

        var first = Sample(Netease, playing: true, raw: 0, updated: FileTimeZero, title: "美瞳");
        strategy.SelectPosition(in first);

        clock.AdvanceSeconds(20);
        Assert.Equal(TimeSpan.FromSeconds(20), strategy.SelectPosition(in first));

        // 换歌：新曲目从 0 开始，不能沿用上一首的 20 秒。
        var second = Sample(Netease, playing: true, raw: 0, updated: FileTimeZero, title: "起风了");
        Assert.Equal(TimeSpan.Zero, strategy.SelectPosition(in second));

        clock.AdvanceSeconds(3);
        Assert.Equal(TimeSpan.FromSeconds(3), strategy.SelectPosition(in second));
    }

    [Fact]
    public void Reset_ClearsAccumulatedPosition()
    {
        var clock = new ManualTimeProvider();
        var strategy = new LocalClockTimelinePositionStrategy(clock);
        var sample = Sample(Netease, playing: true, raw: 0, updated: FileTimeZero);

        strategy.SelectPosition(in sample);
        clock.AdvanceSeconds(30);
        Assert.Equal(TimeSpan.FromSeconds(30), strategy.SelectPosition(in sample));

        strategy.Reset();

        // Reset 之后视作全新曲目：从 0 重新开始。
        Assert.Equal(TimeSpan.Zero, strategy.SelectPosition(in sample));
    }

    // ── 与注册表的集成（本次修复的核心）─────────────────────────────────────

    [Fact]
    public void Registry_PicksLocalClockForNeteaseBrokenTimeline()
    {
        var registry = TimelinePositionStrategyRegistry.CreateDefault(new ManualTimeProvider());
        var sample = Sample(Netease, playing: true, raw: 0, updated: FileTimeZero);

        Assert.Equal("LocalClock", registry.Select(in sample).StrategyName);
    }

    [Fact]
    public void Registry_StillPicksCommonStrategyForNeteaseWhenTimelineWorks()
    {
        // 行为退化保护：其他四家播放器的正常路径不能被抢走。
        var registry = TimelinePositionStrategyRegistry.CreateDefault(new ManualTimeProvider());
        var sample = Sample(Netease, playing: true, raw: 30, updated: FreshOrigin);

        Assert.Equal("CommonExtrapolated", registry.Select(in sample).StrategyName);
    }

    /// <summary>
    /// 这条是本 PR 的回归核心。
    ///
    /// 保位层靠「时间戳有没有前进」判断时间轴刷没刷新。网易云的时间戳恒为
    /// 1601-01-01，若不跳过保位，它会一直判定「没刷新」，
    /// 在播放状态切换后把位置永久钉在 <c>_lastSelectedPosition</c>——
    /// 表现为策略算得再对也不生效。
    /// </summary>
    [Fact]
    public void Registry_AdvancedPositionDespiteFrozenTimestamp()
    {
        var clock = new ManualTimeProvider();
        var registry = TimelinePositionStrategyRegistry.CreateDefault(clock);
        var sample = Sample(Netease, playing: true, raw: 0, updated: FileTimeZero);

        Assert.Equal(TimeSpan.Zero, registry.Select(in sample).Position);

        clock.AdvanceSeconds(3);
        Assert.Equal(TimeSpan.FromSeconds(3), registry.Select(in sample).Position);

        clock.AdvanceSeconds(7);
        Assert.Equal(TimeSpan.FromSeconds(10), registry.Select(in sample).Position);
    }

    [Fact]
    public void Registry_KeepsAdvancingAfterPauseResumeWithFrozenTimestamp()
    {
        // 暂停再恢复是最容易踩到保位层钉死的路径：
        // _waitingForResumedTimeline 会被置 true，而时间戳永远不再前进。
        var clock = new ManualTimeProvider();
        var registry = TimelinePositionStrategyRegistry.CreateDefault(clock);

        var playing = Sample(Netease, playing: true, raw: 0, updated: FileTimeZero);
        var paused = Sample(Netease, playing: false, raw: 0, updated: FileTimeZero);

        registry.Select(in playing);
        clock.AdvanceSeconds(8);
        Assert.Equal(TimeSpan.FromSeconds(8), registry.Select(in playing).Position);

        Assert.Equal(TimeSpan.FromSeconds(8), registry.Select(in paused).Position);

        clock.AdvanceSeconds(120);
        Assert.Equal(TimeSpan.FromSeconds(8), registry.Select(in paused).Position);

        // 恢复播放后必须继续走，而不是卡在 8 秒。
        registry.Select(in playing);
        clock.AdvanceSeconds(4);
        Assert.Equal(TimeSpan.FromSeconds(12), registry.Select(in playing).Position);
    }

    [Fact]
    public void Registry_SeekIsIgnoredByLocalClockStrategy()
    {
        // 网易云不暴露可定位能力（IsPlaybackPositionEnabled = false），
        // 本地时钟策略不该接管 seek，否则位置会被拉到一个未经验证的目标。
        var registry = TimelinePositionStrategyRegistry.CreateDefault(new ManualTimeProvider());
        registry.Select(Sample(Netease, playing: true, raw: 0, updated: FileTimeZero));

        Assert.False(registry.NotifySeek(Netease, TimeSpan.FromSeconds(90), FileTimeZero));
    }

    // ── 曲终归零（单曲循环）───────────────────────────────────────────────

    /// <summary>
    /// 单曲循环时曲目身份（AUMID / 标题 / 歌手）三件套全不变，
    /// 「换歌归零」永远不触发。没有曲长上限时位置只增不减，
    /// 而歌词行的时间戳止于最后一句，于是第二遍起选位永远命中末行。
    /// </summary>
    [Fact]
    public void SelectPosition_RestartsAtZeroWhenTrackEndIsReached()
    {
        var clock = new ManualTimeProvider();
        var strategy = new LocalClockTimelinePositionStrategy(clock);
        strategy.SetTrackEndEstimate(TimeSpan.FromSeconds(60));
        var sample = Sample(Netease, playing: true, raw: 0, updated: FileTimeZero);

        Assert.Equal(TimeSpan.Zero, strategy.SelectPosition(in sample));

        clock.AdvanceSeconds(59);
        Assert.Equal(TimeSpan.FromSeconds(59), strategy.SelectPosition(in sample));

        // 越过曲长 = 一遍唱完：回到开头重新计时。
        clock.AdvanceSeconds(1);
        Assert.Equal(TimeSpan.Zero, strategy.SelectPosition(in sample));

        clock.AdvanceSeconds(3);
        Assert.Equal(TimeSpan.FromSeconds(3), strategy.SelectPosition(in sample));
    }

    /// <summary>
    /// 没有曲长时必须保持修复前的行为（一直累加），
    /// 不能凭空造一个上限——那会让正常播放的歌在中途突然跳回第一句。
    /// </summary>
    [Fact]
    public void SelectPosition_KeepsAccumulatingWithoutTrackEndEstimate()
    {
        var clock = new ManualTimeProvider();
        var strategy = new LocalClockTimelinePositionStrategy(clock);
        var sample = Sample(Netease, playing: true, raw: 0, updated: FileTimeZero);

        strategy.SelectPosition(in sample);
        clock.AdvanceSeconds(600);

        Assert.Equal(TimeSpan.FromSeconds(600), strategy.SelectPosition(in sample));
    }

    /// <summary>
    /// 明显短于合理下限的曲长一律不采信。
    /// 若采信，归零会每帧触发，位置永远锁在 0——歌词停在第一行，
    /// 这比「卡在最后一行」更难让人联想到是时长数据的问题。
    /// </summary>
    [Fact]
    public void SetTrackEndEstimate_IgnoresImplausiblyShortDurations()
    {
        var clock = new ManualTimeProvider();
        var strategy = new LocalClockTimelinePositionStrategy(clock);
        strategy.SetTrackEndEstimate(TimeSpan.FromSeconds(5));
        var sample = Sample(Netease, playing: true, raw: 0, updated: FileTimeZero);

        strategy.SelectPosition(in sample);
        clock.AdvanceSeconds(10);

        Assert.Equal(TimeSpan.FromSeconds(10), strategy.SelectPosition(in sample));
    }

    /// <summary>
    /// 对外暴露的「这个值会不会被采信」必须与 <see cref="LocalClockTimelinePositionStrategy.SetTrackEndEstimate"/>
    /// 的实际行为一致。
    ///
    /// <para>
    /// 之所以单独测：下发点的诊断日志用它来决定写「本地时钟会归位」还是
    /// 「太短，本地时钟忽略」，两者一旦走散，日志会把丢弃说成采用——
    /// 而这条日志存在的唯一理由就是排查这类问题。
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(null, false)]
    [InlineData(0, false)]
    [InlineData(29, false)]
    [InlineData(30, true)]
    [InlineData(215, true)]
    public void IsPlausibleTrackDuration_MatchesAcceptanceRule(int? seconds, bool expected)
    {
        TimeSpan? estimate = seconds is { } value ? TimeSpan.FromSeconds(value) : null;

        Assert.Equal(expected, LocalClockTimelinePositionStrategy.IsPlausibleTrackDuration(estimate));
    }

    /// <summary>
    /// 暂停在最后几秒时不能归零：歌词该停在用户看到的那一句上。
    /// 归零会让它在歌还没重播时跳回第一句。
    /// </summary>
    [Fact]
    public void SelectPosition_DoesNotRestartWhilePausedAtTrackEnd()
    {
        var clock = new ManualTimeProvider();
        var strategy = new LocalClockTimelinePositionStrategy(clock);
        strategy.SetTrackEndEstimate(TimeSpan.FromSeconds(60));

        var playing = Sample(Netease, playing: true, raw: 0, updated: FileTimeZero);
        var paused = Sample(Netease, playing: false, raw: 0, updated: FileTimeZero);

        strategy.SelectPosition(in playing);
        clock.AdvanceSeconds(59);
        Assert.Equal(TimeSpan.FromSeconds(59), strategy.SelectPosition(in playing));

        // 暂停后时钟继续走，但位置必须冻在 59 秒，不能越过曲长而回到 0。
        clock.AdvanceSeconds(10);
        Assert.Equal(TimeSpan.FromSeconds(59), strategy.SelectPosition(in paused));

        // 恢复播放才继续累计，越过曲长后才归零。
        Assert.Equal(TimeSpan.FromSeconds(59), strategy.SelectPosition(in playing));
        clock.AdvanceSeconds(1);
        Assert.Equal(TimeSpan.Zero, strategy.SelectPosition(in playing));
    }

    /// <summary>整条链路（含保位层）走通：时间戳恒坏时也能靠曲长归位。</summary>
    [Fact]
    public void Registry_RestartsAtZeroWhenTrackEndIsReached()
    {
        var clock = new ManualTimeProvider();
        var registry = TimelinePositionStrategyRegistry.CreateDefault(clock);
        var sample = Sample(Netease, playing: true, raw: 0, updated: FileTimeZero);

        registry.SetTrackEndEstimate(TimeSpan.FromSeconds(60));
        Assert.Equal(TimeSpan.Zero, registry.Select(in sample).Position);

        clock.AdvanceSeconds(60);
        Assert.Equal(TimeSpan.Zero, registry.Select(in sample).Position);
    }

    /// <summary>
    /// 换会话时旧曲长必须失效。留着会让新歌在上一首的长度处归零，
    /// 表现为「播到一半歌词突然跳回开头」，比不修更糟。
    /// </summary>
    [Fact]
    public void Registry_ForgetsTrackEndEstimateOnReset()
    {
        var clock = new ManualTimeProvider();
        var registry = TimelinePositionStrategyRegistry.CreateDefault(clock);
        var sample = Sample(Netease, playing: true, raw: 0, updated: FileTimeZero);

        registry.SetTrackEndEstimate(TimeSpan.FromSeconds(60));
        registry.Select(in sample);
        registry.Reset();

        registry.Select(in sample);
        clock.AdvanceSeconds(60);

        Assert.Equal(TimeSpan.FromSeconds(60), registry.Select(in sample).Position);
    }

    private static TimelineSample Sample(
        string source,
        bool playing,
        double raw,
        DateTimeOffset updated,
        string title = "美瞳",
        string artist = "徐良") =>
        new(
            source,
            title,
            artist,
            playing,
            TimeSpan.FromSeconds(raw),
            updated,
            // 网易云的 LastUpdatedTime 是 1601 年，年龄极大；生产代码里
            // FluentFlyoutMusicSessionProvider 会把它钳成 0，这里如实模拟。
            TimeSpan.Zero,
            TimeSpan.FromSeconds(raw));
}
