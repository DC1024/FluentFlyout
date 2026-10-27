// Copyright (c) 2026 anync
// SPDX-License-Identifier: MIT
//
// 本文件源自 TaskbarLyrics (https://github.com/ANYNC/TaskbarLyrics)
// 的 TaskbarLyrics.Core，原始许可为 MIT（见仓库根目录 LICENSE-TaskbarLyrics）。
// 本仓库整体按 GPL-3.0-or-later 分发。

using TaskbarLyrics.Core.Models;

namespace TaskbarLyrics.Core.Abstractions;

public interface ILyricProvider
{
    string SourceApp { get; }

    Task<LyricDocument?> GetLyricsAsync(TrackInfo track, CancellationToken cancellationToken = default);

    async Task<LyricFetchResult> GetLyricsWithDiagnosticsAsync(
        TrackInfo track,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var document = await GetLyricsAsync(track, cancellationToken);
        return new LyricFetchResult(
            document,
            document is null ? LyricAcquisitionKind.NotFound : LyricAcquisitionKind.Unknown,
            stopwatch.ElapsedMilliseconds);
    }
}
