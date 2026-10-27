// Copyright (c) 2026 anync
// SPDX-License-Identifier: MIT
//
// 本文件为本仓库新增，不源自 TaskbarLyrics。

namespace FluentFlyout.Lyrics.Layout;

/// <summary>
/// 歌词文本的水平对齐方式。
///
/// 取值与 TaskbarLyrics 的 <c>lyricsTextAlignments</c> 一致（Left/Center/Right），
/// 也刻意让底层整数是 0/1/2，这样设置页可以直接绑 <c>ComboBox.SelectedIndex</c>，
/// 不需要值转换器。
/// </summary>
public enum LyricsTextAlignment
{
    /// <summary>左对齐（默认）。</summary>
    Left = 0,

    /// <summary>居中。</summary>
    Center = 1,

    /// <summary>右对齐。</summary>
    Right = 2,
}

/// <summary>
/// 按对齐方式算文本的横向偏移。
///
/// 为什么需要这个函数：横向排版下歌词的 <c>TextBlock</c> 宽度是 <c>Auto</c>
/// （必须如此，否则长句滚动就没法溢出视口），而 <c>TextBlock.TextAlignment</c>
/// 只在**有富余宽度**时才起作用——宽度正好等于文本宽度时，三种对齐的结果完全相同。
/// 所以横向不能靠 <c>TextAlignment</c>，只能自己算偏移量写进
/// <c>TranslateTransform.X</c>。
///
/// 竖向排版反过来：那里 <c>TextBlock.Width</c> 被钉成视口宽度（换行需要有限宽度约束），
/// 所以直接用 <c>TextAlignment</c> 就对了，不需要本函数。
/// </summary>
public static class LyricsAlignmentPolicy
{
    /// <summary>
    /// 算对齐偏移（DIP）。返回值直接写进 <c>TranslateTransform.X</c>。
    /// </summary>
    /// <param name="alignment">对齐方式。</param>
    /// <param name="viewportWidth">视口宽度（DIP）。</param>
    /// <param name="textWidth">文本实际宽度（DIP）。</param>
    /// <returns>
    /// 左对齐恒为 0。文本比视口宽（需要滚动）时也恒为 0——
    /// 那种情况下位置由 <see cref="ScrollGeometry"/> 决定，
    /// 对齐偏移会和滚动偏移打架（TaskbarLyrics 的做法也是滚动时强制左对齐）。
    /// </returns>
    public static double GetOffset(
        LyricsTextAlignment alignment,
        double viewportWidth,
        double textWidth)
    {
        if (alignment == LyricsTextAlignment.Left)
        {
            return 0;
        }

        // 视口或文本尺寸还没算出来（首帧之前、布局未完成）时不做偏移：
        // 拿 NaN / 0 参与计算会写出 NaN 到 TranslateTransform，
        // 而 WPF 对 NaN 位移的处理是「整块内容消失」。
        if (!double.IsFinite(viewportWidth) || viewportWidth <= 0 ||
            !double.IsFinite(textWidth) || textWidth <= 0)
        {
            return 0;
        }

        // 文本装不下 → 交给滚动，对齐不参与。
        if (textWidth >= viewportWidth)
        {
            return 0;
        }

        var slack = viewportWidth - textWidth;
        return alignment switch
        {
            LyricsTextAlignment.Center => slack / 2,
            LyricsTextAlignment.Right => slack,
            _ => 0,
        };
    }

    /// <summary>
    /// 把设置里的整数还原成枚举。越界值回落 <see cref="LyricsTextAlignment.Left"/>。
    ///
    /// 为什么要这个函数：设置值来自 <c>settings.xml</c> 或 <c>ComboBox.SelectedIndex</c>，
    /// 两者都可能是任何整数（用户手改过配置文件、将来增删选项导致索引错位）。
    /// 直接强转枚举不会报错，但会让 <c>switch</c> 落到 <c>default</c> 分支——
    /// 显式归一化能让「非法值等于左对齐」这件事有测试可依。
    /// </summary>
    public static LyricsTextAlignment FromSetting(int value) => value switch
    {
        1 => LyricsTextAlignment.Center,
        2 => LyricsTextAlignment.Right,
        _ => LyricsTextAlignment.Left,
    };
}
