// Copyright (c) 2026 anync
// SPDX-License-Identifier: MIT
//
// 本文件源自 TaskbarLyrics (https://github.com/ANYNC/TaskbarLyrics)
// 的 TaskbarLyrics.Core，原始许可为 MIT（见仓库根目录 LICENSE-TaskbarLyrics）。
// 本仓库整体按 GPL-3.0-or-later 分发。

using System.ComponentModel.DataAnnotations;

namespace TaskbarLyrics.Core.Database;

public class SongSearchMap
{
    [Key]
    public int Id { get; set; }

    [Required]
    public string OriginalTitle { get; set; } = string.Empty;

    [Required]
    public string OriginalArtist { get; set; } = string.Empty;

    [Required]
    public string OriginalAlbum { get; set; } = string.Empty;

    public string MappedTitle { get; set; } = string.Empty;
    public string MappedArtist { get; set; } = string.Empty;
    public string MappedAlbum { get; set; } = string.Empty;

    public bool IsMarkedAsPureMusic { get; set; }
    public string? PreferredProvider { get; set; }
}
