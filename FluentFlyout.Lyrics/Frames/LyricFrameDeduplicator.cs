// Copyright (c) 2026 anync
// SPDX-License-Identifier: MIT
//
// 本文件为本仓库新增，不源自 TaskbarLyrics。

using FluentFlyout.Lyrics.Models;

namespace FluentFlyout.Lyrics.Frames;

/// <summary>
/// 判定一帧歌词是否值得推给 UI。
///
/// 不能直接比较 <see cref="LyricDisplayFrame"/> 是否相等：
/// <c>LineProgress</c> 和 <c>WordScanProgress</c> 是连续量，每帧都在变，
/// 直接比较等于永远「有变化」，去重就失效了。
///
/// 所以分成两类信号分别处理：
/// - **离散信号**（换行、换歌、纯音乐、翻译行变化）：立即推送。
/// - **连续信号**（进度、逐字扫描位置）：量化到足够小的粒度后，
///   只有跨过粒度边界才推送。
///
/// 量化的粒度要小于人眼可辨的差异，又要大到能过滤掉无意义抖动。
/// 30fps 下一帧约 33ms，进度按 1% 粒度推送最多 100 次/句，
/// 长句（5 秒）下约 20 次/秒，仍在预算内。
/// </summary>
public sealed class LyricFrameDeduplicator
{
    /// <summary>进度量化粒度。1% 在视觉上不可分辨，但能过滤掉绝大部分重复帧。</summary>
    public const double ProgressQuantum = 0.01;

    /// <summary>逐字扫描进度的量化粒度，同样是 1%。</summary>
    public const double WordScanQuantum = 0.01;

    private LyricDisplayFrame? _last;
    private int _lastProgressBucket = -1;
    private int _lastWordScanBucket = -1;

    /// <summary>累计被判定为「有变化」的帧数，用于诊断。</summary>
    public long EmittedCount { get; private set; }

    /// <summary>累计被判定为「无变化」而跳过的帧数，用于诊断。</summary>
    public long SuppressedCount { get; private set; }

    /// <summary>
    /// 判定是否应推送。返回 true 时内部状态已更新，
    /// 返回 false 时调用方应丢弃该帧。
    /// </summary>
    public bool ShouldEmit(LyricDisplayFrame? frame)
    {
        if (frame is null)
        {
            return false;
        }

        var progressBucket = Bucket(frame.LineProgress, ProgressQuantum);
        var wordScanBucket = frame.WordScanProgress is { } wordScan
            ? Bucket(wordScan, WordScanQuantum)
            : -1;

        if (_last is null || HasDiscreteChange(_last, frame))
        {
            Accept(frame, progressBucket, wordScanBucket);
            return true;
        }

        // 离散信号没变，只剩连续量。跨桶才推送。
        if (progressBucket != _lastProgressBucket || wordScanBucket != _lastWordScanBucket)
        {
            Accept(frame, progressBucket, wordScanBucket);
            return true;
        }

        SuppressedCount++;
        return false;
    }

    /// <summary>清空状态。换歌或停止时调用，保证下一帧一定推送。</summary>
    public void Reset()
    {
        _last = null;
        _lastProgressBucket = -1;
        _lastWordScanBucket = -1;
    }

    /// <summary>
    /// 只比较离散字段。这些字段变了就是真的换了显示内容，
    /// 必须立刻推送，不能等量化边界。
    /// </summary>
    private static bool HasDiscreteChange(LyricDisplayFrame previous, LyricDisplayFrame current)
    {
        return !string.Equals(previous.CurrentLine, current.CurrentLine, StringComparison.Ordinal)
            || !string.Equals(previous.NextLine, current.NextLine, StringComparison.Ordinal)
            || !string.Equals(previous.Title, current.Title, StringComparison.Ordinal)
            || !string.Equals(previous.CurrentTranslation, current.CurrentTranslation, StringComparison.Ordinal)
            || !string.Equals(previous.NextTranslation, current.NextTranslation, StringComparison.Ordinal)
            || previous.CurrentLineIndex != current.CurrentLineIndex
            || previous.IsPureMusic != current.IsPureMusic
            || previous.HasTrackTranslation != current.HasTrackTranslation;
    }

    private void Accept(LyricDisplayFrame frame, int progressBucket, int wordScanBucket)
    {
        _last = frame;
        _lastProgressBucket = progressBucket;
        _lastWordScanBucket = wordScanBucket;
        EmittedCount++;
    }

    /// <summary>
    /// 把连续量映射到桶号。负数与 NaN 归到 -1，
    /// 避免异常输入被当成「变化」反复推送。
    /// </summary>
    private static int Bucket(double value, double quantum)
    {
        if (double.IsNaN(value) || value < 0)
        {
            return -1;
        }

        return (int)Math.Floor(value / quantum);
    }
}
