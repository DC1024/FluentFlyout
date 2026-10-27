// Copyright (c) 2026 anync
// SPDX-License-Identifier: MIT
//
// 本文件为本仓库新增，不源自 TaskbarLyrics。

using FluentFlyout.Lyrics.Abstractions;
using FluentFlyout.Lyrics.Models;
using FluentFlyout.Lyrics.Services;

namespace FluentFlyout.Lyrics.Frames;

/// <summary>
/// 帧泵的核心逻辑：按固定节拍从 <see cref="LyricSyncService"/> 取帧，
/// 去重后交给调用方。
///
/// 这个类刻意不碰 WPF 的 Dispatcher 与 DispatcherTimer——那些留在主工程。
/// 这里只负责「什么时候取、取了之后推不推」，因此可以用假的
/// <see cref="TimeProvider"/> 做完全确定性的单测。
///
/// 线程模型：<see cref="TickAsync"/> 由宿主在任意线程调用，内部不切换线程；
/// 派发到 UI 线程是宿主（WPF 层）的责任。
/// </summary>
public sealed class LyricFramePumpCore : IDisposable
{
    /// <summary>
    /// 默认节拍。与 FF 现有频谱定时器一致（33ms ≈ 30fps），
    /// 复用它可避免两个独立高频定时器叠加。
    /// </summary>
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromMilliseconds(33);

    private readonly Func<PlaybackSnapshot> _snapshotProvider;
    private readonly LyricSyncService _syncService;
    private readonly LyricFrameDeduplicator _deduplicator;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _interval;
    private readonly object _gate = new();

    // 用可空而不是 0 做「从未 tick」的哨兵：0 本身是合法时间戳
    // （Stopwatch 起步阶段、或测试里的假时钟），用 0 当哨兵会让首次节流判断失效。
    private long? _lastTickTimestamp;
    private bool _isStarted;
    private bool _isDisposed;
    private long _skippedByThrottle;

    public LyricFramePumpCore(
        Func<PlaybackSnapshot> snapshotProvider,
        LyricSyncService syncService,
        LyricFrameDeduplicator? deduplicator = null,
        TimeProvider? timeProvider = null,
        TimeSpan? interval = null)
    {
        _snapshotProvider = snapshotProvider ?? throw new ArgumentNullException(nameof(snapshotProvider));
        _syncService = syncService ?? throw new ArgumentNullException(nameof(syncService));
        _deduplicator = deduplicator ?? new LyricFrameDeduplicator();
        _timeProvider = timeProvider ?? TimeProvider.System;
        _interval = interval ?? DefaultInterval;

        if (_interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(interval), "帧间隔必须为正。");
        }
    }

    /// <summary>当前帧间隔。</summary>
    public TimeSpan Interval => _interval;

    /// <summary>因节流被跳过的 tick 数，用于诊断 CPU 占用是否异常。</summary>
    public long SkippedByThrottle => Interlocked.Read(ref _skippedByThrottle);

    /// <summary>泵是否处于运行态。</summary>
    public bool IsStarted
    {
        get
        {
            lock (_gate)
            {
                return _isStarted;
            }
        }
    }

    public void Start()
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            _isStarted = true;
            _lastTickTimestamp = null;
            _deduplicator.Reset();
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            _isStarted = false;
            _deduplicator.Reset();
        }
    }

    /// <summary>
    /// 执行一次取帧。返回 null 表示本次不推送（未启动、节流、或帧无变化）。
    ///
    /// 节流的意义：宿主可能用更密的定时器（比如跟随渲染帧率）调用，
    /// 但歌词不需要那么高的刷新率。低于 <see cref="Interval"/> 的调用直接丢弃。
    /// </summary>
    public async Task<LyricDisplayFrame?> TickAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            if (!_isStarted)
            {
                return null;
            }

            var now = _timeProvider.GetTimestamp();
            if (_lastTickTimestamp is { } lastTick
                && _timeProvider.GetElapsedTime(lastTick, now) < _interval)
            {
                Interlocked.Increment(ref _skippedByThrottle);
                return null;
            }

            _lastTickTimestamp = now;
        }

        var snapshot = _snapshotProvider();

        // 没有曲目时不必惊动歌词引擎：直接推空帧，并让去重器复位，
        // 保证下次有歌时第一帧一定能出去。
        if (snapshot.Track is null)
        {
            _deduplicator.Reset();
            return null;
        }

        var frame = await _syncService.GetDisplayFrameAsync(snapshot).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        return _deduplicator.ShouldEmit(frame) ? frame : null;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;
            _isStarted = false;
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
    }
}
