// Copyright (c) 2026 anync
// SPDX-License-Identifier: MIT
//
// 本文件为本仓库新增，不源自 TaskbarLyrics。

using FluentFlyout.Lyrics.Layout;
using Xunit;

namespace FluentFlyout.Lyrics.Tests;

/// <summary>
/// 任务栏区间避让的测试。
///
/// 这是 P3 里唯一能把「任务栏空间竞争」问题脱离 UI 验证的部分，
/// 所以边界覆盖得比一般模块密：真实任务栏上托盘图标数量随时变，
/// UIA 还会偶尔返回 0 尺寸或越界矩形。
/// </summary>
public sealed class TaskbarFreeSpaceLayoutTests
{
    // ---------- 参数校验 ----------

    [Fact]
    public void InvalidTaskbarLengthReturnsFalse()
    {
        Assert.False(TaskbarFreeSpaceLayout.TryPlace(0, 200, 20, [], out _));
        Assert.False(TaskbarFreeSpaceLayout.TryPlace(-100, 200, 20, [], out _));
        Assert.False(TaskbarFreeSpaceLayout.TryPlace(double.NaN, 200, 20, [], out _));
        Assert.False(TaskbarFreeSpaceLayout.TryPlace(double.PositiveInfinity, 200, 20, [], out _));
    }

    [Fact]
    public void InvalidRequestedLengthReturnsFalse()
    {
        Assert.False(TaskbarFreeSpaceLayout.TryPlace(1920, 0, 20, [], out _));
        Assert.False(TaskbarFreeSpaceLayout.TryPlace(1920, -5, 20, [], out _));
        Assert.False(TaskbarFreeSpaceLayout.TryPlace(1920, double.NaN, 20, [], out _));
    }

    [Fact]
    public void NonFinitePreferredStartReturnsFalse()
    {
        Assert.False(TaskbarFreeSpaceLayout.TryPlace(1920, 200, double.NaN, [], out _));
        Assert.False(TaskbarFreeSpaceLayout.TryPlace(1920, 200, double.NegativeInfinity, [], out _));
    }

    [Fact]
    public void NegativeSafetyGapReturnsFalse()
    {
        Assert.False(TaskbarFreeSpaceLayout.TryPlace(1920, 200, 20, [], out _, safetyGap: -1));
    }

    [Fact]
    public void NullOccupiedReturnsFalse()
    {
        Assert.False(TaskbarFreeSpaceLayout.TryPlace(1920, 200, 20, null!, out _));
    }

    [Fact]
    public void RequestedShorterThanMinimumReturnsFalse()
    {
        // 请求 50 < 默认最小 100：这种宽度放进去也没法显示，直接判失败。
        Assert.False(TaskbarFreeSpaceLayout.TryPlace(1920, 50, 20, [], out _));
    }

    // ---------- 空任务栏 ----------

    [Fact]
    public void EmptyTaskbarPlacesAtPreferredStart()
    {
        Assert.True(TaskbarFreeSpaceLayout.TryPlace(1920, 200, 300, [], out var placement));
        Assert.Equal(300, placement.Start);
        Assert.Equal(200, placement.Length);
    }

    [Fact]
    public void EmptyTaskbarClampsPreferredStartToFit()
    {
        // 期望起点 1800 + 长度 200 会超出 1920，应被夹回 1720。
        Assert.True(TaskbarFreeSpaceLayout.TryPlace(1920, 200, 1800, [], out var placement));
        Assert.Equal(1720, placement.Start);
        Assert.Equal(1920, placement.End);
    }

    [Fact]
    public void EmptyTaskbarShrinksToTaskbarWhenRequestedLonger()
    {
        Assert.True(TaskbarFreeSpaceLayout.TryPlace(500, 800, 0, [], out var placement));
        Assert.Equal(0, placement.Start);
        Assert.Equal(500, placement.Length);
    }

    // ---------- 单块占用 ----------

    [Fact]
    public void AvoidsSingleOccupiedIntervalOnTheRight()
    {
        // 任务栏 1920，右侧 [1700,1920) 被托盘占用。
        // 期望起点 20，安全间距 8 → 禁区 [1692,1920)。
        TaskbarInterval[] occupied = [new(1700, 1920)];

        Assert.True(TaskbarFreeSpaceLayout.TryPlace(1920, 200, 20, occupied, out var placement));
        Assert.Equal(20, placement.Start);
        Assert.True(placement.End <= 1692, $"应避开禁区，实际终点 {placement.End}");
    }

    [Fact]
    public void ShrinksIntoGapWhenRequestedLongerThanFreeSpace()
    {
        // 可用区间 [0,1692) 长 1692，请求 2000 → 压缩到 1692。
        TaskbarInterval[] occupied = [new(1700, 1920)];

        Assert.True(TaskbarFreeSpaceLayout.TryPlace(1920, 2000, 20, occupied, out var placement));
        Assert.Equal(0, placement.Start);
        Assert.Equal(1692, placement.Length);
    }

    [Fact]
    public void SafetyGapIsRespected()
    {
        // 禁区外扩后，终点必须严格小于 占用起点 - safetyGap。
        TaskbarInterval[] occupied = [new(1000, 1100)];

        Assert.True(TaskbarFreeSpaceLayout.TryPlace(1920, 300, 20, occupied, out var placement, safetyGap: 20));
        Assert.True(placement.End <= 980, $"安全间距 20 应让终点 ≤ 980，实际 {placement.End}");
    }

    [Fact]
    public void ZeroSafetyGapAllowsTouchingOccupiedInterval()
    {
        TaskbarInterval[] occupied = [new(500, 700)];

        Assert.True(TaskbarFreeSpaceLayout.TryPlace(1920, 500, 0, occupied, out var placement, safetyGap: 0));
        Assert.Equal(0, placement.Start);
        Assert.Equal(500, placement.Length);
    }

    // ---------- 多块占用 ----------

    [Fact]
    public void PicksGapClosestToPreferredStart()
    {
        // 禁区：[0,108) 与 [1000,1108)。空隙：[108,1000) 长 892，[1108,1920) 长 812。
        // 期望起点 50 → 夹到第一个空隙的 108；第二个空隙起点 1108 距 50 更远。
        TaskbarInterval[] occupied = [new(0, 100), new(1000, 1100)];

        Assert.True(TaskbarFreeSpaceLayout.TryPlace(1920, 300, 50, occupied, out var placement));
        Assert.Equal(108, placement.Start);
    }

    [Fact]
    public void PreferredStartBeyondTaskbarPrefersLastGap()
    {
        // 哨兵：preferredStart > taskbarLength 表示「越靠后越好」。
        TaskbarInterval[] occupied = [new(0, 100), new(1000, 1100)];

        Assert.True(TaskbarFreeSpaceLayout.TryPlace(1920, 300, 99999, occupied, out var placement));
        Assert.Equal(1620, placement.Start); // 1920 - 300
        Assert.Equal(1920, placement.End);
    }

    [Fact]
    public void NegativePreferredStartPrefersFirstGap()
    {
        // 哨兵：preferredStart < 0 表示「越靠前越好」。
        TaskbarInterval[] occupied = [new(0, 100), new(1000, 1100)];

        Assert.True(TaskbarFreeSpaceLayout.TryPlace(1920, 300, -1, occupied, out var placement));
        Assert.Equal(108, placement.Start);
    }

    [Fact]
    public void ReturnsFalseWhenAllGapsAreTooNarrow()
    {
        // 三段占用把任务栏切成两个 < 100 的空隙。
        TaskbarInterval[] occupied = [new(0, 900), new(950, 1850)];

        Assert.False(TaskbarFreeSpaceLayout.TryPlace(1920, 200, 20, occupied, out _));
    }

    [Fact]
    public void GapExactlyAtMinimumLengthIsAccepted()
    {
        // 空隙 [900,1000) 长 100，恰好等于默认 minimumLength，应被接受。
        // 必须显式 safetyGap: 0——默认的 8 会把空隙两边各啃掉 8，
        // 变成 84 而低于阈值（这是设计使然，不是 bug）。
        TaskbarInterval[] occupied = [new(0, 900), new(1000, 1920)];

        Assert.True(TaskbarFreeSpaceLayout.TryPlace(
            1920, 100, 900, occupied, out var placement, safetyGap: 0));
        Assert.Equal(900, placement.Start);
        Assert.Equal(100, placement.Length);
    }

    [Fact]
    public void GapJustBelowMinimumLengthIsRejected()
    {
        // 空隙 [900,999) 长 99 < 100，判失败。
        TaskbarInterval[] occupied = [new(0, 900), new(999, 1920)];

        Assert.False(TaskbarFreeSpaceLayout.TryPlace(
            1920, 100, 900, occupied, out _, safetyGap: 0));
    }

    // ---------- 脏数据 ----------

    [Fact]
    public void IgnoresZeroSizedIntervals()
    {
        // UIA 偶尔返回 0 尺寸矩形。
        TaskbarInterval[] occupied = [new(500, 500), new(1700, 1920)];

        Assert.True(TaskbarFreeSpaceLayout.TryPlace(1920, 200, 20, occupied, out var placement));
        Assert.Equal(20, placement.Start);
    }

    [Fact]
    public void IgnoresReversedIntervals()
    {
        TaskbarInterval[] occupied = [new(900, 500), new(1700, 1920)];

        Assert.True(TaskbarFreeSpaceLayout.TryPlace(1920, 200, 20, occupied, out var placement));
        Assert.Equal(20, placement.Start);
    }

    [Fact]
    public void IgnoresIntervalsFullyOutsideTaskbar()
    {
        TaskbarInterval[] occupied = [new(-500, -100), new(3000, 4000)];

        Assert.True(TaskbarFreeSpaceLayout.TryPlace(1920, 200, 20, occupied, out var placement));
        Assert.Equal(20, placement.Start);
        Assert.Equal(220, placement.End);
    }

    [Fact]
    public void ClipsPartiallyOverlappingIntervals()
    {
        // [1800,2100) 越过右边界，应裁成 [1800,1920) 再外扩。
        TaskbarInterval[] occupied = [new(1800, 2100)];

        Assert.True(TaskbarFreeSpaceLayout.TryPlace(1920, 200, 20, occupied, out var placement));
        Assert.True(placement.End <= 1792);
    }

    [Fact]
    public void IgnoresNonFiniteIntervals()
    {
        TaskbarInterval[] occupied =
        [
            new(double.NaN, 500),
            new(600, double.PositiveInfinity),
            new(1700, 1920)
        ];

        Assert.True(TaskbarFreeSpaceLayout.TryPlace(1920, 200, 20, occupied, out var placement));
        Assert.Equal(20, placement.Start);
    }

    [Fact]
    public void UnsortedInputIsHandled()
    {
        TaskbarInterval[] occupied = [new(1700, 1920), new(0, 100), new(1000, 1100)];

        Assert.True(TaskbarFreeSpaceLayout.TryPlace(1920, 300, 50, occupied, out var placement));
        Assert.Equal(108, placement.Start);
    }

    // ---------- 合并 ----------

    [Fact]
    public void OverlappingIntervalsAreMerged()
    {
        // 两块重叠占用经外扩后连成一片，中间不该有假空隙。
        TaskbarInterval[] occupied = [new(500, 800), new(700, 1000)];

        var blocked = TaskbarFreeSpaceLayout.CollectBlocked(occupied, 1920, safetyGap: 0);

        var single = Assert.Single(blocked);
        Assert.Equal(500, single.Start);
        Assert.Equal(1000, single.End);
    }

    [Fact]
    public void AdjacentIntervalsAreMerged()
    {
        // [100,500) 与 [500,900) 首尾相接，外扩后必然重叠。
        TaskbarInterval[] occupied = [new(100, 500), new(500, 900)];

        var blocked = TaskbarFreeSpaceLayout.CollectBlocked(occupied, 1920, safetyGap: 0);

        var single = Assert.Single(blocked);
        Assert.Equal(100, single.Start);
        Assert.Equal(900, single.End);
    }

    [Fact]
    public void CollectBlockedExpandsBySafetyGap()
    {
        TaskbarInterval[] occupied = [new(500, 700)];

        var blocked = TaskbarFreeSpaceLayout.CollectBlocked(occupied, 1920, safetyGap: 10);

        var single = Assert.Single(blocked);
        Assert.Equal(490, single.Start);
        Assert.Equal(710, single.End);
    }

    [Fact]
    public void CollectBlockedClampsExpansionAtTaskbarEdges()
    {
        TaskbarInterval[] occupied = [new(0, 100), new(1900, 1920)];

        var blocked = TaskbarFreeSpaceLayout.CollectBlocked(occupied, 1920, safetyGap: 20);

        Assert.Equal(2, blocked.Count);
        Assert.Equal(0, blocked[0].Start);      // 左边界不外扩到负数
        Assert.Equal(120, blocked[0].End);
        Assert.Equal(1880, blocked[1].Start);
        Assert.Equal(1920, blocked[1].End);     // 右边界不外扩超范围
    }

    [Fact]
    public void CollectBlockedReturnsSortedResult()
    {
        TaskbarInterval[] occupied = [new(1500, 1600), new(200, 300), new(800, 900)];

        var blocked = TaskbarFreeSpaceLayout.CollectBlocked(occupied, 1920, safetyGap: 0);

        Assert.Equal(3, blocked.Count);
        Assert.True(blocked[0].Start < blocked[1].Start);
        Assert.True(blocked[1].Start < blocked[2].Start);
    }

    // ---------- 真实场景 ----------

    [Fact]
    public void RealisticTaskbarWithWidgetsButtonAndTray()
    {
        // 模拟 1920 宽任务栏：开始按钮 + 搜索 + 任务视图占左侧 ~180，
        // 右侧托盘占 ~200。可用区间约 [188, 1712)。
        TaskbarInterval[] occupied = [new(0, 180), new(1720, 1920)];

        Assert.True(TaskbarFreeSpaceLayout.TryPlace(1920, 240, 200, occupied, out var placement));

        // preferredStart = 200 本身就在空隙内（> 188），被原样尊重；
        // 算法只在越界或离得太远时才夹取，不做「吸附到空隙起点」。
        Assert.Equal(200, placement.Start);
        Assert.Equal(440, placement.End);
        Assert.True(placement.End <= 1712, "不能压到托盘禁区");
    }

    [Fact]
    public void PreferredStartInsideOccupiedIntervalIsPushedOut()
    {
        // 期望起点 100 落在禁区 [0,188) 内，应被推到空隙起点 188。
        TaskbarInterval[] occupied = [new(0, 180), new(1720, 1920)];

        Assert.True(TaskbarFreeSpaceLayout.TryPlace(1920, 240, 100, occupied, out var placement));
        Assert.Equal(188, placement.Start);
    }

    [Fact]
    public void TrayWithAllIconsShowingLeavesNarrowSpace()
    {
        // 「显示所有托盘图标」极端场景：右侧被吃到只剩 ~300。
        TaskbarInterval[] occupied = [new(0, 180), new(1620, 1920)];

        Assert.True(TaskbarFreeSpaceLayout.TryPlace(1920, 240, 200, occupied, out var placement));
        Assert.Equal(200, placement.Start);
        Assert.True(placement.End <= 1612);
    }

    [Fact]
    public void NarrowSpaceForcesWidgetToShrink()
    {
        // 可用区间只有 [188,1612) 长 1424，请求 1500 → 压到 1424。
        TaskbarInterval[] occupied = [new(0, 180), new(1620, 1920)];

        Assert.True(TaskbarFreeSpaceLayout.TryPlace(1920, 1500, 200, occupied, out var placement));
        Assert.Equal(188, placement.Start);
        Assert.Equal(1612, placement.End);
        Assert.Equal(1424, placement.Length);
    }

    [Fact]
    public void ReturnsFalseWhenTaskbarIsCompletelyOccupied()
    {
        TaskbarInterval[] occupied = [new(0, 1920)];

        Assert.False(TaskbarFreeSpaceLayout.TryPlace(1920, 200, 20, occupied, out _));
    }

    [Fact]
    public void VerticalTaskbarUsesSameAlgorithm()
    {
        // 竖向任务栏：主轴长度 = 屏幕高度 1080，托盘在底部。
        TaskbarInterval[] occupied = [new(0, 100), new(880, 1080)];

        Assert.True(TaskbarFreeSpaceLayout.TryPlace(1080, 200, 120, occupied, out var placement));
        // preferredStart = 120 在空隙 [108,872) 内，原样尊重。
        Assert.Equal(120, placement.Start);
        Assert.True(placement.End <= 872);
    }
}
