// Copyright (c) 2026 anync
// SPDX-License-Identifier: MIT
//
// 本文件源自 TaskbarLyrics (https://github.com/ANYNC/TaskbarLyrics)
// 的 TaskbarLyrics.Core，原始许可为 MIT（见仓库根目录 LICENSE-TaskbarLyrics）。
// 本仓库整体按 GPL-3.0-or-later 分发。

using FluentFlyout.Lyrics.Models;

namespace FluentFlyout.Lyrics.Services;

internal static class LyricSearchStageExecutor
{
    public static async Task<IReadOnlyList<SourceTrackCandidate>> ExecuteAsync(
        LyricSearchPlan plan,
        Func<SearchQueryVariant, CancellationToken, Task<IReadOnlyList<SourceTrackCandidate>>> retrieveCandidatesAsync,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(retrieveCandidatesAsync);

        var allCandidates = new Dictionary<string, SourceTrackCandidate>(StringComparer.Ordinal);
        foreach (var variant in plan.Variants)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidates = await retrieveCandidatesAsync(variant, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(candidates);

            var stageCandidates = DeduplicateCandidates(candidates);
            foreach (var candidate in stageCandidates)
            {
                allCandidates.TryAdd(candidate.CandidateId, candidate);
            }

            if (HasAdmittedCandidate(plan.OriginalTrack, stageCandidates, cancellationToken))
            {
                return stageCandidates;
            }
        }

        return allCandidates.Values.ToArray();
    }

    private static SourceTrackCandidate[] DeduplicateCandidates(
        IReadOnlyList<SourceTrackCandidate> candidates)
    {
        var uniqueCandidates = new Dictionary<string, SourceTrackCandidate>(StringComparer.Ordinal);
        foreach (var candidate in candidates)
        {
            ArgumentNullException.ThrowIfNull(candidate);
            uniqueCandidates.TryAdd(candidate.CandidateId, candidate);
        }

        return uniqueCandidates.Values.ToArray();
    }

    private static bool HasAdmittedCandidate(
        TrackIdentity originalTrack,
        IReadOnlyList<SourceTrackCandidate> candidates,
        CancellationToken cancellationToken)
    {
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (LyricIdentityEvaluator.Evaluate(originalTrack, candidate).IsAdmitted)
            {
                return true;
            }
        }

        return false;
    }
}
