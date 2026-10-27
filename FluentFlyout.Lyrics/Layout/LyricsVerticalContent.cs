// Copyright (c) 2026 anync
// SPDX-License-Identifier: MIT
//
// 本文件为本仓库新增，不源自 TaskbarLyrics。

namespace FluentFlyout.Lyrics.Layout;

/// <summary>
/// 竖向歌词面板里「两行内容各占多少」的实测结果，供
/// <see cref="LyricsVerticalLayout.EstimateMainAxisLength(LyricsVerticalContent, double, double)"/>
/// 估算主轴长度。
///
/// 为什么需要这个结构而不是只传一个 <c>contentWidth</c>：
/// 竖向的歌词面板有**两行独立折行**的文本（当前行 + 翻译/下一行），
/// 两行各自按同一个折行宽度排，行数互不相干。只按主行估长度会漏掉
/// 次要行整块——截图矩阵实测：双行状态估 29.9 DIP、实际要 63.4 DIP，
/// 少算了一半，次要行被 <c>ClipToBounds</c> 整行裁掉。
///
/// 字段全部是 <b>实测值</b>，不是按字号推算的：WPF 的
/// <c>FormattedText.Height</c> 在 13px 字号下是 16.5 DIP，
/// 而 <see cref="LyricsPanelMetrics.LineHeightFactor"/>=1.15 推出的
/// 名义行高只有 14.95 DIP，差 10%。估算必须用实测行高，
/// 否则每行都少算 1.5 DIP，五行就少 7.5 DIP。
/// </summary>
/// <param name="PrimaryWidth">
/// 主行**不折行**时的宽度（DIP）。为 0 或非法表示内容还不可知
/// （首帧之前），估算会退回兜底长度。
/// </param>
/// <param name="PrimaryLineHeight">主行单行实测高度（DIP）。</param>
/// <param name="SecondaryWidth">次要行不折行宽度（DIP）。0 表示没有次要行。</param>
/// <param name="SecondaryLineHeight">次要行单行实测高度（DIP）。</param>
/// <param name="Gap">两行之间的间距（DIP）。没有次要行时忽略。</param>
public readonly record struct LyricsVerticalContent(
    double PrimaryWidth,
    double PrimaryLineHeight,
    double SecondaryWidth = 0,
    double SecondaryLineHeight = 0,
    double Gap = 0)
{
    /// <summary>是否有一行真正要画的次要内容。</summary>
    public bool HasSecondary =>
        double.IsFinite(SecondaryWidth) && SecondaryWidth > 0 &&
        double.IsFinite(SecondaryLineHeight) && SecondaryLineHeight > 0;
}
