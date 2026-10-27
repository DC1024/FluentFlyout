// Copyright (c) 2026 anync
// SPDX-License-Identifier: MIT
//
// 本文件源自 TaskbarLyrics (https://github.com/ANYNC/TaskbarLyrics)
// 的 TaskbarLyrics.Core.Tests，原始许可为 MIT（见仓库根目录 LICENSE-TaskbarLyrics）。
// 本仓库整体按 GPL-3.0-or-later 分发。

using System.Globalization;
using FluentFlyout.Lyrics.Utilities;
using Xunit;

namespace FluentFlyout.Lyrics.Tests;

public sealed class LogFileWriterTests
{
    [Fact]
    public void CleanupUsesInvariantDatesUnderNonGregorianCulture()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"taskbar-lyrics-logs-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var today = DateTime.Now.Date;
        var currentPath = Path.Combine(directory,
            $"app_debug-{today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.log");
        var oldPath = Path.Combine(directory,
            $"app_debug-{today.AddDays(-30).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.log");
        File.WriteAllText(currentPath, "current");
        File.WriteAllText(oldPath, "old");
        var previousCulture = CultureInfo.CurrentCulture;

        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("th-TH");

            var activePath = LogFileWriter.GetActiveLogPath(currentPath, retentionDays: 7);

            Assert.Equal(currentPath, activePath);
            Assert.True(File.Exists(currentPath));
            Assert.False(File.Exists(oldPath));
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            Directory.Delete(directory, recursive: true);
        }
    }
}
