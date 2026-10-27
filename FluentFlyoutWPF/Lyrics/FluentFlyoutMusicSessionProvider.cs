// Copyright (c) 2026 anync
// SPDX-License-Identifier: MIT
//
// 本文件为本仓库新增，不源自 TaskbarLyrics。

using FluentFlyout.Classes.Utils;
using FluentFlyout.Lyrics.Abstractions;
using FluentFlyout.Lyrics.Models;
using FluentFlyout.Lyrics.Timeline;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using Windows.Media.Control;
using static WindowsMediaController.MediaManager;

namespace FluentFlyout.Lyrics.Adapter;

/// <summary>
/// 把本工程的 <c>MediaManager</c> 接到歌词引擎的 <see cref="IMusicSessionProvider"/> 上。
///
/// 设计取舍：**会话选择逻辑不在这里重写**，而是回调用方提供的
/// <c>selectSession</c>（即 <c>MainWindow.GetActiveMediaSession()</c>）。
/// 理由：FF 已经有「钉住 → 聚焦 → 首个允许」这套语义，还叠加了应用黑白名单
/// 与 pinned 失效清理。在这里复制一份必然与主界面分叉，用户会看到
/// 「歌词显示的是另一个播放器」。<see cref="SessionSelectionPolicy"/> 只用于
/// 单测覆盖那套语义，生产路径直接复用主窗口实现。
/// </summary>
internal sealed class FluentFlyoutMusicSessionProvider : IMusicSessionProvider
{
    /// <summary>
    /// 时间轴最后更新时刻到采样时刻的最大合理间隔。
    ///
    /// 超过这个值说明播放器已经很久没刷新时间轴（切歌间隙、播放器假死），
    /// 此时按「年龄」外推会得到一个明显偏大的位置，歌词会跳到后面去。
    /// 取 30 秒：正常播放时 SMTC 至少每秒刷新一次，远小于此值。
    /// </summary>
    private static readonly TimeSpan MaximumTimelineAge = TimeSpan.FromSeconds(30);

    private readonly Func<MediaSession?> _selectSession;
    private readonly TimelinePositionStrategyRegistry _timelineRegistry;

    public FluentFlyoutMusicSessionProvider(
        Func<MediaSession?> selectSession,
        TimelinePositionStrategyRegistry? timelineRegistry = null)
    {
        _selectSession = selectSession ?? throw new ArgumentNullException(nameof(selectSession));
        _timelineRegistry = timelineRegistry ?? TimelinePositionStrategyRegistry.CreateDefault();
    }

    /// <summary>最近一次采样得到的会话 Id，供诊断与 seek 使用。</summary>
    public string? LastSessionId { get; private set; }

    /// <summary>
    /// 通知引擎用户拖动了进度条。返回 true 表示引擎已接管，
    /// 调用方不必再自行纠正位置。
    /// </summary>
    public bool NotifySeek(TimeSpan position)
    {
        if (LastSessionId is not { Length: > 0 } sessionId)
        {
            return false;
        }

        return _timelineRegistry.NotifySeek(sessionId, position, DateTimeOffset.UtcNow);
    }

    public Task<PlaybackSnapshot> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // 会话选择与媒体属性读取都是同步 COM 调用，放在调用线程上完成，
        // 由宿主（帧泵）决定在哪个线程跑。这里不做 Task.Run，
        // 避免每次 33ms 都额外压一个线程池任务。
        var session = _selectSession();
        if (session?.ControlSession is not { } control)
        {
            LastSessionId = null;
            _timelineRegistry.Reset();
            return Task.FromResult(new PlaybackSnapshot(false, TimeSpan.Zero, Track: null));
        }

        var sessionId = session.Id ?? string.Empty;
        if (!string.Equals(LastSessionId, sessionId, StringComparison.Ordinal))
        {
            // 换会话时清空策略状态，否则会拿上一个播放器的时间轴做漂移校正。
            _timelineRegistry.Reset();
            LastSessionId = sessionId;
        }

        var properties = TryGetMediaProperties(control);
        if (properties is null)
        {
            return Task.FromResult(new PlaybackSnapshot(false, TimeSpan.Zero, Track: null));
        }

        var playback = TryGetPlaybackInfo(control);
        var isPlaying = playback?.PlaybackStatus
            == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

        var timeline = TryGetTimelineProperties(control);
        var (rawPosition, lastUpdatedUtc, age) = ReadTimeline(timeline);
        var extrapolated = isPlaying ? rawPosition + age : rawPosition;

        var sample = new TimelineSample(
            sessionId,
            properties.Title ?? string.Empty,
            properties.Artist ?? string.Empty,
            isPlaying,
            rawPosition,
            lastUpdatedUtc,
            age,
            extrapolated);

        var position = _timelineRegistry.Select(sample).Position;

        var track = new TrackInfo(
            // Id 用「会话 + 标题 + 艺术家」：SMTC 不提供曲目唯一 Id，
            // 而换歌判定只需要「同一首歌稳定、不同歌不同」。
            BuildTrackId(sessionId, properties.Title, properties.Artist),
            properties.Title ?? string.Empty,
            properties.Artist ?? string.Empty,
            properties.AlbumTitle ?? string.Empty,
            // SourceApp 必须是 AUMID：LyricSourceRoutingPolicy 靠它识别
            // QQ 音乐 / 网易云 / 酷狗 的官方歌词源。
            sessionId,
            timeline?.EndTime ?? TimeSpan.Zero);

        return Task.FromResult(new PlaybackSnapshot(
            isPlaying,
            position,
            track,
            CoverImageBytes: ReadCoverBytes(properties.Thumbnail),
            RawPosition: rawPosition,
            ExtrapolatedPosition: extrapolated,
            IsCoverLoading: false,
            SourceAppUserModelId: sessionId,
            // SMTC 的 PlaybackControls 没有 IsSeekEnabled 这类成员，
            // FF 现有做法是看 MaxSeekTime（MainWindow.xaml.cs:1329），这里沿用同一判据。
            CanSeek: timeline?.MaxSeekTime.TotalSeconds >= 1.0));
    }

    /// <summary>
    /// 读出时间轴的三要素：原始位置、最后更新时刻、以及两者的年龄。
    ///
    /// <c>LastUpdatedTime</c> 是 <see cref="DateTimeOffset"/>，由播放器按本地时区填写；
    /// 这里统一按 UTC 比较，避免跨时区/夏令时造成的负年龄。
    /// </summary>
    private static (TimeSpan RawPosition, DateTimeOffset LastUpdatedUtc, TimeSpan Age) ReadTimeline(
        GlobalSystemMediaTransportControlsSessionTimelineProperties? timeline)
    {
        if (timeline is null)
        {
            return (TimeSpan.Zero, DateTimeOffset.UtcNow, TimeSpan.Zero);
        }

        var raw = timeline.Position;
        if (raw < TimeSpan.Zero)
        {
            raw = TimeSpan.Zero;
        }

        var lastUpdated = timeline.LastUpdatedTime;
        var age = DateTimeOffset.UtcNow - lastUpdated;

        // 时钟回拨或播放器给了未来时间：年龄不可信，按 0 处理。
        if (age < TimeSpan.Zero)
        {
            age = TimeSpan.Zero;
        }
        else if (age > MaximumTimelineAge)
        {
            // 太久没刷新：不补外推，让策略只用原始位置。
            age = TimeSpan.Zero;
        }

        return (raw, lastUpdated, age);
    }

    /// <summary>
    /// 封面转 byte[]。
    ///
    /// <c>BitmapHelper.GetThumbnail</c> 内部会 <c>Freeze()</c>，所以拿到的
    /// <see cref="BitmapImage"/> 可以跨线程读。这里用 PNG 编码而不是原始像素，
    /// 因为歌词引擎只把封面当「给 UI 的不透明载荷」透传，PNG 更省内存。
    /// </summary>
    /// <remarks>
    /// 类型必须写 <c>global::Windows.Storage...</c>：本工程引用了
    /// <c>FluentFlyout.Windows</c> 命名空间，裸写 <c>Windows.*</c> 会被它遮蔽，
    /// 报「命名空间 FluentFlyout.Windows 中不存在 Storage」。
    /// </remarks>
    private static byte[]? ReadCoverBytes(
        global::Windows.Storage.Streams.IRandomAccessStreamReference? thumbnail)
    {
        if (thumbnail is null)
        {
            return null;
        }

        try
        {
            var image = BitmapHelper.GetThumbnail(thumbnail);
            if (image is null)
            {
                return null;
            }

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));

            using var buffer = new MemoryStream();
            encoder.Save(buffer);
            return buffer.ToArray();
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or InvalidOperationException)
        {
            // 封面只是装饰，取不到就不给，绝不能让它影响歌词主链路。
            return null;
        }
    }

    /// <summary>
    /// SMTC 不提供曲目唯一 Id，用「会话 + 标题 + 艺术家」拼一个稳定值。
    /// 同一首歌多次采样必须得到相同 Id，否则歌词会被反复重新检索。
    /// </summary>
    private static string BuildTrackId(string sessionId, string? title, string? artist) =>
        string.Join('|', sessionId, title ?? string.Empty, artist ?? string.Empty);

    private static GlobalSystemMediaTransportControlsSessionMediaProperties? TryGetMediaProperties(
        GlobalSystemMediaTransportControlsSession control)
    {
        try
        {
            return control.TryGetMediaPropertiesAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or UnauthorizedAccessException)
        {
            // 播放器正在退出时常见：会话还在，属性已经取不到了。
            return null;
        }
    }

    private static GlobalSystemMediaTransportControlsSessionPlaybackInfo? TryGetPlaybackInfo(
        GlobalSystemMediaTransportControlsSession control)
    {
        try
        {
            return control.GetPlaybackInfo();
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            return null;
        }
    }

    private static GlobalSystemMediaTransportControlsSessionTimelineProperties? TryGetTimelineProperties(
        GlobalSystemMediaTransportControlsSession control)
    {
        try
        {
            return control.GetTimelineProperties();
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            return null;
        }
    }
}