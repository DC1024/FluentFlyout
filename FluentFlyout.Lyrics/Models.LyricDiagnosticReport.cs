// Copyright (c) 2026 anync
// SPDX-License-Identifier: MIT
//
// 本文件源自 TaskbarLyrics (https://github.com/ANYNC/TaskbarLyrics)
// 的 TaskbarLyrics.Core，原始许可为 MIT（见仓库根目录 LICENSE-TaskbarLyrics）。
// 本仓库整体按 GPL-3.0-or-later 分发。

namespace FluentFlyout.Lyrics.Models;

public sealed record LyricDiagnosticReport(
    DateTimeOffset CapturedAtUtc,
    TrackInfo OriginalTrack,
    TrackInfo? EffectiveTrack,
    bool IsPureMusic,
    string? PreferredProvider,
    IReadOnlyList<LyricDiagnosticSearchVariant> SearchVariants,
    IReadOnlyList<LyricDiagnosticProvider> Providers,
    LyricDiagnosticSelection? Selection,
    string? Error);

public sealed record LyricDiagnosticSearchVariant(
    string Id,
    string Title,
    IReadOnlyList<string> Artists,
    string Album,
    double DurationSeconds,
    IReadOnlyList<string> RelaxationReasons);

public sealed record LyricDiagnosticProvider(
    string ProviderId,
    LyricSourceTerminalState? State,
    string? Detail,
    bool Selected,
    IReadOnlyList<LyricDiagnosticCandidate> Candidates);

public sealed record LyricDiagnosticCandidate(
    string CandidateId,
    string Title,
    IReadOnlyList<string> Artists,
    string Album,
    double DurationSeconds,
    string QueryVariantId,
    IReadOnlyList<string> FetchMetadataKeys,
    bool IsAdmitted,
    bool IsHighConfidence,
    int Score,
    IReadOnlyList<string> RejectionReasons);

public sealed record LyricDiagnosticSelection(
    string ProviderId,
    string CandidateId,
    LyricAcquisitionKind Acquisition,
    LyricPayloadFormat Format,
    LyricTimingKind TimingKind,
    LyricTimingProvenance TimingProvenance,
    int LineCount,
    IReadOnlyDictionary<string, string> Diagnostics);
