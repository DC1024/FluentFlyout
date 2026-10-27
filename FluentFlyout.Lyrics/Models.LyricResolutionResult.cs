// Copyright (c) 2026 anync
// SPDX-License-Identifier: MIT
//
// 本文件源自 TaskbarLyrics (https://github.com/ANYNC/TaskbarLyrics)
// 的 TaskbarLyrics.Core，原始许可为 MIT（见仓库根目录 LICENSE-TaskbarLyrics）。
// 本仓库整体按 GPL-3.0-or-later 分发。

namespace FluentFlyout.Lyrics.Models;

public enum LyricResolutionStatus
{
    Succeeded,
    NotFound,
    TransientFailure,
    Failed,
    Unavailable
}

public sealed record LyricResolutionResult(ResolvedLyrics? Lyrics, LyricResolutionStatus Status)
{
    public static LyricResolutionResult FromLyrics(ResolvedLyrics? lyrics) =>
        new(lyrics, lyrics is null ? LyricResolutionStatus.NotFound : LyricResolutionStatus.Succeeded);
}
