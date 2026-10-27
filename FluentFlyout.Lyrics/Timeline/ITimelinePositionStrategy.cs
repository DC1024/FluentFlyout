// Copyright (c) 2026 anync
// SPDX-License-Identifier: MIT
//
// 本文件源自 TaskbarLyrics (https://github.com/ANYNC/TaskbarLyrics)
// 的 TaskbarLyrics.App/ITimelinePositionStrategy.cs，原始许可为 MIT
// （见仓库根目录 LICENSE-TaskbarLyrics）。
// 本仓库整体按 GPL-3.0-or-later 分发。

namespace FluentFlyout.Lyrics.Timeline;

/// <summary>
/// 时间轴选位策略。
///
/// SMTC 报告的位置更新不稳定：有的播放器几百毫秒才更新一次，
/// 有的会在整秒边界反复刷新同一个位置。直接用 <c>RawPosition</c>
/// 驱动歌词会导致进度条顿跳。策略负责把「播放器报的位置」转成
/// 「此刻应该在的位置」。
/// </summary>
public interface ITimelinePositionStrategy
{
    /// <summary>策略名，用于日志与诊断。</summary>
    string Name { get; }

    /// <summary>本策略是否适用于该采样。</summary>
    bool CanApply(in TimelineSample sample);

    /// <summary>算出该采样对应的播放位置。</summary>
    TimeSpan SelectPosition(in TimelineSample sample);

    /// <summary>清空内部状态。换歌或会话切换时调用。</summary>
    void Reset()
    {
    }

    /// <summary>
    /// 接收「这首歌唱到什么时候结束」的估计值。
    ///
    /// <para>
    /// 只有 <see cref="LocalClockTimelinePositionStrategy"/> 真的用到它：没有上限时
    /// 本地时钟只增不减，单曲循环第二遍起歌词永远停在最后一行。其余策略依赖
    /// 播放器上报的真实时间轴，听到这个值应当忽略——所以这里给一个空的默认实现，
    /// 避免为了一个可选能力去改所有策略与测试桩。
    /// </para>
    /// </summary>
    /// <param name="estimate">曲目总长；<c>null</c> 表示未知，策略需退回无上限行为。</param>
    void SetTrackEndEstimate(TimeSpan? estimate)
    {
    }

    /// <summary>
    /// 用户拖动进度条后通知策略。返回 true 表示策略已接管该 seek，
    /// 调用方应跳过后续的旧时间轴覆盖。
    /// </summary>
    bool TryApplySeek(string sourceAppUserModelId, TimeSpan position, DateTimeOffset timelineUpdatedAtUtc) => false;
}
