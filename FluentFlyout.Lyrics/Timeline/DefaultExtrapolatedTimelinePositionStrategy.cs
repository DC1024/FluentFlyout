// Copyright (c) 2026 anync
// SPDX-License-Identifier: MIT
//
// 本文件源自 TaskbarLyrics (https://github.com/ANYNC/TaskbarLyrics)
// 的 TaskbarLyrics.App/DefaultExtrapolatedTimelinePositionStrategy.cs，
// 原始许可为 MIT（见仓库根目录 LICENSE-TaskbarLyrics）。
// 本仓库整体按 GPL-3.0-or-later 分发。

namespace FluentFlyout.Lyrics.Timeline;

/// <summary>
/// 兜底策略：暂停时用原始位置，播放时用朴素外推值。
///
/// 这是所有策略里唯一无条件适用的一个，作为注册表的默认项。
/// </summary>
public sealed class DefaultExtrapolatedTimelinePositionStrategy : ITimelinePositionStrategy
{
    public string Name => "DefaultExtrapolated";

    public bool CanApply(in TimelineSample sample) => true;

    public TimeSpan SelectPosition(in TimelineSample sample)
    {
        if (!sample.IsPlaying)
        {
            return sample.RawPosition;
        }

        // 外推值是负的说明上游算错了（时钟回拨等），退回原始位置更安全。
        if (sample.ExtrapolatedPosition < TimeSpan.Zero)
        {
            return sample.RawPosition;
        }

        return sample.ExtrapolatedPosition;
    }
}
