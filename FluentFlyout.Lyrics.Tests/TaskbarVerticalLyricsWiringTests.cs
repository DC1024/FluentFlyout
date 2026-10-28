// Copyright (c) 2026 anync
// SPDX-License-Identifier: MIT
//
// 本文件为本仓库新增，不源自 TaskbarLyrics。

using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using FluentFlyout.Classes.Settings;
using FluentFlyout.Controls;
using FluentFlyout.Lyrics.Models;
using Xunit;

namespace FluentFlyout.Lyrics.Tests;

/// <summary>
/// 竖向任务栏歌词的**装配链**测试：走生产的 <c>CalculateSize</c>，
/// 而不是像截图矩阵那样由夹具直接把尺寸写进面板。
///
/// <para>
/// 为什么必须有这一层：竖向下「一个字都不显示」的缺陷（面板宽度恒为 0、
/// 文字被 <c>ClipToBounds</c> 剪光）在 72 张截图矩阵里**全绿**——因为那套
/// 夹具自己给面板写了宽度，绕过了生产路径里唯一真正取宽度的地方。
/// 夹具通过 ≠ 生产路径正确，这一课值得用一个测试长期钉住。
/// </para>
///
/// <para>
/// 断言都落在公开可见的状态上（<see cref="FrameworkElement.Width"/>、
/// 字形块本身），不碰 internal。
/// </para>
/// </summary>
public sealed class TaskbarVerticalLyricsWiringTests
{
    /// <summary>标准任务栏短边（DIP），与 <c>DefaultTaskbarWidgetHeight</c> 同源。</summary>
    private const double DefaultCrossSize = 40;

    /// <summary>小任务栏短边（DIP），与 <c>SmallTaskbarWidgetHeight</c> 同源。</summary>
    private const double SmallCrossSize = 28;

    /// <summary>
    /// 小组件内容区相对短边的内缩（DIP）：<c>TopBorder</c> 上边框 1.25 +
    /// 下外边距 1.25。可用短边 = 短边 − 内缩，与
    /// <c>TaskbarWidgetControl.WidgetCrossInset</c> 同源。
    /// </summary>
    private const double CrossInset = 2.5;

    /// <summary>
    /// 一句真正**排不进**主轴长度的歌词（51 个汉字，字号 13 时约 663 DIP）。
    ///
    /// <para>
    /// 为什么必须有它：文件里原先那条 <c>LongLine</c>（28 字 / 377 DIP）
    /// 从来没超过 <c>DefaultMaxMainAxisLength</c> = 480，于是
    /// 「长句被裁 / 长句滚动」这条路径在整套单测与截图矩阵里一次都没被
    /// 真正渲染过——断言只看墨迹宽度，照样全绿。夹具覆盖不到的分支
    /// 等于没有实现。
    /// </para>
    /// </summary>
    private const string OversizeLine =
        "如果这场雨一直下到明天清晨我们是不是就能一直站在这里不分开而我还在等你说好的那句话就算全世界都安静下来";

    [Fact]
    public void VerticalLyricsModeWritesAvailableCrossSizeIntoPanelHeight()
    {
        var cross = RunOnStaThread(() =>
        {
            var (control, panel) = CreateVerticalLyricsControl();

            control.CalculateSize(dpiScale: 1.0, availableWidth: 800);

            return panel.Height;
        });

        Assert.False(double.IsNaN(cross), "竖向歌词模式下 LyricsPanel.Height 不能被留成 NaN：竖排的列数与列距" +
                                          "都由短边决定，读不到短边就只能排一列。");

        // 期望值是「可用短边」而不是短边本身：TopBorder 吃掉 2.5 DIP，
        // 按 40 写会让两列挤进 40 而边缘被切掉一条。
        Assert.Equal(DefaultCrossSize - CrossInset, cross, precision: 3);
    }

    [Fact]
    public void SmallTaskbarUsesSmallCrossSize()
    {
        var cross = RunOnStaThread(() =>
        {
            var (control, panel) = CreateVerticalLyricsControl();
            control.SetSmallTaskbarMode(true);

            control.CalculateSize(dpiScale: 1.0, availableWidth: 800);

            return panel.Height;
        });

        Assert.Equal(SmallCrossSize - CrossInset, cross, precision: 3);
    }

    [Fact]
    public void SwitchingBackToHorizontalReleasesTheSize()
    {
        var (widthAfterSwitchBack, heightAfterSwitchBack, widthAfterHorizontalLayout) = RunOnStaThread(() =>
        {
            var (control, panel) = CreateVerticalLyricsControl();
            control.CalculateSize(dpiScale: 1.0, availableWidth: 800);

            // 竖向写成「主轴长度宽 + 37.5 高」之后切回横向：两个尺寸都必须回到
            // Auto，否则横向第一帧会按竖向的主轴长度留一条长空白。
            control.SetVerticalMode(false);
            var afterSwitchBack = (panel.Width, panel.Height);

            control.CalculateSize(dpiScale: 1.0, availableWidth: 800);
            return (afterSwitchBack.Width, afterSwitchBack.Height, panel.Width);
        });

        Assert.True(double.IsNaN(widthAfterSwitchBack),
            "切回横向后 LyricsPanel.Width 应被清回 NaN（Auto），实为 " +
            $"{widthAfterSwitchBack.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)}。");
        Assert.True(double.IsNaN(heightAfterSwitchBack),
            "切回横向后 LyricsPanel.Height 应被清回 NaN（Auto），实为 " +
            $"{heightAfterSwitchBack.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)}。");

        // 横向分支自己会重写一个有限宽度（值取决于内容预算，这里不断言具体数值，
        // 只要求「不是 NaN」）。
        Assert.False(double.IsNaN(widthAfterHorizontalLayout), "横向 CalculateSize 之后宽度应被重新写入。");
    }

    [Fact]
    public void CrossSizeEqualsAvailableCrossFromLayout()
    {
        // 可用短边不是控件高度本身：TopBorder 的上边框与下外边距吃掉 2.5 DIP，
        // 40 的高度只剩 37.5 给内容。这条测试用**真实布局量出的可用短边**校验
        // 宿主的常量，XAML 里改边框/外边距而忘了改常量时会红。
        var (writtenCross, availableFromLayout) = RunOnStaThread(() =>
        {
            var (control, _) = CreateVerticalLyricsControl();

            var host = new Canvas { Width = 48, Height = 1440 };
            host.Children.Add(control);
            control.Width = 240;
            control.Height = DefaultCrossSize;
            host.Measure(new Size(48, 1440));
            host.Arrange(new Rect(0, 0, 48, 1440));
            host.UpdateLayout();

            var topBorder = (Border)control.FindName("TopBorder");
            var available = topBorder.ActualHeight - topBorder.BorderThickness.Top;

            control.CalculateSize(dpiScale: 1.0, availableWidth: 800);
            var panel = (FrameworkElement)control.FindName("LyricsPanel");
            return (panel.Height, available);
        });

        Assert.Equal(availableFromLayout, writtenCross, precision: 3);
    }

    [Fact]
    public void ChineseLineIsOneColumnOfCounterRotatedGlyphs()
    {
        // 竖排的核心机制，一条断言钉两面：
        //   1. 每个汉字一格、互不重叠（主轴步进 ≈ 字号）；
        //   2. **每个汉字都挂了 -90 的反向旋转**——少这一下，屏幕上就是
        //      「字全躺倒」，而墨迹总量、内容长度全都正常，肉眼之外的
        //      所有指标都不会红。
        const string Line = "被爱的深夜我在想念";

        var (glyphCount, allCounterRotated, step, length) = RunOnStaThread(() =>
        {
            var (control, panel) = CreateVerticalLyricsControl();
            var blocks = LayOutLine(control, panel, Line);

            var steps = new List<double>();
            var canvas = (Canvas)panel.FindName("VerticalContainer");
            for (var i = 1; i < blocks.Count; i++)
            {
                steps.Add(Canvas.GetLeft(blocks[i]) - Canvas.GetLeft(blocks[i - 1]));
            }

            return (
                blocks.Count,
                blocks.All(b => b.RenderTransform is RotateTransform { Angle: -90 }),
                steps.Count > 0 ? steps.Max() - steps.Min() : 0,
                (double)panel.VerticalContentLength);
        });

        Assert.Equal(Line.Length, glyphCount);
        Assert.True(allCounterRotated, "每个汉字都必须绕自身中心反向旋转 90°，否则竖排文字会躺倒。");

        // 步进一致：差值应当远小于一个字宽（13 DIP）。
        Assert.True(step < 1.0, $"汉字步进不一致，最大差 {step:F2} DIP——不同宽度会排成锯齿。");

        // 整句一列到底：长度 ≈ 字数 × 字号，而不是「两字一折」。
        Assert.InRange(length, Line.Length * 12.0, Line.Length * 14.5);
    }

    [Fact]
    public void LatinWordsStaySidewaysAndAreNeverSplit()
    {
        // 英文必须整词侧躺（歪头读），不能逐字母正立堆成竖塔。
        // 真机缺陷「My / scree / n is / glowi / ng like a / midni」就是
        // 词被拆开的下场——这里断言每个单词都是一整格。
        const string Line = "My screen is glowing like a midnight sea";

        var (joined, screenIsWhole, allUprightAreChinese) = RunOnStaThread(() =>
        {
            var (control, panel) = CreateVerticalLyricsControl();
            var blocks = LayOutLine(control, panel, Line);

            return (
                string.Concat(blocks.Select(b => b.Text)),
                blocks.Any(b => b.Text == "screen"),
                blocks.All(b => b.RenderTransform is not RotateTransform || IsCjk(b.Text)));
        });

        Assert.Equal(Line, joined);
        Assert.True(screenIsWhole, "英文单词被拆开了：竖排里一个词必须是一格。");
        Assert.True(allUprightAreChinese, "只有方块字该反向旋转，拉丁词必须整体侧躺。");
    }

    [Fact]
    public void MixedLineKeepsChineseUprightAndLatinWordsWhole()
    {
        // 中英混排是最容易出岔子的一档：一列里既有要反旋的方块字，
        // 又有必须整词侧躺的拉丁词。**按整列统一处理**（要么全转、要么全不转）
        // 在纯中文与纯英文的用例上都看不出问题，只有混排会暴露：
        //   · 全转 → 英文侧躺的字被转成正立，歪头看不成了
        //   · 全不转 → 汉字躺倒，整句没法读
        // 所以这条用例专门钉住「逐格判定」。
        const string Line = "We were dancing in the rain 直到天亮";

        var (joined, chineseUpright, latinSideways, dancingIsWhole) = RunOnStaThread(() =>
        {
            var (control, panel) = CreateVerticalLyricsControl();
            var blocks = LayOutLine(control, panel, Line);

            var chinese = blocks.Where(b => IsCjk(b.Text)).ToList();
            var latin = blocks.Where(b => !IsCjk(b.Text)).ToList();

            return (
                string.Concat(blocks.Select(b => b.Text)),
                chinese.Count > 0 && chinese.All(b => b.RenderTransform is RotateTransform { Angle: -90 }),
                latin.Count > 0 && latin.All(b => b.RenderTransform is not RotateTransform),
                blocks.Any(b => b.Text == "dancing"));
        });

        Assert.Equal(Line, joined);
        Assert.True(chineseUpright, "混排里的汉字必须反旋为正立，否则整列躺倒。");
        Assert.True(latinSideways, "混排里的拉丁词必须保持侧躺，否则歪头读的词被转正。");
        Assert.True(dancingIsWhole, "混排里的英文单词不能被拆成字母。");
    }

    [Fact]
    public void TranslationGetsItsOwnColumnWhenItFits()
    {
        // 短边 37.5、字号 13：两列 2 × 1.33 × 13 = 34.6 ≤ 37.5，放得下。
        // 两列的判据是「短边方向的起点不同」——主列在小的 y 一侧（屏幕右侧），
        // 译文列在大的 y 一侧，与中文竖排「先读右列」一致。
        var (columnTops, secondaryCount, primaryCount) = RunOnStaThread(() =>
        {
            var (control, panel) = CreateVerticalLyricsControl();
            var blocks = LayOutLine(control, panel, "被爱的深夜我在想念", "I miss you in the late night");

            var tops = blocks
                .Select(b => Math.Round(Canvas.GetTop(b), 2))
                .Distinct()
                .OrderBy(t => t)
                .ToList();

            var primaryFontSize = panel.Metrics.FontSize;
            return (
                tops,
                blocks.Count(b => Math.Abs(b.FontSize - primaryFontSize) > 0.01),
                blocks.Count(b => Math.Abs(b.FontSize - primaryFontSize) <= 0.01));
        });

        Assert.Equal(2, columnTops.Count);
        Assert.True(primaryCount > 0 && secondaryCount > 0, "两列都该有字形块。");
    }

    [Fact]
    public void TranslationIsDroppedWhenTwoColumnsDoNotFit()
    {
        // 小任务栏：短边 25.5 < 34.6，两列硬挤会把两列都压扁。
        // 取舍是「留主列」——译文是辅助，当前歌词才是主体。
        var (columnCount, hasSecondaryFont) = RunOnStaThread(() =>
        {
            var (control, panel) = CreateVerticalLyricsControl();
            control.SetSmallTaskbarMode(true);

            var blocks = LayOutLine(control, panel, "被爱的深夜我在想念", "I miss you in the late night");

            return (
                blocks.Select(b => Math.Round(Canvas.GetTop(b), 2)).Distinct().Count(),
                blocks.Any(b => b.FontSize < panel.Metrics.FontSize - 0.01));
        });

        Assert.Equal(1, columnCount);
        Assert.False(hasSecondaryFont, "短边装不下两列时不该再排出译文列。");
    }

    [Fact]
    public void LongLineGetsFullMainAxisLengthAndIsNotClipped()
    {
        // 真机缺陷（左侧竖排任务栏）：9 个字的「被爱的深夜我在想念」只显示
        // 「被爱的 / 深夜我」——每列 3 字、上限 6 字，第 7 个字往后静默消失。
        //
        // 逐字竖排下这条链变成「面板 Width = 内容长度」，所以断言分两层：
        // 面板沿主轴的长度覆盖内容（不被 ClipToBounds 剪），
        // 且小组件比 StackPanel 要的更宽（不被 MainBorder 剪）。
        const string LongLine = "如果这场雨一直下到明天清晨我们是不是就能一直站在这里不分开";

        var (panelWidth, contentLength, stackWidth, widgetWidth) = RunOnStaThread(() =>
        {
            var (control, panel) = CreateVerticalLyricsControl();
            LayOutLine(control, panel, LongLine);

            var stack = (FrameworkElement)control.FindName("MainStackPanel");
            return (panel.Width, panel.VerticalContentLength, stack.ActualWidth, control.ActualWidth);
        });

        // 28 个字一列到底，长度远大于「两字一折」时代的 37.5。
        Assert.InRange(contentLength, 300, 480);

        Assert.True(panelWidth >= contentLength - 0.5,
            $"竖向歌词面板沿主轴只拿到 {panelWidth:F1} DIP，内容需要 {contentLength:F1} DIP——" +
            "差额会被 ClipToBounds 吃掉，屏幕上表现为「句子没写完」。");

        Assert.True(stackWidth <= widgetWidth + 0.5,
            $"MainStackPanel 要 {stackWidth:F1} DIP，小组件只有 {widgetWidth:F1} DIP——" +
            "超出部分会被 MainBorder 裁掉。");
    }

    [Fact]
    public void RepeatedLayoutKeepsSizeStable()
    {
        // CalculateSize 被 1.5s 定时器、换歌、每次歌词换行反复调用。
        // 重复调用必须幂等：尺寸不变（写入有值比较，不会每轮触发布局遍）。
        var (heights, widths) = RunOnStaThread(() =>
        {
            var (control, panel) = CreateVerticalLyricsControl();
            LayOutLine(control, panel, "被爱的深夜我在想念");

            var h = new double[3];
            var w = new double[3];
            for (var i = 0; i < h.Length; i++)
            {
                control.CalculateSize(dpiScale: 1.0, availableWidth: 800);
                h[i] = panel.Height;
                w[i] = panel.Width;
            }

            return (h, w);
        });

        Assert.All(heights, v => Assert.Equal(DefaultCrossSize - CrossInset, v, precision: 3));
        Assert.All(widths, v => Assert.Equal(widths[0], v, precision: 3));
        Assert.True(widths[0] > DefaultCrossSize, "主轴长度应当由内容决定，远大于短边。");
    }

    [Fact]
    public void OversizeLineScrollsAlongTheMainAxisWhenScrollingIsEnabled()
    {
        // 51 个字排出来约 663 DIP，而主轴预算只给 480 —— 超出 193 DIP。
        // 不滚动的话，被裁掉的正是「还没唱的后半句」，用户看到的是半句歌词。
        //
        // 断言分三层，缺一层都会让「实现没接上」漏过去：
        //   1. 视口确实比内容短（否则下面的位移断言恒等于 0，等于没测）；
        //   2. 位移是负的（确实往前滚了）；
        //   3. 位移等于 ScrollGeometry 的公式值（滚到位了，不是滚了一半）。
        var (viewport, content, offset) = RunOnStaThread(() =>
        {
            var (control, panel) = CreateVerticalLyricsControl();

            return WithScrollingEnabled(true, () =>
            {
                // availableWidth 传 480 而不是默认的 800：主轴长度会被宿主压到
                // 预算以内（TaskbarWidgetControl.CalculateSize 的 Math.Min），
                // 这才是真机上「句子排不下」的成因——不是面板算错了长度，
                // 而是任务栏只肯给这么多。
                var blocks = LayOutLine(control, panel, OversizeLine, availableWidth: 480);

                // 内容长度从字形块量，不用 VerticalContentLength：
                // 后者是「整首最长一列」，在这条用例里恰好等于当前句，
                // 但两个量的语义必须分清（见下一条用例）。
                var last = blocks[^1];
                var content = Canvas.GetLeft(last) + last.Width;

                return (panel.Width, content, panel.VerticalScrollOffset);
            });
        });

        Assert.True(content > 480,
            $"51 个汉字应当排出 600 DIP 以上，实测 {content:F1} DIP——测试数据或字号假设错了。");
        Assert.True(viewport < content - 1,
            $"主轴预算 480、内容 {content:F1} DIP，视口却是 {viewport:F1} DIP——" +
            "宿主没有把主轴长度压到预算以内，这条用例测不到滚动。");

        Assert.True(offset < -1,
            $"进度 0.3 时位移是 {offset:F1} DIP，没有滚动：长句尾部会被 ClipToBounds 吃掉。");

        // 未知时长分支按进度线性推进：offset = −(内容 − 视口 + 10) × 进度。
        // 末尾那 10 DIP 是 ScrollGeometry 的固定余量，两条方向共用同一口径。
        var expected = -((content - viewport) + 10) * 0.3;
        Assert.Equal(expected, offset, precision: 1);
    }

    [Fact]
    public void ShortLineInASongWithLongLinesDoesNotScroll()
    {
        // 滚动长度必须取「当前这一句」，不是 VerticalContentLength。
        //
        // 后者是「整首歌最长的一列」，给宿主定小组件尺寸用（歌内恒定，
        // 否则胶囊随每句伸缩）。如果滚动也读它，那么同专辑里只要有一句
        // 长歌词，这一句 9 个字的短句也会跟着滑——明明一屏放得下。
        const string ShortLine = "被爱的深夜我在想念";

        var (viewport, documentLength, lineLength, offset) = RunOnStaThread(() =>
        {
            var (control, panel) = CreateVerticalLyricsControl();

            return WithScrollingEnabled(true, () =>
            {
                // 文档里放一句长的：小组件的主轴长度因此按 663 DIP 要，
                // 被预算压到 480。当前句仍是最短那句。
                var blocks = LayOutLine(
                    control,
                    panel,
                    ShortLine,
                    document: new[]
                    {
                        new LyricLine(TimeSpan.Zero, ShortLine),
                        new LyricLine(TimeSpan.FromSeconds(4), OversizeLine),
                    },
                    availableWidth: 480);

                // 当前句的实际长度只能从字形块量：VerticalContentLength 在有文档时
                // 返回的是**整首最长一列**（那正是本用例要区分的另一个量）。
                var last = blocks[^1];
                var lineLength = Canvas.GetLeft(last) + last.Width;

                return (panel.Width, panel.VerticalContentLength, lineLength, panel.VerticalScrollOffset);
            });
        });

        // 前提：这首歌确实比视口长，宿主也确实把主轴压到预算以内。
        // 少了这一条，offset == 0 可能只是因为「什么都没排下」。
        Assert.True(documentLength > viewport,
            $"整首歌最长一列 {documentLength:F1} DIP、视口 {viewport:F1} DIP——" +
            "没有超出的歌，这条用例测不出「按整首滚动」这个错误实现。");

        Assert.True(lineLength < viewport - 1,
            $"当前句 {lineLength:F1} DIP、视口 {viewport:F1} DIP——9 个字本来就放得下。");

        Assert.Equal(0, offset, precision: 3);
    }

    [Fact]
    public void ScrollingDisabledLeavesTheColumnAnchored()
    {
        // 设置项默认关闭（UserSettings.TaskbarWidgetScrollingEnabled = false）。
        // 关掉时位移必须恒为 0：用户关这个开关就是不想看到东西在动，
        // 「关掉了但只滚一点点」比不提供开关更让人困惑。
        var offset = RunOnStaThread(() =>
        {
            var (control, panel) = CreateVerticalLyricsControl();

            return WithScrollingEnabled(false, () =>
            {
                LayOutLine(control, panel, OversizeLine, availableWidth: 480);
                return panel.VerticalScrollOffset;
            });
        });

        Assert.Equal(0, offset, precision: 3);
    }

    /// <summary>
    /// 临时改写「长句滚动」设置项，执行完恢复原值。
    /// </summary>
    /// <remarks>
    /// <see cref="SettingsManager.Current"/> 是应用级单例，测试之间共享。
    /// 直接赋值不还原会污染后续用例（尤其截图矩阵里对位移敏感的那几张）。
    ///
    /// <para>
    /// 改这个属性是安全的：<c>UserSettings._initializing</c> 默认为 true，
    /// 而 <c>CompleteInitialization</c> 只在应用启动时由 MainWindow 调用，
    /// 所以变更回调（<c>OnTaskbarWidgetScrollingEnabledChanged</c>）会早退，
    /// 不会去碰 <c>Application.Current.MainWindow</c>。
    /// </para>
    /// </remarks>
    private static void WithScrollingEnabled(bool enabled, Action action)
    {
        var previous = SettingsManager.Current.TaskbarWidgetScrollingEnabled;
        SettingsManager.Current.TaskbarWidgetScrollingEnabled = enabled;

        try
        {
            action();
        }
        finally
        {
            SettingsManager.Current.TaskbarWidgetScrollingEnabled = previous;
        }
    }

    /// <summary>
    /// 同 <see cref="WithScrollingEnabled(bool, Action)"/>，带返回值。
    ///
    /// <para>
    /// 为什么必须整个布局过程都包在里面：滚动开关是在 <c>ApplyFrame</c> 里
    /// 被读的，只包住断言那几行等于开关从来没生效过。
    /// </para>
    /// </summary>
    private static T WithScrollingEnabled<T>(bool enabled, Func<T> action)
    {
        var previous = SettingsManager.Current.TaskbarWidgetScrollingEnabled;
        SettingsManager.Current.TaskbarWidgetScrollingEnabled = enabled;

        try
        {
            return action();
        }
        finally
        {
            SettingsManager.Current.TaskbarWidgetScrollingEnabled = previous;
        }
    }

    /// <summary>
    /// 把一句歌词送进控件并返回竖排建出来的字形块（按主轴顺序）。
    /// </summary>
    /// <remarks>
    /// 顺序必须与生产一致：先排一遍让字体落定 → 给文档 → 给帧 →
    /// <c>CalculateSize</c>（宿主在这里写短边并读长度）→ 再排一遍。
    /// 少了第一次布局，量到的字号来自应用默认字体。
    /// </remarks>
    /// <param name="translation">译文列的内容，null 表示这一句没有译文。</param>
    /// <param name="document">
    /// 整首歌词。null 表示「这首歌只有这一句」。
    ///
    /// <para>
    /// 要区分「当前句长度」与「整首最长句长度」的用例必须显式给：
    /// 宿主的主轴长度按后者定，滚动长度按前者算，两者混了就会
    /// 「短句在长歌里也跟着滑」。
    /// </para>
    /// </param>
    /// <param name="availableWidth">
    /// 任务栏能给的主轴预算（DIP）。默认 800 对一句话总是够用；
    /// 要逼出「内容排不下」必须压到 480 上下——真机上长句被裁就是这个成因。
    /// </param>
    private static List<TextBlock> LayOutLine(
        TaskbarWidgetControl control,
        FrameworkElement panel,
        string line,
        string? translation = null,
        IReadOnlyList<LyricLine>? document = null,
        double availableWidth = 800)
    {
        var host = new Canvas { Width = 48, Height = 1440 };
        host.Children.Add(control);

        control.Width = 240;
        control.Height = DefaultCrossSize;
        control.Measure(new Size(240, DefaultCrossSize));
        control.Arrange(new Rect(0, 0, 240, DefaultCrossSize));
        control.UpdateLayout();

        control.ApplyLyricDocument(document ?? new[] { new LyricLine(TimeSpan.Zero, line) });
        control.UpdateLyricsFrame(new LyricDisplayFrame(
            line,
            string.Empty,
            "标题",
            0.3,
            CurrentTranslation: translation));

        var (logicalWidth, logicalHeight) = control.CalculateSize(dpiScale: 1.0, availableWidth: availableWidth);
        control.Width = logicalWidth;
        control.Height = logicalHeight;
        control.Measure(new Size(logicalWidth, logicalHeight));
        control.Arrange(new Rect(0, 0, logicalWidth, logicalHeight));
        control.UpdateLayout();

        var canvas = (Canvas)panel.FindName("VerticalContainer");
        return canvas.Children
            .OfType<TextBlock>()
            .OrderBy(Canvas.GetLeft)
            .ToList();
    }

    private static bool IsCjk(string text) =>
        text.Length > 0 && text.Any(c => c >= '\u3000' && c <= '\u9FFF');

    /// <summary>
    /// 造一个「竖向 + 歌词模式」的控件。
    /// </summary>
    /// <remarks>
    /// 顺序刻意与生产一致：<c>SetLyricsMode</c> → <c>SetVerticalMode</c> → <c>CalculateSize</c>
    /// （<c>TaskbarWindow.PositionWidget</c> 就是先切方向、再问尺寸）。
    /// </remarks>
    private static (TaskbarWidgetControl Control, LyricsPanel Panel) CreateVerticalLyricsControl()
    {
        var control = new TaskbarWidgetControl();
        control.SetLyricsMode(true);
        control.SetVerticalMode(true);

        var panel = control.FindName("LyricsPanel") as LyricsPanel;
        Assert.NotNull(panel);

        return (control, panel);
    }

    /// <summary>
    /// 在共享的 STA 线程上跑一段 WPF 代码并把结果带回来。
    /// </summary>
    /// <remarks>
    /// xunit 的测试线程是 MTA，WPF 控件必须 STA；这里不引第三方 STA 扩展包，
    /// 手工起线程反而更清楚。异常要显式捕获后重抛——否则会变成
    /// 「测试通过但什么都没跑」。
    /// </remarks>
    private static T RunOnStaThread<T>(Func<T> action) => WpfStaHost.Invoke(action);
}

/// <summary>
/// 测试进程内唯一的 WPF 宿主线程。
/// </summary>
/// <remarks>
/// <para>
/// 两个硬约束决定了它必须是「一个线程 + 一个 Application」而不是每个用例各起一个：
/// </para>
/// <list type="number">
/// <item>同一个 AppDomain 里只能有一个 <see cref="Application"/> 实例，
/// 第二次 <c>new Application()</c> 会直接抛异常。</item>
/// <item><see cref="Application.Current"/> 是线程亲和的，
/// 资源查找必须在创建它的那个线程上做。</item>
/// </list>
/// <para>
/// <c>TaskbarWidgetControl.xaml</c> 里有一处
/// <c>Style="{StaticResource UiContextMenu}"</c>，这个键来自 WPF-UI 的
/// <c>ControlsDictionary</c>。测试里没有真实的 App.xaml，所以补一个等价的
/// 最小 <see cref="ContextMenu"/> 样式——本测试只关心尺寸数值，
/// 不关心右键菜单长什么样。
/// </para>
/// </remarks>
internal static class WpfStaHost
{
    private static readonly Lazy<Dispatcher> HostDispatcher = new(Start, LazyThreadSafetyMode.ExecutionAndPublication);

    public static T Invoke<T>(Func<T> action) => HostDispatcher.Value.Invoke(action);

    private static Dispatcher Start()
    {
        Dispatcher? dispatcher = null;
        Exception? startupFailure = null;
        using var ready = new ManualResetEventSlim(false);

        var thread = new Thread(() =>
        {
            try
            {
                var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                application.Resources["UiContextMenu"] = new Style(typeof(ContextMenu));
                dispatcher = Dispatcher.CurrentDispatcher;
            }
            catch (Exception ex)
            {
                startupFailure = ex;
            }
            finally
            {
                ready.Set();
            }

            if (startupFailure is null)
            {
                Dispatcher.Run();
            }
        })
        {
            IsBackground = true,
            Name = "WPF test host",
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait(TimeSpan.FromSeconds(30));

        if (startupFailure is not null)
        {
            throw new InvalidOperationException("初始化 WPF 测试宿主失败。", startupFailure);
        }

        return dispatcher ?? throw new InvalidOperationException("WPF 测试宿主没有返回 Dispatcher。");
    }
}
