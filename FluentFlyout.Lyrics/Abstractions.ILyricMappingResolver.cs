// Copyright (c) 2026 anync
// SPDX-License-Identifier: MIT
//
// 本文件源自 TaskbarLyrics (https://github.com/ANYNC/TaskbarLyrics)
// 的 TaskbarLyrics.Core，原始许可为 MIT（见仓库根目录 LICENSE-TaskbarLyrics）。
// 本仓库整体按 GPL-3.0-or-later 分发。

using FluentFlyout.Lyrics.Models;

namespace FluentFlyout.Lyrics.Abstractions;

public interface ILyricMappingResolver
{
    LyricMapping Resolve(TrackInfo track);
}

public sealed record LyricMapping(
    string Title,
    string Artist,
    string? PreferredProvider,
    bool IsPureMusic,
    string? Album = null)
{
    public static LyricMapping Unchanged(TrackInfo track) =>
        new(track.Title, track.Artist, null, false, track.Album);
}
