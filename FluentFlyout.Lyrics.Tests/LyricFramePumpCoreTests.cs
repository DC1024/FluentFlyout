// Copyright (c) 2026 anync
// SPDX-License-Identifier: MIT
//
// 本文件为本仓库新增，不源自 TaskbarLyrics。

using FluentFlyout.Lyrics.Abstractions;
using FluentFlyout.Lyrics.Frames;
using FluentFlyout.Lyrics.Models;
using FluentFlyout.Lyrics.Services;
using Xunit;

namespace FluentFlyout.Lyrics.Tests;

/// <summary>
/// 帧泵核心的测试。
///
/// 帧泵把「什么时候取帧」和「取了之后推不推」绑在一起，两者都靠
/// <see cref="ManualTimeProvider"/> 精确控制，因此这些用例是确定性的，
/// 不需要 <c>Task.Delay</c> 之类的等待。
/// </summary>
public sealed class LyricFramePumpCoreTests
{
    [Fact]
    public void DefaultIntervalMatchesFluentFlyoutSpectrumTimer()
    {
        // 与主工程的频谱定时器共用 33ms，避免两个高频定时器叠加。
        Assert.Equal(TimeSpan.FromMilliseconds(33), LyricFramePumpCore.DefaultInterval);
    }

    [Fact]
    public async Task TickBeforeStartReturnsNullAndDoesNotTouchSyncService()
    {
        var clock = new ManualTimeProvider();
        var coordinator = new CountingCoordinator();
        using var sync = new LyricSyncService(coordinator, metadataStabilizationDelay: TimeSpan.Zero);
        using var pump = new LyricFramePumpCore(
            () => Snapshot(WithTrack: true), sync, timeProvider: clock);

        Assert.Null(await pump.TickAsync());
        Assert.Equal(0, coordinator.ResolveCallCount);
        Assert.False(pump.IsStarted);
    }

    [Fact]
    public async Task StartMakesThePumpTick()
    {
        var clock = new ManualTimeProvider();
        var coordinator = new CountingCoordinator();
        using var sync = new LyricSyncService(coordinator, metadataStabilizationDelay: TimeSpan.Zero);
        using var pump = new LyricFramePumpCore(
            () => Snapshot(WithTrack: true), sync, timeProvider: clock);

        pump.Start();
        var frame = await pump.TickAsync();

        Assert.True(pump.IsStarted);
        Assert.NotNull(frame);
        Assert.Equal(1, coordinator.ResolveCallCount);
    }

    [Fact]
    public async Task TicksFasterThanTheIntervalAreThrottled()
    {
        var clock = new ManualTimeProvider();
        var coordinator = new CountingCoordinator();
        using var sync = new LyricSyncService(coordinator, metadataStabilizationDelay: TimeSpan.Zero);
        var snapshotCalls = 0;
        using var pump = new LyricFramePumpCore(
            () =>
            {
                snapshotCalls++;
                return Snapshot(WithTrack: true);
            },
            sync,
            timeProvider: clock);

        pump.Start();
        Assert.NotNull(await pump.TickAsync());
        Assert.Equal(1, snapshotCalls);

        // 只过了 20ms，不足 33ms，直接丢弃。
        // 断言用快照调用次数而不是返回值：返回 null 也可能是被去重挡掉，
        // 只有「根本没走到取快照那一步」才证明是节流生效。
        clock.AdvanceMilliseconds(20);
        Assert.Null(await pump.TickAsync());
        Assert.Equal(1, pump.SkippedByThrottle);
        Assert.Equal(1, snapshotCalls);

        // 补齐到 33ms 之后放行，这次会真的去取快照。
        clock.AdvanceMilliseconds(13);
        await pump.TickAsync();
        Assert.Equal(1, pump.SkippedByThrottle);
        Assert.Equal(2, snapshotCalls);
    }

    [Fact]
    public async Task FirstTickAfterStartIsNotThrottledEvenAtTimestampZero()
    {
        // 时间戳从 0 起步时，若用 0 当「从未 tick」的哨兵，首帧会被误判为节流。
        var clock = new ManualTimeProvider();
        var coordinator = new CountingCoordinator();
        using var sync = new LyricSyncService(coordinator, metadataStabilizationDelay: TimeSpan.Zero);
        using var pump = new LyricFramePumpCore(
            () => Snapshot(WithTrack: true), sync, timeProvider: clock);

        pump.Start();

        Assert.NotNull(await pump.TickAsync());
        Assert.Equal(0, pump.SkippedByThrottle);
    }

    [Fact]
    public async Task RestartResetsThrottleSoTheFirstTickPassesImmediately()
    {
        var clock = new ManualTimeProvider();
        var coordinator = new CountingCoordinator();
        using var sync = new LyricSyncService(coordinator, metadataStabilizationDelay: TimeSpan.Zero);
        using var pump = new LyricFramePumpCore(
            () => Snapshot(WithTrack: true), sync, timeProvider: clock);

        pump.Start();
        await pump.TickAsync();
        pump.Stop();
        pump.Start();

        // 刚 Start 不应被上一轮的 tick 时间戳卡住。
        Assert.NotNull(await pump.TickAsync());
    }

    [Fact]
    public async Task MissingTrackShortCircuitsAndReturnsNull()
    {
        var clock = new ManualTimeProvider();
        var coordinator = new CountingCoordinator();
        using var sync = new LyricSyncService(coordinator, metadataStabilizationDelay: TimeSpan.Zero);
        using var pump = new LyricFramePumpCore(
            () => Snapshot(WithTrack: false), sync, timeProvider: clock);

        pump.Start();

        Assert.Null(await pump.TickAsync());
        Assert.Equal(0, coordinator.ResolveCallCount);
    }

    [Fact]
    public async Task TrackReappearingAfterAGapEmitsImmediately()
    {
        // 停播再开播时，去重器必须已复位，否则第一帧会被当成「无变化」丢掉。
        var clock = new ManualTimeProvider();
        var coordinator = new CountingCoordinator();
        using var sync = new LyricSyncService(coordinator, metadataStabilizationDelay: TimeSpan.Zero);
        var hasTrack = true;
        using var pump = new LyricFramePumpCore(
            () => Snapshot(hasTrack), sync, timeProvider: clock);

        pump.Start();
        Assert.NotNull(await pump.TickAsync());

        hasTrack = false;
        clock.AdvanceMilliseconds(40);
        Assert.Null(await pump.TickAsync());

        hasTrack = true;
        clock.AdvanceMilliseconds(40);
        Assert.NotNull(await pump.TickAsync());
    }

    [Fact]
    public async Task UnchangedFrameIsSuppressedOnSubsequentTicks()
    {
        // 位置完全不动时，歌词帧也应该完全一样 → 只推第一帧。
        var clock = new ManualTimeProvider();
        var coordinator = new CountingCoordinator();
        using var sync = new LyricSyncService(coordinator, metadataStabilizationDelay: TimeSpan.Zero);
        using var pump = new LyricFramePumpCore(
            () => Snapshot(WithTrack: true, position: 1), sync, timeProvider: clock);

        pump.Start();
        Assert.NotNull(await pump.TickAsync());

        clock.AdvanceMilliseconds(40);
        Assert.Null(await pump.TickAsync());

        clock.AdvanceMilliseconds(40);
        Assert.Null(await pump.TickAsync());
    }

    [Fact]
    public async Task PositionAdvanceCrossingALineEmitsANewFrame()
    {
        var clock = new ManualTimeProvider();
        var coordinator = new CountingCoordinator();
        using var sync = new LyricSyncService(coordinator, metadataStabilizationDelay: TimeSpan.Zero);
        var position = 1.0;
        using var pump = new LyricFramePumpCore(
            () => Snapshot(WithTrack: true, position: position), sync, timeProvider: clock);

        pump.Start();
        var first = await pump.TickAsync();
        Assert.Equal("Line one", first!.CurrentLine);

        // 跨到第二句。
        position = 6.0;
        clock.AdvanceMilliseconds(40);
        var second = await pump.TickAsync();

        Assert.NotNull(second);
        Assert.Equal("Line two", second.CurrentLine);
    }

    [Fact]
    public async Task StopPreventsFurtherTicks()
    {
        var clock = new ManualTimeProvider();
        var coordinator = new CountingCoordinator();
        using var sync = new LyricSyncService(coordinator, metadataStabilizationDelay: TimeSpan.Zero);
        using var pump = new LyricFramePumpCore(
            () => Snapshot(WithTrack: true), sync, timeProvider: clock);

        pump.Start();
        await pump.TickAsync();
        pump.Stop();

        clock.AdvanceMilliseconds(100);
        Assert.Null(await pump.TickAsync());
        Assert.False(pump.IsStarted);
    }

    [Fact]
    public async Task CancelledTokenPropagates()
    {
        var clock = new ManualTimeProvider();
        var coordinator = new CountingCoordinator();
        using var sync = new LyricSyncService(coordinator, metadataStabilizationDelay: TimeSpan.Zero);
        using var pump = new LyricFramePumpCore(
            () => Snapshot(WithTrack: true), sync, timeProvider: clock);

        pump.Start();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pump.TickAsync(cts.Token));
    }

    [Fact]
    public async Task TickAfterDisposeThrows()
    {
        var clock = new ManualTimeProvider();
        var coordinator = new CountingCoordinator();
        using var sync = new LyricSyncService(coordinator, metadataStabilizationDelay: TimeSpan.Zero);
        var pump = new LyricFramePumpCore(
            () => Snapshot(WithTrack: true), sync, timeProvider: clock);

        pump.Start();
        pump.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => pump.TickAsync());
    }

    [Fact]
    public void DoubleDisposeIsHarmless()
    {
        var coordinator = new CountingCoordinator();
        using var sync = new LyricSyncService(coordinator, metadataStabilizationDelay: TimeSpan.Zero);
        var pump = new LyricFramePumpCore(() => Snapshot(true), sync, timeProvider: new ManualTimeProvider());

        pump.Dispose();
        pump.Dispose();
    }

    [Fact]
    public void ConstructorRejectsNonPositiveInterval()
    {
        var coordinator = new CountingCoordinator();
        using var sync = new LyricSyncService(coordinator, metadataStabilizationDelay: TimeSpan.Zero);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new LyricFramePumpCore(
                () => Snapshot(true), sync, timeProvider: new ManualTimeProvider(), interval: TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new LyricFramePumpCore(
                () => Snapshot(true), sync, timeProvider: new ManualTimeProvider(), interval: TimeSpan.FromMilliseconds(-1)));
    }

    [Fact]
    public void ConstructorRejectsNullDependencies()
    {
        var coordinator = new CountingCoordinator();
        using var sync = new LyricSyncService(coordinator, metadataStabilizationDelay: TimeSpan.Zero);

        Assert.Throws<ArgumentNullException>(
            () => new LyricFramePumpCore(null!, sync));
        Assert.Throws<ArgumentNullException>(
            () => new LyricFramePumpCore(() => Snapshot(true), null!));
    }

    [Fact]
    public void CustomIntervalIsHonoured()
    {
        var coordinator = new CountingCoordinator();
        using var sync = new LyricSyncService(coordinator, metadataStabilizationDelay: TimeSpan.Zero);
        using var pump = new LyricFramePumpCore(
            () => Snapshot(true),
            sync,
            timeProvider: new ManualTimeProvider(),
            interval: TimeSpan.FromMilliseconds(100));

        Assert.Equal(TimeSpan.FromMilliseconds(100), pump.Interval);
    }

    // ── 辅助 ───────────────────────────────────────────────────────────────

    private static PlaybackSnapshot Snapshot(bool WithTrack, double position = 1)
    {
        var track = WithTrack
            ? new TrackInfo(
                "track-id",
                "Midnight City",
                "M83",
                "Hurry Up, We're Dreaming",
                "Spotify",
                TimeSpan.FromSeconds(244))
            : null;

        return new PlaybackSnapshot(true, TimeSpan.FromSeconds(position), track);
    }

    /// <summary>
    /// 立即返回固定两句歌词的协调器，让帧泵测试只关心节流与去重，
    /// 不牵扯真实检索流程。
    /// </summary>
    private sealed class CountingCoordinator : ILyricResolutionCoordinator
    {
        private readonly ResolvedLyrics _resolved = new(
            new ParsedLyrics(
                [
                    new ParsedLyricLine(TimeSpan.FromSeconds(1), null, "Line one"),
                    new ParsedLyricLine(TimeSpan.FromSeconds(5), null, "Line two")
                ],
                LyricTimingKind.LineTimed,
                LyricTimingProvenance.ProviderSupplied,
                LyricPayloadFormat.Lrc),
            new LyricProviderId("Test"),
            "candidate",
            LyricAcquisitionKind.Remote,
            new Dictionary<string, string>());

        public int ResolveCallCount { get; private set; }

        public Task<ResolvedLyrics?> ResolveAsync(
            TrackInfo track,
            CancellationToken cancellationToken = default)
        {
            ResolveCallCount++;
            return Task.FromResult<ResolvedLyrics?>(_resolved);
        }

        public void Dispose()
        {
        }
    }
}
