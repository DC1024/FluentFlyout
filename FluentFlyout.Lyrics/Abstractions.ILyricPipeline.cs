// Copyright (c) 2026 anync
// SPDX-License-Identifier: MIT
//
// 本文件源自 TaskbarLyrics (https://github.com/ANYNC/TaskbarLyrics)
// 的 TaskbarLyrics.Core，原始许可为 MIT（见仓库根目录 LICENSE-TaskbarLyrics）。
// 本仓库整体按 GPL-3.0-or-later 分发。

using TaskbarLyrics.Core.Models;

namespace TaskbarLyrics.Core.Abstractions;

public interface ILyricSource
{
    LyricProviderId ProviderId { get; }

    Task<IReadOnlyList<SourceTrackCandidate>> SearchAsync(
        LyricSearchPlan plan,
        CancellationToken cancellationToken = default);

    Task<RawLyricPayload?> FetchAsync(
        SourceTrackCandidate candidate,
        CancellationToken cancellationToken = default);
}

public interface ILyricPayloadDecoder
{
    bool CanDecode(LyricPayloadFormat format);

    Task<DecodedLyricPayload> DecodeAsync(
        RawLyricPayload payload,
        CancellationToken cancellationToken = default);
}

public interface ILyricPayloadParser
{
    bool CanParse(LyricPayloadFormat format);

    Task<ParsedLyrics> ParseAsync(
        DecodedLyricPayload payload,
        CancellationToken cancellationToken = default);
}
