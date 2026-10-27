// Copyright (c) 2026 anync
// SPDX-License-Identifier: MIT
//
// 本文件源自 TaskbarLyrics (https://github.com/ANYNC/TaskbarLyrics)
// 的 TaskbarLyrics.Core，原始许可为 MIT（见仓库根目录 LICENSE-TaskbarLyrics）。
// 本仓库整体按 GPL-3.0-or-later 分发。

using System.IO;
using FluentFlyout.Lyrics.Abstractions;
using FluentFlyout.Lyrics.Models;
using FluentFlyout.Lyrics.Utilities;

namespace FluentFlyout.Lyrics.Services;

public sealed class LyricifyPayloadDecoder : ILyricPayloadDecoder
{
    public bool CanDecode(LyricPayloadFormat format) =>
        format is LyricPayloadFormat.Qrc or LyricPayloadFormat.Krc or LyricPayloadFormat.Lrc;

    public Task<DecodedLyricPayload> DecodeAsync(
        RawLyricPayload payload,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);
        cancellationToken.ThrowIfCancellationRequested();

        var original = payload.OriginalLyrics;
        var translation = payload.TranslationLyrics;
        if (payload.ProviderId == KnownLyricProviders.QQMusic)
        {
            original = DecodeQqContent(original);
            try
            {
                translation = DecodeQqContent(translation);
            }
            catch (FormatException exception)
            {
                Log.Warn($"QQ translation rejected. Candidate='{payload.CandidateId}' Error='{exception.Message}'");
                translation = null;
            }

            return Task.FromResult(new DecodedLyricPayload(
                payload.ProviderId, payload.CandidateId, payload.Format,
                original, translation, payload.IsPureMusic, payload.Diagnostics));
        }

        if (payload.IsEncrypted && !string.IsNullOrWhiteSpace(original))
        {
            original = payload.Format switch
            {
                // QRC 密文解压后是 QrcInfos XML，真正的歌词文本在 Lyric_1 的 LyricContent 属性里。
                LyricPayloadFormat.Qrc =>
                    QqMusicResponseMapper.UnwrapQrcContent(
                        Lyricify.Lyrics.Decrypter.Qrc.Decrypter.DecryptLyrics(original)),
                LyricPayloadFormat.Krc =>
                    Lyricify.Lyrics.Decrypter.Krc.Decrypter.DecryptLyrics(original),
                _ => throw new NotSupportedException(
                    $"No decoder is registered for encrypted {payload.Format} payloads.")
            };
        }

        return Task.FromResult(new DecodedLyricPayload(
            payload.ProviderId,
            payload.CandidateId,
            payload.Format,
            original,
            translation,
            payload.IsPureMusic,
            payload.Diagnostics));
    }

    internal static string? DecodeQqContent(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return content;
        }

        try
        {
            var decoded = QqMusicResponseMapper.IsHexPayload(content)
                ? Lyricify.Lyrics.Decrypter.Qrc.Decrypter.DecryptLyrics(content)
                : content;
            return QqMusicResponseMapper.UnwrapQrcContent(decoded);
        }
        catch (Exception exception) when (exception is
            ArgumentException or IndexOutOfRangeException or InvalidDataException or
            ICSharpCode.SharpZipLib.SharpZipBaseException)
        {
            // 将第三方解密器的坏载荷异常收敛成内容错误，允许 QQ 源回退 LRC。
            throw new FormatException("QQ encrypted lyric content is invalid.", exception);
        }
    }
}
