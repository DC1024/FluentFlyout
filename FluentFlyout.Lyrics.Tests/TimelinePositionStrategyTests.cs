// Copyright (c) 2026 anync
// SPDX-License-Identifier: MIT
//
// 本文件为本仓库新增，不源自 TaskbarLyrics。

using FluentFlyout.Lyrics.Timeline;
using Xunit;

namespace FluentFlyout.Lyrics.Tests;

/// <summary>
/// 时间轴选位策略的测试。
///
/// 断言尽量写成精确值而不是范围：这些策略的产出会直接驱动歌词行切换，
/// 「差不多对」的容差会掩盖掉真正的边界错误（尤其是整秒量化与 seek 回声）。
/// 时间推进全部通过 <see cref="ManualTimeProvider"/> 控制，不依赖真实时钟。
/// </summary>
public sealed class TimelinePositionStrategyTests
{
    private static readonly DateTimeOffset Origin = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private const string AppleMusic = "AppleMusic";
    private const string UnknownPlayer = "SomeUnknownPlayer";

    // ── 兜底策略 ───────────────────────────────────────────────────────────

    [Fact]
    public void DefaultStrategy_PausedReturnsRawPosition()
    {
        var strategy = new DefaultExtrapolatedTimelinePositionStrategy();
        var sample = Sample(UnknownPlayer, playing: false, raw: 5, extrapolated: 9);

        Assert.Equal(TimeSpan.FromSeconds(5), strategy.SelectPosition(in sample));
    }

    [Fact]
    public void DefaultStrategy_PlayingReturnsExtrapolatedPosition()
    {
        var strategy = new DefaultExtrapolatedTimelinePositionStrategy();
        var sample = Sample(UnknownPlayer, playing: true, raw: 5, extrapolated: 5.4);

        Assert.Equal(TimeSpan.FromSeconds(5.4), strategy.SelectPosition(in sample));
    }

    [Fact]
    public void DefaultStrategy_NegativeExtrapolationFallsBackToRawPosition()
    {
        // 外推成负数说明上游时钟回拨，跟着走会让歌词跳回开头。
        var strategy = new DefaultExtrapolatedTimelinePositionStrategy();
        var sample = Sample(UnknownPlayer, playing: true, raw: 0.2, extrapolated: -1);

        Assert.Equal(TimeSpan.FromSeconds(0.2), strategy.SelectPosition(in sample));
    }

    [Fact]
    public void DefaultStrategy_AppliesToEverySource()
    {
        var strategy = new DefaultExtrapolatedTimelinePositionStrategy();
        var sample = Sample(AppleMusic, playing: true, raw: 1, extrapolated: 1);

        Assert.True(strategy.CanApply(in sample));
    }

    // ── 播放器识别 ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("AppleMusic")]
    [InlineData("applemusic")]
    [InlineData("  AppleMusic  ")]
    [InlineData("appleinc.applemusicwin_nzyj5cx40ttqa!app")]
    [InlineData("AppleInc.AppleMusicWin_nzyj5cx40ttqa!App")]
    public void PlayerIdentity_RecognizesAppleMusicInBothNamingForms(string source)
    {
        Assert.True(PlayerIdentity.IsAppleMusic(source));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify")]
    [InlineData("CloudMusic")]
    public void PlayerIdentity_RejectsNonAppleMusicSources(string? source)
    {
        Assert.False(PlayerIdentity.IsAppleMusic(source));
    }

    [Theory]
    [InlineData("SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify", "spotify")]
    [InlineData("cloudmusic.exe", "netease")]
    [InlineData("NeteaseCloudMusic", "netease")]
    [InlineData("163music", "netease")]
    [InlineData("music.163.com", "netease")]
    [InlineData("QQMusic.exe", "qqmusic")]
    [InlineData("KuGou.exe", "kugou")]
    public void PlayerIdentity_RecognizesTheFourCommonPlayers(string source, string family)
    {
        var expected = family switch
        {
            "spotify" => PlayerIdentity.IsSpotify(source),
            "netease" => PlayerIdentity.IsNetease(source),
            "qqmusic" => PlayerIdentity.IsQqMusic(source),
            "kugou" => PlayerIdentity.IsKugou(source),
            _ => false
        };

        Assert.True(expected);
        Assert.True(PlayerIdentity.UsesCommonExtrapolation(source));
    }

    [Fact]
    public void PlayerIdentity_DoesNotTreatAppleMusicAsCommonPlayer()
    {
        // Apple Music 必须走专用策略，绝不能落进「四家共用」分支。
        Assert.False(PlayerIdentity.UsesCommonExtrapolation(AppleMusic));
        Assert.False(PlayerIdentity.UsesCommonExtrapolation(
            "appleinc.applemusicwin_nzyj5cx40ttqa!app"));
    }

    // ── 四家共用策略 ───────────────────────────────────────────────────────

    [Theory]
    [InlineData("SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify", true)]
    [InlineData("cloudmusic.exe", true)]
    [InlineData("QQMusic.exe", true)]
    [InlineData("KuGou.exe", true)]
    [InlineData(AppleMusic, false)]
    [InlineData(UnknownPlayer, false)]
    public void CommonStrategy_AppliesOnlyToTheFourKnownPlayers(string source, bool expected)
    {
        var strategy = new CommonExtrapolatedTimelinePositionStrategy();
        var sample = Sample(source, playing: true, raw: 1, extrapolated: 1);

        Assert.Equal(expected, strategy.CanApply(in sample));
    }

    [Fact]
    public void CommonStrategy_SelectsPositionWithTheSameRulesAsDefault()
    {
        var common = new CommonExtrapolatedTimelinePositionStrategy();
        var paused = Sample("Spotify.exe", playing: false, raw: 5, extrapolated: 9);
        var playing = Sample("Spotify.exe", playing: true, raw: 5, extrapolated: 5.4);
        var negative = Sample("Spotify.exe", playing: true, raw: 0.2, extrapolated: -1);

        Assert.Equal(TimeSpan.FromSeconds(5), common.SelectPosition(in paused));
        Assert.Equal(TimeSpan.FromSeconds(5.4), common.SelectPosition(in playing));
        Assert.Equal(TimeSpan.FromSeconds(0.2), common.SelectPosition(in negative));
    }

    // ── Apple Music 策略 ───────────────────────────────────────────────────

    [Fact]
    public void AppleStrategy_AppliesOnlyToAppleMusic()
    {
        var strategy = new AppleMusicTimelinePositionStrategy(new ManualTimeProvider());

        Assert.True(strategy.CanApply(Sample(AppleMusic, true, 1, 1)));
        Assert.False(strategy.CanApply(Sample("Spotify.exe", true, 1, 1)));
        Assert.False(strategy.CanApply(Sample(UnknownPlayer, true, 1, 1)));
    }

    [Fact]
    public void AppleStrategy_FirstSampleAnchorsAndAddsOneResolutionOfAge()
    {
        // 首次锚定允许补上最多一个分辨率周期的外推量。
        var clock = new ManualTimeProvider();
        var strategy = new AppleMusicTimelinePositionStrategy(clock);

        var sample = Sample(AppleMusic, playing: true, raw: 10, extrapolated: 10.4, age: 0.4);

        Assert.Equal(TimeSpan.FromSeconds(10.4), strategy.SelectPosition(in sample));
    }

    [Fact]
    public void AppleStrategy_FirstSampleIgnoresTimelinesOlderThanOneResolution()
    {
        // 时间轴已经 5 秒没更新，外推不可信，只能信播放器报的原始位置。
        var clock = new ManualTimeProvider();
        var strategy = new AppleMusicTimelinePositionStrategy(clock);

        var sample = Sample(AppleMusic, playing: true, raw: 10, extrapolated: 15, age: 5);

        Assert.Equal(TimeSpan.FromSeconds(10), strategy.SelectPosition(in sample));
    }

    [Fact]
    public void AppleStrategy_RepeatedTimestampRefreshWithinTheSameSecondKeepsAdvancing()
    {
        // 这是整个策略存在的理由：Apple Music 会在同一整秒内反复刷新
        // LastUpdatedTime 却不改 RawPosition。若把「时间戳刷新」当成新锚点，
        // 位置会被反复拉回 10 秒，表现为歌词卡住。
        var clock = new ManualTimeProvider();
        var strategy = new AppleMusicTimelinePositionStrategy(clock);

        var first = Sample(AppleMusic, playing: true, raw: 10, extrapolated: 10, updated: Origin);
        Assert.Equal(TimeSpan.FromSeconds(10), strategy.SelectPosition(in first));

        clock.AdvanceSeconds(0.5);
        var refreshedOnly = Sample(
            AppleMusic,
            playing: true,
            raw: 10,
            extrapolated: 10.5,
            updated: Origin.AddSeconds(0.5));

        Assert.Equal(TimeSpan.FromSeconds(10.5), strategy.SelectPosition(in refreshedOnly));
    }

    [Fact]
    public void AppleStrategy_BackwardJumpReanchorsImmediately()
    {
        var clock = new ManualTimeProvider();
        var strategy = new AppleMusicTimelinePositionStrategy(clock);
        strategy.SelectPosition(Sample(AppleMusic, true, 10, 10, updated: Origin));

        clock.AdvanceSeconds(0.5);
        var rewound = Sample(
            AppleMusic,
            playing: true,
            raw: 3,
            extrapolated: 3,
            updated: Origin.AddSeconds(0.5));

        Assert.Equal(TimeSpan.FromSeconds(3), strategy.SelectPosition(in rewound));
    }

    [Fact]
    public void AppleStrategy_ForwardJumpBeyondSeekThresholdReanchors()
    {
        var clock = new ManualTimeProvider();
        var strategy = new AppleMusicTimelinePositionStrategy(clock);
        strategy.SelectPosition(Sample(AppleMusic, true, 10, 10, updated: Origin));

        clock.AdvanceSeconds(0.5);
        // 预测位置 10.5，播放器报 20，偏差 9.5 秒远超 2 秒阈值 → 按跳转处理。
        var jumped = Sample(
            AppleMusic,
            playing: true,
            raw: 20,
            extrapolated: 20,
            updated: Origin.AddSeconds(0.5));

        Assert.Equal(TimeSpan.FromSeconds(20), strategy.SelectPosition(in jumped));
    }

    [Fact]
    public void AppleStrategy_SubSecondDriftIsCorrectedByRateOnlyAfterThreeSamples()
    {
        // 场景：播放器一次性多报了 1.5 秒（低于 seek 阈值），之后恢复正常 1x 前进。
        // 预期：前两次采样仍按 1x 走（不动声色），第三次起用 +5% 速率慢慢追平。
        var clock = new ManualTimeProvider();
        var strategy = new AppleMusicTimelinePositionStrategy(clock);

        var anchor = Sample(AppleMusic, playing: true, raw: 10, extrapolated: 10, updated: Origin);
        var p1 = strategy.SelectPosition(in anchor);

        clock.AdvanceSeconds(1);
        var p2 = strategy.SelectPosition(
            Sample(AppleMusic, true, 12.5, 12.5, updated: Origin.AddSeconds(1)));

        clock.AdvanceSeconds(1);
        var p3 = strategy.SelectPosition(
            Sample(AppleMusic, true, 13.5, 13.5, updated: Origin.AddSeconds(2)));

        clock.AdvanceSeconds(1);
        var p4 = strategy.SelectPosition(
            Sample(AppleMusic, true, 14.5, 14.5, updated: Origin.AddSeconds(3)));

        clock.AdvanceSeconds(1);
        var p5 = strategy.SelectPosition(
            Sample(AppleMusic, true, 15.5, 15.5, updated: Origin.AddSeconds(4)));

        Assert.Equal(TimeSpan.FromSeconds(10), p1);
        Assert.Equal(TimeSpan.FromSeconds(11), p2);
        Assert.Equal(TimeSpan.FromSeconds(12), p3);

        // 第 3 次同向偏差之后才开始调速，所以 p3→p4 仍是 1x。
        Assert.Equal(TimeSpan.FromSeconds(13), p4);

        // 速率上限 +5%，一秒钟只走 1.05 秒。
        Assert.Equal(TimeSpan.FromSeconds(14.05), p5);
    }

    [Fact]
    public void AppleStrategy_DriftCorrectionResetsWhenDeviationShrinksBelowResolution()
    {
        // 偏差回落到一个分辨率周期以内 = 量化噪声，不是漂移，速率必须归位。
        // 注意这里的位置必须仍然单调前进：一旦回退，代码会先走「重新锚定」
        // 分支，测不到 UpdateCorrection 里的归零逻辑。
        var clock = new ManualTimeProvider();
        var strategy = new AppleMusicTimelinePositionStrategy(clock);
        strategy.SelectPosition(Sample(AppleMusic, true, 10, 10, updated: Origin));

        clock.AdvanceSeconds(1);
        strategy.SelectPosition(Sample(AppleMusic, true, 12.5, 12.5, updated: Origin.AddSeconds(1)));

        clock.AdvanceSeconds(1);
        strategy.SelectPosition(Sample(AppleMusic, true, 13.5, 13.5, updated: Origin.AddSeconds(2)));

        clock.AdvanceSeconds(1);
        var beforeReset = strategy.SelectPosition(
            Sample(AppleMusic, true, 14.5, 14.5, updated: Origin.AddSeconds(3)));

        // 速率此时已被拉到 1.05。这次播放器报 14.8，与预测 14.05 相差 0.75 秒，
        // 落在一个分辨率周期内 → 视为噪声，速率归 1。
        clock.AdvanceSeconds(1);
        var noisy = strategy.SelectPosition(
            Sample(AppleMusic, true, 14.8, 14.8, updated: Origin.AddSeconds(4)));

        clock.AdvanceSeconds(1);
        var afterReset = strategy.SelectPosition(
            Sample(AppleMusic, true, 15.8, 15.8, updated: Origin.AddSeconds(5)));

        Assert.Equal(TimeSpan.FromSeconds(13), beforeReset);
        Assert.Equal(TimeSpan.FromSeconds(14.05), noisy);

        // 速率已归 1：一秒只走一秒（若仍是 1.05，这里会是 1.05 秒）。
        Assert.Equal(TimeSpan.FromSeconds(1), afterReset - noisy);
    }

    [Fact]
    public void AppleStrategy_PausedTimelineKeepsPositionFrozen()
    {
        var clock = new ManualTimeProvider();
        var strategy = new AppleMusicTimelinePositionStrategy(clock);
        strategy.SelectPosition(Sample(AppleMusic, true, 10, 10, updated: Origin));

        clock.AdvanceSeconds(1);
        var paused = Sample(
            AppleMusic,
            playing: false,
            raw: 10,
            extrapolated: 11,
            updated: Origin.AddSeconds(1));

        var p1 = strategy.SelectPosition(in paused);

        // 暂停后时间继续走，但位置不能动。
        clock.AdvanceSeconds(2);
        var stillPaused = Sample(
            AppleMusic,
            playing: false,
            raw: 10,
            extrapolated: 13,
            updated: Origin.AddSeconds(1));

        var p2 = strategy.SelectPosition(in stillPaused);

        Assert.Equal(p1, p2);
    }

    [Fact]
    public void AppleStrategy_PausedSeekIsAdopted()
    {
        var clock = new ManualTimeProvider();
        var strategy = new AppleMusicTimelinePositionStrategy(clock);
        strategy.SelectPosition(Sample(AppleMusic, false, 10, 10, updated: Origin));

        // 暂停态下位置大幅变化 = 用户拖动，必须采纳。
        clock.AdvanceSeconds(1);
        var dragged = Sample(
            AppleMusic,
            playing: false,
            raw: 60,
            extrapolated: 60,
            updated: Origin.AddSeconds(1));

        Assert.Equal(TimeSpan.FromSeconds(60), strategy.SelectPosition(in dragged));
    }

    [Fact]
    public void AppleStrategy_ResumeWithoutFreshTimelineHoldsPosition()
    {
        var clock = new ManualTimeProvider();
        var strategy = new AppleMusicTimelinePositionStrategy(clock);
        strategy.SelectPosition(Sample(AppleMusic, false, 10, 10, updated: Origin));

        // 用户按下播放，但播放器还没给新时间轴：不能自己往前跑。
        clock.AdvanceSeconds(1);
        var resumed = Sample(
            AppleMusic,
            playing: true,
            raw: 10,
            extrapolated: 11,
            updated: Origin);

        Assert.Equal(TimeSpan.FromSeconds(10), strategy.SelectPosition(in resumed));
    }

    [Fact]
    public void AppleStrategy_SeekPinsPositionAgainstStaleTimeline()
    {
        var clock = new ManualTimeProvider();
        var strategy = new AppleMusicTimelinePositionStrategy(clock);
        strategy.SelectPosition(Sample(AppleMusic, true, 10, 10, updated: Origin));

        clock.AdvanceSeconds(0.5);
        Assert.True(strategy.TryApplySeek(AppleMusic, TimeSpan.FromSeconds(60), Origin.AddSeconds(0.5)));

        // 播放器还在报旧时间轴（10 秒），不能让歌词跳回去。
        var stale = Sample(
            AppleMusic,
            playing: true,
            raw: 10,
            extrapolated: 10,
            updated: Origin.AddSeconds(0.5));

        var pinned = strategy.SelectPosition(in stale);

        Assert.True(pinned >= TimeSpan.FromSeconds(60), $"seek 后位置被旧时间轴覆盖：{pinned}");
        Assert.True(pinned <= TimeSpan.FromSeconds(61), $"seek 后位置漂移过大：{pinned}");
    }

    [Fact]
    public void AppleStrategy_SeekGivesUpAfterConfirmationTimeoutAndReanchors()
    {
        var clock = new ManualTimeProvider();
        var strategy = new AppleMusicTimelinePositionStrategy(clock);
        strategy.SelectPosition(Sample(AppleMusic, true, 10, 10, updated: Origin));

        clock.AdvanceSeconds(0.5);
        strategy.TryApplySeek(AppleMusic, TimeSpan.FromSeconds(60), Origin.AddSeconds(0.5));

        // 两秒内播放器始终没确认 seek，放弃接管，回到播放器报的位置。
        clock.AdvanceSeconds(2.5);
        var stale = Sample(
            AppleMusic,
            playing: true,
            raw: 10,
            extrapolated: 10,
            updated: Origin.AddSeconds(0.5));

        Assert.Equal(TimeSpan.FromSeconds(10), strategy.SelectPosition(in stale));
    }

    [Fact]
    public void AppleStrategy_TryApplySeekRejectsUnknownSources()
    {
        var clock = new ManualTimeProvider();
        var strategy = new AppleMusicTimelinePositionStrategy(clock);
        strategy.SelectPosition(Sample(AppleMusic, true, 10, 10, updated: Origin));

        Assert.False(strategy.TryApplySeek("Spotify.exe", TimeSpan.FromSeconds(60), Origin));
    }

    [Fact]
    public void AppleStrategy_TryApplySeekRejectsBeforeAnySample()
    {
        var strategy = new AppleMusicTimelinePositionStrategy(new ManualTimeProvider());

        Assert.False(strategy.TryApplySeek(AppleMusic, TimeSpan.FromSeconds(60), Origin));
    }

    [Fact]
    public void AppleStrategy_TrackChangeDiscardsPreviousState()
    {
        var clock = new ManualTimeProvider();
        var strategy = new AppleMusicTimelinePositionStrategy(clock);
        strategy.SelectPosition(Sample(AppleMusic, true, 10, 10, updated: Origin));

        clock.AdvanceSeconds(0.5);
        var nextTrack = new TimelineSample(
            AppleMusic,
            "Another Song",
            "Another Artist",
            IsPlaying: true,
            RawPosition: TimeSpan.FromSeconds(3),
            LastUpdatedTimeUtc: Origin.AddSeconds(0.5),
            LastUpdateAge: TimeSpan.Zero,
            ExtrapolatedPosition: TimeSpan.FromSeconds(3));

        Assert.Equal(TimeSpan.FromSeconds(3), strategy.SelectPosition(in nextTrack));
    }

    [Fact]
    public void AppleStrategy_NegativePositionClampsToZero()
    {
        var strategy = new AppleMusicTimelinePositionStrategy(new ManualTimeProvider());
        var sample = Sample(AppleMusic, playing: false, raw: -2, extrapolated: -2);

        Assert.Equal(TimeSpan.Zero, strategy.SelectPosition(in sample));
    }

    // ── 注册表 ─────────────────────────────────────────────────────────────

    [Fact]
    public void Registry_PicksAppleMusicStrategyForAppleMusic()
    {
        var registry = TimelinePositionStrategyRegistry.CreateDefault(new ManualTimeProvider());
        var sample = Sample(AppleMusic, true, 10, 10);

        Assert.Equal("AppleMusicContinuous", registry.Select(in sample).StrategyName);
    }

    [Theory]
    [InlineData("SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify")]
    [InlineData("cloudmusic.exe")]
    [InlineData("QQMusic.exe")]
    [InlineData("KuGou.exe")]
    public void Registry_PicksCommonStrategyForTheFourPlayers(string source)
    {
        var registry = TimelinePositionStrategyRegistry.CreateDefault(new ManualTimeProvider());
        var sample = Sample(source, true, 10, 10);

        Assert.Equal("CommonExtrapolated", registry.Select(in sample).StrategyName);
    }

    [Fact]
    public void Registry_FallsBackToDefaultStrategyForUnknownPlayer()
    {
        var registry = TimelinePositionStrategyRegistry.CreateDefault(new ManualTimeProvider());
        var sample = Sample(UnknownPlayer, true, 10, 10);

        Assert.Equal("DefaultExtrapolated", registry.Select(in sample).StrategyName);
    }

    [Fact]
    public void Registry_KeepsPositionWhenPausedTimelineHasNotRefreshed()
    {
        // 暂停的瞬间播放器往往还会再报一两次旧时间轴。时间戳没变就说明
        // 位置不可能变，不能采纳。
        var registry = TimelinePositionStrategyRegistry.CreateDefault(new ManualTimeProvider());

        var playing = Sample(UnknownPlayer, true, 10, 10, updated: Origin);
        Assert.Equal(TimeSpan.FromSeconds(10), registry.Select(in playing).Position);

        var pausedStale = Sample(UnknownPlayer, false, 10, 10, updated: Origin);
        Assert.Equal(TimeSpan.FromSeconds(10), registry.Select(in pausedStale).Position);
    }

    [Fact]
    public void Registry_DefersSmallForwardAdvanceWhilePaused()
    {
        // 暂停后位置继续前进 0.3 秒 = 残留时间轴，忽略；否则歌词会多滚一句。
        var registry = TimelinePositionStrategyRegistry.CreateDefault(new ManualTimeProvider());

        registry.Select(Sample(UnknownPlayer, true, 10, 10, updated: Origin));
        var paused = Sample(UnknownPlayer, false, 10.3, 10.3, updated: Origin.AddSeconds(1));

        Assert.Equal(TimeSpan.FromSeconds(10), registry.Select(in paused).Position);
    }

    [Fact]
    public void Registry_AdoptsLargeForwardAdvanceWhilePaused()
    {
        // 暂停态下前进 3 秒只可能是用户拖动进度条，必须采纳。
        var registry = TimelinePositionStrategyRegistry.CreateDefault(new ManualTimeProvider());

        registry.Select(Sample(UnknownPlayer, true, 10, 10, updated: Origin));
        var dragged = Sample(UnknownPlayer, false, 13, 13, updated: Origin.AddSeconds(1));

        Assert.Equal(TimeSpan.FromSeconds(13), registry.Select(in dragged).Position);
    }

    [Fact]
    public void Registry_HoldsPositionWhenPlaybackResumesWithoutFreshTimeline()
    {
        var registry = TimelinePositionStrategyRegistry.CreateDefault(new ManualTimeProvider());

        registry.Select(Sample(UnknownPlayer, false, 10, 10, updated: Origin));

        // 播放态但时间戳没刷新：外推值 10.5 不能直接用。
        var resumedStale = Sample(UnknownPlayer, true, 10, 10.5, updated: Origin);
        Assert.Equal(TimeSpan.FromSeconds(10), registry.Select(in resumedStale).Position);

        // 时间戳刷新后才接受新位置。
        var resumedFresh = Sample(UnknownPlayer, true, 11, 11.5, updated: Origin.AddSeconds(1));
        Assert.Equal(TimeSpan.FromSeconds(11.5), registry.Select(in resumedFresh).Position);
    }

    [Fact]
    public void Registry_NewTrackResetsPreservationState()
    {
        var registry = TimelinePositionStrategyRegistry.CreateDefault(new ManualTimeProvider());

        registry.Select(Sample(UnknownPlayer, true, 10, 10, updated: Origin));

        // 换歌后即便时间戳没变，也必须采纳新曲目的位置。
        var nextTrack = new TimelineSample(
            UnknownPlayer,
            "Another Song",
            "Another Artist",
            IsPlaying: false,
            RawPosition: TimeSpan.Zero,
            LastUpdatedTimeUtc: Origin,
            LastUpdateAge: TimeSpan.Zero,
            ExtrapolatedPosition: TimeSpan.Zero);

        Assert.Equal(TimeSpan.Zero, registry.Select(in nextTrack).Position);
    }

    [Fact]
    public void Registry_NotifySeekIsDelegatedToActiveStrategy()
    {
        var registry = TimelinePositionStrategyRegistry.CreateDefault(new ManualTimeProvider());
        registry.Select(Sample(AppleMusic, true, 10, 10, updated: Origin));

        Assert.True(registry.NotifySeek(AppleMusic, TimeSpan.FromSeconds(60), Origin.AddSeconds(1)));
    }

    [Fact]
    public void Registry_NotifySeekReturnsFalseWhenNoStrategyIsActive()
    {
        var registry = TimelinePositionStrategyRegistry.CreateDefault(new ManualTimeProvider());

        Assert.False(registry.NotifySeek(AppleMusic, TimeSpan.FromSeconds(60), Origin));
    }

    [Fact]
    public void Registry_ResetClearsPreservationState()
    {
        var registry = TimelinePositionStrategyRegistry.CreateDefault(new ManualTimeProvider());
        registry.Select(Sample(UnknownPlayer, true, 10, 10, updated: Origin));
        registry.Reset();

        // Reset 之后应回到「首次采样」语义：直接采纳，不做保位。
        var sample = Sample(UnknownPlayer, false, 10.3, 10.3, updated: Origin.AddSeconds(1));
        Assert.Equal(TimeSpan.FromSeconds(10.3), registry.Select(in sample).Position);
    }

    [Fact]
    public void Registry_ConstructorRejectsNullArguments()
    {
        Assert.Throws<ArgumentNullException>(
            () => new TimelinePositionStrategyRegistry(null!, new DefaultExtrapolatedTimelinePositionStrategy()));
        Assert.Throws<ArgumentNullException>(
            () => new TimelinePositionStrategyRegistry([], null!));
    }

    // ── 辅助 ───────────────────────────────────────────────────────────────

    private static TimelineSample Sample(
        string source,
        bool playing,
        double raw,
        double extrapolated,
        double age = 0,
        DateTimeOffset? updated = null,
        string title = "Midnight City",
        string artist = "M83") =>
        new(
            source,
            title,
            artist,
            playing,
            TimeSpan.FromSeconds(raw),
            updated ?? Origin,
            TimeSpan.FromSeconds(age),
            TimeSpan.FromSeconds(extrapolated));
}
