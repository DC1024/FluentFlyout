// Copyright (c) 2026 anync
// SPDX-License-Identifier: MIT
//
// 本文件源自 TaskbarLyrics (https://github.com/ANYNC/TaskbarLyrics)
// 的 TaskbarLyrics.Core，原始许可为 MIT（见仓库根目录 LICENSE-TaskbarLyrics）。
// 本仓库整体按 GPL-3.0-or-later 分发。

using FluentFlyout.Lyrics.Models;
using FluentFlyout.Lyrics.Utilities;

namespace FluentFlyout.Lyrics.Services;

public static class LyricIdentityEvaluator
{
    public static LyricCandidateEvaluation Evaluate(
        TrackIdentity originalTrack,
        SourceTrackCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(originalTrack);
        ArgumentNullException.ThrowIfNull(candidate);

        var track = new TrackInfo(
            originalTrack.TrackId,
            originalTrack.Title,
            string.Join(", ", originalTrack.Artists),
            originalTrack.Album,
            originalTrack.SourceApp,
            originalTrack.Duration,
            originalTrack.SongId);
        var score = LyricMatcher.Score(
            track,
            candidate.Title,
            string.Join(", ", candidate.Artists),
            (int)Math.Round(candidate.Duration.TotalSeconds),
            candidate.Album);
        return score >= LyricMatchingPolicy.MinimumAcceptedMatchScore
            ? LyricCandidateEvaluation.Accepted(score)
            : LyricCandidateEvaluation.Rejected(
                score,
                score == 0 ? "identity-conflict" : "below-admission-threshold");
    }
}

public static class ProviderSongIdPolicy
{
    public static bool CanUseDirectSongId(
        TrackIdentity track,
        LyricProviderId providerId)
    {
        if (string.IsNullOrWhiteSpace(track.SongId))
        {
            return false;
        }

        return LyricSourceRoutingPolicy.TryGetOfficialProvider(track.SourceApp, out var officialProvider) &&
               string.Equals(officialProvider, providerId.Value, StringComparison.OrdinalIgnoreCase);
    }
}
