// Copyright (c) 2026 anync
// SPDX-License-Identifier: MIT
//
// 本文件源自 TaskbarLyrics (https://github.com/ANYNC/TaskbarLyrics)
// 的 TaskbarLyrics.App/TimelinePositionStrategyRegistry.cs，
// 原始许可为 MIT（见仓库根目录 LICENSE-TaskbarLyrics）。
// 本仓库整体按 GPL-3.0-or-later 分发。

namespace FluentFlyout.Lyrics.Timeline;

/// <summary>
/// 按采样挑选合适的策略，并在其上叠一层「播放状态切换期的保位」。
///
/// 保位这一层解决的是策略管不到的问题：用户按下暂停的瞬间，播放器
/// 往往还会再报一两次旧时间轴，位置继续往前走几百毫秒，歌词就多滚
/// 一句。这里记住上一次的选位结果，在确认状态切换完成前不采纳新的
/// 小步前进。
/// </summary>
public sealed class TimelinePositionStrategyRegistry
{
    /// <summary>
    /// 暂停态下允许忽略的最大前进量。超过这个值说明是真的跳转
    /// （用户拖了进度条），必须采纳。
    /// </summary>
    private static readonly TimeSpan MaximumDeferredPausedAdvance = TimeSpan.FromSeconds(1);

    private readonly IReadOnlyList<ITimelinePositionStrategy> _strategies;
    private readonly ITimelinePositionStrategy _defaultStrategy;
    private readonly object _sync = new();
    private ITimelinePositionStrategy? _activeStrategy;
    private string _lastTrackIdentity = string.Empty;
    private DateTimeOffset _lastTimelineUpdatedAtUtc;
    private TimeSpan _lastSelectedPosition;
    private bool _hasTimelineState;
    private bool _wasPlaying;
    private bool _waitingForResumedTimeline;
    private TimeSpan? _trackEndEstimate;

    public TimelinePositionStrategyRegistry(
        IReadOnlyList<ITimelinePositionStrategy> strategies,
        ITimelinePositionStrategy defaultStrategy)
    {
        ArgumentNullException.ThrowIfNull(strategies);
        _strategies = strategies;
        _defaultStrategy = defaultStrategy ?? throw new ArgumentNullException(nameof(defaultStrategy));
    }

    /// <summary>
    /// 用默认策略集合构建。
    ///
    /// 顺序有意讲究，<b>不能随意调换</b>：注册表按顺序取第一个
    /// <c>CanApply</c> 命中的策略。
    /// <list type="number">
    /// <item><see cref="LocalClockTimelinePositionStrategy"/> 排最前——
    /// 「时间轴根本不可用」比「这是哪家播放器」更根本。若放到
    /// <see cref="CommonExtrapolatedTimelinePositionStrategy"/> 之后，
    /// 网易云会先命中后者（它按 AUMID 认 netease/cloudmusic），
    /// 于是永远用不上本地时钟，位置恒 0。</item>
    /// <item>其次 Apple Music，它有专门的量化/漂移处理。</item>
    /// <item>再次四家共用策略。</item>
    /// <item>最后是兜底朴素外推。</item>
    /// </list>
    /// </summary>
    public static TimelinePositionStrategyRegistry CreateDefault(TimeProvider? timeProvider = null)
    {
        return new TimelinePositionStrategyRegistry(
            [
                new LocalClockTimelinePositionStrategy(timeProvider),
                new AppleMusicTimelinePositionStrategy(timeProvider),
                new CommonExtrapolatedTimelinePositionStrategy()
            ],
            new DefaultExtrapolatedTimelinePositionStrategy());
    }

    /// <summary>
    /// 选出该采样对应的播放位置，并记录策略名。
    /// </summary>
    public (string StrategyName, TimeSpan Position) Select(in TimelineSample sample)
    {
        lock (_sync)
        {
            var (strategyName, position) = SelectCore(in sample);
            var preserved = PreservePositionWhilePlaybackTransitionIsStale(in sample, position);
            return (strategyName, preserved);
        }
    }

    private (string StrategyName, TimeSpan Position) SelectCore(in TimelineSample sample)
    {
        // 这里刻意用循环而不是 FirstOrDefault：in 参数不能被 lambda 捕获。
        ITimelinePositionStrategy? matched = null;
        foreach (var candidate in _strategies)
        {
            if (candidate.CanApply(in sample))
            {
                matched = candidate;
                break;
            }
        }

        var strategy = matched ?? _defaultStrategy;

        if (!ReferenceEquals(_activeStrategy, strategy))
        {
            _activeStrategy?.Reset();
            strategy.Reset();
            _activeStrategy = strategy;
        }

        return (strategy.Name, strategy.SelectPosition(in sample));
    }

    /// <summary>
    /// 通知策略用户做了 seek。返回 true 表示策略已接管，
    /// 调用方不应再用旧时间轴覆盖位置。
    /// </summary>
    public bool NotifySeek(string sourceAppUserModelId, TimeSpan position, DateTimeOffset timelineUpdatedAtUtc)
    {
        lock (_sync)
        {
            if (_activeStrategy?.TryApplySeek(sourceAppUserModelId, position, timelineUpdatedAtUtc) != true)
            {
                return false;
            }

            _lastSelectedPosition = position;
            _lastTimelineUpdatedAtUtc = timelineUpdatedAtUtc;
            _waitingForResumedTimeline = false;
            return true;
        }
    }

    /// <summary>
    /// 下发「这首歌唱到什么时候结束」的估计值。由装配层在歌词解析完成后调用，
    /// 换歌时传 <c>null</c> 清空。
    ///
    /// <para>
    /// 时间轴策略看不到歌词文档——注册表运行在采样路径上（每 33ms 一次），
    /// 不该在那里读歌词；所以由外面把结论推给它。所有策略都收到一份，
    /// 只有本地时钟策略会记住它，其余策略的默认实现直接忽略。
    /// </para>
    /// </summary>
    public void SetTrackEndEstimate(TimeSpan? estimate)
    {
        lock (_sync)
        {
            _trackEndEstimate = estimate is { } value && value > TimeSpan.Zero ? value : null;
            BroadcastTrackEndEstimate();
        }
    }

    public void Reset()
    {
        lock (_sync)
        {
            _activeStrategy?.Reset();
            _activeStrategy = null;
            _hasTimelineState = false;
            _waitingForResumedTimeline = false;
            _wasPlaying = false;
            _lastTrackIdentity = string.Empty;
            // 会话变了，旧曲目的时长对新曲目毫无意义；留着会让本地时钟
            // 在错误的位置归零（比如拿一首 3 分钟的上限去卡一首 8 分钟的歌）。
            _trackEndEstimate = null;
            BroadcastTrackEndEstimate();
        }
    }

    private void BroadcastTrackEndEstimate()
    {
        foreach (var strategy in _strategies)
        {
            strategy.SetTrackEndEstimate(_trackEndEstimate);
        }

        _defaultStrategy.SetTrackEndEstimate(_trackEndEstimate);
    }

    private TimeSpan PreservePositionWhilePlaybackTransitionIsStale(
        in TimelineSample sample,
        TimeSpan selectedPosition)
    {
        // 时间轴不可用时整层「保位」必须跳过，否则会把本地时钟外推钉死。
        //
        // 失败链条：这段逻辑靠「时间戳有没有前进」判断时间轴刷没刷新
        // （`LastUpdatedTimeUtc <= _lastTimelineUpdatedAtUtc`）。网易云的时间戳
        // 恒为 1601-01-01，于是每次采样都被判为「没刷新」：
        //   - 首帧 _hasTimelineState 为 false，记下时间戳后返回，看似正常；
        //   - 但一旦经历一次暂停再恢复，_waitingForResumedTimeline 会被置 true，
        //     而时间戳永远 <= 上次值，判定条件恒成立 → 永远返回
        //     _lastSelectedPosition，位置彻底不动。
        // 结果就是 LocalClockTimelinePositionStrategy 算得再对也没用。
        if (TimelineValidity.IsTimestampUnusable(sample.LastUpdatedTimeUtc))
        {
            _lastTimelineUpdatedAtUtc = sample.LastUpdatedTimeUtc;
            _lastSelectedPosition = selectedPosition;
            return selectedPosition;
        }

        // 上游这里用 ResolvedSource，我们只有 AUMID；配合 Title/Artist 已足以识别换歌。
        var trackIdentity = string.Join('|', sample.SourceAppUserModelId, sample.Title, sample.Artist);

        if (!_hasTimelineState || !string.Equals(trackIdentity, _lastTrackIdentity, StringComparison.Ordinal))
        {
            _hasTimelineState = true;
            _lastTrackIdentity = trackIdentity;
            _lastTimelineUpdatedAtUtc = sample.LastUpdatedTimeUtc;
            _lastSelectedPosition = selectedPosition;
            _wasPlaying = sample.IsPlaying;
            _waitingForResumedTimeline = false;
            return selectedPosition;
        }

        if (sample.IsPlaying)
        {
            return PreserveWhilePlaying(in sample, selectedPosition);
        }

        return PreserveWhilePaused(in sample, selectedPosition);
    }

    private TimeSpan PreserveWhilePlaying(in TimelineSample sample, TimeSpan selectedPosition)
    {
        if (!_wasPlaying)
        {
            // 刚从暂停切到播放。若播放器还没给出新的时间轴，
            // 标记为等待，期间保持上一次的位置不动。
            _waitingForResumedTimeline = sample.LastUpdatedTimeUtc <= _lastTimelineUpdatedAtUtc;
            _wasPlaying = true;
        }

        if (_waitingForResumedTimeline && sample.LastUpdatedTimeUtc <= _lastTimelineUpdatedAtUtc)
        {
            return _lastSelectedPosition;
        }

        _lastTimelineUpdatedAtUtc = sample.LastUpdatedTimeUtc;
        _lastSelectedPosition = selectedPosition;
        _waitingForResumedTimeline = false;
        return selectedPosition;
    }

    private TimeSpan PreserveWhilePaused(in TimelineSample sample, TimeSpan selectedPosition)
    {
        if (_wasPlaying)
        {
            _wasPlaying = false;
            _waitingForResumedTimeline = false;
        }

        // 时间轴没刷新，位置不可能变。
        if (sample.LastUpdatedTimeUtc <= _lastTimelineUpdatedAtUtc)
        {
            return _lastSelectedPosition;
        }

        _lastTimelineUpdatedAtUtc = sample.LastUpdatedTimeUtc;

        // 暂停态下的小幅前进是播放器残留的旧时间轴，忽略。
        // 大幅前进才是用户拖动，必须采纳。
        if (ShouldDeferPausedAdvance(selectedPosition))
        {
            return _lastSelectedPosition;
        }

        _waitingForResumedTimeline = false;
        _lastSelectedPosition = selectedPosition;
        return selectedPosition;
    }

    private bool ShouldDeferPausedAdvance(TimeSpan selectedPosition)
    {
        var advance = selectedPosition - _lastSelectedPosition;
        return advance > TimeSpan.Zero && advance <= MaximumDeferredPausedAdvance;
    }
}
