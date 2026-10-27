// Copyright (c) 2026 anync
// SPDX-License-Identifier: MIT
//
// 本文件源自 TaskbarLyrics (https://github.com/ANYNC/TaskbarLyrics)
// 的 TaskbarLyrics.Core.Tests，原始许可为 MIT（见仓库根目录 LICENSE-TaskbarLyrics）。
// 本仓库整体按 GPL-3.0-or-later 分发。

using TaskbarLyrics.Core.Abstractions;
using TaskbarLyrics.Core.Models;

namespace TaskbarLyrics.Core.Tests;

internal sealed class PlainTextLyricParser : ILyricPayloadParser
{
    public bool CanParse(LyricPayloadFormat format) => format == LyricPayloadFormat.PlainText;

    public Task<ParsedLyrics> ParseAsync(
        DecodedLyricPayload payload,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new ParsedLyrics(
            [new ParsedLyricLine(TimeSpan.Zero, null, payload.OriginalLyrics ?? "")],
            LyricTimingKind.LineTimed,
            LyricTimingProvenance.ProviderSupplied,
            payload.Format));
    }
}
