// Copyright (c) 2026 anync
// SPDX-License-Identifier: MIT
//
// 本文件为本仓库新增，不源自 TaskbarLyrics。

using FluentFlyout.Lyrics.Layout;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FluentFlyout.Controls;

/// <summary>
/// 竖排（任务栏在屏幕左/右）的逐字排版。
///
/// <para>
/// <b>为什么不能沿用「抵消旋转 + 换行」。</b>那个方案让字形正立，靠的是
/// 给整个面板挂 <c>LayoutTransform(-90)</c> 把任务栏的 <c>+90</c> 抵掉，
/// 于是面板内部又回到横向排版，只能靠 <see cref="TextWrapping.Wrap"/> 折行。
/// 任务栏短边只有 40 DIP，抵消后一行只剩 37.5 DIP——**两个汉字**。
/// 一句 28 字的歌词要折 14 行，屏幕上就是一串两字一行的碎块。
/// 这也是「竖向歌词只显示 6 个字」那个缺陷的根源。
/// </para>
///
/// <para>
/// <b>竖排的容量。</b>逐字沿主轴排，一列能装 <c>578 ÷ 13 = 44</c> 个字，
/// 整句一列到底；两列刚好是「原文 + 译文」。这多出来的二十多倍容量，
/// 就是逐字排版唯一但足够充分的理由。
/// </para>
///
/// <para>
/// <b>坐标系。</b><c>TaskbarWindow.PositionWidget</c> 给整个控件挂
/// <c>LayoutTransform(+90)</c>，所以本面板的逻辑 <b>x 轴在屏幕上是向下的</b>、
/// 逻辑 <b>y 轴在屏幕上是向左的</b>。本文所有「主轴」「短边」都指面板坐标：
/// 主轴 = 面板 x = 屏幕纵向，短边 = 面板 y = 屏幕横向。
/// 面板尺寸也由宿主按这个含义写入：<c>Width</c> = 主轴长度、
/// <c>Height</c> = 可用短边（见 <c>TaskbarWidgetControl.CalculateSize</c>）。
/// </para>
///
/// <para>
/// <b>朝向怎么来的。</b>每个字形是一个独立的 <see cref="TextBlock"/>，
/// 方块字（汉字/假名/谚文/标点/emoji）加
/// <c>RenderTransform = RotateTransform(-90)</c> 把宿主的 +90 抵掉，
/// 屏幕上看起来是正的；拉丁词**整词不转**，沿列向下侧躺读（歪头），
/// 与 CSS <c>text-orientation: mixed</c> 同一做法——逐字母正立堆叠会把
/// <c>screen</c> 变成六个字高的竖塔，可读性归零。
/// </para>
///
/// <para>
/// <b>两列是硬约束，不是偏好。</b>列距 = 行距 = <c>1.33 × 字号</c>，
/// 两列要 <c>2.66 × 字号</c>：字号 13 时 34.6 DIP ≤ 37.5 DIP（默认字号刚好），
/// 字号 16 时 42.6 DIP 就超了，只能留主列。这不是 bug，是竖排的几何事实
/// （探针实测：字号 13 / 列距 17.29 / 一列 44 字 / 两列横跨 24.7）。
/// </para>
/// </summary>
public partial class LyricsPanel
{
    /// <summary>
    /// 列距系数：一列占用的短边宽度 = 字号 × 它。
    ///
    /// 取 1.33 与排版惯例的 <c>line-height: 1.33</c> 一致，也正好等于
    /// 默认字体栈的行高（Segoe UI + 微软雅黑的后备链）。这个数同时决定
    /// 「两列放不放得下」——见类注释里的容量账。
    /// </summary>
    public const double VerticalColumnPitchFactor = 1.33;

    /// <summary>
    /// 所有正立字形共用的反向旋转。
    ///
    /// 一列最多几十个字，每个字都 new 一个 <see cref="RotateTransform"/> 会让
    /// 每次换行多出几十个 Freezable；冻结后共享同一个实例，WPF 只维护一份。
    /// </summary>
    private static readonly RotateTransform UprightRotation = CreateUprightRotation();

    /// <summary>竖排里属于主行的字形块。配色变化要跟着重涂，所以留引用。</summary>
    private readonly List<TextBlock> _verticalPrimaryBlocks = new();

    /// <summary>竖排里属于次要行的字形块（译文或下一行预览）。</summary>
    private readonly List<TextBlock> _verticalSecondaryBlocks = new();

    /// <summary>
    /// 上一次建树时的输入。与当前输入逐项相等就跳过重建。
    ///
    /// <para>
    /// 为什么需要它：宿主在 <c>CalculateSize</c> 里会读
    /// <see cref="VerticalContentLength"/>，而该方法由定时器、换歌、
    /// 每一次换行驱动，一秒可能来好几次；<c>SizeChanged</c> 也会来
    /// （宿主写主轴长度就会触发）。建一次树是 50 个 <see cref="TextBlock"/>、
    /// 实测约 3ms，不能承受每轮重来。
    /// </para>
    /// <para>
    /// 也刻意**不用** <c>bool _dirty</c>：置脏的点有六七处（换文本、换字号、
    /// 换短边、关翻译、切方向、清空……），漏一处就是「歌词停在上一句」，
    /// 而这种缺陷在截图上看不出来。拿输入当键，漏置脏也不会漏重建。
    /// </para>
    /// </summary>
    private VerticalBuildKey? _verticalKey;

    /// <summary>最近一次建树算出的主轴长度（DIP）。0 表示无内容。</summary>
    private double _verticalContentLength;

    /// <summary>已经为「短边还没写入」打过日志。只打一次，避免每轮布局刷屏。</summary>
    private bool _verticalCrossWarned;

    /// <summary>已经为「两列放不下、译文被丢掉」打过日志（按字号去重）。</summary>
    private double _verticalDropWarnedFontSize = -1;

    /// <summary>
    /// 竖排内容沿主轴（屏幕纵向）需要的长度（DIP），供宿主决定小组件占多长。
    ///
    /// <para>
    /// 与横向的 <see cref="DesiredContentWidth"/> 对称：横向协商宽度、
    /// 竖向协商长度，两个方向各有一个「内容要多大」的出口。
    /// </para>
    /// <para>
    /// 返回 0 表示「不在竖向」「还没度量」或「短边还没由宿主写入」——
    /// 都不是错误，调用方应退回自己的兜底值（通常是「先占满可用区间」）。
    /// </para>
    /// <para>
    /// 会按需建树（见 <see cref="EnsureVerticalLayout"/>），但**不写面板尺寸**：
    /// 尺寸只能由宿主给，否则「按内容定长度 → 长度又反过来影响内容」会自证。
    /// </para>
    /// </summary>
    public double VerticalContentLength
    {
        get
        {
            if (!_isVertical || !_hasMetrics)
            {
                return 0;
            }

            EnsureVerticalLayout();
            return _verticalContentLength;
        }
    }

    /// <summary>
    /// 按当前文本、字号、可用短边重建竖排字形树（输入没变则什么都不做）。
    ///
    /// <b>不读 <c>ActualWidth</c>/<c>ActualHeight</c> 决定内容</b>：短边取
    /// <see cref="FrameworkElement.Height"/>（宿主写的显式值），主轴长度根本
    /// 不参与排版——字形是从 x = 0 顺着排过去的，长度是结果而不是输入。
    /// 这一点让「宿主读长度定尺寸、尺寸又改变长度」的循环不可能成立。
    /// </summary>
    private void EnsureVerticalLayout()
    {
        if (!_isVertical || !_hasMetrics)
        {
            return;
        }

        var cross = ResolveVerticalCross();
        if (cross <= 0)
        {
            // 宿主还没写短边。真机上这应当只出现一次，紧跟着 SizeChanged
            // 带来的第二次调用；若持续打印，说明宿主没写 Height。
            if (!_verticalCrossWarned)
            {
                _verticalCrossWarned = true;
                Logger.Debug(
                    "Vertical lyrics: cross size is {0} (panel ActualHeight={1}, Height={2}); delaying glyph layout until the host writes a finite height.",
                    cross, ActualHeight, Height);
            }

            return;
        }

        _verticalCrossWarned = false;

        var primary = PrimaryText.Text ?? string.Empty;
        var secondary = ShouldShowVerticalSecondary() ? SecondaryText.Text ?? string.Empty : string.Empty;
        var primaryFontSize = PrimaryText.FontSize;
        var secondaryFontSize = SecondaryText.FontSize;

        var key = new VerticalBuildKey(primary, secondary, primaryFontSize, secondaryFontSize, cross);
        if (_verticalKey == key)
        {
            return;
        }

        _verticalKey = key;
        _verticalContentLength = BuildVerticalTree(
            primary, secondary, primaryFontSize, secondaryFontSize, cross);
    }

    /// <summary>
    /// 次要行在竖排里该不该占一列。
    ///
    /// 判据取「容器可见性」而不是「帧里有没有译文」：显示什么（译文 / 下一行预览）
    /// 由 <see cref="UpdateSecondaryLine"/> 决定，可见性就是它的结论；
    /// 再看一眼 <see cref="LyricsPanelMetrics.IsSingleLine"/> 兜住
    /// 「度量切成单行、但这一帧还没到」的空窗。
    /// </summary>
    private bool ShouldShowVerticalSecondary() =>
        !_metrics.IsSingleLine &&
        SecondaryContainer.Visibility == Visibility.Visible &&
        !string.IsNullOrWhiteSpace(SecondaryText.Text);

    /// <summary>
    /// 可用短边（DIP）：面板的显式 <c>Height</c>，还没布局时退回实际高度。
    ///
    /// 顺序不能反：宿主在 <c>CalculateSize</c> 里写 <c>Height</c> 之后立刻就要读
    /// <see cref="VerticalContentLength"/>，这时 <c>ActualHeight</c> 还是上一轮的
    /// 旧值（布局遍还没跑），读它会按旧短边决定列数。
    /// </summary>
    private double ResolveVerticalCross()
    {
        var height = Height;

        if (!double.IsFinite(height) || height <= 0)
        {
            height = ActualHeight;
        }

        return double.IsFinite(height) && height > 0 ? height : 0;
    }

    /// <summary>
    /// 建字形树，返回内容沿主轴需要的长度（DIP）。
    ///
    /// 列的位置只有两处决策：
    /// <list type="number">
    /// <item><b>几列</b>：译文非空且两列列距之和放得进短边 → 两列，否则一列；
    /// 放不下时丢掉译文而不是压字号，理由见 <see cref="VerticalColumnPitchFactor"/>。</item>
    /// <item><b>哪列在哪边</b>：主列在短边的小 y 一侧，译文在大 y 一侧。
    /// 面板 +y 映射到屏幕 −x，所以主列落在屏幕上靠右，译文在其左——
    /// 与中文竖排「先读右列」的顺序一致。</item>
    /// </list>
    /// </summary>
    private double BuildVerticalTree(
        string primary,
        string secondary,
        double primaryFontSize,
        double secondaryFontSize,
        double cross)
    {
        VerticalContainer.Children.Clear();
        _verticalPrimaryBlocks.Clear();
        _verticalSecondaryBlocks.Clear();

        var primaryPitch = ColumnPitch(primaryFontSize);
        var secondaryPitch = ColumnPitch(secondaryFontSize);
        var twoColumns = secondary.Length > 0 && primaryPitch + secondaryPitch <= cross;

        if (secondary.Length > 0 && !twoColumns && Math.Abs(_verticalDropWarnedFontSize - primaryFontSize) > 0.01)
        {
            _verticalDropWarnedFontSize = primaryFontSize;
            Logger.Debug(
                "Vertical lyrics: two columns need {0:F1} DIP but only {1:F1} is available (font size {2:F1}); showing the primary line only.",
                primaryPitch + secondaryPitch, cross, primaryFontSize);
        }

        var totalCross = twoColumns ? primaryPitch + secondaryPitch : primaryPitch;

        // 两列的余量通常只有 2.9 DIP（字号 13 时 34.6 / 37.5），居中与否
        // 肉眼几乎看不出差别；真正需要它的是「单列 + 字号 19.5」那种极端，
        // 不居中会贴着短边的一侧、与任务栏边缘打架。
        var top = Math.Max((cross - totalCross) / 2, 0);

        var primaryLength = BuildVerticalColumn(
            primary, primaryFontSize, primaryPitch, top, isPrimary: true);

        var secondaryLength = twoColumns
            ? BuildVerticalColumn(
                secondary, secondaryFontSize, secondaryPitch, top + primaryPitch, isPrimary: false)
            : 0;

        return Math.Max(primaryLength, secondaryLength);
    }

    /// <summary>
    /// 排一列，返回这一列占用的主轴长度（DIP）。
    /// </summary>
    /// <param name="pitch">列距，也是每个字形块的短边尺寸。</param>
    /// <param name="top">这一列在短边方向的起点。</param>
    private double BuildVerticalColumn(
        string text,
        double fontSize,
        double pitch,
        double top,
        bool isPrimary)
    {
        var blocks = isPrimary ? _verticalPrimaryBlocks : _verticalSecondaryBlocks;
        var source = isPrimary ? PrimaryText : SecondaryText;
        var runs = VerticalRunSplitter.Split(text);
        var x = 0.0;

        foreach (var run in runs)
        {
            // 每格的步进按**自己的实测宽度**算，不用字号：
            // 汉字是 1em，空格只有 0.33em，标点各有各的宽度。
            // 一律按字号算会让空格变成一个大豁口。
            var advance = MeasureText(run.Text, fontSize, source.FontWeight);

            if (advance <= 0)
            {
                // 量不出来（零宽字符、字体缺字）也别让后继字形叠在一起。
                advance = fontSize * 0.2;
            }

            var block = new TextBlock
            {
                Text = run.Text,
                FontFamily = source.FontFamily,
                FontSize = fontSize,
                FontStyle = source.FontStyle,
                FontWeight = source.FontWeight,
                FontStretch = source.FontStretch,
                Foreground = source.Foreground,

                // 主行取常量的 0.90 而不是读 PrimaryText.Opacity：
                // 逐字扫描会把主行压到 0.60，而竖排这一轮没有逐字扫描，
                // 照抄那个瞬间值会让整列在扫描开始后莫名变暗。
                Opacity = isPrimary ? LyricsPanelMetrics.PrimaryOpacity : source.Opacity,

                // 宽度 = 沿主轴的步进，高度 = 列距。写死而不是靠 Auto：
                // Canvas 给子元素的是无限约束，Auto 会让每个 TextBlock
                // 按自己的内容撑开，步进就失控了。
                Width = advance,
                Height = pitch,

                // 行框撑满格高：字形在格内纵向居中，转 90° 后才会落在
                // 列中线附近。不设它，字会贴着格子的一侧排成锯齿。
                LineHeight = pitch,
                LineStackingStrategy = LineStackingStrategy.BlockLineHeight,

                // 格内横向居中：转 90° 后这一维变成「沿主轴」，
                // 居中可以吸收不同字形的宽度差（数字窄、汉字满格）。
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.NoWrap,

                // 字形块不参与命中测试：点击要落到窗口上，不能被几十个
                // TextBlock 拦掉（横向的 PrimaryText 同理，没有交互语义）。
                IsHitTestVisible = false,
            };

            // 文本管线与横向那个 TextBlock 保持一致：Display/Ideal 的字形步进
            // 不同，混用会让实测宽度与实际绘制差出半个字。
            TextOptions.SetTextFormattingMode(block, TextOptions.GetTextFormattingMode(source));
            TextOptions.SetTextRenderingMode(block, TextOptions.GetTextRenderingMode(source));

            if (run.Upright)
            {
                // 绕自身中心反向旋转：抵消宿主的 +90，屏幕上正立。
                // 用 RenderTransform 而不是 LayoutTransform——后者会改变
                // 布局占位（宽高互换），把「宽 = 步进、高 = 列距」的设计打乱。
                block.RenderTransformOrigin = new Point(0.5, 0.5);
                block.RenderTransform = UprightRotation;
            }

            Canvas.SetLeft(block, x);
            Canvas.SetTop(block, top);
            VerticalContainer.Children.Add(block);
            blocks.Add(block);

            x += advance;
        }

        return x;
    }

    /// <summary>列距：一列占用的短边宽度（DIP）。</summary>
    private static double ColumnPitch(double fontSize) => fontSize * VerticalColumnPitchFactor;

    /// <summary>
    /// 把配色同步到已经建好的字形块上。
    ///
    /// <see cref="ApplyPalette"/> 换的是 <c>PrimaryText.Foreground</c> 那个
    /// <see cref="SolidColorBrush"/> 实例，字形块手里还是旧画笔的引用，
    /// 不重涂的话切主题后竖排会停在旧颜色上（横向没问题，它直接读属性）。
    /// </summary>
    private void SyncVerticalPalette()
    {
        var primary = PrimaryText.Foreground;
        foreach (var block in _verticalPrimaryBlocks)
        {
            block.Foreground = primary;
        }

        var secondary = SecondaryText.Foreground;
        foreach (var block in _verticalSecondaryBlocks)
        {
            block.Foreground = secondary;
        }
    }

    /// <summary>
    /// 清空竖排字形树。切回横向方向、或清空内容时调用。
    /// </summary>
    private void ResetVerticalTree()
    {
        VerticalContainer.Children.Clear();
        _verticalPrimaryBlocks.Clear();
        _verticalSecondaryBlocks.Clear();
        _verticalKey = null;
        _verticalContentLength = 0;
    }

    private static RotateTransform CreateUprightRotation()
    {
        var transform = new RotateTransform(-90);
        transform.Freeze();
        return transform;
    }

    /// <summary>
    /// 建树输入。任何一项变了都要重排，其余情况一律跳过。
    /// </summary>
    /// <param name="Cross">
    /// 可用短边。它同时决定「两列放不放得下」与列在短边方向的位置，
    /// 所以尺寸变化必须触发重建——文件类注释里那笔两列账就是按它算的。
    /// </param>
    private readonly record struct VerticalBuildKey(
        string Primary,
        string Secondary,
        double PrimaryFontSize,
        double SecondaryFontSize,
        double Cross);
}