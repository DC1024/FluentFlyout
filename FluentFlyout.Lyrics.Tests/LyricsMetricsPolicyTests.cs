// Copyright (c) 2026 anync
// SPDX-License-Identifier: MIT
//
// 本文件为本仓库新增，不源自 TaskbarLyrics。

using FluentFlyout.Lyrics.Layout;
using Xunit;

namespace FluentFlyout.Lyrics.Tests;

/// <summary>
/// 「任务栏形态 → 歌词度量」的 2×2 组合测试。
///
/// 为什么值得单独测：这段决策只有一行三元表达式，但它把**方向**与
/// **是否小任务栏**两个独立变化的输入混在一起。用户在设置里改任务栏位置、
/// 改图标大小、或者把小组件挪到另一块屏，都会让这两个输入各自翻转。
/// 组合错了的表现是「竖任务栏里歌词只剩三四个字」——不会崩、不会报错，
/// 只是不好用，靠人眼验收很容易漏。
/// </summary>
public sealed class LyricsMetricsPolicyTests
{
    // ---------- 2×2 矩阵 ----------

    [Fact]
    public void HorizontalNormalTaskbarShowsTwoLines()
    {
        var metrics = LyricsMetricsPolicy.Create(isVertical: false, isSmallTaskbar: false);

        Assert.False(metrics.IsSingleLine);
        Assert.Equal(2, metrics.VisibleLineCount);
        Assert.Equal(LyricsMetricsPolicy.DefaultPreferredFontSize, metrics.FontSize);
    }

    [Fact]
    public void HorizontalSmallTaskbarForcesSingleLine()
    {
        // 28 DIP 高度装不下两行，必须压成一行。
        var metrics = LyricsMetricsPolicy.Create(isVertical: false, isSmallTaskbar: true);

        Assert.True(metrics.IsSingleLine);
        Assert.Equal(1, metrics.VisibleLineCount);
    }

    [Fact]
    public void VerticalNormalTaskbarShowsTwoLines()
    {
        var metrics = LyricsMetricsPolicy.Create(isVertical: true, isSmallTaskbar: false);

        Assert.False(metrics.IsSingleLine);
        Assert.Equal(2, metrics.VisibleLineCount);
    }

    [Fact]
    public void VerticalSmallTaskbarDoesNotForceSingleLine()
    {
        // 这条是核心：竖向的约束是宽度窄，靠换行解决；
        // 压成单行只会让屏幕上只剩三四个字。
        //
        // 判据不能只看 IsSingleLine——28 DIP 作为「纵向可用高度」本来就会
        // 触发降级，所以真正要锁的是「竖向时纵向不受短边约束」。
        // 表现就是：竖向下度量与横向标准任务栏一致，字号也是 13 而不是放大后的值。
        var metrics = LyricsMetricsPolicy.Create(isVertical: true, isSmallTaskbar: true);

        Assert.False(metrics.IsSingleLine);
        Assert.Equal(2, metrics.VisibleLineCount);
        Assert.Equal(LyricsMetricsPolicy.DefaultPreferredFontSize, metrics.FontSize);
    }

    // ---------- 矩阵的一致性 ----------

    [Fact]
    public void VerticalIgnoresSmallTaskbarFlag()
    {
        // 竖向时 isSmallTaskbar 不参与决策——两个值必须给出同一份度量。
        // 这样「用户改了图标大小」不会让竖任务栏上的歌词跳动。
        var normal = LyricsMetricsPolicy.Create(isVertical: true, isSmallTaskbar: false);
        var small = LyricsMetricsPolicy.Create(isVertical: true, isSmallTaskbar: true);

        Assert.Equal(normal, small);
    }

    [Fact]
    public void OnlyHorizontalSmallTaskbarDiffersFromBaseline()
    {
        var baseline = LyricsMetricsPolicy.Create(isVertical: false, isSmallTaskbar: false);

        // 只有「横向 + 小」应该与基线不同（被压成单行）。
        Assert.NotEqual(baseline, LyricsMetricsPolicy.Create(isVertical: false, isSmallTaskbar: true));

        // 竖向的两种组合都应**等于**基线：竖向不旋转排版，纵向不受短边约束，
        // 所以度量与横向标准任务栏完全一致。这条同时也说明「竖向 + 小任务栏」
        // 不会因为用户调小图标而改变字号——切设置时歌词不会跳动。
        Assert.Equal(baseline, LyricsMetricsPolicy.Create(isVertical: true, isSmallTaskbar: false));
        Assert.Equal(baseline, LyricsMetricsPolicy.Create(isVertical: true, isSmallTaskbar: true));
    }

    // ---------- 与 DPI 无关 ----------

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public void MetricsAreDpiIndependent(double dpiScale)
    {
        // 度量全部是 DIP，物理像素由 WPF 换算。所以同一块任务栏在
        // 100% / 125% / 150% / 200% 下算出的度量必须完全一致——
        // 这正是「混合 DPI 切换时不需要重算度量」的依据。
        //
        // dpiScale 在这里只是被显式忽略：这个测试的价值在于**锁定**
        // 「度量不接受 DPI 参数」这个设计决定，防止后来者「顺手」加一个。
        _ = dpiScale;

        var metrics = LyricsMetricsPolicy.Create(isVertical: false, isSmallTaskbar: false);

        Assert.Equal(LyricsMetricsPolicy.DefaultTaskbarCrossSize, 40);
        Assert.Equal(13, metrics.FontSize);
    }

    // ---------- 字号透传 ----------

    [Theory]
    [InlineData(10)]
    [InlineData(13)]
    [InlineData(16)]
    public void PreferredFontSizeIsForwarded(double fontSize)
    {
        var metrics = LyricsMetricsPolicy.Create(
            isVertical: false, isSmallTaskbar: false, preferredFontSize: fontSize);

        // 40 DIP 高度下 16px 仍装得下两行，字号应原样透传。
        Assert.Equal(fontSize, metrics.FontSize);
    }

    [Fact]
    public void LargePreferredFontDegradesToSingleLineEvenOnNormalTaskbar()
    {
        // 用户把字号调到 24，40 DIP 装不下两行 → 自动降级。
        // 这条不是策略层的判断，而是 LyricsPanelMetrics 的兜底，
        // 但策略层必须把「可用高度」正确传下去，否则兜底无从触发。
        var metrics = LyricsMetricsPolicy.Create(
            isVertical: false, isSmallTaskbar: false, preferredFontSize: 24);

        Assert.True(metrics.IsSingleLine);
        Assert.True(metrics.TotalHeight <= LyricsMetricsPolicy.DefaultTaskbarCrossSize);
    }

    // ---------- 用户主动单行（P4-4 的单/双行开关） ----------

    [Fact]
    public void UserSingleLineForcesSingleLineOnNormalHorizontalTaskbar()
    {
        // 40 DIP 本来装得下两行，用户主动要求单行。
        var auto = LyricsMetricsPolicy.Create(isVertical: false, isSmallTaskbar: false);
        var forced = LyricsMetricsPolicy.Create(
            isVertical: false, isSmallTaskbar: false, userSingleLine: true);

        Assert.False(auto.IsSingleLine);
        Assert.True(forced.IsSingleLine);
    }

    [Fact]
    public void UserSingleLineAlsoAppliesOnVerticalTaskbar()
    {
        // 关键差异点：竖向 + 小任务栏是「系统不降级」的组合
        // （见 VerticalSmallTaskbarDoesNotForceSingleLine），
        // 但用户主动选单行时必须生效——那是明确意图，不是系统在替他取舍。
        var forced = LyricsMetricsPolicy.Create(
            isVertical: true, isSmallTaskbar: true, userSingleLine: true);

        Assert.True(forced.IsSingleLine);
    }

    [Fact]
    public void UserSingleLineReducesTotalHeight()
    {
        // 单行的收益是省高度：只占一行，不占两行 + 间距。
        var twoLine = LyricsMetricsPolicy.Create(isVertical: false, isSmallTaskbar: false);
        var oneLine = LyricsMetricsPolicy.Create(
            isVertical: false, isSmallTaskbar: false, userSingleLine: true);

        Assert.True(oneLine.TotalHeight < twoLine.TotalHeight);
    }

    [Fact]
    public void DefaultIsNotUserSingleLine()
    {
        // 默认必须是「自动」：多数用户没理由主动关掉第二行，
        // 而小任务栏的降级已经由 isSmallTaskbar 覆盖。
        var metrics = LyricsMetricsPolicy.Create(isVertical: false, isSmallTaskbar: false);

        Assert.False(metrics.IsSingleLine);
    }

    [Fact]
    public void UserSingleLineScalesFontUpButStaysWithinAvailableHeight()
    {
        // 单行模式会**放大**字号（SingleLineFontScale = 1.15）：省下来的
        // 那一行高度可以换更大的字，读起来更轻松。这是既有行为，
        // 用户主动选单行时同样适用——不该因为「是用户选的」就取消这个收益。
        //
        // 这里锁两件事：确实放大了，且放大后仍装得进任务栏。
        var twoLine = LyricsMetricsPolicy.Create(
            isVertical: false, isSmallTaskbar: false, preferredFontSize: 15);
        var oneLine = LyricsMetricsPolicy.Create(
            isVertical: false, isSmallTaskbar: false, preferredFontSize: 15, userSingleLine: true);

        Assert.True(
            oneLine.FontSize > twoLine.FontSize,
            $"单行应放大字号：双行 {twoLine.FontSize}，单行 {oneLine.FontSize}");

        Assert.True(
            oneLine.TotalHeight <= LyricsMetricsPolicy.DefaultTaskbarCrossSize,
            $"单行总高 {oneLine.TotalHeight} 超出任务栏短边");
    }

    // ---------- 边界 ----------

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    public void InvalidFontSizeFallsBackToDefault(double fontSize)
    {
        var metrics = LyricsMetricsPolicy.Create(
            isVertical: false, isSmallTaskbar: false, preferredFontSize: fontSize);

        Assert.Equal(LyricsMetricsPolicy.DefaultPreferredFontSize, metrics.FontSize);
    }

    [Fact]
    public void AllFourCombinationsProduceUsableMetrics()
    {
        foreach (var isVertical in new[] { false, true })
        {
            foreach (var isSmall in new[] { false, true })
            {
                var metrics = LyricsMetricsPolicy.Create(isVertical, isSmall);

                Assert.True(metrics.FontSize > 0, $"({isVertical},{isSmall}) 字号非正");
                Assert.True(metrics.LineHeight > 0, $"({isVertical},{isSmall}) 行高非正");
                Assert.False(double.IsNaN(metrics.TotalHeight), $"({isVertical},{isSmall}) 总高 NaN");

                // 竖向时纵向不受约束，总高可以超过短边——这正是设计意图
                // （文本沿主轴换行，纵向延伸由屏幕高度决定）。
                if (isVertical)
                {
                    Assert.False(metrics.IsSingleLine, "竖向不应降级单行");
                    continue;
                }

                // 横向时总高必须装进任务栏短边（字号下限 9 允许极小溢出）。
                var crossSize = isSmall
                    ? LyricsMetricsPolicy.SmallTaskbarCrossSize
                    : LyricsMetricsPolicy.DefaultTaskbarCrossSize;
                Assert.True(
                    metrics.TotalHeight <= crossSize * 1.2,
                    $"横向({isSmall}) 总高 {metrics.TotalHeight} 超出 {crossSize} 过多");
            }
        }
    }

    // ---------- 竖向的宽度约束（短边 → 字号） ----------

    [Fact]
    public void VerticalNarrowTaskbarCapsFontSoTwoGlyphsFit()
    {
        // 28 DIP 短边 + 用户把字号拉到 20：不夹的话一行只站得下一个汉字，
        // 30 字的长句变成一座独字塔（截图矩阵 edge-vertical-v28-font20 复现过，
        // 而且顺带被 480 的长度上限截在半句）。
        var metrics = LyricsMetricsPolicy.Create(
            isVertical: true,
            isSmallTaskbar: true,
            preferredFontSize: 20);

        Assert.Equal(
            LyricsMetricsPolicy.GetVerticalMaxFontSize(isSmallTaskbar: true),
            metrics.FontSize,
            precision: 6);

        Assert.True(
            metrics.FontSize * LyricsMetricsPolicy.MinimumGlyphsPerLine
                <= LyricsMetricsPolicy.SmallTaskbarCrossSize,
            $"字号 {metrics.FontSize} 放不下两个汉字（短边 {LyricsMetricsPolicy.SmallTaskbarCrossSize}）");
    }

    [Fact]
    public void VerticalNarrowTaskbarKeepsDefaultFont()
    {
        // 默认 13px 在 28 DIP 短边里本来就能站两个汉字（26 ≤ 28），
        // 不该被这条约束顺手压小一圈。
        var metrics = LyricsMetricsPolicy.Create(isVertical: true, isSmallTaskbar: true);

        Assert.Equal(LyricsMetricsPolicy.DefaultPreferredFontSize, metrics.FontSize, precision: 6);
    }

    [Fact]
    public void VerticalNormalTaskbarKeepsLargeFont()
    {
        // 60 DIP 宽的真实竖向任务栏放到 20px 也够两个汉字。
        //
        // 但策略层只知道「是不是小任务栏」这个布尔量，短边取的是规范值
        // DefaultTaskbarCrossSize(40)，于是上限是 19.5——略小于用户要的 20。
        // 这是**有意保守**：真实竖向任务栏宽度 ≥ 40，按 40 推出的上限
        // 在任何形态下都成立。断言写成属性而不是写死 19.5，避免以后
        // 调常量时被测试卡住。
        var metrics = LyricsMetricsPolicy.Create(
            isVertical: true,
            isSmallTaskbar: false,
            preferredFontSize: 20);

        Assert.Equal(
            LyricsMetricsPolicy.GetVerticalMaxFontSize(isSmallTaskbar: false),
            metrics.FontSize,
            precision: 6);

        Assert.True(
            metrics.FontSize * LyricsMetricsPolicy.MinimumGlyphsPerLine
                <= LyricsMetricsPolicy.DefaultTaskbarCrossSize,
            $"字号 {metrics.FontSize} 放不下两个汉字（短边 {LyricsMetricsPolicy.DefaultTaskbarCrossSize}）");

        // 只压到几何上限，不该被压得比默认字号还小。
        Assert.True(metrics.FontSize >= LyricsMetricsPolicy.DefaultPreferredFontSize);
    }

    [Fact]
    public void HorizontalTaskbarIsNotConstrainedByVerticalWidthCap()
    {
        // 横向的短边约束的是「行高」（已由 availableHeight 处理），
        // 不该被竖向那条「两个汉字」规则误伤。
        //
        // 20px 字在 40 DIP 高里装不下两行 → 降级单行并放大 1.15 倍，
        // 这是横向唯一正确的行为。
        var metrics = LyricsMetricsPolicy.Create(
            isVertical: false,
            isSmallTaskbar: false,
            preferredFontSize: 20);

        Assert.True(metrics.IsSingleLine);
        Assert.Equal(
            Math.Min(20 * LyricsPanelMetrics.SingleLineFontScale, LyricsMetricsPolicy.DefaultTaskbarCrossSize / LyricsPanelMetrics.LineHeightFactor),
            metrics.FontSize,
            precision: 6);
    }
}
