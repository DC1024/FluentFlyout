// Copyright (c) 2026 anync
// SPDX-License-Identifier: MIT
//
// 本文件源自 TaskbarLyrics (https://github.com/ANYNC/TaskbarLyrics)
// 的 TaskbarLyrics.Core，原始许可为 MIT（见仓库根目录 LICENSE-TaskbarLyrics）。
// 本仓库整体按 GPL-3.0-or-later 分发。

namespace FluentFlyout.Lyrics.Models;

/// <summary>
/// Versioned on-disk envelope for the final lyric cache.
/// </summary>
public sealed record ResolvedLyricCacheEnvelope(
    int Version,
    IReadOnlyDictionary<string, ResolvedLyricCacheEntry>? Entries);

/// <summary>
/// A persisted final resolution and the metadata observed when it was selected.
/// </summary>
public sealed record ResolvedLyricCacheEntry(
    string? TrackId,
    string? Title,
    string? Artist,
    string? Album,
    string? SourceApp,
    TimeSpan Duration,
    string? SongId,
    string? ProviderId,
    string? CandidateId,
    LyricAcquisitionKind Acquisition,
    IReadOnlyDictionary<string, string>? Diagnostics,
    ParsedLyrics? Content,
    string? SelectionContext = null);
