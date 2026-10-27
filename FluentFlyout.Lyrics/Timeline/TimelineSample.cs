// Copyright (c) 2026 anync
// SPDX-License-Identifier: MIT
//
// 本文件源自 TaskbarLyrics (https://github.com/ANYNC/TaskbarLyrics)
// 的 TaskbarLyrics.App/SmtcTimelineDiagnostics.cs，原始许可为 MIT
// （见仓库根目录 LICENSE-TaskbarLyrics）。
// 本仓库整体按 GPL-3.0-or-later 分发。

namespace FluentFlyout.Lyrics.Timeline;

/// <summary>
/// 一次时间轴采样的输入。
///
/// 上游的 <c>SmtcTimelineDiagnostics</c> 有 14 个字段，其中
/// <c>CapturedAtUtc</c>、<c>SelectedPosition</c>、<c>StrategyName</c>、
/// <c>IsFallbackSnapshot</c> 都只服务于它自己的诊断面板。这里只保留
/// 策略选位真正需要的输入，让策略可以脱离 SMTC 单测。
/// </summary>
/// <param name="SourceAppUserModelId">会话的 AUMID，用于识别播放器。</param>
/// <param name="Title">曲目标题，与 Artist 一起构成曲目身份，用于检测换歌。</param>
/// <param name="Artist">曲目艺术家。</param>
/// <param name="IsPlaying">播放器是否处于播放态。</param>
/// <param name="RawPosition">播放器报告的时间轴位置，未做任何外推。</param>
/// <param name="LastUpdatedTimeUtc">播放器报告的时间轴最后更新时刻。</param>
/// <param name="LastUpdateAge">从 <paramref name="LastUpdatedTimeUtc"/> 到采样时刻的间隔。</param>
/// <param name="ExtrapolatedPosition">按 <c>RawPosition + LastUpdateAge</c> 算出的朴素外推值。</param>
public readonly record struct TimelineSample(
    string SourceAppUserModelId,
    string Title,
    string Artist,
    bool IsPlaying,
    TimeSpan RawPosition,
    DateTimeOffset LastUpdatedTimeUtc,
    TimeSpan LastUpdateAge,
    TimeSpan ExtrapolatedPosition);
