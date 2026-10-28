// Copyright (c) 2026 anync
// SPDX-License-Identifier: MIT
//
// 本文件源自 TaskbarLyrics (https://github.com/ANYNC/TaskbarLyrics)
// 的 TaskbarLyrics.Core.Tests，原始许可为 MIT（见仓库根目录 LICENSE-TaskbarLyrics）。
// 本仓库整体按 GPL-3.0-or-later 分发。

using FluentFlyout.Lyrics.Abstractions;
using FluentFlyout.Lyrics.Models;

namespace FluentFlyout.Lyrics.Tests;

/// <summary>
/// 不改变曲目信息的映射器替身。
/// 协调器不传 <c>mappingResolver</c> 时会退到真实的 <c>SongSearchMapResolver</c>，
/// 它会去 <c>%APPDATA%\TaskbarLyrics\database\song_maps.db</c> 查表——
/// 而歌词解析现在会顺带建表，测试就等于在改用户的真实数据目录。
/// 凡是只关心「映射这一步原样通过」的用例，都应该显式传这个替身。
/// </summary>
internal sealed class PassThroughMappingResolver : ILyricMappingResolver
{
    public LyricMapping Resolve(TrackInfo track) => LyricMapping.Unchanged(track);
}
