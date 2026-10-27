// Copyright (c) 2026 anync
// SPDX-License-Identifier: MIT
//
// 本文件源自 TaskbarLyrics (https://github.com/ANYNC/TaskbarLyrics)
// 的 TaskbarLyrics.Core，原始许可为 MIT（见仓库根目录 LICENSE-TaskbarLyrics）。
// 本仓库整体按 GPL-3.0-or-later 分发。

using FluentFlyout.Lyrics.Models;

namespace FluentFlyout.Lyrics.Abstractions;

public interface IContextualResolvedLyricCache : IResolvedLyricCache
{
    bool TryGet(TrackInfo track, string context, out ResolvedLyrics? resolvedLyrics);

    bool Store(TrackInfo track, ResolvedLyrics resolvedLyrics, string context);

    ResolvedLyricCacheStoreResult StoreWithResult(
        TrackInfo track,
        ResolvedLyrics resolvedLyrics,
        string context) => new(Store(track, resolvedLyrics, context), null);
}
