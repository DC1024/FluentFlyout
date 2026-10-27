// Copyright (c) 2026 anync
// SPDX-License-Identifier: MIT
//
// 本文件源自 TaskbarLyrics (https://github.com/ANYNC/TaskbarLyrics)
// 的 TaskbarLyrics.Core，原始许可为 MIT（见仓库根目录 LICENSE-TaskbarLyrics）。
// 本仓库整体按 GPL-3.0-or-later 分发。

namespace FluentFlyout.Lyrics.Models;

public sealed record RawLyricCacheEnvelope(
    int CacheVersion,
    string ProviderId,
    string CandidateId,
    LyricPayloadFormat Format,
    string PayloadHash,
    DateTimeOffset FetchedAtUtc,
    string? OriginalLyrics,
    string? TranslationLyrics,
    bool IsEncrypted,
    bool IsPureMusic,
    IReadOnlyDictionary<string, string> Diagnostics);

public sealed record ParsedLyricCacheEnvelope(
    int CacheVersion,
    string ProviderId,
    string CandidateId,
    string RawPayloadHash,
    string ParserId,
    string ParserVersion,
    string NormalizationVersion,
    ParsedLyrics Content);
