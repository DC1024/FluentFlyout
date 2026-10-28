// Copyright (c) 2026 anync
// SPDX-License-Identifier: MIT

using FluentFlyout.Lyrics.Diagnostics;
using Xunit;

namespace FluentFlyout.Lyrics.Tests;

/// <summary>
/// <see cref="RepositionBudget"/> 的用例。
///
/// 守的是 R1 那条风险：歌词帧把整窗重排打成每秒几十次，任务栏在装了
/// UI mod 的机器上直接不响应。这类回归在截图上完全看不出来，只能靠计数，
/// 所以计数本身必须是对的——窗口滚动、基线豁免、告警节流这三处最容易写错。
/// </summary>
public class RepositionBudgetTests
{
    private static TimeSpan At(double seconds) => TimeSpan.FromSeconds(seconds);

    [Fact]
    public void StaysWithinBudgetBelowLimit()
    {
        var budget = new RepositionBudget(budget: 3, window: At(60));

        for (var i = 0; i < 3; i++)
        {
            budget.Hit(At(i), "line");
        }

        Assert.False(budget.IsExceeded);
        Assert.Equal(3, budget.Counted);
    }

    /// <summary>
    /// 「刚好用满」不算超支：预算是上限，不是警戒线。
    ///
    /// 这条容易写成 <c>&gt;=</c>，结果预算 20 在第 20 次就报，
    /// 与文档里「&lt; 20 次」的口径差一次，长期看会让人怀疑计数漏了。
    /// </summary>
    [Fact]
    public void ExceedsOnlyWhenStrictlyAboveBudget()
    {
        var budget = new RepositionBudget(budget: 3, window: At(60));

        for (var i = 0; i < 4; i++)
        {
            budget.Hit(At(i), "line");
        }

        Assert.True(budget.IsExceeded);
        Assert.Equal(4, budget.Counted);
    }

    /// <summary>滑出窗口的记录必须被丢掉，否则一分钟之后永远超支。</summary>
    [Fact]
    public void DropsEntriesOlderThanWindow()
    {
        var budget = new RepositionBudget(budget: 2, window: At(10));

        budget.Hit(At(0), "line");
        budget.Hit(At(1), "line");
        budget.Hit(At(2), "line");
        Assert.True(budget.IsExceeded);

        // 12 秒时那三条都已滑出 10 秒窗口
        budget.Hit(At(12), "line");

        Assert.False(budget.IsExceeded);
        Assert.Equal(1, budget.Counted);
        Assert.Equal(1, budget.Total);
    }

    /// <summary>窗口边界是「等于也算过期」，不然窗口会悄悄变成 window + ε。</summary>
    [Fact]
    public void TreatsExactWindowBoundaryAsExpired()
    {
        var budget = new RepositionBudget(budget: 1, window: At(10));

        budget.Hit(At(0), "line");
        budget.Hit(At(10), "line");

        Assert.Equal(1, budget.Total);
    }

    /// <summary>基线心跳计入总数，但不计入预算——1.5s 定时器本身就是 40 次/分。</summary>
    [Fact]
    public void BaselineDoesNotCountAgainstBudget()
    {
        var budget = new RepositionBudget(budget: 2, window: At(60));

        for (var i = 0; i < 40; i++)
        {
            budget.Hit(At(i), "timer", baseline: true);
        }

        Assert.False(budget.IsExceeded);
        Assert.Equal(0, budget.Counted);
        Assert.Equal(40, budget.Total);
    }

    /// <summary>按来源统计要能区分「谁在重排」，否则日志里只知道超了、不知道找谁。</summary>
    /// <remarks>
    /// 同分按名称字典序：日志要稳定可 grep，不能因为 Dictionary 的枚举顺序
    /// 每次跑出不同结果。
    /// </remarks>
    [Fact]
    public void CountsByReasonInDescendingOrder()
    {
        var budget = new RepositionBudget(budget: 10, window: At(60));

        budget.Hit(At(0), "timer", baseline: true);
        budget.Hit(At(1), "line");
        budget.Hit(At(2), "line");
        budget.Hit(At(3), "line");
        budget.Hit(At(4), "dpi");

        var counts = budget.CountByReason();

        Assert.Equal("line", counts[0].Key);
        Assert.Equal(3, counts[0].Value);
        Assert.Equal("dpi", counts[1].Key);
        Assert.Equal("timer", counts[2].Key);
    }

    /// <summary>超支后一个窗口只报一次：不节流就是每秒几十条 Warn。</summary>
    [Fact]
    public void ReportsViolationOncePerWindow()
    {
        var budget = new RepositionBudget(budget: 1, window: At(10));

        budget.Hit(At(0), "line");
        budget.Hit(At(1), "line");

        Assert.True(budget.TryBeginViolationReport(At(2)));
        Assert.False(budget.TryBeginViolationReport(At(3)));
        Assert.False(budget.TryBeginViolationReport(At(11)));

        // 12 秒距上次上报已满一个窗口，可以再报
        Assert.True(budget.TryBeginViolationReport(At(12)));
    }

    /// <summary>没超支时永远不该报。</summary>
    [Fact]
    public void NeverReportsWhileWithinBudget()
    {
        var budget = new RepositionBudget(budget: 5, window: At(10));

        budget.Hit(At(0), "line");

        Assert.False(budget.TryBeginViolationReport(At(1)));
        Assert.False(budget.TryBeginViolationReport(At(100)));
    }

    /// <summary>
    /// 时间戳回退（时钟被拨回 / 调用方传了旧值）不能把窗口撑长。
    ///
    /// 实现上是把回退值夹到上一个时间戳。这条不做，回退一次就能让
    /// 「窗口内 60 秒」变成几分钟，超支永远消不掉。
    /// </summary>
    [Fact]
    public void ClampsBackwardTimestamps()
    {
        var budget = new RepositionBudget(budget: 2, window: At(10));

        budget.Hit(At(5), "line");
        budget.Hit(At(1), "line"); // 回退

        // 两次都被记在 t=5，窗口仍从 5 起算
        Assert.Equal(2, budget.Total);
        Assert.False(budget.IsExceeded);
    }

    /// <summary>摘要要同时给出总数、预算、超了多少和分来源次数。</summary>
    [Fact]
    public void SummarizeMentionsTotalBudgetAndReasons()
    {
        var budget = new RepositionBudget(budget: 2, window: At(60));

        budget.Hit(At(0), "timer", baseline: true);
        budget.Hit(At(1), "line");
        budget.Hit(At(2), "line");
        budget.Hit(At(3), "line");

        var text = budget.Summarize();

        Assert.Contains("4 次", text);
        Assert.Contains("预算 2", text);
        Assert.Contains("超 1", text);
        Assert.Contains("timer 1", text);
        Assert.Contains("line 3", text);
    }

    /// <summary>Reset 之后重新计数，且告警闸门也一起复位。</summary>
    [Fact]
    public void ResetClearsEntriesAndReportGate()
    {
        var budget = new RepositionBudget(budget: 1, window: At(10));

        budget.Hit(At(0), "line");
        budget.Hit(At(1), "line");
        Assert.True(budget.TryBeginViolationReport(At(2)));

        budget.Reset();

        Assert.Equal(0, budget.Total);
        Assert.False(budget.IsExceeded);

        budget.Hit(At(3), "line");
        budget.Hit(At(4), "line");

        // 复位后同一个窗口内还能再报一次
        Assert.True(budget.TryBeginViolationReport(At(5)));
    }

    [Fact]
    public void RejectsNonPositiveBudgetOrWindow()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RepositionBudget(budget: 0, window: At(60)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RepositionBudget(budget: 5, window: TimeSpan.Zero));
    }
}
