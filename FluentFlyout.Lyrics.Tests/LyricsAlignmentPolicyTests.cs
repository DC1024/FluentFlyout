// Copyright (c) 2026 anync
// SPDX-License-Identifier: MIT
//
// 本文件为本仓库新增，不源自 TaskbarLyrics。

using FluentFlyout.Lyrics.Layout;
using Xunit;

namespace FluentFlyout.Lyrics.Tests;

/// <summary>
/// 对齐偏移计算测试。
///
/// 核心约束只有一条：**滚动与对齐不能同时生效**。
/// 两者都写同一个 <c>TranslateTransform.X</c>，叠加的后果是
/// 长句被推出视口（左半边空白、右半边截断），而且只在
/// 「长句 + 居中/右对齐」这个组合下出现——最容易漏测的组合。
/// </summary>
public class LyricsAlignmentPolicyTests
{
    private const double Viewport = 200;
    private const double ShortText = 80;   // 装得下，有富余
    private const double LongText = 300;   // 装不下，需要滚动

    // ---------- 左对齐 ----------

    [Theory]
    [InlineData(ShortText)]
    [InlineData(LongText)]
    [InlineData(0)]
    public void LeftAlignmentAlwaysHasZeroOffset(double textWidth)
    {
        Assert.Equal(
            0,
            LyricsAlignmentPolicy.GetOffset(LyricsTextAlignment.Left, Viewport, textWidth));
    }

    // ---------- 居中 ----------

    [Fact]
    public void CenterSplitsSlackEvenly()
    {
        // 富余 120 → 居中偏移 60
        Assert.Equal(
            60,
            LyricsAlignmentPolicy.GetOffset(LyricsTextAlignment.Center, Viewport, ShortText));
    }

    [Fact]
    public void CenterWithExactFitHasZeroOffset()
    {
        Assert.Equal(
            0,
            LyricsAlignmentPolicy.GetOffset(LyricsTextAlignment.Center, Viewport, Viewport));
    }

    // ---------- 右对齐 ----------

    [Fact]
    public void RightAlignmentPushesToTheEnd()
    {
        // 富余 120 全部给左边
        Assert.Equal(
            120,
            LyricsAlignmentPolicy.GetOffset(LyricsTextAlignment.Right, Viewport, ShortText));
    }

    // ---------- 滚动优先 ----------

    [Theory]
    [InlineData(LyricsTextAlignment.Center)]
    [InlineData(LyricsTextAlignment.Right)]
    public void AlignmentIsIgnoredWhenTextOverflows(LyricsTextAlignment alignment)
    {
        // 文本比视口宽时位置由 ScrollGeometry 决定。若这里返回非 0，
        // 会与滚动偏移相加，把文本推出视口。
        Assert.Equal(
            0,
            LyricsAlignmentPolicy.GetOffset(alignment, Viewport, LongText));
    }

    // ---------- 非法尺寸 ----------

    [Theory]
    [InlineData(0, ShortText)]                    // 视口还没布局
    [InlineData(-1, ShortText)]                   // 脏值
    [InlineData(double.NaN, ShortText)]           // 布局未完成
    [InlineData(double.PositiveInfinity, ShortText)]
    [InlineData(Viewport, 0)]                     // 文本还没量出来
    [InlineData(Viewport, -5)]
    [InlineData(Viewport, double.NaN)]
    public void InvalidDimensionsProduceZeroOffset(double viewport, double textWidth)
    {
        // 写出 NaN 到 TranslateTransform 会让整块内容消失（WPF 行为），
        // 所以任何非有限输入都必须退回 0。
        var offset = LyricsAlignmentPolicy.GetOffset(
            LyricsTextAlignment.Center, viewport, textWidth);

        Assert.Equal(0, offset);
        Assert.True(double.IsFinite(offset));
    }

    // ---------- 设置值归一化 ----------

    [Theory]
    [InlineData(0, LyricsTextAlignment.Left)]
    [InlineData(1, LyricsTextAlignment.Center)]
    [InlineData(2, LyricsTextAlignment.Right)]
    public void ValidSettingMapsToEnum(int setting, LyricsTextAlignment expected)
    {
        Assert.Equal(expected, LyricsAlignmentPolicy.FromSetting(setting));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    [InlineData(99)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public void OutOfRangeSettingFallsBackToLeft(int setting)
    {
        // 设置值可能来自手改的 settings.xml，或将来增删选项导致的索引错位。
        // 直接强转枚举不会报错，但会让下游 switch 落到 default 分支——
        // 显式归一化让「非法值等于左对齐」这件事有测试可依。
        Assert.Equal(LyricsTextAlignment.Left, LyricsAlignmentPolicy.FromSetting(setting));
    }

    [Fact]
    public void EnumUnderlyingValuesMatchSettingIndices()
    {
        // 设置页把 ComboBox.SelectedIndex 直接绑到 LyricsAlignment（int），
        // 所以枚举的整数值必须与索引一致。改了枚举顺序就会错位，
        // 而错位不会编译失败——只会在界面上选「居中」却右对齐。
        Assert.Equal(0, (int)LyricsTextAlignment.Left);
        Assert.Equal(1, (int)LyricsTextAlignment.Center);
        Assert.Equal(2, (int)LyricsTextAlignment.Right);
    }

    // ---------- 与滚动几何的联合约束 ----------

    [Theory]
    [InlineData(LyricsTextAlignment.Left)]
    [InlineData(LyricsTextAlignment.Center)]
    [InlineData(LyricsTextAlignment.Right)]
    public void AlignmentNeverDisagreesWithScrollAboutPositioning(LyricsTextAlignment alignment)
    {
        // 面板里 X 位移的来源有两个：滚动与对齐。同一时刻只能有一个说了算，
        // 否则两者相加会把长句推出视口（左半边空白、右半边截断）。
        //
        // 这里用 ScrollGeometry 的真实判定交叉验证，而不是自己另写一套
        // 「文本比视口宽」的判断——两套判断迟早会分叉。
        var scroll = ScrollGeometry.Create(
            lineProgress: 0.5,
            textWidth: LongText,
            viewportWidth: Viewport,
            lineDurationSeconds: 0,
            enabled: true);

        var alignmentOffset = LyricsAlignmentPolicy.GetOffset(alignment, Viewport, LongText);

        Assert.True(scroll.NeedsScroll);
        Assert.Equal(0, alignmentOffset);
    }

    [Fact]
    public void ShortTextNeverNeedsScrollSoAlignmentAlwaysApplies()
    {
        // 反面：装得下时滚动不参与，对齐必须生效——否则设置等于没用。
        var scroll = ScrollGeometry.Create(
            lineProgress: 0.5,
            textWidth: ShortText,
            viewportWidth: Viewport,
            lineDurationSeconds: 0,
            enabled: true);

        Assert.False(scroll.NeedsScroll);
        Assert.True(
            LyricsAlignmentPolicy.GetOffset(LyricsTextAlignment.Center, Viewport, ShortText) > 0);
    }
}
