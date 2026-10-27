// Copyright (c) 2026 anync
// SPDX-License-Identifier: MIT

using FluentFlyout.Lyrics.Layout;
using Xunit;

namespace FluentFlyout.Lyrics.Tests;

/// <summary>
/// <see cref="TaskbarWidthBudget"/> 的用例。
///
/// 覆盖重点是「降级路径」：UIA 失败、空隙过窄、输入非法这三条
/// 都必须退回常量，而不是把内容画到托盘图标上。
/// </summary>
public class TaskbarWidthBudgetTests
{
    private const double Fallback = 216;
    private const double TaskbarLength = 1920;

    // ── 正常路径 ────────────────────────────────────────────────

    [Fact]
    public void PlacesAtPreferredStartWhenGapIsWide()
    {
        var result = TaskbarWidthBudget.Resolve(
            desiredWidth: 300,
            fallbackWidth: Fallback,
            taskbarLength: TaskbarLength,
            preferredStart: 200,
            occupied: []);

        Assert.Equal(WidthBudgetOutcome.Placed, result.Outcome);
        Assert.Equal(300, result.Width);
        Assert.Equal(200, result.Placement.Start);
        Assert.Equal(500, result.Placement.End);
    }

    [Fact]
    public void ShrinksToGapLengthWhenGapIsNarrowerThanDesired()
    {
        // 空隙 [200, 350) 名义长 150，但 TryPlace 默认 safetyGap = 8
        // 会把两边各啃掉 8 → 实际可用 134。理想 300 → 压到 134。
        var result = TaskbarWidthBudget.Resolve(
            desiredWidth: 300,
            fallbackWidth: Fallback,
            taskbarLength: TaskbarLength,
            preferredStart: 200,
            occupied: [new TaskbarInterval(0, 200), new TaskbarInterval(350, TaskbarLength)]);

        Assert.Equal(WidthBudgetOutcome.Placed, result.Outcome);
        Assert.Equal(134, result.Width);
    }

    [Fact]
    public void NeverExpandsBeyondDesiredWidth()
    {
        // 整条任务栏都空着，理想只要 300——绝不能撑成 1920。
        var result = TaskbarWidthBudget.Resolve(
            desiredWidth: 300,
            fallbackWidth: Fallback,
            taskbarLength: TaskbarLength,
            preferredStart: -1,
            occupied: []);

        Assert.Equal(300, result.Width);
        Assert.True(result.Width < TaskbarLength);
    }

    [Theory]
    [InlineData(-1)]           // 越靠前越好
    [InlineData(0)]            // 左对齐
    [InlineData(500)]          // 指定中点
    [InlineData(1921)]         // 越靠后越好（哨兵值 > taskbarLength）
    public void HonoursAllPreferredStartForms(double preferredStart)
    {
        var result = TaskbarWidthBudget.Resolve(
            desiredWidth: 300,
            fallbackWidth: Fallback,
            taskbarLength: TaskbarLength,
            preferredStart: preferredStart,
            occupied: []);

        Assert.Equal(WidthBudgetOutcome.Placed, result.Outcome);
        Assert.Equal(300, result.Width);
        Assert.InRange(result.Placement.Start, 0, TaskbarLength - 300);
    }

    [Fact]
    public void PreferredStartBeyondGapIsClampedInsideGap()
    {
        // 唯一空隙名义上是 [500, 800)，safetyGap = 8 后是 [508, 792) 长 284。
        // 起点给 100（在空隙外）→ 必须夹到 508。
        var result = TaskbarWidthBudget.Resolve(
            desiredWidth: 200,
            fallbackWidth: Fallback,
            taskbarLength: TaskbarLength,
            preferredStart: 100,
            occupied: [new TaskbarInterval(0, 500), new TaskbarInterval(800, TaskbarLength)]);

        Assert.Equal(WidthBudgetOutcome.Placed, result.Outcome);
        Assert.Equal(508, result.Placement.Start);
        Assert.Equal(200, result.Width);
    }

    // ── 降级：占用区间未知 ──────────────────────────────────────

    [Fact]
    public void NullOccupancyFallsBackAndIsNotTreatedAsEmpty()
    {
        // 「不知道」与「知道且为空」必须区别对待。
        var result = TaskbarWidthBudget.Resolve(
            desiredWidth: 300,
            fallbackWidth: Fallback,
            taskbarLength: TaskbarLength,
            preferredStart: 200,
            occupied: null);

        Assert.Equal(WidthBudgetOutcome.OccupancyUnknown, result.Outcome);
        Assert.Equal(Fallback, result.Width);
    }

    [Fact]
    public void NullOccupancyClampsDesiredDownToFallback()
    {
        // 理想 100 < 兜底 216：此时应取较小的那个（100），
        // 因为「用户就要这么宽」比「给足兜底宽度」更符合意图。
        var result = TaskbarWidthBudget.Resolve(
            desiredWidth: 100,
            fallbackWidth: Fallback,
            taskbarLength: TaskbarLength,
            preferredStart: 200,
            occupied: null);

        Assert.Equal(WidthBudgetOutcome.OccupancyUnknown, result.Outcome);
        Assert.Equal(100, result.Width);
    }

    [Fact]
    public void EmptyOccupancyIsTreatedAsFullyFree()
    {
        // 与上一条对照：空集合是「知道，且全空」。
        var result = TaskbarWidthBudget.Resolve(
            desiredWidth: 300,
            fallbackWidth: Fallback,
            taskbarLength: TaskbarLength,
            preferredStart: 200,
            occupied: []);

        Assert.Equal(WidthBudgetOutcome.Placed, result.Outcome);
        Assert.NotEqual(WidthBudgetOutcome.OccupancyUnknown, result.Outcome);
    }

    // ── 降级：没有可用空隙 ──────────────────────────────────────

    [Fact]
    public void FallsBackWhenAllGapsAreBelowMinimumDisplayLength()
    {
        // 任务栏被切成一堆 50 宽的小块，全都 < MinimumDisplayLength(100)。
        var occupied = new List<TaskbarInterval>();
        for (var start = 0d; start < TaskbarLength; start += 100)
        {
            occupied.Add(new TaskbarInterval(start, start + 50));
        }

        var result = TaskbarWidthBudget.Resolve(
            desiredWidth: 300,
            fallbackWidth: Fallback,
            taskbarLength: TaskbarLength,
            preferredStart: 200,
            occupied: occupied);

        Assert.Equal(WidthBudgetOutcome.NoUsableGap, result.Outcome);
        Assert.Equal(Fallback, result.Width);
    }

    [Fact]
    public void FallsBackWhenDesiredExceedsMinimumButNoGapFitsIt()
    {
        // 所有空隙都 ≥ 100 但 < 300：TryPlace 会把内容压缩进空隙，
        // 所以这不是 NoUsableGap，而是 Placed + 变窄。
        var result = TaskbarWidthBudget.Resolve(
            desiredWidth: 300,
            fallbackWidth: Fallback,
            taskbarLength: 400,
            preferredStart: 0,
            occupied: [new TaskbarInterval(0, 250)]);

        Assert.Equal(WidthBudgetOutcome.Placed, result.Outcome);
        Assert.True(result.Width < 300);
        Assert.True(result.Width >= TaskbarFreeSpaceLayout.MinimumDisplayLength);
    }

    // ── 输入非法 ────────────────────────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidDesiredWidthFallsBack(double desired)
    {
        var result = TaskbarWidthBudget.Resolve(
            desiredWidth: desired,
            fallbackWidth: Fallback,
            taskbarLength: TaskbarLength,
            preferredStart: 0,
            occupied: []);

        Assert.Equal(WidthBudgetOutcome.InvalidInput, result.Outcome);
        Assert.Equal(Fallback, result.Width);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    [InlineData(double.NaN)]
    public void InvalidTaskbarLengthFallsBack(double length)
    {
        var result = TaskbarWidthBudget.Resolve(
            desiredWidth: 300,
            fallbackWidth: Fallback,
            taskbarLength: length,
            preferredStart: 0,
            occupied: []);

        Assert.Equal(WidthBudgetOutcome.InvalidInput, result.Outcome);
        Assert.Equal(Fallback, result.Width);
    }

    // ── 真实场景 ────────────────────────────────────────────────

    [Fact]
    public void DpiScalingKeepsWidthProportional()
    {
        // 同一份物理布局，150% DPI 下所有值都乘 1.5，结果比例必须不变。
        // 这条守着「单位换算只做一次」这个约定。
        var at100 = TaskbarWidthBudget.Resolve(
            desiredWidth: 300,
            fallbackWidth: Fallback,
            taskbarLength: 1920,
            preferredStart: 200,
            occupied: [new TaskbarInterval(0, 200), new TaskbarInterval(800, 1920)]);

        var at150 = TaskbarWidthBudget.Resolve(
            desiredWidth: 450,
            fallbackWidth: 324,
            taskbarLength: 2880,
            preferredStart: 300,
            occupied: [new TaskbarInterval(0, 300), new TaskbarInterval(1200, 2880)]);

        Assert.Equal(at100.Outcome, at150.Outcome);
        Assert.Equal(at100.Width * 1.5, at150.Width, 6);
    }

    [Fact]
    public void TrayWithAllIconsShowingStillFindsGapNearStart()
    {
        // 「显示所有托盘图标」的极端场景：托盘从 1100 一直铺到 1920。
        // 起点偏好靠前（-1），应落在 [200, 1100) 这个空隙里。
        var result = TaskbarWidthBudget.Resolve(
            desiredWidth: 400,
            fallbackWidth: Fallback,
            taskbarLength: TaskbarLength,
            preferredStart: -1,
            occupied: [new TaskbarInterval(0, 200), new TaskbarInterval(1100, TaskbarLength)]);

        Assert.Equal(WidthBudgetOutcome.Placed, result.Outcome);
        Assert.Equal(400, result.Width);
        Assert.InRange(result.Placement.Start, 200, 1100 - 400);
        Assert.InRange(result.Placement.End, 200 + 400, 1100);
    }

    [Fact]
    public void PlacementNeverOverlapsOccupiedIntervals()
    {
        var occupied = new List<TaskbarInterval>
        {
            new(0, 150),
            new(400, 700),
            new(1200, 1500),
            new(1800, 1920),
        };

        var result = TaskbarWidthBudget.Resolve(
            desiredWidth: 300,
            fallbackWidth: Fallback,
            taskbarLength: TaskbarLength,
            preferredStart: 500,
            occupied: occupied);

        Assert.Equal(WidthBudgetOutcome.Placed, result.Outcome);

        foreach (var interval in occupied)
        {
            Assert.False(
                result.Placement.Start < interval.End && result.Placement.End > interval.Start,
                $"placement [{result.Placement.Start},{result.Placement.End}) overlaps occupied [{interval.Start},{interval.End})");
        }
    }
}
