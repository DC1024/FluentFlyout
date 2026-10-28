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
    /// 上一次通知出去的歌词文档。按**引用**比较：Core 换歌时会换成新实例，
    /// 同一首歌的重复检索也会换实例（歌词源修正），两者都该重新定宽度。
    /// </summary>
    private LyricDocument? _lastDocument;

    /// <summary>
    /// 当前曲目的整份歌词换了（含变成 null：纯音乐、搜不到、停止播放）。
    ///
    /// <para>
    /// 渲染层需要整首歌词才能定下「这首歌的固定宽度」——按当前行量宽度会让
    /// 胶囊随每句胀缩，长句还会先滚动再变宽。事件排在
    /// <see cref="FrameReady"/> 之前触发，保证第一帧就用新宽度排。
    /// </para>
    /// </summary>
    public event EventHandler<LyricDocument?>? DocumentChanged;

    /// <summary>
    /// 同一首歌连续产出「空行」帧的次数。
    ///
    /// 为什么要专门数：<b>「没开歌词」和「开了但搜不到词」在现象上完全一样</b>
    /// ——任务栏小组件都显示原样，用户无法区分，日志里也看不出。
    /// 这里在连续空帧超过阈值后打一条 Warn，把「这首歌没拿到歌词」这件事
    /// 落到日志里，排查时就不必再靠猜。
    ///
    /// 阈值取 <see cref="EmptyFrameReportThreshold"/> 次 tick：帧泵约 33 ms
    /// 一次，约合 3 秒——足够覆盖一次正常的远程检索（实测单曲 0.8 秒内命中）。
    /// </summary>
    private int _emptyFrameStreak;

    /// <summary>已经为哪首歌报过「没拿到歌词」，避免同一首歌刷屏。</summary>
    private string? _emptyReportedForTitle;

    /// <summary>连续空帧多少次之后报一次日志。见 <see cref="_emptyFrameStreak"/>。</summary>
    private const int EmptyFrameReportThreshold = 90;

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
                TimeSpan.FromMilliseconds(_offsetStore.GetOffsetMilliseconds(track, lyricSource)),
            // 歌词解析完成后把「这首多长」推给时间轴层：网易云的 SMTC 连 EndTime
            // 都是 0，没有这个值本地时钟就只增不减，单曲循环第二遍起歌词会卡在末行。
            onTrackEndEstimateChanged: _sessionProvider.SetTrackEndEstimate);

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

        // 只在状态真的变化时记日志：这个方法被设置钩子和窗口 Loaded
        // 反复调用，逐次记录会变成噪音。
        if (enabled != _pump.IsRunning)
        {
            Logger.Info("Lyrics frame pump {0}", enabled ? "started" : "stopped");

            if (!enabled)
            {
                // 停泵后重置诊断状态，下次开启重新计数。
                _emptyFrameStreak = 0;
                _emptyReportedForTitle = null;
            }
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

        TrackEmptyFrameDiagnostics(frame);

        // 文档变了要先通知：渲染层拿它算「整首歌的固定宽度」，
        // 排在 FrameReady 之前才能保证第一帧就按新宽度排版。
        //
        // 按引用比较而不是按内容：换歌、歌词源修正、清空都会换实例，
        // 三种情况都该重新定宽度，而每帧深比较一份几百行的列表不值得。
        var document = _syncService.CurrentDocument;
        if (!ReferenceEquals(document, _lastDocument))
        {
            _lastDocument = document;
            DocumentChanged?.Invoke(this, document);
        }

        FrameReady?.Invoke(this, frame);
    }

    /// <summary>
    /// 连续空帧计数与一次性上报。见 <see cref="_emptyFrameStreak"/> 的说明。
    ///
    /// 判据是 <c>CurrentLine</c> 为空而不是 <c>CurrentLineIndex &lt; 0</c>：
    /// 逐字歌词（QRC）在某一行刚开始时可能还没有索引，但行文本已经有了，
    /// 那种帧是正常的，不该计进空帧。
    /// </summary>
    private void TrackEmptyFrameDiagnostics(LyricDisplayFrame frame)
    {
        // 没有曲目时不算「这首歌拿不到歌词」——那只是没在放歌。
        // 把它计进空帧会让日志里出现与问题无关的告警，误导排查方向。
        if (string.IsNullOrEmpty(frame.Title))
        {
            _emptyFrameStreak = 0;
            _emptyReportedForTitle = null;
            return;
        }

        if (!string.IsNullOrEmpty(frame.CurrentLine))
        {
            // 拿到词了，链条是通的；同一首歌以后即使再变空也不重复报警。
            _emptyFrameStreak = 0;
            return;
        }

        _emptyFrameStreak++;

        if (_emptyFrameStreak < EmptyFrameReportThreshold)
        {
            return;
        }

        // 已经为这首歌报过就不再刷屏。换歌时 title 变化，
        // 比较自然失败，于是新歌会重新走一遍上报流程。
        if (string.Equals(_emptyReportedForTitle, frame.Title, StringComparison.Ordinal))
        {
            return;
        }

        _emptyReportedForTitle = frame.Title;

        // 用 Warn 而不是 Debug：默认 NLog 配置是 minlevel="Info"，
        // Debug 写进去也看不见——这个诊断的意义就没了。
        Logger.Warn(
            "No lyric line for \"{0}\" after {1} consecutive empty frames; the resolution chain likely failed for this track",
            frame.Title,
            _emptyFrameStreak);

        // 报过之后把计数钉在阈值上：日志里的数字不会无限增长，
        // 也免得长时间播放后这个 int 被推到没意义的量级。
        _emptyFrameStreak = EmptyFrameReportThreshold;
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