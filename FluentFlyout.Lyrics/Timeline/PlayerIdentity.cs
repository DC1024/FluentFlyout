// Copyright (c) 2026 anync
// SPDX-License-Identifier: MIT
//
// 本文件部分逻辑源自 TaskbarLyrics (https://github.com/ANYNC/TaskbarLyrics)
// 的 TaskbarLyrics.App/PlayerSourceIdentity.cs 与
// CommonExtrapolatedTimelinePositionStrategy.cs，原始许可为 MIT
// （见仓库根目录 LICENSE-TaskbarLyrics）。
// 本仓库整体按 GPL-3.0-or-later 分发。

namespace FluentFlyout.Lyrics.Timeline;

/// <summary>
/// 从会话的 AUMID 识别播放器。只做识别，不做排序——
/// 上游 <c>PlayerSourceIdentity</c> 还带一个 <c>NormalizeOrder</c> 用于
/// 歌词源优先级排序，那部分属于路由策略，不在这里。
/// </summary>
public static class PlayerIdentity
{
    /// <summary>
    /// Apple Music 的 AUMID 有两种写法：进程名与包族名。
    /// </summary>
    private const string AppleMusicPackageId = "appleinc.applemusicwin_nzyj5cx40ttqa!app";

    public static bool IsAppleMusic(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return false;
        }

        var trimmed = source.Trim();
        return trimmed.Equals("AppleMusic", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals(AppleMusicPackageId, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsSpotify(string? source) => Contains(source, "spotify");

    public static bool IsNetease(string? source)
    {
        return Contains(source, "netease")
            || Contains(source, "cloudmusic")
            || Contains(source, "163music")
            || Contains(source, "music.163")
            || Contains(source, "wyy");
    }

    public static bool IsQqMusic(string? source) => Contains(source, "qqmusic");

    public static bool IsKugou(string? source) => Contains(source, "kugou");

    /// <summary>
    /// 上游 <c>CommonExtrapolated</c> 策略覆盖的四家播放器。
    /// 它们的 SMTC 时间轴行为一致，可以共用朴素外推。
    /// </summary>
    public static bool UsesCommonExtrapolation(string? source)
    {
        return IsSpotify(source) || IsNetease(source) || IsQqMusic(source) || IsKugou(source);
    }

    private static bool Contains(string? value, string token)
    {
        return !string.IsNullOrWhiteSpace(value)
            && value.Contains(token, StringComparison.OrdinalIgnoreCase);
    }
}
