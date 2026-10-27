// Copyright (c) 2026 anync
// SPDX-License-Identifier: MIT
//
// 本文件为本仓库新增，不源自 TaskbarLyrics。

using Xunit;

namespace FluentFlyout.Lyrics.Tests;

/// <summary>
/// 可手动推进的时钟，供依赖 <see cref="TimeProvider.GetTimestamp"/> /
/// <see cref="TimeProvider.GetElapsedTime"/> 的组件做确定性测试。
///
/// 为什么不用 <c>LyricSyncServiceTests.RetryTimeProvider</c>：那个只覆写
/// <c>CreateTimer</c>，用来断言重试延迟，时间戳仍是真实系统时钟。
/// 外推策略与帧泵的节流完全建立在时间戳上，必须能精确控制它。
///
/// 时间戳以 100ns 为一个 tick（<see cref="TimeProvider.TimestampFrequency"/> 取 1e7），
/// 这样 <c>GetElapsedTime</c> 的换算结果与 <see cref="TimeSpan"/> 一一对应，
/// 断言里可以直接写毫秒，不必做浮点容差。
/// </summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    private long _timestamp;

    public ManualTimeProvider(TimeSpan? start = null)
    {
        _timestamp = (start ?? TimeSpan.Zero).Ticks;
    }

    /// <summary>频率取 1e7，让 1 tick == 100ns，时间戳与 TimeSpan.Ticks 同量纲。</summary>
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => _timestamp;

    /// <summary>把时钟向前推进。负值会抛异常，避免测试里出现「时间倒流」的假象。</summary>
    public void Advance(TimeSpan delta)
    {
        if (delta < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(delta), "时间只能前进。");
        }

        _timestamp += delta.Ticks;
    }

    public void AdvanceMilliseconds(double milliseconds) =>
        Advance(TimeSpan.FromMilliseconds(milliseconds));

    public void AdvanceSeconds(double seconds) => Advance(TimeSpan.FromSeconds(seconds));

    /// <summary>直接跳到某个时刻（用于构造跨度较大的场景）。</summary>
    public void SetTo(TimeSpan position)
    {
        if (position < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(position), "时刻不能为负。");
        }

        _timestamp = position.Ticks;
    }
}
