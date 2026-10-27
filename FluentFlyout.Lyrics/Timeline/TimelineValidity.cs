// Copyright (c) 2026 anync
// SPDX-License-Identifier: MIT
//
// 本文件为本仓库新增，不源自 TaskbarLyrics。

namespace FluentFlyout.Lyrics.Timeline;

/// <summary>
/// 判断 SMTC 时间轴是否可信。
///
/// <para>
/// <b>为什么必须单独抽出来</b>：这个判据有两个使用方——
/// <see cref="LocalClockTimelinePositionStrategy"/>（决定要不要接管）
/// 和 <see cref="TimelinePositionStrategyRegistry"/>（决定要不要跳过「保位」层）。
/// 两处若各写一份，阈值一旦改动必然漂移，症状是「策略接管了但位置被钉住」，
/// 极难排查。
/// </para>
///
/// <para>
/// <b>背景：网易云音乐的 SMTC 时间轴是坏的。</b>实测（cloudmusic.exe）：
/// </para>
/// <list type="bullet">
/// <item><c>Position</c> 恒为 <c>00:00.000</c>，连续 12 秒采样一动不动；</item>
/// <item><c>EndTime</c> 恒为 <c>00:00</c>，即不提供曲目时长；</item>
/// <item><c>LastUpdatedTime</c> 恒为 <c>1601-01-01T08:00:00+08:00</c>
/// （<see cref="DateTimeOffset"/> 的默认值，从未被赋值）；</item>
/// <item><c>Controls.IsPlaybackPositionEnabled</c> 为 <c>false</c>。</item>
/// </list>
/// <para>
/// 而标题、艺术家、播放状态都是正常的。所以「有没有歌」能判断，
/// 「播到第几秒」判断不了——歌词按位置取行，位置恒 0 就永远停在第一行。
/// </para>
/// </summary>
internal static class TimelineValidity
{
    /// <summary>
    /// 时间戳的可信下界。早于 Unix 纪元的时刻不可能是真实的采样时间。
    ///
    /// <para>
    /// 实测网易云报的是 <c>1601-01-01T00:00:00Z</c>，正好是 Windows
    /// <c>FILETIME</c> 的纪元——说明它把这个字段当「未赋值」留着，
    /// 系统读到的是 FILETIME 的零值。
    /// </para>
    ///
    /// <para>
    /// <b>为什么不用「距今超过 N 天」</b>：那个判据会把「播放器时间戳稍旧
    /// 但确实在刷新」的正常场景一并判死，也会误伤任何以固定历史时刻构造的
    /// 测试数据。用绝对下界只命中「这个字段从来没被维护过」这一类，
    /// 语义更窄也更准。
    /// </para>
    /// </summary>
    private static readonly DateTimeOffset MinimumPlausibleTimestamp =
        new(1970, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// 该采样是否来自一个「不可用的时间轴」。
    ///
    /// 两条同时成立才算：
    /// <list type="number">
    /// <item><c>RawPosition == 0</c>——没有报出任何进度；</item>
    /// <item>时间戳早于 Unix 纪元——说明这个字段根本没被维护过。</item>
    /// </list>
    /// 只判其中一条会误伤：刚开播时位置确实是 0，但时间戳是新鲜的。
    /// </summary>
    internal static bool IsUnusable(in TimelineSample sample)
    {
        return sample.RawPosition == TimeSpan.Zero
            && IsTimestampUnusable(sample.LastUpdatedTimeUtc);
    }

    /// <summary>
    /// 时间戳是否根本不可能来自一次真实采样。
    /// </summary>
    internal static bool IsTimestampUnusable(DateTimeOffset lastUpdatedUtc)
    {
        return lastUpdatedUtc < MinimumPlausibleTimestamp;
    }
}
