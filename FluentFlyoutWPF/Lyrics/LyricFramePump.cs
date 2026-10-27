// Copyright (c) 2026 anync
// SPDX-License-Identifier: MIT
//
// 本文件为本仓库新增，不源自 TaskbarLyrics。

using FluentFlyout.Lyrics.Abstractions;
using FluentFlyout.Lyrics.Frames;
using FluentFlyout.Lyrics.Models;
using FluentFlyout.Lyrics.Services;
using System.Windows.Threading;

namespace FluentFlyout.Lyrics.Adapter;

/// <summary>
/// 帧泵的 WPF 外壳：<see cref="DispatcherTimer"/> 驱动 + 结果派发到 UI 线程。
///
/// 真正的节流与去重逻辑在 <see cref="LyricFramePumpCore"/> 里（纯逻辑、可单测）。
/// 这里只做三件 WPF 专属的事：
/// 1. 用 <see cref="DispatcherTimer"/> 按 <see cref="LyricFramePumpCore.DefaultInterval"/> 打拍；
/// 2. 保证同一时刻只有一次取帧在跑（<see cref="_isTicking"/>），避免慢帧堆积；
/// 3. 把帧投递回 UI 线程再交给渲染层。
///
/// 为什么不用 <c>DispatcherTimer</c> 的 Tick 直接 await：Tick 是同步回调，
/// 内部 await 会让异常逃出 WPF 的异常边界。这里改成 fire-and-forget 但自己
/// 兜住所有异常，保证一次取帧失败不会杀掉整个定时器。
/// </summary>
internal sealed class LyricFramePump : IDisposable
{
    private readonly LyricFramePumpCore _core;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _timer;
    private bool _isTicking;
    private bool _isDisposed;

    /// <summary>
    /// 帧就绪。始终在 UI 线程上触发，接收方可以直接改可视树。
    /// </summary>
    public event EventHandler<LyricDisplayFrame>? FrameReady;

    /// <summary>
    /// 取帧过程出错。在 UI 线程上触发，参数为异常信息。
    /// </summary>
    public event EventHandler<Exception>? TickFailed;

    public LyricFramePump(
        IMusicSessionProvider sessionProvider,
        LyricSyncService syncService,
        Dispatcher dispatcher,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(sessionProvider);
        ArgumentNullException.ThrowIfNull(syncService);
        ArgumentNullException.ThrowIfNull(dispatcher);

        _dispatcher = dispatcher;

        // 快照获取直接同步调用：provider 内部走的是 SMTC 的同步 COM API
        // （GetPlaybackInfo / GetTimelineProperties），返回的 Task 已经是完成态。
        // 不要套 Task.Run(...).GetResult()——那会让 UI 线程去等线程池，
        // 白白多一次切换，还可能在 COM 需要消息泵时卡住。
        // 这也与 FF 现有代码一致：MediaManager_OnAnyMediaPropertyChanged
        // 就是在 UI 线程上直接调 TryGetMediaProperties。
        _core = new LyricFramePumpCore(
            () => sessionProvider.GetCurrentAsync().GetAwaiter().GetResult(),
            syncService,
            timeProvider: timeProvider);

        _timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
        {
            Interval = _core.Interval
        };
        _timer.Tick += OnTimerTick;
    }

    public bool IsRunning => _timer.IsEnabled;

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        if (_timer.IsEnabled)
        {
            return;
        }

        _core.Start();
        _timer.Start();
    }

    public void Stop()
    {
        if (_isDisposed)
        {
            return;
        }

        _timer.Stop();
        _core.Stop();
    }

    private async void OnTimerTick(object? sender, EventArgs e)
    {
        // 上一帧还没取完就跳过：慢帧不能堆积成队列，
        // 否则用户切歌时会看到一串过期帧依次闪过。
        if (_isTicking || _isDisposed)
        {
            return;
        }

        _isTicking = true;
        try
        {
            var frame = await _core.TickAsync().ConfigureAwait(true);
            if (frame is null || _isDisposed)
            {
                return;
            }

            // ConfigureAwait(true) 已经回到 UI 线程；这里再确认一次，
            // 防止宿主用非 UI Dispatcher 构造时静默在错误线程上改可视树。
            // BeginInvoke 的返回值刻意丢弃：这是一次性投递，没有需要等待的语义。
            if (!_dispatcher.CheckAccess())
            {
                _ = _dispatcher.BeginInvoke(() => FrameReady?.Invoke(this, frame));
                return;
            }

            FrameReady?.Invoke(this, frame);
        }
        catch (ObjectDisposedException)
        {
            // Dispose 与 Tick 竞争，正常收尾。
        }
        catch (OperationCanceledException)
        {
            // 同上。
        }
        catch (Exception ex)
        {
            // 一次取帧失败不能让定时器停摆。
            TickFailed?.Invoke(this, ex);
        }
        finally
        {
            _isTicking = false;
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _timer.Stop();
        _timer.Tick -= OnTimerTick;
        _core.Dispose();
    }
}