// Copyright (c) 2026 anync
// SPDX-License-Identifier: MIT
//
// 本文件源自 TaskbarLyrics (https://github.com/ANYNC/TaskbarLyrics)
// 的 TaskbarLyrics.App/TaskbarFreeSpaceLayout.cs，原始许可为 MIT
// （见仓库根目录 LICENSE-TaskbarLyrics）。
// 本仓库整体按 GPL-3.0-or-later 分发。
//
// 相对上游的改动：
// 1. 命名空间归位到 FluentFlyout.Lyrics.Layout；
// 2. 抽出了 <see cref="CollectBlocked"/> 与 <see cref="ConsiderGap"/>，
//    便于对「区间合并」和「候选评分」分别单测；
// 3. 补了中文注释，说明为什么必须留 safetyGap。

using System.Diagnostics.CodeAnalysis;

namespace FluentFlyout.Lyrics.Layout;

/// <summary>
/// 在一维任务栏上为浮层找一块空位，避开已被系统占用的区间。
///
/// 这是把「歌词/小组件该放哪」从硬编码常量
/// （FluentFlyout 里的 <c>_nativeWidgetsPadding = 216</c>）改成
/// 实测避让的核心算法。纯函数，不碰 WPF，可直接单测。
///
/// 算法：把占用区间按 <c>safetyGap</c> 外扩并合并，得到若干「禁区」；
/// 禁区之间的空隙就是候选位。每个空隙里取一段长度 = min(请求长度, 空隙长度)
/// 的区间，起点尽量贴近 <c>preferredStart</c>；最后在所有候选里挑离
/// preferredStart 最近的那个，距离相同则取更长的。
/// </summary>
public static class TaskbarFreeSpaceLayout
{
    /// <summary>
    /// 低于这个长度的空隙不放——挤进去也显示不了几个字，
    /// 反而会跟相邻的托盘图标贴在一起，看起来像 bug。
    /// </summary>
    public const double MinimumDisplayLength = 100;

    /// <summary>
    /// 默认安全间距。托盘图标与任务栏按钮之间的实际间隙常常是 0，
    /// 不留间距会出现「文字紧贴图标边缘」的观感。
    /// </summary>
    public const double DefaultSafetyGap = 8;

    /// <summary>
    /// 尝试为一段内容找位置。
    /// </summary>
    /// <param name="taskbarLength">任务栏主轴总长度（DIP）。</param>
    /// <param name="requestedLength">希望占用的长度（DIP）。</param>
    /// <param name="preferredStart">
    /// 期望的起点。小于 0 表示「越靠前越好」，大于 taskbarLength 表示
    /// 「越靠后越好」——这两个哨兵值让「靠左对齐」和「靠右对齐」
    /// 两种设置都能复用同一个函数。
    /// </param>
    /// <param name="occupied">已被占用的区间，允许越界、乱序、重叠。</param>
    /// <param name="placement">输出：最终位置。</param>
    /// <param name="safetyGap">与占用区间保持的最小间距。</param>
    /// <param name="minimumLength">低于此长度的空隙不考虑。</param>
    /// <returns>
    /// true 表示找到位置。<b>false 表示放不下</b>，调用方应退回
    /// 原有行为（例如使用硬编码偏移），而不是把内容画到托盘图标上。
    /// </returns>
    public static bool TryPlace(
        double taskbarLength,
        double requestedLength,
        double preferredStart,
        IReadOnlyList<TaskbarInterval> occupied,
        out TaskbarInterval placement,
        double safetyGap = DefaultSafetyGap,
        double minimumLength = MinimumDisplayLength)
    {
        placement = default;

        // 参数校验一次性做完再进主循环：这些条件都是「调用方算错了」，
        // 不该静默返回一个看似合理的位置。
        if (!double.IsFinite(taskbarLength) || taskbarLength <= 0 ||
            !double.IsFinite(requestedLength) || requestedLength <= 0 ||
            !double.IsFinite(preferredStart) ||
            !double.IsFinite(safetyGap) || safetyGap < 0 ||
            !double.IsFinite(minimumLength) || minimumLength <= 0 ||
            requestedLength < minimumLength ||
            occupied is null)
        {
            return false;
        }

        var merged = CollectBlocked(occupied, taskbarLength, safetyGap);

        var found = false;
        var bestPlacement = default(TaskbarInterval);

        // 逐个空隙试探。空隙 = [上一个禁区终点, 这个禁区起点)，
        // 以及最后一个禁区终点到任务栏末尾。
        var gapStart = 0d;
        foreach (var interval in merged)
        {
            ConsiderGap(gapStart, interval.Start);
            gapStart = interval.End;
        }

        ConsiderGap(gapStart, taskbarLength);

        if (!found)
        {
            return false;
        }

        placement = bestPlacement;
        return true;

        void ConsiderGap(double start, double end)
        {
            var gapLength = end - start;
            if (gapLength < minimumLength)
            {
                return;
            }

            // 空隙够大就按请求长度放，不够就压缩到空隙大小。
            var length = Math.Min(requestedLength, gapLength);

            // 起点夹到空隙内，保证不越界。
            var candidateStart = Math.Clamp(preferredStart, start, end - length);

            var distanceComparison = found
                ? CompareDistance(candidateStart, bestPlacement.Start, preferredStart, taskbarLength)
                : -1;

            if (!found || distanceComparison < 0 ||
                (distanceComparison == 0 && length > bestPlacement.Length))
            {
                found = true;
                bestPlacement = new TaskbarInterval(candidateStart, candidateStart + length);
            }
        }
    }

    /// <summary>
    /// 把占用区间裁到任务栏范围内、按 <paramref name="safetyGap"/> 外扩，
    /// 再合并重叠部分。返回按起点升序、互不重叠的禁区列表。
    /// </summary>
    /// <remarks>
    /// 抽成独立方法是为了能单独验证「乱序输入」「完全在范围外」
    /// 「首尾相接」这些边界——它们在真实任务栏上都会出现
    /// （托盘图标数量变化时尤其明显）。
    /// </remarks>
    internal static List<TaskbarInterval> CollectBlocked(
        IReadOnlyList<TaskbarInterval> occupied,
        double taskbarLength,
        double safetyGap)
    {
        var blocked = new List<TaskbarInterval>(occupied.Count);
        foreach (var interval in occupied)
        {
            // 丢弃非法或完全在范围外的区间。真实数据里确实会出现：
            // UIA 偶尔返回 0 尺寸的矩形，或者窗口刚销毁时坐标变成极大值。
            if (!double.IsFinite(interval.Start) ||
                !double.IsFinite(interval.End) ||
                interval.End <= interval.Start ||
                interval.End <= 0 ||
                interval.Start >= taskbarLength)
            {
                continue;
            }

            var clippedStart = Math.Clamp(interval.Start, 0, taskbarLength);
            var clippedEnd = Math.Clamp(interval.End, 0, taskbarLength);
            var expandedStart = Math.Max(0, clippedStart - safetyGap);
            var expandedEnd = Math.Min(taskbarLength, clippedEnd + safetyGap);
            if (expandedEnd > expandedStart)
            {
                blocked.Add(new TaskbarInterval(expandedStart, expandedEnd));
            }
        }

        blocked.Sort(static (left, right) => left.Start.CompareTo(right.Start));
        return Merge(blocked);
    }

    /// <summary>合并重叠或首尾相接的区间。输入必须已按起点升序。</summary>
    private static List<TaskbarInterval> Merge(List<TaskbarInterval> sortedIntervals)
    {
        if (sortedIntervals.Count < 2)
        {
            return sortedIntervals;
        }

        var merged = new List<TaskbarInterval>(sortedIntervals.Count);
        var current = sortedIntervals[0];
        for (var index = 1; index < sortedIntervals.Count; index++)
        {
            var next = sortedIntervals[index];
            if (next.Start <= current.End)
            {
                current = current with { End = Math.Max(current.End, next.End) };
                continue;
            }

            merged.Add(current);
            current = next;
        }

        merged.Add(current);
        return merged;
    }

    /// <summary>
    /// 比较两个候选起点谁更贴近期望位置。
    /// 返回负数表示 <paramref name="candidateStart"/> 更优。
    /// </summary>
    /// <remarks>
    /// <paramref name="preferredStart"/> 的两个哨兵值必须特殊处理：
    /// 用它直接算绝对距离的话，「越靠前越好」（-1）会因为
    /// <c>|x - (-1)|</c> 恰好等于 <c>x + 1</c> 而**碰巧**正确，
    /// 但「越靠后越好」（taskbarLength + 1）就会算错方向。
    /// 显式分支比依赖巧合可靠。
    /// </remarks>
    private static int CompareDistance(
        double candidateStart,
        double otherStart,
        double preferredStart,
        double taskbarLength)
    {
        if (preferredStart < 0)
        {
            // 越靠前越好：起点小的赢。
            return candidateStart.CompareTo(otherStart);
        }

        if (preferredStart > taskbarLength)
        {
            // 越靠后越好：起点大的赢。
            return otherStart.CompareTo(candidateStart);
        }

        return Math.Abs(candidateStart - preferredStart)
            .CompareTo(Math.Abs(otherStart - preferredStart));
    }
}
