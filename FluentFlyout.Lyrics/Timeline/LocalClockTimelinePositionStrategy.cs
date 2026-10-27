// Copyright (c) 2026 anync
// SPDX-License-Identifier: MIT
//
// 本文件为本仓库新增，不源自 TaskbarLyrics。

namespace FluentFlyout.Lyrics.Timeline;

/// <summary>
/// 给「不向系统提供播放进度」的播放器兜底：用本地时钟推算位置。
///
/// <para>
/// <b>为什么需要它。</b>网易云音乐的 SMTC 时间轴是坏的（见
/// <see cref="TimelineValidity"/> 的实测数据）：位置恒 0、时间戳恒为 1601 年。
/// 已有的 <see cref="CommonExtrapolatedTimelinePositionStrategy"/> 算的是
/// <c>RawPosition + LastUpdateAge</c>，而这里的两个加数都是 0（时间戳太旧，
/// 年龄被 <c>MaximumTimelineAge</c> 钳成 0），于是位置恒等于 0——
/// 歌词永远停在第 0 行。现象上就是「明明有歌词，任务栏却只显示开头一句」，
/// 或者被误认为「根本没出歌词」。
/// </para>
///
/// <para>
/// <b>做法。</b>既然播放器不报进度，就假设它「从被看到的第一刻开始正常播放」，
/// 用 <see cref="TimeProvider"/> 的单调时钟累计已播放时长。
/// 每帧都把当前位置与当前时刻重新记为锚点，因此暂停时位置自然冻结、
/// 恢复时从冻结处继续，不需要额外的状态机。
/// </para>
///
/// <para>
/// <b>曲终归零。</b>纯累加有个必然的终点问题：歌词行的时间戳止于最后一句，
/// 而本地时钟没有尽头。单曲循环时曲目身份（AUMID/标题/歌手）三件套一个都不变，
/// 「换歌归零」永远不触发，于是第二遍起位置一路超过最后一句的时间，
/// 选位永远命中末行——表现为「歌词卡在这一句不动了」。
/// 所以这里接收外部下发的曲长估计（<see cref="SetTrackEndEstimate"/>），
/// 位置越过它就归零重来。拿不到曲长时退回无上限行为，即修复前的样子。
/// </para>
///
/// <para>
/// <b>已知取舍。</b>从歌曲中间开始播（或用户拖了进度条）时，本地累计会从 0 起算，
/// 歌词与声音错位，错位量等于起播位置。这无法通过 SMTC 修正——
/// 网易云的 <c>IsPlaybackPositionEnabled</c> 本身就是 <c>false</c>，
/// 系统层拿不到真实进度。用户可用设置页的「时间偏移」手动对齐。
/// 相比「永远卡在第一行」，错位但会滚动是明显更好的结果。
/// </para>
///
/// <para>
/// <b>何时退出。</b>判据只看当次采样：一旦播放器开始报出可信时间轴
/// （<c>RawPosition != 0</c> 且时间戳新鲜），<see cref="CanApply"/> 就返回
/// <c>false</c>，注册表会自然切回按播放器分类的普通策略。
/// </para>
/// </summary>
public sealed class LocalClockTimelinePositionStrategy : ITimelinePositionStrategy
{
    /// <summary>
    /// 低于这个值的「曲终估计」一律不采信。
    ///
    /// 网易云连 <c>EndTime</c> 都是 0，时长只能靠歌词源给；一旦上游给了个
    /// 明显错的短值（比如歌词源把「试听片段」的时长当成了整首），归零逻辑
    /// 会每帧触发，位置永远锁在 0——那比「卡在最后一行」更难排查。
    /// 30 秒是歌曲的合理下限，比它短的正常曲目几乎不存在。
    /// </summary>
    private static readonly TimeSpan MinimumPlausibleTrackDuration = TimeSpan.FromSeconds(30);

    private readonly TimeProvider _timeProvider;

    /// <summary>曲目身份（AUMID + 标题 + 艺术家），用于检测换歌。</summary>
    private string? _trackIdentity;

    /// <summary>上次提交时的单调时钟读数。</summary>
    private long _anchorTimestamp;

    /// <summary>上次提交时的播放位置。</summary>
    private TimeSpan _anchorPosition;

    /// <summary>上一帧是否处于播放态，用于「恢复播放」那一帧不误加一整段时长。</summary>
    private bool _playing;

    /// <summary>是否已有有效状态。与 <see cref="_trackIdentity"/> 分开，
    /// 避免空标题的曲目被判成「每帧都换歌」。</summary>
    private bool _hasState;

    /// <summary>曲目总长估计。null 表示未知，此时不做归零（保持旧行为）。</summary>
    private TimeSpan? _trackEndEstimate;

    public LocalClockTimelinePositionStrategy(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public string Name => "LocalClock";

    public bool CanApply(in TimelineSample sample)
    {
        return TimelineValidity.IsUnusable(in sample);
    }

    public TimeSpan SelectPosition(in TimelineSample sample)
    {
        var now = _timeProvider.GetTimestamp();
        var identity = string.Join('|', sample.SourceAppUserModelId, sample.Title, sample.Artist);

        // 换歌：位置归零重新累计。
        // 这里不尝试保留旧位置——新歌的进度与旧歌无关，任何保留都是错的。
        if (!_hasState || !string.Equals(_trackIdentity, identity, StringComparison.Ordinal))
        {
            Reset();
            _trackIdentity = identity;
            _hasState = true;
            return Commit(TimeSpan.Zero, now, sample.IsPlaying);
        }

        // 只有「上一帧在播 且 这一帧还在播」才把流逝的时间算进去。
        // 暂停→播放的那一帧，_playing 仍是 false，于是不累加，
        // 避免把暂停期间的时间也算成播放进度。
        var elapsed = _playing && sample.IsPlaying
            ? _timeProvider.GetElapsedTime(_anchorTimestamp, now)
            : TimeSpan.Zero;

        var next = _anchorPosition + elapsed;

        // 一遍唱完了，回到开头重新计时。
        //
        // 没有这一步时位置只增不减，而歌词行的时间戳有上限（最后一句）：
        // 「单曲循环」第二遍起，选位永远命中最后一行，歌词永久冻结。
        // 曲目身份在单曲循环时不变（AUMID/标题/歌手全一样），上面的换歌分支
        // 根本不会触发，所以必须靠时长判断一遍结束了。
        //
        // 这里用「归零」而不是「对曲长取模」：曲长是歌词源给的近似值，与真实
        // 歌长有偏差。取模会把偏差按遍数累积——第二遍错 15 秒、第三遍错 30 秒；
        // 归零每遍都从 0 重新起算，误差不会滚雪球。
        //
        // 只在播放态归零：用户暂停在最后几秒时位置本就该停在末行，
        // 若照归零，歌词会从他眼前的最后一句跳回第一句，而歌根本没重播。
        if (sample.IsPlaying && IsPastTrackEnd(next))
        {
            next = TimeSpan.Zero;
        }

        return Commit(next, now, sample.IsPlaying);
    }

    public void SetTrackEndEstimate(TimeSpan? estimate)
    {
        // 明显短于合理下限的值直接丢弃：宁可退回「无上限」的旧行为，
        // 也不要因为一个脏值把位置每帧归零、把歌词锁死在第一行。
        _trackEndEstimate = estimate is { } value && value >= MinimumPlausibleTrackDuration
            ? value
            : null;
    }

    private bool IsPastTrackEnd(TimeSpan position) =>
        _trackEndEstimate is { } end && position >= end;

    public void Reset()
    {
        _trackIdentity = null;
        _anchorTimestamp = 0;
        _anchorPosition = TimeSpan.Zero;
        _playing = false;
        _hasState = false;
    }

    private TimeSpan Commit(TimeSpan position, long now, bool playing)
    {
        _anchorPosition = position < TimeSpan.Zero ? TimeSpan.Zero : position;
        _anchorTimestamp = now;
        _playing = playing;
        return _anchorPosition;
    }
}
