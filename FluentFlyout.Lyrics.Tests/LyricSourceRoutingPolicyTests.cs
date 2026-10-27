// Copyright (c) 2026 anync
// SPDX-License-Identifier: MIT
//
// 本文件源自 TaskbarLyrics (https://github.com/ANYNC/TaskbarLyrics)
// 的 TaskbarLyrics.Core.Tests，原始许可为 MIT（见仓库根目录 LICENSE-TaskbarLyrics）。
// 本仓库整体按 GPL-3.0-or-later 分发。

using FluentFlyout.Lyrics.Models;
using FluentFlyout.Lyrics.Services;
using Xunit;

namespace FluentFlyout.Lyrics.Tests;

public sealed class LyricSourceRoutingPolicyTests
{
    [Theory]
    [InlineData("QQMusic.exe", "QQMusic")]
    [InlineData("CloudMusic", "Netease")]
    [InlineData("music.163.com", "Netease")]
    [InlineData("KuGou Music", "Kugou")]
    [InlineData("Spotify", "")]
    public void TryGetOfficialProviderMapsKnownPlayersAndRejectsOthers(string sourceApp, string expectedProvider)
    {
        var found = LyricSourceRoutingPolicy.TryGetOfficialProvider(sourceApp, out var provider);

        Assert.Equal(!string.IsNullOrEmpty(expectedProvider), found);
        Assert.Equal(expectedProvider, provider);
    }

    [Fact]
    public void BuildFallbackBatchesForOfficialPlayerExcludesItsOwnProvider()
    {
        var batches = LyricSourceRoutingPolicy.BuildFallbackBatches(CreateTrack("QQMusic"));

        var batch = Assert.Single(batches);
        Assert.Equal(["Netease", "Kugou", "LRCLIB"], batch);
    }

    [Fact]
    public void BuildFallbackBatchesForOtherPlayerKeepsConfiguredTwoPhaseFallback()
    {
        var batches = LyricSourceRoutingPolicy.BuildFallbackBatches(CreateTrack("Spotify"));

        Assert.Collection(
            batches,
            first => Assert.Equal(["QQMusic", "Netease", "LRCLIB"], first),
            second => Assert.Equal(["Kugou"], second));
    }

    private static TrackInfo CreateTrack(string sourceApp) => new(
        "track-id",
        "Midnight City",
        "M83",
        "Hurry Up, We're Dreaming",
        sourceApp,
        TimeSpan.FromSeconds(244));
}
