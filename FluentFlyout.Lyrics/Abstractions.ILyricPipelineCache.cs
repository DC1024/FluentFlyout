// Copyright (c) 2026 anync
// SPDX-License-Identifier: MIT
//
// 本文件源自 TaskbarLyrics (https://github.com/ANYNC/TaskbarLyrics)
// 的 TaskbarLyrics.Core，原始许可为 MIT（见仓库根目录 LICENSE-TaskbarLyrics）。
// 本仓库整体按 GPL-3.0-or-later 分发。

using FluentFlyout.Lyrics.Models;

namespace FluentFlyout.Lyrics.Abstractions;

public interface ILyricPipelineCache
{
    bool TryGetRaw(
        LyricProviderId providerId,
        string candidateId,
        out RawLyricPayload? payload,
        out LyricAcquisitionKind acquisition);

    void StoreRaw(RawLyricPayload payload, DateTimeOffset fetchedAtUtc);

    void Invalidate(LyricProviderId providerId, string candidateId);

    bool TryGetParsed(
        RawLyricPayload rawPayload,
        string parserId,
        string parserVersion,
        string normalizationVersion,
        out ParsedLyrics? parsedLyrics,
        out LyricAcquisitionKind acquisition);

    void StoreParsed(
        RawLyricPayload rawPayload,
        ParsedLyrics parsedLyrics,
        string parserId,
        string parserVersion,
        string normalizationVersion);
}
