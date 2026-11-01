// Copyright (c) 2026 anync
// SPDX-License-Identifier: MIT
//
// 本文件源自 TaskbarLyrics (https://github.com/ANYNC/TaskbarLyrics)
// 的 TaskbarLyrics.Core，原始许可为 MIT（见仓库根目录 LICENSE-TaskbarLyrics）。
// 本仓库整体按 GPL-3.0-or-later 分发。

using FluentFlyout.Lyrics.Models;
using FluentFlyout.Lyrics.Utilities;

namespace FluentFlyout.Lyrics.Services;

public static class ResolvedLyricsCompatibilityProjector
{
    public static LyricDocument ToLyricDocument(
        ResolvedLyrics resolved,
        bool includeInformationLines = true)
    {
        return ToLyricDocumentCore(resolved, track: null, includeInformationLines);
    }

    /// <summary>
    /// 带上曲目信息投影。有了歌名与歌手，就能把开片那张「歌名 - 歌手」的标题卡
    /// 与片头片尾的制作名单一起剔掉——它们不是歌词，却会占满开片几十秒，
    /// 并把「整首定宽」撑到任务栏可用上限。
    /// </summary>
    /// <param name="includeInformationLines">
    /// <c>false</c> 时剔除信息行。剔除只按行内容与位置判断，不改动持久化缓存。
    /// </param>
    public static LyricDocument ToLyricDocument(
        ResolvedLyrics resolved,
        TrackInfo? track,
        bool includeInformationLines = true)
    {
        return ToLyricDocumentCore(resolved, track, includeInformationLines);
    }

    private static LyricDocument ToLyricDocumentCore(
        ResolvedLyrics resolved,
        TrackInfo? track,
        bool includeInformationLines)
    {
        ArgumentNullException.ThrowIfNull(resolved);

        var source = resolved.Content.Lines;
        var lines = source
            .Where(line => includeInformationLines || !line.IsInformationLine)
            .ToList();

        if (!includeInformationLines && lines.Count > 0)
        {
            var mask = LyricInformationLineFilter.BuildMask(
                lines,
                track?.Title,
                track?.Artist);
            var kept = lines.Where((_, index) => !mask[index]).ToList();

            // 整份文档都是信息行时（例如只有一句「纯音乐，请欣赏」），
            // 剔干净反而会让歌词面板变空，所以退回原始行。
            if (kept.Count > 0)
            {
                lines = kept;
            }
        }

        return new LyricDocument(
            lines.Select(ToLyricLine).ToArray(),
            isPureMusic: resolved.Content.IsPureMusic);
    }

    private static LyricLine ToLyricLine(ParsedLyricLine line)
    {
        var syllables = line.Segments.Count == 0
            ? null
            : line.Segments
                .Select(segment => new LyricSyllable(
                    segment.StartTime - line.StartTime,
                    segment.EndTime - segment.StartTime,
                    segment.Text))
                .ToList();

        return new LyricLine(
            line.StartTime,
            line.Text,
            line.Translation,
            syllables);
    }
}
