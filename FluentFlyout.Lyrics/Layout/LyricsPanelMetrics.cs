// Copyright (c) 2026 anync
// SPDX-License-Identifier: MIT
//
// 本文件为本仓库新增，不源自 TaskbarLyrics。
// 度量思路参照 TaskbarLyrics.App/LyricsLayoutMetrics.cs，
// 但砍掉了浮动窗口/频谱/封面等 FluentFlyout 用不到的字段。

namespace FluentFlyout.Lyrics.Layout;

/// <summary>
/// 歌词面板的排版度量。
///
/// 为什么放在 Core 而不是 WPF 层：这些都是纯数值推导（字号、行高、
/// 可见行数、内边距），跟 WPF 一点关系都没有。放在这里可以用假 DPI
/// 精确断言边界，而不用起一个真实的 <c>Visual</c> 去量。
///
/// 单位统一是 DIP（设备无关像素），与 WPF 的 <c>FontSize</c> 一致。
/// 需要物理像素时由调用方乘 DPI 缩放。
/// </summary>
public readonly record struct LyricsPanelMetrics
{
    /// <summary>
    /// 双行模式下每行的高度系数。
    ///
    /// 取 1.15 而不是 1.0：中文字形（尤其是思源黑体这类）的 ascent+descent
    /// 会超出 em 框，行距按 1.0 算会看到上下两行粘在一起。
    /// 参照 TL 的 <c>--line-pitch: 15px / --current-size: 13px ≈ 1.15</c>。
    /// </summary>
    public const double LineHeightFactor = 1.15;

    /// <summary>双行之间的额外间距（DIP）。TL 用 1px，这里留 2 便于中文。</summary>
    public const double LineGap = 2;

    /// <summary>单行模式下字号相对双行的放大倍数（空间够时把字放大更好读）。</summary>
    public const double SingleLineFontScale = 1.15;

    /// <summary>翻译行相对当前行的字号比例。</summary>
    public const double TranslationFontScale = 0.86;

    /// <summary>主行文字颜色不透明度。与 TL 的 <c>--primary: 0.90</c> 一致。</summary>
    public const double PrimaryOpacity = 0.90;

    /// <summary>翻译行不透明度。TL 用 <c>--translation: 0.70</c>。</summary>
    public const double TranslationOpacity = 0.70;

    /// <summary>下一行（非翻译模式）的次要色不透明度。TL 用 <c>--secondary: 0.60</c>。</summary>
    public const double SecondaryOpacity = 0.60;

    /// <summary>字号（DIP）。</summary>
    public required double FontSize { get; init; }

    /// <summary>翻译行字号（DIP）。</summary>
    public required double TranslationFontSize { get; init; }

    /// <summary>单行行高（DIP）。</summary>
    public required double LineHeight { get; init; }

    /// <summary>两行之间的间距（DIP）。单行模式为 0。</summary>
    public required double Gap { get; init; }

    /// <summary>两行占用的总高度（DIP）。</summary>
    public double TotalHeight => IsSingleLine ? LineHeight : (LineHeight * 2) + Gap;

    /// <summary>是否单行模式（小任务栏或用户选择）。</summary>
    public required bool IsSingleLine { get; init; }

    /// <summary>是否显示翻译行。</summary>
    public required bool ShowTranslation { get; init; }

    /// <summary>
    /// 实际可见的行数：单行模式 1；双行 + 翻译 2（当前行 + 翻译）；
    /// 双行无翻译 2（当前行 + 下一行）。
    /// </summary>
    public int VisibleLineCount => IsSingleLine ? 1 : 2;

    /// <summary>
    /// 根据可用高度与宽度推导度量。
    /// </summary>
    /// <param name="availableHeight">
    /// 面板可用的高度（DIP）。横向任务栏上通常是 40 或 28；
    /// 竖向任务栏上是旋转后的视觉高度。
    /// </param>
    /// <param name="preferredFontSize">
    /// 用户设置的字号。会被可用高度上限压缩，但不会被放大超过它。
    /// </param>
    /// <param name="forceSingleLine">
    /// 强制单行（小任务栏）。为 true 时忽略可用高度，直接走单行。
    /// </param>
    /// <param name="showTranslation">
    /// 用户是否开启翻译行。即使开启，单行模式下也会被忽略——
    /// 28px 高装不下两行，硬塞会把正文挤没。
    /// </param>
    public static LyricsPanelMetrics Create(
        double availableHeight,
        double preferredFontSize,
        bool forceSingleLine = false,
        bool showTranslation = true)
    {
        // 兜底：调用方传了非法值时不抛异常，退到一个能看的默认值。
        // 这类问题应该在上层被拦住，但歌词面板不该因为一个 NaN 就整块消失。
        if (!double.IsFinite(availableHeight) || availableHeight <= 0)
        {
            availableHeight = 40;
        }

        if (!double.IsFinite(preferredFontSize) || preferredFontSize <= 0)
        {
            preferredFontSize = 13;
        }

        // 两行需要的总高度（含间距）。
        var twoLineHeight = (preferredFontSize * LineHeightFactor * 2) + LineGap;

        // 装不下两行就降级单行。这不只是「小任务栏」的专利：
        // 用户把字号调到 20 时，40px 任务栏同样装不下两行。
        var isSingleLine = forceSingleLine || twoLineHeight > availableHeight;

        if (isSingleLine)
        {
            // 单行：可以放大一点，但仍受可用高度约束。
            var fontSize = Math.Min(
                preferredFontSize * SingleLineFontScale,
                availableHeight / LineHeightFactor);

            // 下限 9：再小就看不清了，宁可让调用方知道装不下。
            fontSize = Math.Max(fontSize, 9);

            return new LyricsPanelMetrics
            {
                FontSize = fontSize,
                TranslationFontSize = fontSize * TranslationFontScale,
                LineHeight = fontSize * LineHeightFactor,
                Gap = 0,
                IsSingleLine = true,
                // 单行模式下丢弃翻译行：28px 里塞两行等于两行都看不清。
                ShowTranslation = false
            };
        }

        // 双行：字号受「两行总高」约束。
        var fittedFontSize = Math.Min(
            preferredFontSize,
            (availableHeight - LineGap) / (LineHeightFactor * 2));

        fittedFontSize = Math.Max(fittedFontSize, 9);

        return new LyricsPanelMetrics
        {
            FontSize = fittedFontSize,
            TranslationFontSize = fittedFontSize * TranslationFontScale,
            LineHeight = fittedFontSize * LineHeightFactor,
            Gap = LineGap,
            IsSingleLine = false,
            ShowTranslation = showTranslation
        };
    }
}
