// Copyright (c) 2026 anync
// SPDX-License-Identifier: MIT
//
// 本文件为本仓库新增，不源自 TaskbarLyrics。

using FluentFlyout.Lyrics.Abstractions;
using FluentFlyout.Lyrics.Models;
using FluentFlyout.Lyrics.Services;
using System.Windows.Threading;
using static WindowsMediaController.MediaManager;

namespace FluentFlyout.Lyrics.Adapter;

/// <summary>
/// 歌词功能的装配根：把 Core 层的检索/解析/取帧链路接成一个可启停的整体。
///
/// 为什么要有这一层：P3 结束时渲染层已经能画了，但 <c>SetLyricsMode</c> /
/// <c>UpdateLyricsFrame</c> 在生产代码里**一个调用点都没有**——渲染代码
/// 只有在探针里跑过。这个类补的就是那段「谁在什么时候把帧喂给谁」。
///
/// 设计取舍：
/// <list type="bullet">
/// <item><b>不引入 DI 容器</b>。FF 全项目都是静态单例 + 构造函数直连，
/// 为一个功能单独引入容器会让后续上游合并变困难。</item>
/// <item><b>帧泵的生命周期跟服务走，不跟窗口走</b>。任务栏窗口会被
/// Explorer 重启、切屏、改设置反复重建（<c>MainWindow.RecreateTaskbarWindow</c>），
/// 而重新装配一遍检索链路意味着丢掉已解析的歌词文档、重发一次网络请求。
/// 所以泵只启动一次，窗口重建时只换「帧往哪送」。</item>
/// <item><b>关掉歌词就停泵</b>。33ms 的定时器空转着也要做一次 SMTC 采样，
/// 而采样是跨进程 COM 调用——用户没开歌词时不该有这份开销。</item>
/// </list>
/// </remarks>
internal sealed class LyricsService : IDisposable
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    private readonly FluentFlyoutMusicSessionProvider _sessionProvider;
    private readonly TrackLyricOffsetStore _offsetStore;
    private readonly JsonResolvedLyricCache _resolvedCache;
    private readonly LyricResolutionCoordinator _coordinator;
    private readonly LyricSyncService _syncService;
    private readonly LyricFramePump _pump;

    private bool _isDisposed;

    /// <summary>
    /// 帧就绪。始终在 UI 线程上触发。
    /// </summary>
    public event EventHandler<LyricDisplayFrame>? FrameReady;

    /// <summary>
    /// 当前曲目变化（换歌 / 停止播放）。参数为 <c>null</c> 表示没有曲目。
    ///
    /// 设置界面用它把「当前歌曲偏移」滑块的显示同步成新曲目的已存值。
    /// 由 <see cref="OnFrameReady"/> 顺带检测——不额外采样，
    /// 帧里本来就带 <c>Title</c>，够判断换没换歌。
    /// </summary>
    public event EventHandler<string?>? CurrentTrackChanged;

    /// <summary>最近一帧的曲目标题，用于检测换歌。</summary>
    private string? _lastFrameTitle;

    /// <summary>
    /// 当前曲目的歌词源（如 <c>qqmusic</c>）。偏移按「曲目 + 源」存，
    /// 写回时必须带上它，否则会存到另一条记录上。
    /// </summary>
    public string? CurrentLyricSource => _syncService.CurrentLyricSourceApp;

    /// <summary>
    /// 最近一次采样到的曲目。设置界面保存偏移时需要它做定位键。
    /// </summary>
    public TrackInfo? CurrentTrack { get; private set; }

    /// <summary>
    /// 读出当前曲目已保存的偏移（毫秒）。没有曲目或没有记录时返回 0。
    /// </summary>
    public int GetCurrentOffsetMilliseconds()
    {
        if (CurrentTrack is not { } track)
        {
            return 0;
        }

        return _offsetStore.GetOffsetMilliseconds(track, CurrentLyricSource);
    }

    /// <summary>
    /// 保存当前曲目的偏移（毫秒）。
    ///
    /// 异步落库，但**先同步更新内存里的值**——<see cref="LyricSyncService"/>
    /// 每帧现取偏移，内存值一改下一帧就生效，用户拖完滑块立刻看到效果，
    /// 不必等磁盘写完。
    /// </summary>
    /// <returns>true 表示已受理（内存已更新且落库任务已排队）。</returns>
    public bool SaveCurrentOffsetMilliseconds(int milliseconds)
    {
        if (_isDisposed || CurrentTrack is not { } track || CurrentLyricSource is not { Length: > 0 } source)
        {
            return false;
        }

        var clamped = Math.Clamp(
            milliseconds,
            TrackLyricOffsetStore.MinimumOffsetMilliseconds,
            TrackLyricOffsetStore.MaximumOffsetMilliseconds);

        // 不 await：写盘失败只记日志，不该让 UI 卡住或弹窗。
        // 内存值已由 store 同步更新，下一帧就生效。
        _ = _offsetStore.SetOffsetAsync(track, source, clamped).ContinueWith(
            task =>
            {
                if (task.IsCompletedSuccessfully && !task.Result.IsSaved)
                {
                    Logger.Warn("Failed to persist lyric offset: {0}", task.Result.ErrorMessage);
                }
                else if (task.IsFaulted)
                {
                    Logger.Warn(task.Exception, "Failed to persist lyric offset");
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        return true;
    }

    public LyricsService(Func<MediaSession?> selectSession, Dispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(selectSession);
        ArgumentNullException.ThrowIfNull(dispatcher);

        _sessionProvider = new FluentFlyoutMusicSessionProvider(selectSession);
        _offsetStore = new TrackLyricOffsetStore();
        _resolvedCache = JsonResolvedLyricCache.CreateDefault();

        _coordinator = CreateCoordinator(_resolvedCache);

        // 两级偏移：播放器级（同一个 App 的所有歌）+ 曲目级（单曲微调）。
        // 目前只有曲目级有 UI 入口（P4-9 的托盘菜单），播放器级先给 0——
        // 不编造一个用户没设置过的偏移，否则「第一次用就对不上」比不修更难查。
        _syncService = new LyricSyncService(
            _coordinator,
            getPlayerLeadTime: _ => TimeSpan.Zero,
            getTrackLeadTime: (track, lyricSource) =>
                TimeSpan.FromMilliseconds(_offsetStore.GetOffsetMilliseconds(track, lyricSource)));

        _pump = new LyricFramePump(_sessionProvider, _syncService, dispatcher);
        _pump.FrameReady += OnFrameReady;
        _pump.TickFailed += OnTickFailed;
    }

    /// <summary>
    /// 当前是否在取帧。
    /// </summary>
    public bool IsRunning => !_isDisposed && _pump.IsRunning;

    /// <summary>
    /// 按设置启停。设置变化与窗口重建都走这里，重复调用是幂等的。
    /// </summary>
    /// <remarks>
    /// 已释放时**静默返回**而不是抛异常：这个方法的调用方是
    /// <c>UserSettings</c> 的属性变更钩子与窗口 <c>Loaded</c>，
    /// 两者都可能在退出流程里最后跑一次（设置正在保存、窗口正在关闭）。
    /// 在那种时机抛 <see cref="ObjectDisposedException"/> 只会变成
    /// 一条无意义的错误日志，用户看不到，排查时还干扰判断。
    /// </remarks>
    public void SetEnabled(bool enabled)
    {
        if (_isDisposed)
        {
            return;
        }

        if (enabled)
        {
            _pump.Start();
        }
        else
        {
            _pump.Stop();
        }
    }

    /// <summary>
    /// 单曲偏移变化后调用。
    ///
    /// <b>当前是空实现</b>，而且是有意为之：偏移通过
    /// <c>LyricSyncService</c> 的 <c>getTrackLeadTime</c> 回调**每帧现取**，
    /// 用户拖完滑块下一帧就生效，不需要任何失效动作。
    ///
    /// 保留这个方法是为了让调用点显式表达「我改了偏移」——
    /// 将来如果加了偏移缓存（比如为了省掉每帧一次字典查找），
    /// 失效点已经在这里了，不必回头找齐所有调用方。
    /// </summary>
    public void NotifyOffsetChanged()
    {
    }

    private void OnFrameReady(object? sender, LyricDisplayFrame frame)
    {
        // 换歌检测顺带在这里做，不额外采样：帧里本来就带 Title，
        // 而「标题变了」在这个功能里等价于「换歌了」——
        // 同名不同歌会让偏移滑块显示错值，代价可接受（用户点一下就好），
        // 换来的是不必再维护一套独立的曲目比对逻辑。
        if (!string.Equals(_lastFrameTitle, frame.Title, StringComparison.Ordinal))
        {
            _lastFrameTitle = frame.Title;
            CurrentTrack = _sessionProvider.LastTrack;
            CurrentTrackChanged?.Invoke(this, frame.Title);
        }

        FrameReady?.Invoke(this, frame);
    }

    private void OnTickFailed(object? sender, Exception exception)
    {
        // 一次取帧失败不能让用户看到「歌词卡住」，但也不能刷屏。
        // 帧泵本身不会因异常停摆（它自己兜住了），这里只记日志。
        Logger.Debug(exception, "Lyric frame tick failed");
    }

    /// <summary>
    /// 组装检索链路。
    ///
    /// 四个源与 TL 的 <c>AppCompositionRoot</c> 完全一致，顺序也一致：
    /// QQ音乐 → 酷狗 → 网易云 → LRCLIB。顺序有意义——
    /// <see cref="LyricProviderTrustPolicy.CreateDefault"/> 按传入顺序决定
    /// 同名候选的优先级，QQ音乐在前是因为它对中文曲目的逐字（QRC）覆盖最好。
    /// </summary>
    private static LyricResolutionCoordinator CreateCoordinator(IResolvedLyricCache resolvedCache)
    {
        ILyricSource[] sources =
        [
            new QqMusicLyricSource(),
            new KugouLyricSource(),
            new NeteaseLyricSource(),
            new LrcLibLyricSource(),
        ];

        return new LyricResolutionCoordinator(
            sources,
            [new LyricifyPayloadDecoder()],
            [new LyricifyPayloadParser()],
            LyricPipelineCache.CreateDefault(),
            resolvedLyricCache: resolvedCache);
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;

        // 顺序有讲究：先停泵（不再产生新帧），再拆订阅，
        // 最后释放下游。反过来会让最后一次 Tick 撞上已释放的 syncService。
        _pump.FrameReady -= OnFrameReady;
        _pump.TickFailed -= OnTickFailed;
        _pump.Dispose();
        _syncService.Dispose();
        _coordinator.Dispose();
        _resolvedCache.Dispose();
        _offsetStore.Dispose();
    }
}