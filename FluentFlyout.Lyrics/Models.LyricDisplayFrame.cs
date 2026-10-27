// Copyright (c) 2026 anync
// SPDX-License-Identifier: MIT
//
// 本文件源自 TaskbarLyrics (https://github.com/ANYNC/TaskbarLyrics)
// 的 TaskbarLyrics.Core，原始许可为 MIT（见仓库根目录 LICENSE-TaskbarLyrics）。
// 本仓库整体按 GPL-3.0-or-later 分发。

namespace TaskbarLyrics.Core.Models;

public sealed record LyricDisplayFrame(
    string CurrentLine,
    string NextLine,
    string Title,
    double LineProgress = 0.0,
    int CurrentLineIndex = -1,
    bool IsPureMusic = false,
    double? WordScanProgress = null,
    string? CurrentTranslation = null,
    string? NextTranslation = null,
    bool HasTrackTranslation = false);
