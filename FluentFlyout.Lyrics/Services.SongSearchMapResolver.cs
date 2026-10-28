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
using Microsoft.EntityFrameworkCore;

namespace FluentFlyout.Lyrics.Services;

public sealed class SongSearchMapResolver : ILyricMappingResolver
{
    /// <summary>
    /// 建表失败后的静默期。映射库不可用不影响歌词主链路，
    /// 但如果每次解析都重试，一条 ERROR 会跟着每次搜词刷一遍。
    /// </summary>
    private const int SchemaRetrySeconds = 600;

    private readonly Func<SongSearchMapDbContext> _createContext;
    private readonly object _schemaGate = new();
    private bool _schemaReady;
    private long _schemaRetryAfterUtcTicks;
    private int _failureReported;

    public SongSearchMapResolver()
        : this(static () => new SongSearchMapDbContext())
    {
    }

    internal SongSearchMapResolver(Func<SongSearchMapDbContext> createContext)
    {
        ArgumentNullException.ThrowIfNull(createContext);
        _createContext = createContext;
    }

    public LyricMapping Resolve(TrackInfo track)
    {
        // 映射库只是「别名纠正」这层可选增强：表建不起来就直接放行原曲目信息，
        // 不能让一个从未被写入过的库拖住歌词解析。
        if (!EnsureSchema())
        {
            return LyricMapping.Unchanged(track);
        }

        try
        {
            using var db = _createContext();
            var candidates = db.SongSearchMaps
                .AsNoTracking()
                .Where(candidate =>
                    candidate.OriginalTitle == track.Title &&
                    candidate.OriginalArtist == track.Artist)
                .ToArray();
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
            ReportFailure($"查询 SQLite 映射库失败: {exception.Message}");
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

    /// <summary>
    /// 首次解析时补建表结构（幂等）。这个库的建表入口原来只有
    /// <see cref="SongSearchMapDbContext.InitializeDatabase"/>，而它没有任何调用点，
    /// 于是 <c>song_maps.db</c> 只被 EF 建成 0 字节空文件，每次查询都报
    /// <c>no such table: SongSearchMaps</c>。改成由使用方自行兜底，
    /// 免得再多一个「新增入口忘了初始化」的机会。
    /// </summary>
    private bool EnsureSchema()
    {
        if (_schemaReady)
        {
            return true;
        }

        lock (_schemaGate)
        {
            if (_schemaReady)
            {
                return true;
            }

            if (_schemaRetryAfterUtcTicks > DateTimeOffset.UtcNow.UtcTicks)
            {
                return false;
            }

            try
            {
                using var context = _createContext();
                _schemaReady = context.EnsureSchema();
            }
            catch (Exception exception)
            {
                Log.Error($"初始化 SQLite 映射库失败: {exception.Message}");
                _schemaReady = false;
            }

            if (!_schemaReady)
            {
                _schemaRetryAfterUtcTicks = DateTimeOffset.UtcNow.AddSeconds(SchemaRetrySeconds).UtcTicks;
            }

            return _schemaReady;
        }
    }

    /// <summary>
    /// 只在首次失败时写 ERROR，之后降到 Debug。
    /// 解析会在每次换歌、每次搜索尝试时被调用，同一故障重复上报只会把错误日志刷满，
    /// 反而把真问题埋掉。
    /// </summary>
    private void ReportFailure(string message)
    {
        if (Interlocked.Exchange(ref _failureReported, 1) == 0)
        {
            Log.Error(message);
            return;
        }

        Log.Debug(message);
    }
}
