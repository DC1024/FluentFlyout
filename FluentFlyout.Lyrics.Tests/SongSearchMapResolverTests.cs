// Copyright (c) 2026 anync
// SPDX-License-Identifier: MIT
//
// 本文件源自 TaskbarLyrics (https://github.com/ANYNC/TaskbarLyrics)
// 的 TaskbarLyrics.Core.Tests，原始许可为 MIT（见仓库根目录 LICENSE-TaskbarLyrics）。
// 本仓库整体按 GPL-3.0-or-later 分发。

using FluentFlyout.Lyrics.Database;
using FluentFlyout.Lyrics.Services;
using Xunit;

namespace FluentFlyout.Lyrics.Tests;

public sealed class SongSearchMapResolverTests
{
    [Fact]
    public void SelectBestMappingPrefersExactAlbum()
    {
        var legacy = CreateMapping(string.Empty, "Legacy title");
        var exact = CreateMapping("Studio album", "Studio title");

        var selected = SongSearchMapResolver.SelectBestMapping(
            [legacy, exact],
            "Studio album");

        Assert.Same(exact, selected);
    }

    [Fact]
    public void SelectBestMappingFallsBackToAlbumlessLegacyMapping()
    {
        var legacy = CreateMapping(string.Empty, "Legacy title");
        var otherAlbum = CreateMapping("Other album", "Other title");

        var selected = SongSearchMapResolver.SelectBestMapping(
            [legacy, otherAlbum],
            "Unmapped album");

        Assert.Same(legacy, selected);
    }

    [Fact]
    public void SelectBestMappingAvoidsAmbiguousCrossAlbumFallback()
    {
        var first = CreateMapping("First album", "First title");
        var second = CreateMapping("Second album", "Second title");

        var selected = SongSearchMapResolver.SelectBestMapping(
            [first, second],
            "Unmapped album");

        Assert.Null(selected);
    }

    [Fact]
    public void SelectBestMappingKeepsSingleExistingMappingCompatible()
    {
        var existing = CreateMapping("Old album metadata", "Mapped title");

        var selected = SongSearchMapResolver.SelectBestMapping(
            [existing],
            "Corrected album metadata");

        Assert.Same(existing, selected);
    }

    private static SongSearchMap CreateMapping(string originalAlbum, string mappedTitle) => new()
    {
        OriginalTitle = "Original title",
        OriginalArtist = "Original artist",
        OriginalAlbum = originalAlbum,
        MappedTitle = mappedTitle
    };
}
