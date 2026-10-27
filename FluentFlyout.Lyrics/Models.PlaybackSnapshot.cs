// Copyright (c) 2026 anync
// SPDX-License-Identifier: MIT
//
// 本文件源自 TaskbarLyrics (https://github.com/ANYNC/TaskbarLyrics)
// 的 TaskbarLyrics.Core，原始许可为 MIT（见仓库根目录 LICENSE-TaskbarLyrics）。
// 本仓库整体按 GPL-3.0-or-later 分发。

namespace TaskbarLyrics.Core.Models;

public sealed record PlaybackSnapshot(
    bool IsPlaying,
    TimeSpan Position,
    TrackInfo? Track,
    byte[]? CoverImageBytes = null,
    TimeSpan? RawPosition = null,
    TimeSpan? ExtrapolatedPosition = null,
    bool IsCoverLoading = false,
    string SourceAppUserModelId = "",
    bool CanSeek = false);
