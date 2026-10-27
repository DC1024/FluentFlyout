// Copyright (c) 2026 anync
// SPDX-License-Identifier: MIT
//
// 本文件为本仓库新增，不源自 TaskbarLyrics。

using FluentFlyout.Lyrics.Layout;
using Xunit;

namespace FluentFlyout.Lyrics.Tests;

/// <summary>
/// 竖向任务栏主轴长度估算的测试。
///
/// 这类「估算」的 bug 很难靠截图发现——偏小 30% 在渲染图上看起来
/// 只是「字少了点」，容易被当成正常。所以把边界全部锁在单测里。
/// </summary>
public sealed class LyricsVerticalLayoutTests
{
    private const double LineHeight = 14.95;   // 13px × 1.15
    private const double CrossSize = 40;       // 标准任务栏宽度
    private const double MaxLength = 400;

    // ---------- 折行行数 ----------

    [Fact]
    public void ShortContentFitsInOneRow()
    {
        // 内容比可用宽度还短 → 一行。
        var length = LyricsVerticalLayout.EstimateMainAxisLength(
            contentWidth: 30, CrossSize, LineHeight, MaxLength);

        Assert.Equal(LineHeight, length, precision: 6);
    }

    [Fact]
    public void ContentExactlyFitsUsableWidthUsesOneRow()
    {
        var usable = CrossSize - LyricsVerticalLayout.WrapSafetyMargin;

        var length = LyricsVerticalLayout.EstimateMainAxisLength(
            usable, CrossSize, LineHeight, MaxLength);

        Assert.Equal(LineHeight, length, precision: 6);
    }

    [Fact]
    public void OnePixelOverUsableWidthWrapsToTwoRows()
    {
        var usable = CrossSize - LyricsVerticalLayout.WrapSafetyMargin;

        var length = LyricsVerticalLayout.EstimateMainAxisLength(
            usable + 0.1, CrossSize, LineHeight, MaxLength);

        // 多 0.1 也要算第二行——向上取整，否则最后几个字被切掉。
        Assert.Equal(2 * LineHeight, length, precision: 6);
    }

    [Theory]
    [InlineData(30, 1)]    // < 36：一行
    [InlineData(36, 1)]    // = 36：恰好一行
    [InlineData(37, 2)]    // 略超：两行
    [InlineData(72, 2)]    // = 2×36：两行
    [InlineData(73, 3)]    // 略超：三行
    [InlineData(108, 3)]   // = 3×36：三行
    public void RowCountScalesWithContentWidth(double contentWidth, int expectedRows)
    {
        var length = LyricsVerticalLayout.EstimateMainAxisLength(
            contentWidth, CrossSize, LineHeight, MaxLength);

        Assert.Equal(expectedRows * LineHeight, length, precision: 6);
    }

    // ---------- 上限与下限 ----------

    [Fact]
    public void NeverExceedsMaxLength()
    {
        // 一句 2000 DIP 宽的长歌词，主轴只给 100。
        var length = LyricsVerticalLayout.EstimateMainAxisLength(
            contentWidth: 2000, CrossSize, LineHeight, maxLength: 100);

        Assert.True(length <= 100, $"长度 {length} 超过上限 100");
    }

    [Fact]
    public void VeryLongContentDoesNotOverflow()
    {
        // rows 极大时不能溢出成 Infinity。
        var length = LyricsVerticalLayout.EstimateMainAxisLength(
            contentWidth: 1e12, CrossSize, LineHeight, MaxLength);

        Assert.True(double.IsFinite(length));
        Assert.True(length <= MaxLength);
    }

    [Fact]
    public void SmallMaxLengthWinsOverMinimumLength()
    {
        // 主轴只给 30，比 MinimumLength(64) 还小 → 取 30，不能反过来撑大。
        var length = LyricsVerticalLayout.EstimateMainAxisLength(
            contentWidth: 200, CrossSize, LineHeight, maxLength: 30);

        Assert.Equal(30, length);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    [InlineData(double.NaN)]
    public void InvalidMaxLengthReturnsZero(double maxLength)
    {
        // 主轴长度非法说明调用方还没拿到任务栏矩形，
        // 返回 0 让上层走自己的兜底路径，不要瞎猜。
        var length = LyricsVerticalLayout.EstimateMainAxisLength(
            contentWidth: 200, CrossSize, LineHeight, maxLength);

        Assert.Equal(0, length);
    }

    // ---------- 脏输入 ----------

    [Theory]
    [InlineData(0, 40, 15)]
    [InlineData(-5, 40, 15)]
    [InlineData(double.NaN, 40, 15)]
    [InlineData(200, 0, 15)]
    [InlineData(200, -1, 15)]
    [InlineData(200, 40, 0)]
    [InlineData(200, 40, double.NaN)]
    public void InvalidInputsFallBackToBoundedDefault(double contentWidth, double crossSize, double lineHeight)
    {
        var length = LyricsVerticalLayout.EstimateMainAxisLength(
            contentWidth, crossSize, lineHeight, MaxLength);

        // 兜底值必须有限、非负、不超上限。
        Assert.True(double.IsFinite(length), $"长度 {length} 非有限");
        Assert.True(length >= 0);
        Assert.True(length <= MaxLength);
        Assert.Equal(Math.Min(LyricsVerticalLayout.FallbackLength, MaxLength), length);
    }

    [Fact]
    public void FallbackIsClampedByMaxLength()
    {
        var length = LyricsVerticalLayout.EstimateMainAxisLength(
            contentWidth: 0, CrossSize, LineHeight, maxLength: 50);

        // 兜底 120 超过上限 50 → 取 50。
        Assert.Equal(50, length);
    }

    // ---------- 单调性 ----------

    [Fact]
    public void LongerContentNeverShrinksLength()
    {
        double previous = 0;
        foreach (var width in new[] { 20d, 40d, 80d, 160d, 320d })
        {
            var length = LyricsVerticalLayout.EstimateMainAxisLength(
                width, CrossSize, LineHeight, MaxLength);

            Assert.True(length >= previous,
                $"内容宽 {width} 时长度 {length} 小于上一档 {previous}");
            previous = length;
        }
    }

    [Fact]
    public void NarrowerTaskbarNeverShrinksLength()
    {
        // 任务栏越窄，折行越多，需要的纵向空间只会更大。
        double previous = 0;
        foreach (var crossSize in new[] { 60d, 50d, 40d, 32d })
        {
            var length = LyricsVerticalLayout.EstimateMainAxisLength(
                contentWidth: 200, crossSize, LineHeight, MaxLength);

            Assert.True(length >= previous,
                $"任务栏宽 {crossSize} 时长度 {length} 小于上一档 {previous}");
            previous = length;
        }
    }

    // ---------- 真实尺寸 ----------

    [Fact]
    public void TypicalLyricLineOnStandardVerticalTaskbar()
    {
        // 一句 20 字的歌词，13px 字号约 260 DIP 宽；
        // 竖向任务栏 40 DIP 宽，可用 36。
        var length = LyricsVerticalLayout.EstimateMainAxisLength(
            contentWidth: 260, crossSize: 40, lineHeight: LineHeight, maxLength: 800);

        // 260 / 36 = 7.22 → 8 行 → 8 × 14.95 = 119.6
        Assert.Equal(8 * LineHeight, length, precision: 6);

        // 这个量在 1080p 屏（主轴约 1040 DIP）上只占 11%，
        // 不会盖住任务栏中部的应用按钮。
        Assert.True(length < 800 * 0.2);
    }

    [Fact]
    public void EstimateStaysWithinSaneRangeForWideTaskbar()
    {
        // 用户把任务栏调宽到 60 DIP（平板模式），折行变少。
        var length = LyricsVerticalLayout.EstimateMainAxisLength(
            contentWidth: 260, crossSize: 60, lineHeight: LineHeight, maxLength: 800);

        // 260 / 56 = 4.64 → 5 行 → 74.75
        Assert.Equal(5 * LineHeight, length, precision: 6);
    }

    // ---------- 两行内容（当前行 + 翻译/下一行）----------
    //
    // 这一组对应截图矩阵实测到的真实缺陷：原实现只按主行估长度，
    // 双行状态估 29.9 DIP、实际要 63.4 DIP，次要行被 ClipToBounds
    // 整行裁掉。以下每条都是那次实测的具体数字。

    /// <summary>截图矩阵里 two-line 状态的实测值（60 DIP 宽、13px 字号）。</summary>
    private const double MeasuredPrimaryWidth = 104;   // 8 个中文字
    private const double MeasuredPrimaryHeight = 16.5; // FormattedText 实测，不是名义 14.95
    private const double MeasuredSecondaryWidth = 104;
    private const double MeasuredGap = 2;

    [Fact]
    public void TwoLinesAddUpInsteadOfTakingMax()
    {
        // 主行折 2 行、次要行也折 2 行 → 必须相加得 4 行。
        // 若错误地取「两行较宽者的行数」，结果会是 2 行，次要行被裁掉。
        var content = new LyricsVerticalContent(
            PrimaryWidth: MeasuredPrimaryWidth,
            PrimaryLineHeight: MeasuredPrimaryHeight,
            SecondaryWidth: MeasuredSecondaryWidth,
            SecondaryLineHeight: MeasuredPrimaryHeight,
            Gap: MeasuredGap);

        var length = LyricsVerticalLayout.EstimateMainAxisLength(content, 60, 800);

        // 104 / 56 = 1.86 → 2 行；两行各 2 行 → 4 × 16.5 + 2 = 68
        Assert.Equal((4 * MeasuredPrimaryHeight) + MeasuredGap, length, precision: 6);
    }

    [Fact]
    public void SecondaryLineIsNotDropped()
    {
        // 回归护栏：只要加了次要行，结果必须严格变大。
        // 这条断言的价值在于——将来若有人「优化」成只算主行，它会立刻失败。
        var primaryOnly = new LyricsVerticalContent(MeasuredPrimaryWidth, MeasuredPrimaryHeight);
        var withSecondary = primaryOnly with
        {
            SecondaryWidth = MeasuredSecondaryWidth,
            SecondaryLineHeight = MeasuredPrimaryHeight,
            Gap = MeasuredGap,
        };

        var withoutLength = LyricsVerticalLayout.EstimateMainAxisLength(primaryOnly, 60, 800);
        var withLength = LyricsVerticalLayout.EstimateMainAxisLength(withSecondary, 60, 800);

        Assert.True(
            withLength > withoutLength + MeasuredPrimaryHeight,
            $"带次要行应至少多一行：{withLength} vs {withoutLength}");
    }

    [Fact]
    public void SingleLineMatchesLegacyOverload()
    {
        // 简化入口必须与「只有主行」的完整入口完全等价，
        // 否则既有调用方会在无感知的情况下改变行为。
        var legacy = LyricsVerticalLayout.EstimateMainAxisLength(
            contentWidth: 260, crossSize: 40, lineHeight: LineHeight, maxLength: 800);

        var full = LyricsVerticalLayout.EstimateMainAxisLength(
            new LyricsVerticalContent(260, LineHeight), 40, 800);

        Assert.Equal(legacy, full, precision: 6);
    }

    [Fact]
    public void EmptySecondaryWidthIsTreatedAsNoSecondaryLine()
    {
        // 单行模式下次要行是 Collapsed，宽度会是 0。
        // 这时不能因为「有 Gap」就凭空多算 2 DIP。
        var content = new LyricsVerticalContent(
            PrimaryWidth: MeasuredPrimaryWidth,
            PrimaryLineHeight: MeasuredPrimaryHeight,
            SecondaryWidth: 0,
            SecondaryLineHeight: 0,
            Gap: MeasuredGap);

        var length = LyricsVerticalLayout.EstimateMainAxisLength(content, 60, 800);

        Assert.Equal(2 * MeasuredPrimaryHeight, length, precision: 6);
        Assert.False(content.HasSecondary);
    }

    [Fact]
    public void TwoLinesClampToMaxLength()
    {
        // 两行都很长时，结果仍不能超过预算。
        var content = new LyricsVerticalContent(
            PrimaryWidth: 5000,
            PrimaryLineHeight: MeasuredPrimaryHeight,
            SecondaryWidth: 5000,
            SecondaryLineHeight: MeasuredPrimaryHeight,
            Gap: MeasuredGap);

        var length = LyricsVerticalLayout.EstimateMainAxisLength(content, 40, 400);

        Assert.Equal(400, length, precision: 6);
    }

    [Fact]
    public void MeasuredLineHeightIsHonouredNotNominal()
    {
        // 名义行高 14.95 与实测 16.5 差 10%：五行就差 7.5 DIP。
        // 估算必须用调用方给的实测值，不能自己按字号推。
        var content = new LyricsVerticalContent(
            PrimaryWidth: 260,
            PrimaryLineHeight: MeasuredPrimaryHeight);

        var length = LyricsVerticalLayout.EstimateMainAxisLength(content, 60, 800);

        // 260 / 56 = 4.64 → 5 行 × 16.5 = 82.5（不是 5 × 14.95 = 74.75）
        Assert.Equal(5 * MeasuredPrimaryHeight, length, precision: 6);
    }

    [Fact]
    public void EstimateIsPureWidthDivisionWithoutKinsokuFudge()
    {
        // **设计决策的回归护栏**：估算层刻意**不做**中文标点禁则修正。
        //
        // 禁则（标点不能出现在行首）会让实际折行比纯宽度除法多约一行，
        // 但这个偏差取决于标点位置，是文本内容的函数，不是宽度的函数——
        // 本文件是纯算术层，拿不到文本。硬塞「每行减一个字」的系数会在
        // 短句上凭空多算一行（实测 single 状态从 33.0 涨到 49.5），
        // 而长句上又可能仍不够。
        //
        // 正确的分工：估算给「下限」，实际偏差由 LyricsPanel.VerticalContentHeight
        // 在布局后用实测值兜住（宿主取两者的较大者）。
        //
        // 这条断言锁住「估算必须保持纯算术」——若将来有人往 CountRows 里
        // 加系数，它会立刻失败，提醒去看 VerticalContentHeight 那条路径。
        var content = new LyricsVerticalContent(
            PrimaryWidth: 377,      // 长句实测宽度
            PrimaryLineHeight: 16.51);

        var length = LyricsVerticalLayout.EstimateMainAxisLength(content, 60, 800);

        // 377 / 56 = 6.73 → 7 行（纯算术）。实际 TextBlock 折了 8 行，
        // 差的那一行由 VerticalContentHeight 补。
        Assert.Equal(7 * 16.51, length, precision: 6);
    }

    [Fact]
    public void ShortSingleLineDoesNotInflate()
    {
        // 回归护栏：一句放得下的短歌词必须只报一行。
        // 若有人给 CountRows 加了「每行减一个字」的系数，这里会变成两行，
        // 任务栏上就会白占一行高度。
        var content = new LyricsVerticalContent(
            PrimaryWidth: 104,      // 8 个中文字，60 DIP 宽里折 2 行
            PrimaryLineHeight: 16.51);

        var length = LyricsVerticalLayout.EstimateMainAxisLength(content, 60, 800);

        // 104 / 56 = 1.86 → 2 行。这是**真实需要**的两行，不是膨胀。
        Assert.Equal(2 * 16.51, length, precision: 6);
    }
}
