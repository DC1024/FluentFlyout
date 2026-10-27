// Copyright (c) 2026 anync
// SPDX-License-Identifier: MIT
//
// 本文件源自 TaskbarLyrics (https://github.com/ANYNC/TaskbarLyrics)
// 的 TaskbarLyrics.Core，原始许可为 MIT（见仓库根目录 LICENSE-TaskbarLyrics）。
// 本仓库整体按 GPL-3.0-or-later 分发。

namespace FluentFlyout.Lyrics.Models;

/// <summary>Enabled online sources in selection order, with an optional resolved-cache scope.</summary>
public sealed record LyricSourceSelection(
    IReadOnlyList<LyricProviderId> Order,
    string? CacheContext)
{
    public const string ManualCacheContext = "user-binding-v1";
}
