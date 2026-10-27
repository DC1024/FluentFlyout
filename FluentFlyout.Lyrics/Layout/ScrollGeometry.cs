// Copyright (c) 2026 anync
// SPDX-License-Identifier: MIT
//
// 本文件为本仓库新增，不源自 TaskbarLyrics。

namespace FluentFlyout.Lyrics.Layout;

/// <summary>
/// 长句滚动的位置计算。
///
/// 与 <see cref="ScanGeometry"/> 分开：扫描是「唱到哪」，
/// 滚动是「视口该往哪移」。两者可以同时发生（长句 + 逐字扫描），
/// 但推导完全独立，混在一起会让两个都难测。
///
/// 采用的策略与 FluentFlyout 现有标题滚动一致（<c>UpdateMarquee</c>）：
/// 文字超出视口时先停 <see cref="LeadInSeconds"/> 让用户看清开头，
/// 再用 <see cref="ScrollSpeedDipPerSecond"/> 的速度匀速滚到底，
/// 末尾停 <see cref="TrailOutSeconds"/>，然后回到开头。
///
/// 为什么不做「无级连续滚动」：歌词一句通常只停留 3-6 秒，
/// 连续滚动会让整句几乎没有静止时刻，读起来比截断更累。
/// 停顿-滚动-停顿的节奏更像「跟着唱」。
/// </summary>
public readonly record struct ScrollGeometry
{
    /// <summary>开头静止时长（秒）。</summary>
    public const double LeadInSeconds = 0.8;

    /// <summary>末尾静止时长（秒）。</summary>
    public const double TrailOutSeconds = 0.8;

    /// <summary>
    /// 滚动速度（DIP/秒）。
    ///
    /// 40 比 FluentFlyout 标题滚动默认的 20 快一倍：歌词的可用时间
    /// 由句子时长决定，不像标题那样可以无限循环。太慢会导致
    /// 一句唱完了还没滚到底。
    /// </summary>
    public const double ScrollSpeedDipPerSecond = 40;

    /// <summary>滚动偏移量（DIP，负值表示向左移）。</summary>
    public required double OffsetX { get; init; }

    /// <summary>是否需要滚动（文字超出视口）。</summary>
    public required bool NeedsScroll { get; init; }

    /// <summary>滚动总距离（DIP）。不滚动时为 0。</summary>
    public required double ScrollDistance { get; init; }

    /// <summary>
    /// 滚动一程需要的时间（秒）。不滚动时为 0。
    /// 用于判断「这句的时长够不够滚完」。
    /// </summary>
    public required double ScrollDurationSeconds { get; init; }

    /// <summary>不滚动的静止状态。</summary>
    public static ScrollGeometry None => new()
    {
        OffsetX = 0,
        NeedsScroll = false,
        ScrollDistance = 0,
        ScrollDurationSeconds = 0
    };

    /// <summary>
    /// 按行进度计算滚动偏移。
    /// </summary>
    /// <param name="lineProgress">
    /// 行内进度 0..1（<c>LyricDisplayFrame.LineProgress</c>）。
    /// </param>
    /// <param name="textWidth">文本实际宽度（DIP）。</param>
    /// <param name="viewportWidth">视口宽度（DIP）。</param>
    /// <param name="lineDurationSeconds">
    /// 这句的时长（秒）。用于把「进度」换算成「真实时间」，
    /// 因为滚动速度是按秒定义的，不按进度百分比。
    /// 传 0 或负值表示未知，此时退化为按进度线性滚动。
    /// </param>
    /// <param name="enabled">用户是否开启长句滚动。</param>
    public static ScrollGeometry Create(
        double lineProgress,
        double textWidth,
        double viewportWidth,
        double lineDurationSeconds,
        bool enabled = true)
    {
        if (!enabled ||
            !double.IsFinite(textWidth) || textWidth <= 0 ||
            !double.IsFinite(viewportWidth) || viewportWidth <= 0 ||
            textWidth <= viewportWidth)
        {
            return None;
        }

        var scrollDistance = textWidth - viewportWidth;

        // 留 10 DIP 余量，让最后一个字不会紧贴视口边缘。
        scrollDistance += 10;

        if (!double.IsFinite(lineProgress))
        {
            lineProgress = 0;
        }

        lineProgress = Math.Clamp(lineProgress, 0, 1);

        var scrollDuration = scrollDistance / ScrollSpeedDipPerSecond;

        double offset;
        if (!double.IsFinite(lineDurationSeconds) || lineDurationSeconds <= 0)
        {
            // 时长未知：按进度线性滚。不是最理想，但至少不会不动。
            offset = -scrollDistance * lineProgress;
        }
        else
        {
            var elapsed = lineProgress * lineDurationSeconds;

            if (elapsed <= LeadInSeconds)
            {
                // 开头静止：让用户看清第一个字。
                offset = 0;
            }
            else if (elapsed >= LeadInSeconds + scrollDuration)
            {
                // 滚到底了：停在末尾。
                offset = -scrollDistance;
            }
            else
            {
                var scrolled = (elapsed - LeadInSeconds) * ScrollSpeedDipPerSecond;
                offset = -scrolled;
            }
        }

        return new ScrollGeometry
        {
            OffsetX = offset,
            NeedsScroll = true,
            ScrollDistance = scrollDistance,
            ScrollDurationSeconds = scrollDuration
        };
    }

    /// <summary>
    /// 这句的时长够不够完整滚完（含首尾静止）。
    /// 不够的话应该缩短静止时间或直接截断，而不是滚到一半跳回开头。
    /// </summary>
    public bool FitsWithin(double lineDurationSeconds)
    {
        if (!NeedsScroll)
        {
            return true;
        }

        if (!double.IsFinite(lineDurationSeconds) || lineDurationSeconds <= 0)
        {
            return false;
        }

        return lineDurationSeconds >= LeadInSeconds + ScrollDurationSeconds + TrailOutSeconds;
    }
}
