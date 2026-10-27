// Copyright (c) 2026 anync
// SPDX-License-Identifier: MIT
//
// 本文件为本仓库新增，不源自 TaskbarLyrics。

namespace FluentFlyout.Lyrics.Layout;

/// <summary>
/// 歌词配色方案。
///
/// 为什么放在 Core 而不是 WPF 层：配色解析（十六进制字符串 → ARGB）
/// 是纯字符串处理，与 WPF 的 <c>Color</c> 无关。放在这里可以用普通
/// 单元测试覆盖「用户输了个乱七八糟的值」这类边界，不用起 WPF。
/// WPF 层只负责把 <see cref="Argb"/> 转成 <c>Color</c>。
/// </summary>
public readonly record struct LyricsColorScheme
{
    /// <summary>
    /// 「跟随系统主题」的哨兵值。
    ///
    /// 用「没有值」表示跟随主题，而<b>不是</b>用一个特殊颜色值：
    /// 后者必须占用一个合法颜色，而 <c>#00000000</c>（全透明）是
    /// 用户完全可能真的想要的输入，两者会撞车。
    ///
    /// 这一点踩过一次：最初用 <c>Argb != 0</c> 判断「是否显式指定」，
    /// 于是全透明的合法输入被当成「跟随主题」——用户写 <c>#00000000</c>
    /// 想彻底隐藏歌词，结果看到的是主题色。
    /// </summary>
    public static LyricsColorScheme FollowSystem { get; } = default;

    /// <summary>主行颜色（ARGB，0xAARRGGBB）。</summary>
    public uint Argb { get; init; }

    /// <summary>
    /// 是否真的携带颜色值。
    ///
    /// 与 <see cref="Argb"/> 分开是必须的：<c>Argb == 0</c> 同时对应
    /// 「没有值」与「全透明黑」两种语义，光看 <see cref="Argb"/>
    /// 无法区分。
    /// </summary>
    public bool HasValue { get; init; }

    /// <summary>是否显式指定了颜色（false = 跟随系统主题）。</summary>
    public bool IsExplicit => HasValue;

    /// <summary>
    /// 从用户输入的十六进制字符串解析。
    ///
    /// 接受的形式（大小写不敏感、允许前缀 <c>#</c>）：
    /// <list type="bullet">
    /// <item><c>RRGGBB</c> → 不透明，alpha 补 0xFF；</item>
    /// <item><c>AARRGGBB</c> → 原样；</item>
    /// <item><c>RGB</c> → 每位重复一次（<c>F0A</c> → <c>FF00AA</c>）。</item>
    /// </list>
    ///
    /// 解析失败返回 <see cref="FollowSystem"/> 而不是抛异常：这个值来自
    /// 用户手输的文本框，非法输入是常态而非异常。抛异常会让设置页
    /// 在输入到一半（比如刚敲了一个 <c>#</c>）时就崩。
    /// </summary>
    public static LyricsColorScheme Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return FollowSystem;
        }

        var span = text.AsSpan().Trim();
        if (span.Length > 0 && span[0] == '#')
        {
            span = span[1..];
        }

        // 3 位简写：每位重复一次（F0A → FF00AA），所以每位乘 17。
        //
        // 这里直接算出 ARGB 而不是把 span 换成展开后的 6 位：后者需要
        // stackalloc，而 stackalloc 变量不能赋给 span 变量（CS8352，
        // 编译器认为它会逃逸出声明作用域）。直接算还省一次遍历。
        if (span.Length == 3 && IsHex(span))
        {
            var r3 = HexValue(span[0]) * 17u;
            var g3 = HexValue(span[1]) * 17u;
            var b3 = HexValue(span[2]) * 17u;
            return new LyricsColorScheme
            {
                Argb = 0xFF000000u | (r3 << 16) | (g3 << 8) | b3,
                HasValue = true,
            };
        }

        if (span.Length == 6 && IsHex(span))
        {
            return new LyricsColorScheme
            {
                // 6 位默认不透明：用户写 #FF0000 想要的是「红」，
                // 不是「透明红」。要半透明必须显式写 8 位。
                Argb = 0xFF000000u | ParseHex(span),
                HasValue = true,
            };
        }

        if (span.Length == 8 && IsHex(span))
        {
            // 注意 #00000000（全透明）走这条分支，HasValue 仍为 true。
            // 它是合法的「我要完全隐藏」，与「跟随主题」是两回事。
            return new LyricsColorScheme { Argb = ParseHex(span), HasValue = true };
        }

        return FollowSystem;
    }

    /// <summary>
    /// 格式化成 <c>#AARRGGBB</c>（用于回填文本框）。
    /// </summary>
    public string ToHexString() => HasValue ? $"#{Argb:X8}" : string.Empty;

    /// <summary>
    /// 从 ARGB 值构造。给「跟随主题」之外的程序化调用用。
    /// </summary>
    public static LyricsColorScheme FromArgb(uint argb) => new() { Argb = argb, HasValue = true };

    private static bool IsHex(ReadOnlySpan<char> span)
    {
        foreach (var c in span)
        {
            if (!char.IsAsciiHexDigit(c))
            {
                return false;
            }
        }

        return true;
    }

    private static uint ParseHex(ReadOnlySpan<char> span)
    {
        var value = 0u;
        foreach (var c in span)
        {
            value = (value << 4) | HexValue(c);
        }

        return value;
    }

    /// <summary>
    /// 单个十六进制字符 → 0..15。调用方必须先用 <see cref="IsHex"/> 保证合法。
    /// </summary>
    private static uint HexValue(char c) => c switch
    {
        >= '0' and <= '9' => (uint)(c - '0'),
        >= 'a' and <= 'f' => (uint)(c - 'a' + 10),
        >= 'A' and <= 'F' => (uint)(c - 'A' + 10),
        // IsHex 已经挡掉了其它字符，这里只是让 switch 成为完备表达式。
        _ => 0u,
    };
}
