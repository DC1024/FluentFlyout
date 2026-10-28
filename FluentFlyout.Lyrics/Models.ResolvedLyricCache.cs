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
    string? SelectionContext = null,
    /// <summary>
    /// 歌词源声明的曲目时长（QQ 音乐的 interval / Lyricify 的 duration）。
    ///
    /// <para>
    /// 注意与 <see cref="Duration"/> 的区别：后者是「追踪的曲目时长」，
    /// 来自 SMTC 的 <c>EndTime</c>，在网易云场景恒为 0；这里才是真正可用的曲长。
    /// </para>
    ///
    /// <para>
    /// 缺省为 <c>null</c> 是刻意的：升级前写下的缓存条目没有这个字段，
    /// 反序列化后自然是 <c>null</c>，于是退回「末行 + 尾奏余量」的退化路径，
    /// 既不需要清空用户缓存，也不会因为缺字段而丢弃整条命中。
    /// </para>
    /// </summary>
    TimeSpan? SourceTrackDuration = null);
