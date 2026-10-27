// Copyright (c) 2026 anync
// SPDX-License-Identifier: MIT
//
// 本文件源自 TaskbarLyrics (https://github.com/ANYNC/TaskbarLyrics)
// 的 TaskbarLyrics.Core，原始许可为 MIT（见仓库根目录 LICENSE-TaskbarLyrics）。
// 本仓库整体按 GPL-3.0-or-later 分发。

using FluentFlyout.Lyrics.Abstractions;
using FluentFlyout.Lyrics.Database;
using FluentFlyout.Lyrics.Models;
using FluentFlyout.Lyrics.Utilities;

namespace FluentFlyout.Lyrics.Services;

public sealed class SongSearchMapResolver : ILyricMappingResolver
{
    public LyricMapping Resolve(TrackInfo track)
    {
        try
        {
            using var db = new SongSearchMapDbContext();
            var candidates = db.SongSearchMaps.Where(candidate =>
                candidate.OriginalTitle == track.Title &&
                candidate.OriginalArtist == track.Artist).ToArray();
            var map = SelectBestMapping(candidates, track.Album);
            if (map is null)
            {
                return LyricMapping.Unchanged(track);
            }

            Log.Info($"SQLite 别名映射命中: {track.Title} - {track.Artist}");
            return new LyricMapping(
                string.IsNullOrWhiteSpace(map.MappedTitle) ? track.Title : map.MappedTitle,
                string.IsNullOrWhiteSpace(map.MappedArtist) ? track.Artist : map.MappedArtist,
                map.PreferredProvider,
                map.IsMarkedAsPureMusic,
                string.IsNullOrWhiteSpace(map.MappedAlbum) ? track.Album : map.MappedAlbum);
        }
        catch (Exception exception)
        {
            Log.Error($"查询 SQLite 映射库失败: {exception.Message}");
            return LyricMapping.Unchanged(track);
        }
    }

    internal static SongSearchMap? SelectBestMapping(
        IEnumerable<SongSearchMap> candidates,
        string album)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        var matches = candidates.ToArray();
        var exact = matches.FirstOrDefault(candidate => candidate.OriginalAlbum == album);
        if (exact is not null)
        {
            return exact;
        }

        var legacy = matches.FirstOrDefault(candidate => string.IsNullOrWhiteSpace(candidate.OriginalAlbum));
        if (legacy is not null)
        {
            return legacy;
        }

        return matches.Length == 1 ? matches[0] : null;
    }
}
