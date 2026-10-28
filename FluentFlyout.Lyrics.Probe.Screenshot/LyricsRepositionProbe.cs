// Copyright (c) 2026 anync
// SPDX-License-Identifier: MIT
//
// 本文件为本仓库新增，不源自 TaskbarLyrics.

using System.Windows;
using System.Windows.Controls;
using FluentFlyout.Controls;
using FluentFlyout.Lyrics.Models;

namespace FluentFlyout.Lyrics.Probe.Screenshot;

/// <summary>
/// R1「歌词帧触发整窗重排」的回归护栏：模拟一段 60s 播放，数歌词路径一共
/// 请求了多少次重排。
/// </summary>
/// <remarks>
/// <para>
/// 为什么这件事值得一条探针：一次重排不是「重画一遍」那么便宜。宿主的
/// <c>UpdatePosition</c> 里包含 UIA 跨进程查询（单个最坏 1.5s 超时）、
/// <c>SetWindowPos</c> 与 <c>SetWindowRgn</c>，而这几样在装了 Nilesoft Shell /
/// Windhawk 的机器上会直接把任务栏拖住（<c>TaskbarWindow</c> 自己在
/// <c>WindowProc</c> 里屏蔽 <c>WM_GETOBJECT</c> 就是被这类 mod 坑过）。
/// 30fps 下每帧一次 = 每秒 30 次，这不是「略卡」，是整条任务栏不响应。
/// </para>
/// <para>
/// 断言的判据刻意选在「换行级别」而不是「帧级别」：<b>同一首歌里宽度应当恒定</b>
/// （见落点文档 3.1 的修订），所以有文档时整首歌的播放期间一次重排都不需要。
/// 阈值留 2 次余量给「换歌 / 首帧」这类离散事件，而不是留给每行一次——
/// 每行一次会让 20 句的歌刚好凑到 19 次，那样的阈值等于什么都没测。
/// </para>
/// <para>
/// 对照组（无文档）也量一遍：搜不到歌词时宽度只能跟着当前行走，换行重排是
/// <b>必要的</b>，那条路径不该被这条断言误伤——它的上限是「每行一次」，
/// 不是「每帧一次」。
/// </para>
/// </remarks>
internal static class LyricsRepositionProbe
{
    /// <summary>帧率。生产里 <c>LyricFramePump</c> 推帧的上限是 30fps。</summary>
    private const int FramesPerSecond = 30;

    /// <summary>模拟时长（秒）。验收口径就是「播放 60s」。</summary>
    private const int PlaybackSeconds = 60;

    /// <summary>每句停留时长（秒）。3s 是流行歌的常见换行密度。</summary>
    private const double SecondsPerLine = 3.0;

    /// <summary>有文档时，整首歌播放期间允许的重排请求数（余量 2）。</summary>
    private const int MaxRequestsWithDocument = 2;

    /// <summary>无文档时，允许的重排请求数——上限是「每行一次」再加余量。</summary>
    private const int MaxRequestsWithoutDocument = 25;

    /// <summary>主轴预算（DIP）。与装配链探针一致，取 400。</summary>
    private const double MaxMainAxisLength = 400;

    private static int _failures;

    /// <summary>跑一遍，返回失败数（0 = 全过）。</summary>
    public static int Run()
    {
        if (Application.Current is null)
        {
            var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            application.Resources["UiContextMenu"] = new Style(typeof(ContextMenu));
        }

        Console.WriteLine("=== R1 卡顿护栏：60s 播放的重排请求数 ===");
        Console.WriteLine();

        var lines = BuildLines();

        foreach (var vertical in new[] { false, true })
        {
            var label = vertical ? "竖向" : "横向";

            var withDocument = Measure(vertical, lines, useDocument: true);
            var withoutDocument = Measure(vertical, lines, useDocument: false);

            Console.WriteLine(
                $"  {label}：{withDocument.Frames} 帧 / {PlaybackSeconds}s，" +
                $"有文档时重排请求 {withDocument.Requests} 次" +
                $"（换歌那一次另计 {withDocument.DocumentRequests}），" +
                $"无文档对照 {withoutDocument.Requests} 次");

            Check(
                $"{label}：有文档时整首歌播放期间不重排（≤ {MaxRequestsWithDocument} 次）",
                withDocument.Requests <= MaxRequestsWithDocument,
                $"{withDocument.Requests} 次 / {withDocument.Frames} 帧");

            Check(
                $"{label}：无文档时重排按行发生，不是按帧（≤ {MaxRequestsWithoutDocument} 次）",
                withoutDocument.Requests <= MaxRequestsWithoutDocument,
                $"{withoutDocument.Requests} 次 / {withoutDocument.Frames} 帧");
        }

        Console.WriteLine();
        Console.WriteLine(_failures == 0 ? "R1 护栏全部通过" : $"{_failures} 项失败");
        return _failures;
    }

    /// <summary>一次测量的结果。请求数按 60s 窗口统计，不含换歌那一次。</summary>
    private readonly record struct Measurement(int Frames, int Requests, int DocumentRequests);

    /// <summary>
    /// 模拟一段播放，统计 <see cref="TaskbarWidgetControl.WidgetWidthStrategyChanged"/>
    /// 触发了多少次。
    /// </summary>
    /// <remarks>
    /// 只用事件计数、不做布局：生产里这个事件的处理者就是
    /// <c>Dispatcher.BeginInvoke(() =&gt; UpdatePosition())</c>，所以「事件次数」
    /// 与「整窗重排次数」是同一件事，而渲染出来的像素与这条判据无关。
    /// </remarks>
    private static Measurement Measure(bool vertical, IReadOnlyList<LyricLine> lines, bool useDocument)
    {
        var control = new TaskbarWidgetControl();
        var requests = 0;

        control.WidgetWidthStrategyChanged += (_, _) => requests++;

        control.SetLyricsMode(true);
        control.SetVerticalMode(vertical);

        var panel = (LyricsPanel)control.FindName("LyricsPanel")!;
        panel.ApplyWindowsTheme(isDark: false);
        panel.SetActive(true);

        // 尺寸必须按生产那样写进面板：竖向的排版要**先有短边**才建得起来
        // （列距 = 1.33 × 字号，短边决定放得下几列），不写尺寸的话
        // EnsureVerticalLayout 直接空转、VerticalContentLength 恒为 0，
        // 「无文档时换行要不要重排」这条分组就变成了一句空断言。
        //
        // 数值取生产的默认档：短边 40 DIP 减内缩 2.5 → 37.5，主轴预算 400。
        panel.Width = MaxMainAxisLength;
        panel.Height = 37.5;

        if (useDocument)
        {
            // 换歌时整份文档一次性送到渲染层。这一步会（也只该）触发一次请求。
            panel.ApplyDocument(lines);
        }

        var documentRequests = requests;

        var frames = FramesPerSecond * PlaybackSeconds;
        var framesPerLine = (int)(FramesPerSecond * SecondsPerLine);

        for (var frame = 0; frame < frames; frame++)
        {
            var index = Math.Min(frame / framesPerLine, lines.Count - 1);
            var progress = (frame % framesPerLine) / (double)framesPerLine;
            var next = lines.Count > index + 1 ? lines[index + 1].Text : string.Empty;

            control.UpdateLyricsFrame(new LyricDisplayFrame(
                CurrentLine: lines[index].Text,
                NextLine: next,
                Title: "测试曲目",
                LineProgress: progress,
                CurrentLineIndex: index,
                WordScanProgress: progress));
        }

        return new Measurement(frames, requests - documentRequests, documentRequests);
    }

    /// <summary>
    /// 20 句、长短交替的夹具。
    /// </summary>
    /// <remarks>
    /// 长短必须拉开：宽度上报带 4 DIP 阈值，若每句都差不多宽，
    /// 「换行重排」这个缺陷会被阈值吃掉，探针就测不出东西（假绿）。
    /// </remarks>
    private static LyricLine[] BuildLines()
    {
        var shortLine = "如果这场雨";
        var longLine = "如果这场雨一直下到明天清晨我们是不是就能一直站在这里不分开";
        var lines = new LyricLine[20];

        for (var i = 0; i < lines.Length; i++)
        {
            var text = i % 2 == 0 ? shortLine : longLine;
            lines[i] = new LyricLine(TimeSpan.FromSeconds(i * SecondsPerLine), $"{text}{i}");
        }

        return lines;
    }

    private static void Check(string name, bool passed, string detail)
    {
        if (!passed)
        {
            _failures++;
        }

        Console.WriteLine($"    [{(passed ? "PASS" : "FAIL")}] {name}（{detail}）");
    }
}
