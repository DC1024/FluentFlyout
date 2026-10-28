// Copyright (c) 2026 anync
// SPDX-License-Identifier: MIT
//
// 本文件为本仓库新增，不源自 TaskbarLyrics。

using System.Globalization;
using System.Text;

namespace FluentFlyout.Lyrics.Layout;

/// <summary>
/// 竖排里的一个「排版格」。
/// </summary>
/// <param name="Text">
/// 这一格要画的文本。拉丁整词会包含多个字素（<c>screen</c> 是一格），
/// 汉字与标点恒为一个字素。
/// </param>
/// <param name="Upright">
/// <see langword="true"/> 表示字形要**正立**：竖排时每个汉字绕自身中心
/// 反向旋转 90°（抵消任务栏的旋转），屏幕上看起来是正的。
/// <see langword="false"/> 表示这一格**整体侧躺**、沿列向下读（歪头看）——
/// 拉丁词、数字走这条：把 <c>screen</c> 逐字母正立堆叠会变成六行，
/// 可读性归零，这也是 CSS <c>text-orientation: mixed</c> 的做法。
/// </param>
public readonly record struct VerticalRun(string Text, bool Upright);

/// <summary>
/// 把一行歌词切成竖排可用的「格」序列。
///
/// <para>
/// 两条规则：
/// <list type="number">
/// <item>**按字素簇切，不按 char 切**。按 <c>char</c> 切，<c>é</c>（e + U+0301）
/// 会变成两格、其中一格是没有基底的组合符；<c>😀</c> 是代理对，会变成两个孤立
/// 代理码位，屏幕上就是两个空心方块。<see cref="StringInfo.ParseCombiningCharacters"/>
/// 给的是用户感知的一个「字」的边界。</item>
/// <item>**拉丁整词不拆**。单词逐字母竖排等于把 <c>screen</c> 变成六行，
/// 所以整词合成一格并标记为非正立（侧躺沿列读）。汉字、假名、谚文、
/// 标点、emoji 都是方块或独立记号，逐字正立。</item>
/// </list>
/// </para>
///
/// <para>
/// 这个类刻意只做**切分**，不做测量：宽度要用 WPF 的字体度量，
/// 而 Core 层不引 WPF。切分规则能单测、值得单测，测量交给面板。
/// </para>
/// </summary>
public static class VerticalRunSplitter
{
    /// <summary>
    /// 切分一行文本。空串返回空列表（调用方据此走「无内容」分支）。
    /// </summary>
    public static IReadOnlyList<VerticalRun> Split(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return Array.Empty<VerticalRun>();
        }

        var runs = new List<VerticalRun>(text.Length);
        var starts = StringInfo.ParseCombiningCharacters(text);

        var i = 0;
        while (i < starts.Length)
        {
            var element = ElementAt(text, starts, i);

            if (IsSpace(element))
            {
                // 空格单独成格：它的宽度由调用方按字号量，
                // 合并进相邻词会破坏「词间只有一个空格」的假设。
                runs.Add(new VerticalRun(" ", Upright: false));
                i++;
                continue;
            }

            if (FormsWord(element))
            {
                var word = new StringBuilder(element);
                i++;
                while (i < starts.Length && FormsWord(element = ElementAt(text, starts, i)))
                {
                    word.Append(element);
                    i++;
                }

                runs.Add(new VerticalRun(word.ToString(), Upright: false));
                continue;
            }

            runs.Add(new VerticalRun(element, Upright: true));
            i++;
        }

        return runs;
    }

    private static string ElementAt(string text, int[] starts, int index)
    {
        var start = starts[index];
        var end = index + 1 < starts.Length ? starts[index + 1] : text.Length;
        return text[start..end];
    }

    private static bool IsSpace(string element) =>
        element.Length > 0 && char.IsWhiteSpace(element[0]);

    /// <summary>
    /// 这一格能否与相邻格拼成一个「词」（从而整体侧躺）。
    ///
    /// 判定顺序有讲究：先排除 CJK 方块文字与假名，再落到
    /// 「字母或数字」。反过来的话，用 <see cref="char.IsLetter(char)"/>
    /// 会把汉字也算成词的一部分，整句歌词变成一个巨大的侧躺块。
    /// </summary>
    private static bool FormsWord(string element)
    {
        if (element.Length == 0)
        {
            return false;
        }

        var c = element[0];

        // 撇号、连字符、& 跟着单词走：don't / well-known / R&B 不该被切开。
        if (c is '\'' or '’' or '-' or '&')
        {
            return true;
        }

        if (IsSquareScript(c))
        {
            return false;
        }

        return char.IsLetterOrDigit(c);
    }

    /// <summary>
    /// 方块类文字：汉字（含扩展区）、假名、谚文、注音。
    /// 这些逐字正立堆叠才是正确读法，不能拼成侧躺的「词」。
    /// </summary>
    private static bool IsSquareScript(char c) =>
        (c >= '\u3040' && c <= '\u30FF') ||   // 平假名 + 片假名（含长音符 U+30FC）
        (c >= '\u3400' && c <= '\u4DBF') ||   // 汉字扩展 A
        (c >= '\u4E00' && c <= '\u9FFF') ||   // 汉字基本区
        (c >= '\uF900' && c <= '\uFAFF') ||   // 兼容汉字
        (c >= '\uAC00' && c <= '\uD7AF') ||   // 谚文音节
        (c >= '\u3100' && c <= '\u312F') ||   // 注音
        (c >= '\u31A0' && c <= '\u31BF');     // 注音扩展
}
