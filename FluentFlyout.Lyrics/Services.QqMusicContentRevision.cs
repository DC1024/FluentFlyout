// Copyright (c) 2026 anync
// SPDX-License-Identifier: MIT
//
// 本文件源自 TaskbarLyrics (https://github.com/ANYNC/TaskbarLyrics)
// 的 TaskbarLyrics.Core，原始许可为 MIT（见仓库根目录 LICENSE-TaskbarLyrics）。
// 本仓库整体按 GPL-3.0-or-later 分发。

using FluentFlyout.Lyrics.Models;

namespace FluentFlyout.Lyrics.Services;

internal static class QqMusicContentRevision
{
    internal const string Current = "3";
    internal const string DiagnosticKey = "qqContentRevision";

    internal static bool NeedsRefresh(LyricProviderId provider, LyricPayloadFormat format,
        IReadOnlyDictionary<string, string> diagnostics) =>
        provider == KnownLyricProviders.QQMusic && format is LyricPayloadFormat.Lrc or LyricPayloadFormat.Qrc &&
        (!diagnostics.TryGetValue(DiagnosticKey, out var revision) || revision != Current);
}
