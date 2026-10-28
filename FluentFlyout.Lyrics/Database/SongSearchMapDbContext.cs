// Copyright (c) 2026 anync
// SPDX-License-Identifier: MIT
//
// 本文件源自 TaskbarLyrics (https://github.com/ANYNC/TaskbarLyrics)
// 的 TaskbarLyrics.Core，原始许可为 MIT（见仓库根目录 LICENSE-TaskbarLyrics）。
// 本仓库整体按 GPL-3.0-or-later 分发。

using FluentFlyout.Lyrics.Utilities;
using Microsoft.EntityFrameworkCore;
using System;
using System.IO;

namespace FluentFlyout.Lyrics.Database;

public class SongSearchMapDbContext : DbContext
{
    private readonly string? _databasePath;

    public SongSearchMapDbContext()
    {
    }

    internal SongSearchMapDbContext(string databasePath)
    {
        _databasePath = Path.GetFullPath(databasePath);
    }

    public DbSet<SongSearchMap> SongSearchMaps => Set<SongSearchMap>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        var dbPath = _databasePath;
        if (dbPath is null)
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var dbDirectory = Path.Combine(appData, "TaskbarLyrics", "database");
            dbPath = Path.Combine(dbDirectory, "song_maps.db");

            try
            {
                Directory.CreateDirectory(dbDirectory);
            }
            catch
            {
                // Ignore folder creation errors, EF will throw during use if directory is inaccessible.
            }
        }

        optionsBuilder.UseSqlite($"Data Source={dbPath}");
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // 为原始元数据字段建立复合唯一索引，保障高频查询效率
        modelBuilder.Entity<SongSearchMap>()
            .HasIndex(s => new { s.OriginalTitle, s.OriginalArtist, s.OriginalAlbum })
            .IsUnique();
    }

    /// <summary>
    /// 建表（幂等）。表已存在时只做一次 <c>sqlite_master</c> 探测。
    /// 失败**不抛**，由调用方决定是否降级：映射库是纯可选增强，
    /// 它不可用时歌词主链路必须照常工作。
    /// </summary>
    internal bool EnsureSchema()
    {
        try
        {
            Database.EnsureCreated();
            return true;
        }
        catch (Exception exception)
        {
            Log.Error($"初始化 SQLite 映射库失败: {exception.Message}");
            return false;
        }
    }

    public static bool InitializeDatabase()
    {
        using var context = new SongSearchMapDbContext();
        return context.EnsureSchema();
    }
}
