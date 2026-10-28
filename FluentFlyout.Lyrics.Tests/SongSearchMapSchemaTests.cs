// Copyright (c) 2026 anync
// SPDX-License-Identifier: MIT
//
// 本文件源自 TaskbarLyrics (https://github.com/ANYNC/TaskbarLyrics)
// 的 TaskbarLyrics.Core.Tests，原始许可为 MIT（见仓库根目录 LICENSE-TaskbarLyrics）。
// 本仓库整体按 GPL-3.0-or-later 分发。

using FluentFlyout.Lyrics.Database;
using FluentFlyout.Lyrics.Models;
using FluentFlyout.Lyrics.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace FluentFlyout.Lyrics.Tests;

/// <summary>
/// 映射库的建表与降级行为。
/// 线上缺陷是「<c>song_maps.db</c> 一直是 0 字节，每次查询报 no such table」——
/// 根因不在查询，在于建表入口从未被调用过，所以这里把「谁负责建表」钉死成行为。
/// </summary>
public sealed class SongSearchMapSchemaTests
{
    private static readonly TrackInfo Track =
        new("track-id", "晚风", "陈婧霏", "专辑", "QQMusic", TimeSpan.FromMinutes(4));

    [Fact]
    public void ResolveCreatesSchemaOnFreshDatabase()
    {
        var directory = CreateTempDirectory();
        var databasePath = Path.Combine(directory, "song_maps.db");

        try
        {
            var resolver = new SongSearchMapResolver(() => new SongSearchMapDbContext(databasePath));

            var mapping = resolver.Resolve(Track);

            Assert.Equal(Track.Title, mapping.Title);
            Assert.Equal(Track.Artist, mapping.Artist);
            Assert.True(TableExists(databasePath, "SongSearchMaps"));
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    /// <summary>
    /// 复现线上那份 0 字节文件：EF 打开数据源时会把文件建出来但不建表，
    /// 之后 <c>EnsureCreated</c> 必须把它补成完整结构，而不是判定「库已存在」就跳过。
    /// </summary>
    [Fact]
    public void ResolveCreatesSchemaWhenDatabaseFileIsEmpty()
    {
        var directory = CreateTempDirectory();
        var databasePath = Path.Combine(directory, "song_maps.db");
        File.WriteAllBytes(databasePath, Array.Empty<byte>());

        try
        {
            Assert.Equal(0, new FileInfo(databasePath).Length);

            var resolver = new SongSearchMapResolver(() => new SongSearchMapDbContext(databasePath));
            var mapping = resolver.Resolve(Track);

            Assert.Equal(Track.Title, mapping.Title);
            Assert.True(TableExists(databasePath, "SongSearchMaps"));
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    [Fact]
    public void ResolveServesStoredMapping()
    {
        var directory = CreateTempDirectory();
        var databasePath = Path.Combine(directory, "song_maps.db");

        try
        {
            using (var context = new SongSearchMapDbContext(databasePath))
            {
                Assert.True(context.EnsureSchema());
                context.SongSearchMaps.Add(new SongSearchMap
                {
                    OriginalTitle = Track.Title,
                    OriginalArtist = Track.Artist,
                    OriginalAlbum = Track.Album,
                    MappedTitle = "晚风心里吹",
                    MappedArtist = "阿梨粤",
                    MappedAlbum = "晚风心里吹",
                    PreferredProvider = "QqMusic",
                    IsMarkedAsPureMusic = false
                });
                context.SaveChanges();
            }

            var resolver = new SongSearchMapResolver(() => new SongSearchMapDbContext(databasePath));
            var mapping = resolver.Resolve(Track);

            Assert.Equal("晚风心里吹", mapping.Title);
            Assert.Equal("阿梨粤", mapping.Artist);
            Assert.Equal("晚风心里吹", mapping.Album);
            Assert.Equal("QqMusic", mapping.PreferredProvider);
            Assert.False(mapping.IsPureMusic);
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    /// <summary>
    /// 库彻底不可用时（这里把目录本身当数据源）必须降级成「无映射」，
    /// 不能把异常抛到歌词解析链路上。
    /// </summary>
    [Fact]
    public void ResolveFallsBackWhenSchemaCannotBeCreated()
    {
        var directory = CreateTempDirectory();

        try
        {
            var resolver = new SongSearchMapResolver(() => new SongSearchMapDbContext(directory));

            var mapping = resolver.Resolve(Track);

            Assert.Equal(Track.Title, mapping.Title);
            Assert.Equal(Track.Artist, mapping.Artist);
            Assert.Null(mapping.PreferredProvider);
            Assert.Equal(Track.Album, mapping.Album);
        }
        finally
        {
            DeleteTempDirectory(directory);
        }
    }

    private static bool TableExists(string databasePath, string tableName)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadOnly
        }.ToString();

        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name";
        command.Parameters.AddWithValue("$name", tableName);
        return Convert.ToInt64(command.ExecuteScalar()) > 0;
    }

    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"taskbar-lyrics-songmaps-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void DeleteTempDirectory(string directory)
    {
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(directory, recursive: true);
        }
        catch (IOException)
        {
            // 临时目录清不掉不影响断言结论。
        }
    }
}
