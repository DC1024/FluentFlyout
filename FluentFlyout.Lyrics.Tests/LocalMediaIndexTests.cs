// Copyright (c) 2026 anync
// SPDX-License-Identifier: MIT
//
// 本文件源自 TaskbarLyrics (https://github.com/ANYNC/TaskbarLyrics)
// 的 TaskbarLyrics.Core.Tests，原始许可为 MIT（见仓库根目录 LICENSE-TaskbarLyrics）。
// 本仓库整体按 GPL-3.0-or-later 分发。

using FluentFlyout.Lyrics.Services;
using Xunit;

namespace FluentFlyout.Lyrics.Tests;

public sealed class LocalMediaIndexTests
{
    [Fact]
    public void EnumerateEntriesSafelyKeepsPriorEntriesWhenEnumerationFails()
    {
        var entries = LocalMediaIndexRegistry.EnumerateEntriesSafely(FailingEntries).ToArray();

        Assert.Equal(["first.lrc"], entries);

        static IEnumerable<string> FailingEntries()
        {
            yield return "first.lrc";
            throw new IOException("Directory disappeared during enumeration.");
        }
    }

    [Fact]
    public void EnumerateEntriesSafelySkipsInaccessibleDirectory()
    {
        var entries = LocalMediaIndexRegistry.EnumerateEntriesSafely(
            () => throw new UnauthorizedAccessException()).ToArray();

        Assert.Empty(entries);
    }

    [Fact]
    public async Task AcquireForTheSameFoldersSharesOneCancellableFileIndex()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"taskbar-lyrics-index-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, "Artist - Song.mp3"), []);
        File.WriteAllText(Path.Combine(directory, "Artist - Song.lrc"), "[00:00.00]Song");
        File.WriteAllText(Path.Combine(directory, "ignored.txt"), "ignored");

        try
        {
            using var first = LocalMediaIndexRegistry.Acquire([directory]);
            using var second = LocalMediaIndexRegistry.Acquire([directory]);

            var snapshot = await WaitForFilesAsync(first, expectedCount: 2);

            Assert.Equal(2, snapshot.Files.Count);
            Assert.Contains(snapshot.Files, entry => entry.Kind == LocalMediaFileKind.Audio);
            Assert.Contains(snapshot.Files, entry => entry.Kind == LocalMediaFileKind.Lyric);
            Assert.Equal(snapshot.Version, second.GetSnapshot().Version);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task<LocalMediaIndexSnapshot> WaitForFilesAsync(
        ILocalMediaIndex index,
        int expectedCount)
    {
        // 10 秒而非 1 秒：这里有真实的临时目录扫描 + 后台建索引，共享 CI runner 上
        // Defender 扫描新写入的临时文件会把 1 秒预算吃光（master 上正是这个用例超时）。
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!timeout.IsCancellationRequested)
        {
            var snapshot = index.GetSnapshot();
            if (snapshot.Files.Count == expectedCount)
            {
                return snapshot;
            }

            await Task.Delay(10, timeout.Token);
        }

        throw new TimeoutException("Local media index did not finish in time.");
    }
}
