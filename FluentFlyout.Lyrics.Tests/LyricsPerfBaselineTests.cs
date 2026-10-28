// Copyright (c) 2026 anync
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using System.Text;
using FluentFlyout.Lyrics.Abstractions;
using FluentFlyout.Lyrics.Frames;
using FluentFlyout.Lyrics.Layout;
using FluentFlyout.Lyrics.Models;
using FluentFlyout.Lyrics.Services;
using Xunit;
using Xunit.Abstractions;

namespace FluentFlyout.Lyrics.Tests;

/// <summary>
/// 歌词链路的性能基线。**这个用例默认不测**，只在设置了环境变量
/// <c>FF_PERF=1</c> 时才真正采样。
///
/// <para>
/// 为什么不写成带阈值的断言：耗时会随机器、负载、JIT 状态浮动 2–3 倍，
/// 任何写死的上限都会变成「在别人电脑上挂」的假失败。所以这里只测量、只打印，
/// 数字抄进 <c>docs/lyrics-integration/perf-baseline.md</c> 与 PR 描述。
/// </para>
/// <para>
/// 测量口径（写清楚，避免数字被误读）：
/// <list type="number">
/// <item>全部在同一线程同步执行，**不含** SMTC 跨进程采样（那需要真的在放歌），
/// 也**不含** WPF 的布局与渲染（那需要 UI 线程）。量的是「我们自己的代码」。</item>
/// <item>文本测量用 <c>chars × 7.0</c> 的确定性近似替代 <c>StringWidth.GetStringWidth</c>，
/// 宽度算法的复杂度不受影响，绝对值不用于对比像素。</item>
/// <item>分配量用 <c>GC.GetAllocatedBytesForCurrentThread</c> 前后差，单线程有效。</item>
/// </list>
/// </para>
/// <para>
/// 复现（只跑这一个用例，避免并行用例污染计时）：
/// <code>
/// FF_PERF=1 dotnet test FluentFlyout.Lyrics.Tests --filter LyricsPerfBaseline
/// </code>
/// </para>
/// </summary>
public class LyricsPerfBaselineTests
{
    /// <summary>合成歌词的行数。真实曲目常见 60–120 行，取 200 覆盖长歌词。</summary>
    private const int LineCount = 200;

    /// <summary>帧间隔，与 <see cref="LyricFramePumpCore.DefaultInterval"/> 一致。</summary>
    private const double FrameBudgetMicroseconds = 33_000;

    private static readonly bool Enabled =
        Environment.GetEnvironmentVariable("FF_PERF") == "1";

    private readonly ITestOutputHelper _output;

    public LyricsPerfBaselineTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task ReportsPerFrameCostAndAllocation()
    {
        var track = new TrackInfo(
            "perf-fixture",
            "性能基线",
            "Fixture",
            "Fixture",
            "Spotify.exe",
            TimeSpan.FromSeconds(LineCount * 4));

        // 走真实解析路径而不是手搓 ParsedLyrics：解析成本本身也是要报的数。
        var decoded = new DecodedLyricPayload(
            KnownLyricProviders.QQMusic,
            "perf-candidate",
            LyricPayloadFormat.Lrc,
            BuildLrc(LineCount),
            null,
            false,
            new Dictionary<string, string>());

        var parser = new LyricifyPayloadParser();
        var parsed = await parser.ParseAsync(decoded);

        var resolved = new ResolvedLyrics(
            parsed,
            KnownLyricProviders.QQMusic,
            "perf-candidate",
            LyricAcquisitionKind.Remote,
            new Dictionary<string, string>(),
            track.Duration);

        var document = ResolvedLyricsCompatibilityProjector
            .ToLyricDocument(resolved, includeInformationLines: false);

        // 夹具本身先断言：测量结果只有在夹具成立时才有意义，这一步在 CI 里也跑。
        Assert.Equal(LineCount, parsed.Lines.Count);
        Assert.Equal(LineCount, document.Lines.Count);

        if (!Enabled)
        {
            _output.WriteLine("FF_PERF 未设置，跳过采样（夹具校验通过，含义：解析与投影可用）");
            return;
        }

        var rows = new List<(string Name, string Frequency, double Microseconds, double Bytes)>
        {
            Measure("解析 LRC（整首）", "每首歌 1 次", () => parser.ParseAsync(decoded), 50),
            Measure("建立文档（投影 + 去信息行）", "每首歌 1 次",
                () => Wrap(() => ResolvedLyricsCompatibilityProjector.ToLyricDocument(resolved, false)), 300),
            Measure("整首定宽（取最宽一句）", "每首歌 1 次",
                () => Wrap(() => LyricsDocumentWidth.MeasureMaxLineWidth(document.Lines, MeasureText)), 500),
        };

        // 热路径：把 sync service 与帧泵接成生产形态。时钟必须手动推进，
        // 否则 33ms 的节流判定会认为「还太早」而跳过绝大多数 tick。
        var clock = new SteppedTimeProvider();
        using var coordinator = new StubCoordinator();
        using var sync = new LyricSyncService(
            coordinator,
            metadataStabilizationDelay: TimeSpan.Zero,
            timeProvider: clock);

        var position = TimeSpan.Zero;
        using var pump = new LyricFramePumpCore(
            () => Snapshot(track, position),
            sync,
            timeProvider: clock,
            interval: TimeSpan.FromMilliseconds(33));

        // 首次 tick 会走「换歌」分支并把 trackId 定下来，之后才能灌歌词。
        // 必须先 Start()：TickAsync 在未启动时直接返回 null，不会碰歌词引擎。
        pump.Start();
        _ = await pump.TickAsync();
        Assert.True(sync.TryApplyResolvedLyrics(track, resolved), "灌入歌词失败，热路径测量无意义");

        rows.Add(Measure("每帧取帧（GetDisplayFrameAsync）", "30 次/秒", () =>
        {
            position += TimeSpan.FromMilliseconds(33);
            return sync.GetDisplayFrameAsync(Snapshot(track, position));
        }, 3000));

        rows.Add(Measure("每帧全链路（节流 + 取帧 + 去重）", "30 次/秒", () =>
        {
            position += TimeSpan.FromMilliseconds(33);
            clock.Advance(TimeSpan.FromMilliseconds(33));
            return pump.TickAsync();
        }, 3000));

        // 守一条底线：跑完之后确实落在某一行上。若 document 没灌进去、
        // 或位置根本没推进，上面的耗时测的是一条空路径，数字再漂亮也没意义。
        var last = await sync.GetDisplayFrameAsync(Snapshot(track, position));
        Assert.False(string.IsNullOrEmpty(last.CurrentLine), "取到的帧没有当前行，测的可能是空路径");
        Assert.True(last.CurrentLineIndex > 0, "行号没有推进，位置计算可能没生效");

        Report(rows, pump.SkippedByThrottle);

        // 不设上限，只守一条下限：全链路必须真的跑完了，否则上面的数全是 0 也没人发现。
        var perFrame = rows[^1];
        Assert.True(perFrame.Microseconds > 0, "每帧耗时采不到值，节流可能把所有 tick 都跳过了");
    }

    private void Report(
        IReadOnlyList<(string Name, string Frequency, double Microseconds, double Bytes)> rows,
        long skippedTicks)
    {
        const string rule = "────────────────────────────────────────────────────────────";
        _output.WriteLine(rule);
        _output.WriteLine($"{"场景",-34}{"频率",-12}{"平均耗时",-14}{"每次分配",-14}");
        _output.WriteLine(rule);

        foreach (var row in rows)
        {
            var cost = row.Microseconds >= 100
                ? $"{row.Microseconds / 1000:F2} ms"
                : $"{row.Microseconds:F1} µs";
            var bytes = row.Bytes >= 1024
                ? $"{row.Bytes / 1024:F1} KB"
                : $"{row.Bytes:F0} B";
            _output.WriteLine($"{row.Name,-34}{row.Frequency,-12}{cost,-14}{bytes,-14}");
        }

        _output.WriteLine(rule);
        _output.WriteLine("");

        var perFrame = rows[^1];
        var share = perFrame.Microseconds / FrameBudgetMicroseconds * 100;
        var perThousandFramesMs = perFrame.Microseconds * 1000 / 1000;
        _output.WriteLine(
            $"每帧全链路 {perFrame.Microseconds:F1} µs → 占 33 ms 帧预算的 {share:F4}%；"
            + $"1000 帧（约 33 秒）累计 {perThousandFramesMs:F2} ms");
        _output.WriteLine(
            $"每帧分配 {perFrame.Bytes:F0} B → 30fps 约 {perFrame.Bytes * 30 / 1024:F1} KB/s（GC 压力的实际上限）");
        _output.WriteLine($"节流跳过 {skippedTicks} 次（手动推进时钟时不应发生，非 0 说明节流判定与设想不一致）");
        _output.WriteLine("");
        _output.WriteLine("口径：不含 SMTC 跨进程采样（需真在放歌）、不含 WPF 布局与渲染（需 UI 线程）；");
        _output.WriteLine("      文本宽度用 chars×7 近似，绝对值不用于对比像素。");
    }

    private static PlaybackSnapshot Snapshot(TrackInfo track, TimeSpan position) =>
        new(true, position, track, RawPosition: position, SourceAppUserModelId: "Spotify.exe");

    /// <summary>
    /// 确定性的文本宽度近似。真实实现是 <c>StringWidth.GetStringWidth(text, 400)</c>，
    /// 需要 WPF 的 <c>FormattedText</c>；这里只要复杂度一致，绝对值不参与任何断言。
    /// </summary>
    private static double MeasureText(string text) => text.Length * 7.0;

    private static Task Wrap(Action action)
    {
        action();
        return Task.CompletedTask;
    }

    private static (string Name, string Frequency, double Microseconds, double Bytes) Measure(
        string name,
        string frequency,
        Func<Task> body,
        int iterations)
    {
        for (var i = 0; i < Math.Min(32, iterations); i++)
        {
            body().GetAwaiter().GetResult();
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var before = GC.GetAllocatedBytesForCurrentThread();
        var start = Stopwatch.GetTimestamp();
        for (var i = 0; i < iterations; i++)
        {
            body().GetAwaiter().GetResult();
        }

        var elapsed = Stopwatch.GetTimestamp() - start;
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        var microseconds = elapsed * 1_000_000.0 / Stopwatch.Frequency / iterations;
        return (name, frequency, microseconds, allocated / (double)iterations);
    }

    private static string BuildLrc(int lineCount)
    {
        // 中文 12–16 字与英文 30–40 字符交替，接近真实曲目里最宽/最窄句的比例。
        string[] samples =
        [
            "如果时间可以停在那一秒钟的雨里",
            "and the lights keep turning over the bay tonight",
            "我们把所有的话都留在了那年夏天",
            "I never learned to say the things I meant",
        ];

        var builder = new StringBuilder();
        for (var i = 0; i < lineCount; i++)
        {
            builder.Append('[')
                .Append(TimeSpan.FromSeconds(i * 4).ToString(@"mm\:ss\.ff"))
                .Append(']')
                .Append(samples[i % samples.Length])
                .Append('\n');
        }

        return builder.ToString();
    }

    /// <summary>
    /// 只负责「不联网、不阻塞」的占位协调器：歌词由 <c>TryApplyResolvedLyrics</c> 灌入，
    /// 生产路径上的检索被有意绕过——性能基线不该依赖网络。
    /// </summary>
    private sealed class StubCoordinator : ILyricResolutionCoordinator
    {
        public Task<ResolvedLyrics?> ResolveAsync(
            TrackInfo track,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<ResolvedLyrics?>(null);

        public void Dispose()
        {
        }
    }

    /// <summary>
    /// 手动推进的时钟。<see cref="LyricFramePumpCore"/> 按 <c>GetTimestamp()</c> 做 33ms 节流，
    /// 用真实时钟连跑 3000 次会让绝大多数 tick 被判为「太早」而跳过。
    /// </summary>
    private sealed class SteppedTimeProvider : TimeProvider
    {
        private long _timestamp;
        private DateTimeOffset _utcNow = new(2026, 10, 28, 0, 0, 0, TimeSpan.Zero);

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => Interlocked.Read(ref _timestamp);

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan delta)
        {
            Interlocked.Add(ref _timestamp, delta.Ticks);
            _utcNow += delta;
        }
    }
}