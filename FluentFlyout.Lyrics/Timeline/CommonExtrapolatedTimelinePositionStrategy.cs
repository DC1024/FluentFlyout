// Copyright (c) 2026 anync
// SPDX-License-Identifier: MIT
//
// 本文件源自 TaskbarLyrics (https://github.com/ANYNC/TaskbarLyrics)
// 的 TaskbarLyrics.App/CommonExtrapolatedTimelinePositionStrategy.cs，
// 原始许可为 MIT（见仓库根目录 LICENSE-TaskbarLyrics）。
// 本仓库整体按 GPL-3.0-or-later 分发。

namespace FluentFlyout.Lyrics.Timeline;

/// <summary>
/// Spotify / 网易云 / QQ 音乐 / 酷狗 共用的策略。
///
/// 选位逻辑与 <see cref="DefaultExtrapolatedTimelinePositionStrategy"/> 完全相同，
/// 单独成一个类是为了让「哪家播放器走哪条路径」这件事在日志里可辨认。
/// 将来某家播放器需要特殊处理时，改这里即可，不会波及兜底逻辑。
/// </summary>
public sealed class CommonExtrapolatedTimelinePositionStrategy : ITimelinePositionStrategy
{
    public string Name => "CommonExtrapolated";

    public bool CanApply(in TimelineSample sample)
    {
        return PlayerIdentity.UsesCommonExtrapolation(sample.SourceAppUserModelId);
    }

    public TimeSpan SelectPosition(in TimelineSample sample)
    {
        if (!sample.IsPlaying)
        {
            return sample.RawPosition;
        }

        if (sample.ExtrapolatedPosition < TimeSpan.Zero)
        {
            return sample.RawPosition;
        }

        return sample.ExtrapolatedPosition;
    }
}
