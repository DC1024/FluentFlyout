// Copyright (c) 2026 anync
// SPDX-License-Identifier: MIT
//
// 本文件为本仓库新增，不源自 TaskbarLyrics。

using FluentFlyout.Lyrics.Layout;
using Xunit;

namespace FluentFlyout.Lyrics.Tests;

/// <summary>
/// 配色解析测试。
///
/// 为什么值得单独测：输入来自设置页的自由文本框，用户在输入过程中
/// 会经过 <c>#</c>、<c>#FF</c>、<c>#FF00</c> 这些**中间态**。
/// 解析器一旦在这些状态下抛异常，设置页就会在打字时崩。
/// 所以「非法输入返回 FollowSystem」不是宽容，是硬性要求。
/// </summary>
public class LyricsColorSchemeTests
{
    // ---------- 合法输入 ----------

    [Theory]
    [InlineData("#FF0000", 0xFFFF0000u)]      // 6 位，带 #
    [InlineData("FF0000", 0xFFFF0000u)]       // 6 位，不带 #
    [InlineData("#00ff00", 0xFF00FF00u)]      // 小写
    [InlineData("#00FF00", 0xFF00FF00u)]      // 大写
    [InlineData("#80FF0000", 0x80FF0000u)]    // 8 位，alpha 原样保留
    [InlineData("#00000000", 0x00000000u)]    // 全透明也是合法输入
    public void ValidHexParsesToExpectedArgb(string input, uint expected)
    {
        var scheme = LyricsColorScheme.Parse(input);

        Assert.True(scheme.IsExplicit);
        Assert.Equal(expected, scheme.Argb);
    }

    [Theory]
    [InlineData("#F0A", 0xFFFF00AAu)]   // 3 位简写：每位重复
    [InlineData("F0A", 0xFFFF00AAu)]
    [InlineData("#abc", 0xFFAABBCCu)]
    [InlineData("#FFF", 0xFFFFFFFFu)]
    [InlineData("#000", 0xFF000000u)]   // 3 位简写补不透明，不是全透明
    public void ThreeDigitShorthandExpandsByDuplication(string input, uint expected)
    {
        var scheme = LyricsColorScheme.Parse(input);

        Assert.True(scheme.IsExplicit);
        Assert.Equal(expected, scheme.Argb);
    }

    [Theory]
    [InlineData("  #FF0000  ")]
    [InlineData("\t#FF0000\n")]
    public void SurroundingWhitespaceIsIgnored(string input)
    {
        Assert.Equal(0xFFFF0000u, LyricsColorScheme.Parse(input).Argb);
    }

    // ---------- 非法输入 ----------

    [Theory]
    [InlineData(null)]          // 字段缺失
    [InlineData("")]            // 用户清空（语义 = 跟随主题）
    [InlineData("   ")]         // 只有空白
    [InlineData("#")]           // 刚敲了一个 #（打字中间态）
    [InlineData("#F")]          // 敲到一半
    [InlineData("#FF")]         // 敲到一半
    [InlineData("#FF00")]       // 4 位，不支持
    [InlineData("#FF000")]      // 5 位，不支持
    [InlineData("#FF00000")]    // 7 位，不支持
    [InlineData("#FF0000000")]  // 9 位，不支持
    [InlineData("#GGGGGG")]     // 非十六进制字符
    [InlineData("red")]         // 颜色名不支持
    [InlineData("#FF 0000")]    // 中间有空格
    [InlineData("##FF0000")]    // 双 #
    public void InvalidInputFallsBackToFollowSystemWithoutThrowing(string? input)
    {
        // 关键：不抛异常。抛了设置页在打字时就会崩。
        var scheme = LyricsColorScheme.Parse(input);

        Assert.False(scheme.IsExplicit);
        Assert.Equal(0u, scheme.Argb);
    }

    [Fact]
    public void ThreeDigitInputIsValidNotInvalid()
    {
        // 单独拎出来说明：#FF0 是**合法**的 3 位简写（展开成 #FFFF00 黄）。
        // 它不能出现在上面那个 Theory 里，否则会误以为 3 位不受支持。
        var scheme = LyricsColorScheme.Parse("#FF0");

        Assert.True(scheme.IsExplicit);
        Assert.Equal(0xFFFFFF00u, scheme.Argb);
    }

    // ---------- 往返 ----------

    [Theory]
    [InlineData(0xFFFF0000u)]
    [InlineData(0x80FF0000u)]
    [InlineData(0xFF123456u)]
    public void ToHexStringRoundTripsThroughParse(uint argb)
    {
        var original = LyricsColorScheme.FromArgb(argb);
        var text = original.ToHexString();

        Assert.Equal($"#{argb:X8}", text);
        Assert.Equal(argb, LyricsColorScheme.Parse(text).Argb);
    }

    [Fact]
    public void FollowSystemFormatsAsEmptyString()
    {
        // 空字符串是「跟随主题」在设置页里的表示——
        // 文本框清空就是这个状态，不能输出 "#00000000"
        // （那会被 Parse 读成全透明，与「跟随主题」混淆）。
        Assert.Equal(string.Empty, LyricsColorScheme.FollowSystem.ToHexString());
    }

    [Fact]
    public void DefaultValueIsFollowSystem()
    {
        // default(LyricsColorScheme) 必须是 FollowSystem。
        // 这条约束保证「UserSettings 里新加字段、未初始化」时
        // 得到的是跟随主题而不是某个黑色。
        Assert.False(default(LyricsColorScheme).IsExplicit);
    }
}
