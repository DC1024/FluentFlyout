// Copyright (c) 2026 anync
// SPDX-License-Identifier: MIT
//
// 本文件源自 TaskbarLyrics (https://github.com/ANYNC/TaskbarLyrics)
// 的 TaskbarLyrics.Core，原始许可为 MIT（见仓库根目录 LICENSE-TaskbarLyrics）。
// 本仓库整体按 GPL-3.0-or-later 分发。

using TaskbarLyrics.Core.Models;

namespace TaskbarLyrics.Core.Abstractions;

/// <summary>
/// Stores the final lyric resolution for a normalized title-and-artist identity.
/// </summary>
public interface IResolvedLyricCache
{
    bool TryGet(TrackInfo track, out ResolvedLyrics? resolvedLyrics);

    bool Store(TrackInfo track, ResolvedLyrics resolvedLyrics);

    ResolvedLyricCacheStoreResult StoreWithResult(TrackInfo track, ResolvedLyrics resolvedLyrics) =>
        new(Store(track, resolvedLyrics), null);

    void Clear();
}

public readonly record struct ResolvedLyricCacheStoreResult(bool IsSaved, string? FailureReason);
