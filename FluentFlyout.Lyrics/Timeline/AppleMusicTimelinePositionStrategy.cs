// Copyright (c) 2026 anync
// SPDX-License-Identifier: MIT
//
// 本文件源自 TaskbarLyrics (https://github.com/ANYNC/TaskbarLyrics)
// 的 TaskbarLyrics.App/AppleMusicTimelinePositionStrategy.cs，
// 原始许可为 MIT（见仓库根目录 LICENSE-TaskbarLyrics）。
// 本仓库整体按 GPL-3.0-or-later 分发。

namespace FluentFlyout.Lyrics.Timeline;

/// <summary>
/// Apple Music 专用的时间轴策略。
///
/// 为什么不能和其他播放器共用朴素外推：Apple Music 的 SMTC 时间轴有几个
/// 别的播放器没有的行为。
///
/// 1. **整秒量化**：它只报整秒位置，但会在同一秒内反复刷新
///    <c>LastUpdatedTime</c>。如果按「时间戳更新了就重新锚定」处理，
///    位置会在一秒内被反复拉回起点，表现为歌词卡住然后猛跳。
///    这里只在 <c>RawPosition</c> 真正变化时才认作新锚点。
/// 2. **亚秒漂移**：整秒量化意味着每次采样都有最多 1 秒的量化误差。
///    直接跟随会让歌词在整秒边界抖动。这里用「连续三次同向偏差才调整速率」
///    的方式做缓变校正（slew），且把速率限制在 ±5% 以内，避免听感上
///    的忽快忽慢。
/// 3. **seek 回声**：用户拖动进度条后，播放器会先返回旧时间轴，
///    过一会儿才报新位置。这里用一个「seek 目标」把它压住，
///    超时（2 秒）则放弃并重新锚定。
/// </summary>
public sealed class AppleMusicTimelinePositionStrategy : ITimelinePositionStrategy
{
    /// <summary>Apple Music 的时间轴分辨率是整秒，低于此值的差异视为量化噪声。</summary>
    private static readonly TimeSpan PositionResolution = TimeSpan.FromSeconds(1);

    /// <summary>超过此偏差视为跳转（seek）而非漂移。</summary>
    private static readonly TimeSpan SeekThreshold = TimeSpan.FromSeconds(2);

    /// <summary>seek 后等待播放器确认的最长时间，超时则重新锚定。</summary>
    private static readonly TimeSpan SeekConfirmationTimeout = TimeSpan.FromSeconds(2);

    /// <summary>连续多少次同向偏差才启动速率校正。防止量化噪声触发抖动。</summary>
    private const int DriftSamplesBeforeSlew = 3;

    private readonly TimeProvider _timeProvider;
    private string? _trackIdentity;
    private long _sampleTimestamp;
    private TimeSpan _position;
    private TimeSpan _rawPosition;
    private DateTimeOffset _timelineUpdatedAtUtc;
    private bool _playing;
    private double _rate = 1;
    private int _driftDirection;
    private int _driftSamples;
    private TimeSpan? _seekTarget;
    private long _seekRequestedAt;

    public AppleMusicTimelinePositionStrategy(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public string Name => "AppleMusicContinuous";

    public bool CanApply(in TimelineSample sample)
    {
        return PlayerIdentity.IsAppleMusic(sample.SourceAppUserModelId);
    }

    public TimeSpan SelectPosition(in TimelineSample sample)
    {
        var now = _timeProvider.GetTimestamp();

        // 换歌时状态全部作废，直接重新锚定。
        var identity = string.Join('|', sample.SourceAppUserModelId, sample.Title, sample.Artist);
        if (_trackIdentity != identity)
        {
            Reset();
            _trackIdentity = identity;
            return Reanchor(in sample, now);
        }

        var elapsed = _timeProvider.GetElapsedTime(_sampleTimestamp, now);
        var projected = _position + (_playing && sample.IsPlaying ? elapsed * _rate : TimeSpan.Zero);
        var refreshed = sample.LastUpdatedTimeUtc > _timelineUpdatedAtUtc;

        if (_seekTarget is { } seekTarget)
        {
            return ResolveSeek(in sample, now, projected, refreshed, seekTarget);
        }

        if (!sample.IsPlaying)
        {
            return HandlePaused(in sample, now, refreshed);
        }

        if (!_playing)
        {
            return HandleResumed(in sample, now, refreshed);
        }

        if (refreshed)
        {
            // 时间戳刷新不等于新锚点：Apple Music 会在同一整秒内重复刷新。
            // 只有 RawPosition 真的变了，才认为拿到了新的精确位置。
            if (sample.RawPosition != _rawPosition)
            {
                var error = sample.RawPosition - projected;

                // 位置倒退、或偏差超过 seek 阈值，都按跳转处理。
                if (sample.RawPosition < _rawPosition || error > SeekThreshold)
                {
                    return Reanchor(in sample, now);
                }

                UpdateCorrection(error);
                _rawPosition = sample.RawPosition;
            }

            _timelineUpdatedAtUtc = sample.LastUpdatedTimeUtc;
        }

        return Commit(projected, now, playing: true);
    }

    /// <summary>
    /// seek 之后的收敛过程。
    /// 播放器确认前，旧时间轴会一直返回，不能让它覆盖用户刚拖到的位置。
    /// </summary>
    private TimeSpan ResolveSeek(
        in TimelineSample sample,
        long now,
        TimeSpan projected,
        bool refreshed,
        TimeSpan seekTarget)
    {
        // 播放器报了接近目标的位置，说明 seek 已被接受。
        if (refreshed && (sample.RawPosition - projected).Duration() <= PositionResolution)
        {
            _seekTarget = null;
            _rawPosition = sample.RawPosition;
            _timelineUpdatedAtUtc = sample.LastUpdatedTimeUtc;
            return Commit(projected, now, sample.IsPlaying);
        }

        // 等太久还没确认，放弃接管，重新锚定到播放器报的位置。
        if (_timeProvider.GetElapsedTime(_seekRequestedAt, now) >= SeekConfirmationTimeout)
        {
            _seekTarget = null;
            return Reanchor(in sample, now);
        }

        // 仍在等待确认：暂停时钉在目标位置，播放时继续按原速率走。
        return Commit(_playing ? projected : seekTarget, now, _playing && sample.IsPlaying);
    }

    private TimeSpan HandlePaused(in TimelineSample sample, long now, bool refreshed)
    {
        ResetCorrection();

        if (refreshed)
        {
            // 暂停态下位置倒退、或偏差超过分辨率，说明用户在暂停时拖动了进度条。
            if (sample.RawPosition < _rawPosition
                || (sample.RawPosition - _position).Duration() > PositionResolution)
            {
                _position = sample.RawPosition;
            }

            _rawPosition = sample.RawPosition;
            _timelineUpdatedAtUtc = sample.LastUpdatedTimeUtc;
        }

        return Commit(_position, now, playing: false);
    }

    private TimeSpan HandleResumed(in TimelineSample sample, long now, bool refreshed)
    {
        // 从暂停切回播放，但播放器还没给新时间轴：保持原位，等下一次刷新。
        if (!refreshed)
        {
            return Commit(_position, now, playing: false);
        }

        if (sample.RawPosition < _rawPosition
            || (sample.RawPosition - _position).Duration() > PositionResolution)
        {
            return Reanchor(in sample, now);
        }

        _rawPosition = sample.RawPosition;
        _timelineUpdatedAtUtc = sample.LastUpdatedTimeUtc;
        return Commit(_position, now, playing: true);
    }

    public bool TryApplySeek(string sourceAppUserModelId, TimeSpan position, DateTimeOffset timelineUpdatedAtUtc)
    {
        if (_trackIdentity is null || !PlayerIdentity.IsAppleMusic(sourceAppUserModelId))
        {
            return false;
        }

        _seekTarget = position;
        _seekRequestedAt = _timeProvider.GetTimestamp();
        _timelineUpdatedAtUtc = timelineUpdatedAtUtc;
        ResetCorrection();
        Commit(position, _seekRequestedAt, _playing);
        return true;
    }

    public void Reset()
    {
        _trackIdentity = null;
        _seekTarget = null;
        _playing = false;
        ResetCorrection();
    }

    private TimeSpan Reanchor(in TimelineSample sample, long now)
    {
        _rawPosition = sample.RawPosition;
        _timelineUpdatedAtUtc = sample.LastUpdatedTimeUtc;
        ResetCorrection();

        // 首次锚定时只接受不超过一个分辨率周期的外推量：
        // 更老的时间轴说明播放器已经很久没更新，外推不可信。
        var age = sample.IsPlaying
            && sample.LastUpdateAge >= TimeSpan.Zero
            && sample.LastUpdateAge <= PositionResolution
                ? sample.LastUpdateAge
                : TimeSpan.Zero;

        return Commit(sample.RawPosition + age, now, sample.IsPlaying);
    }

    private TimeSpan Commit(TimeSpan position, long now, bool playing)
    {
        _position = position < TimeSpan.Zero ? TimeSpan.Zero : position;
        _sampleTimestamp = now;
        _playing = playing;
        return _position;
    }

    /// <summary>
    /// 用速率微调（slew）消化量化误差，而不是直接跳位置。
    ///
    /// 关键取舍：把「整秒量化」当噪声而不是漂移。只有偏差明显超过一个
    /// 分辨率周期才算真实漂移，且必须连续三次同向才动手——单次偏差
    /// 更可能是采样相位差。
    /// </summary>
    private void UpdateCorrection(TimeSpan error)
    {
        var direction = error > PositionResolution ? 1 : error < -PositionResolution ? -1 : 0;

        if (direction == 0)
        {
            ResetCorrection();
            return;
        }

        // 方向翻转说明不是持续漂移，速率立刻归位。
        if (direction != _driftDirection)
        {
            _rate = 1;
        }

        _driftSamples = direction == _driftDirection ? _driftSamples + 1 : 1;
        _driftDirection = direction;

        if (_driftSamples >= DriftSamplesBeforeSlew)
        {
            // 减去一个方向量，把量化误差本身排除在待补偿的漂移之外。
            _rate = Math.Clamp(1 + (error.TotalSeconds - direction) / 5, 0.95, 1.05);
        }
    }

    private void ResetCorrection()
    {
        _rate = 1;
        _driftDirection = 0;
        _driftSamples = 0;
    }
}
