// Copyright (c) 2026 anync
// SPDX-License-Identifier: MIT
//
// 本文件为本仓库新增，不源自 TaskbarLyrics。

using System.Diagnostics;

namespace FluentFlyout.Lyrics.Diagnostics;

/// <summary>
/// 整窗重排的预算与计数（R1 护栏）。
/// </summary>
/// <remarks>
/// <para>
/// 为什么需要它：一次 <c>UpdatePosition</c> 不是「重画一遍」那么便宜——里面有
/// 跨进程 UIA 查询（单个最坏 1500ms 超时）、<c>SetWindowPos</c> 与
/// <c>SetWindowRgn</c>。在装了 Nilesoft Shell / Windhawk 的机器上，每秒几十次
/// 会把整条任务栏拖住。这类回归**在截图上完全看不出来**，只能靠计数发现。
/// </para>
/// <para>
/// 预算按「滚动窗口」算而不是固定分桶：分桶会让边界两侧的两次抖动都算进各自
/// 的桶里，正好把最该抓的突发（一瞬间几十次）摊薄成两半。
/// </para>
/// <para>
/// 计时用调用方给的单调时间戳（生产传 <see cref="Stopwatch.GetElapsedTime(long)"/>
/// 的结果），本类不读时钟——这样才能用单测把「窗口滚动」「告警节流」
/// 这些只随时间发生的分支跑到，而不是靠等 60 秒。
/// </para>
/// </remarks>
public sealed class RepositionBudget
{
    /// <summary>默认预算：每分钟 20 次非基线重排（落点文档 R1 的验收口径）。</summary>
    public const int DefaultBudget = 20;

    /// <summary>默认窗口：60 秒。</summary>
    public static readonly TimeSpan DefaultWindow = TimeSpan.FromSeconds(60);

    private readonly int _budget;
    private readonly TimeSpan _window;
    private readonly List<Entry> _entries = new();

    private TimeSpan _lastTimestamp = TimeSpan.MinValue;
    private TimeSpan? _lastReportAt;

    /// <summary>建一个预算器。</summary>
    /// <param name="budget">
    /// 窗口内允许的<b>非基线</b>次数。超过（严格大于）才算超支——
    /// 「刚好用满」是合规的。
    /// </param>
    /// <param name="window">滚动窗口长度。</param>
    public RepositionBudget(int budget = DefaultBudget, TimeSpan? window = null)
    {
        if (budget <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(budget), budget, "Budget must be positive.");
        }

        _budget = budget;
        _window = window ?? DefaultWindow;

        if (_window <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(window), _window, "Window must be positive.");
        }
    }

    /// <summary>窗口内允许的非基线次数。</summary>
    public int Budget => _budget;

    /// <summary>滚动窗口长度。</summary>
    public TimeSpan Window => _window;

    /// <summary>当前窗口内的总次数（含基线）。</summary>
    public int Total => _entries.Count;

    /// <summary>当前窗口内计入预算的次数（不含基线）。</summary>
    public int Counted => Count(baseline: false);

    /// <summary>是否超支。严格大于预算才算超。</summary>
    public bool IsExceeded => Counted > _budget;

    /// <summary>
    /// 记一次重排。
    /// </summary>
    /// <param name="timestamp">
    /// 单调递增的时间戳。回退的时间戳会被夹到上一个值，避免时钟回拨把窗口撑长。
    /// </param>
    /// <param name="reason">来源标签，只用于统计与日志。</param>
    /// <param name="baseline">
    /// true = 基线心跳（定时器轮询），计总数但**不计入预算**。
    /// 1.5s 的定时器本身就是 40 次/分，把它算进 20 的预算里，告警会永远亮着，
    /// 也就等于没有告警。
    /// </param>
    /// <returns>记完这一次之后是否已超支。</returns>
    public bool Hit(TimeSpan timestamp, string reason, bool baseline = false)
    {
        if (timestamp < _lastTimestamp)
        {
            timestamp = _lastTimestamp;
        }

        _lastTimestamp = timestamp;
        Prune(timestamp);
        _entries.Add(new Entry(timestamp, reason ?? string.Empty, baseline));

        return IsExceeded;
    }

    /// <summary>
    /// 这一次要不要打告警。
    /// </summary>
    /// <remarks>
    /// 一个窗口内只放行一次：超支之后每一帧都可能继续「超支」，不节流就是
    /// 每秒几十条 Warn，把日志刷满之后真正的第一条反而找不到。
    /// </remarks>
    public bool TryBeginViolationReport(TimeSpan timestamp)
    {
        if (!IsExceeded)
        {
            return false;
        }

        if (_lastReportAt is { } last && timestamp - last < _window)
        {
            return false;
        }

        _lastReportAt = timestamp;
        return true;
    }

    /// <summary>按来源统计当前窗口内的次数（含基线），按次数降序。</summary>
    public IReadOnlyList<KeyValuePair<string, int>> CountByReason()
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var entry in _entries)
        {
            counts.TryGetValue(entry.Reason, out var current);
            counts[entry.Reason] = current + 1;
        }

        return counts
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>一行摘要，给日志用。形如 <c>23 次/60.0s（预算 20，超 3）：timer 18、line 5</c>。</summary>
    public string Summarize()
    {
        var parts = CountByReason()
            .Select(pair => $"{pair.Key} {pair.Value}");

        var over = Counted - _budget;

        return $"{Total} 次/{_window.TotalSeconds:F1}s（预算 {_budget}，" +
               $"{(over > 0 ? $"超 {over}" : "未超")}）：{string.Join("、", parts)}";
    }

    /// <summary>清空（换屏、重建窗口这类「重新开始」的时刻用）。</summary>
    public void Reset()
    {
        _entries.Clear();
        _lastReportAt = null;
    }

    private int Count(bool baseline)
    {
        var count = 0;

        foreach (var entry in _entries)
        {
            if (entry.Baseline == baseline)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>丢掉窗口外的记录。列表按时间递增，所以从头开始丢即可。</summary>
    private void Prune(TimeSpan timestamp)
    {
        var cutoff = timestamp - _window;
        var drop = 0;

        while (drop < _entries.Count && _entries[drop].Timestamp <= cutoff)
        {
            drop++;
        }

        if (drop > 0)
        {
            _entries.RemoveRange(0, drop);
        }
    }

    private readonly record struct Entry(TimeSpan Timestamp, string Reason, bool Baseline);
}
