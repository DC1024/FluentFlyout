// Copyright (c) 2026 anync
// SPDX-License-Identifier: MIT
//
// 本文件为本仓库新增，不源自 TaskbarLyrics。

using FluentFlyout.Lyrics.Layout;
using FluentFlyout.Lyrics.Models;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FluentFlyout.Controls;

/// <summary>
/// 任务栏上的歌词面板：双行（当前行 + 翻译/下一行），支持逐字扫描高亮。
///
/// 设计约束（来自 P3 落点测绘，见 docs/lyrics-integration/p3-landing-points.md）：
///
/// 1. **每帧不能触发重排**。任务栏小组件的一次重排要过
///    <c>SetWindowPos</c> + UIA 跨进程查询 + <c>SetWindowRgn</c>，
///    30fps 走这条路会卡死任务栏。所以帧更新只改 <c>Text</c>、
///    <c>OpacityMask</c> 的 <c>Offset</c>、少量 <c>Foreground</c>，
///    绝不改 <c>Width</c>/<c>Height</c>/<c>Margin</c>。
///
/// 2. **逐字扫描不重建画笔**。<see cref="LinearGradientBrush"/> 与它的
///    <c>GradientStop</c> 在构造时创建一次，之后每帧只改两个 <c>Offset</c>。
///    重建对象会让 WPF 每帧重新解析画刷，实测是掉帧的主因。
///
/// 3. **尺寸由外部给**。面板不自己算宽度——宽度取决于任务栏可用区间，
///    那是 <c>TaskbarWindow</c> 的职责。这里只接受
///    <see cref="ApplyMetrics"/> 传进来的度量。
/// </summary>
public partial class LyricsPanel : UserControl
{
    /// <summary>
    /// 扫描渐变的羽化宽度比例（相对单行文本宽度）。
    ///
    /// 0.12 取自 TL 的 <c>--word-scan-feather-width: 0.12em</c>，
    /// 换算成「相对行宽」后视觉上接近。太小会看到硬边，太大会糊成一片。
    /// </summary>
    private const double ScanFeatherRatio = 0.12;

    /// <summary>羽化宽度的上下限（DIP），避免极短/极长句子下比例失真。</summary>
    private const double MinFeatherWidth = 2;
    private const double MaxFeatherWidth = 24;

    private readonly LinearGradientBrush _scanMask;

    private LyricDisplayFrame? _frame;
    private LyricsPanelMetrics _metrics;
    private bool _hasMetrics;

    /// <summary>当前是否处于逐字扫描态（帧里带 WordScanProgress）。</summary>
    private bool _isScanning;

    /// <summary>最近一次设置的扫描进度，避免重复写同一批 Offset。</summary>
    private double _lastScanProgress = -1;

    /// <summary>
    /// 面板内容宽度变化。仅在文本真的换行且宽度差超过阈值时触发，
    /// 宿主据此决定是否请求一次任务栏重排（并自行节流）。
    /// </summary>
    public event EventHandler<double>? DesiredWidthChanged;

    /// <summary>
    /// 触发重排的宽度差阈值（DIP）。小于这个值不值得动窗口——
    /// 用户看不出差别，但一次重排的代价是几十毫秒。
    /// </summary>
    public const double WidthChangeThreshold = 4;

    private double _lastReportedWidth = -1;

    public LyricsPanel()
    {
        InitializeComponent();

        // 扫描遮罩构造一次，之后只改 Offset。
        // MappingMode=Absolute 让 Offset 直接是像素值，
        // 不用操心 RelativeToBoundingBox 下「进度 × 宽度」的换算误差。
        _scanMask = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 0),
            MappingMode = BrushMappingMode.Absolute,
            SpreadMethod = GradientSpreadMethod.Pad
        };

        // 三段式：已唱部分全不透明 → 羽化 → 未唱部分全透明。
        _scanMask.GradientStops.Add(new GradientStop(Color.FromArgb(255, 255, 255, 255), 0));
        _scanMask.GradientStops.Add(new GradientStop(Color.FromArgb(255, 255, 255, 255), 0));
        _scanMask.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 1));

        PrimaryScanText.OpacityMask = _scanMask;

        // 面板默认不可见，由宿主在切到歌词模式时打开。
        // 走 UpdateEffectiveVisibility 而不是直接写 Visibility，
        // 保证可见性只有一个来源。
        UpdateEffectiveVisibility();
    }

    /// <summary>当前是否显示歌词。宿主据此决定是否显示标题/艺术家。</summary>
    public bool IsActive => Visibility == Visibility.Visible;

    /// <summary>当前度量，供宿主查询所需宽度/高度。</summary>
    public LyricsPanelMetrics Metrics => _metrics;

    /// <summary>
    /// 应用度量。会调整字号与行高，<b>因此会触发一次重排</b>——
    /// 只应在尺寸/字号/DPI 变化时调用，不要放进帧循环。
    /// </summary>
    public void ApplyMetrics(LyricsPanelMetrics metrics)
    {
        _metrics = metrics;
        _hasMetrics = true;

        PrimaryText.FontSize = metrics.FontSize;
        PrimaryScanText.FontSize = metrics.FontSize;
        SecondaryText.FontSize = metrics.TranslationFontSize;

        PrimaryContainer.Height = metrics.LineHeight;
        SecondaryContainer.Height = metrics.LineHeight;

        if (metrics.IsSingleLine)
        {
            SecondaryRow.Height = new GridLength(0);
            SecondaryContainer.Visibility = Visibility.Collapsed;
        }
        else
        {
            SecondaryRow.Height = new GridLength(metrics.LineHeight);
            SecondaryContainer.Margin = new Thickness(0, metrics.Gap, 0, 0);
        }

        // 度量变了，之前记的宽度作废，下次帧更新会重新上报。
        _lastReportedWidth = -1;
    }

    /// <summary>
    /// 应用一帧歌词。这是热路径——每帧最多调用 30 次。
    /// </summary>
    /// <remarks>
    /// 刻意不调用 <see cref="ApplyMetrics"/>：度量只跟尺寸/字号有关，
    /// 跟歌词内容无关。把两者混在一起是「每帧重排」的根源。
    /// </remarks>
    public void ApplyFrame(LyricDisplayFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        _frame = frame;

        var primary = frame.CurrentLine ?? string.Empty;
        if (!string.Equals(PrimaryText.Text, primary, StringComparison.Ordinal))
        {
            PrimaryText.Text = primary;
            PrimaryScanText.Text = primary;
        }

        UpdateSecondaryLine(frame);
        UpdateScan(frame);

        ReportDesiredWidth();
    }

    /// <summary>
    /// 决定次要行显示什么：
    /// - 单行模式 → 不显示；
    /// - 有翻译 → 显示当前行的翻译（优先级最高，用户开翻译就是想看它）；
    /// - 无翻译 → 显示下一行预览，用次要色区分。
    /// </summary>
    private void UpdateSecondaryLine(LyricDisplayFrame frame)
    {
        if (_hasMetrics && _metrics.IsSingleLine)
        {
            SecondaryText.Text = string.Empty;
            SecondaryContainer.Visibility = Visibility.Collapsed;
            return;
        }

        var translation = frame.CurrentTranslation;
        if (!string.IsNullOrWhiteSpace(translation))
        {
            if (!string.Equals(SecondaryText.Text, translation, StringComparison.Ordinal))
            {
                SecondaryText.Text = translation;
            }

            // 翻译行用更低的透明度，与 TL 的 --translation: 0.70 一致。
            SecondaryText.Opacity = LyricsPanelMetrics.TranslationOpacity;
            SecondaryContainer.Visibility = Visibility.Visible;
            return;
        }

        var next = frame.NextLine ?? string.Empty;
        if (string.IsNullOrWhiteSpace(next))
        {
            SecondaryText.Text = string.Empty;
            SecondaryContainer.Visibility = Visibility.Collapsed;
            return;
        }

        if (!string.Equals(SecondaryText.Text, next, StringComparison.Ordinal))
        {
            SecondaryText.Text = next;
        }

        // 下一行预览比翻译更淡：它只是「预告」，不是当前内容。
        SecondaryText.Opacity = LyricsPanelMetrics.SecondaryOpacity;
        SecondaryContainer.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// 更新逐字扫描。
    ///
    /// <c>WordScanProgress</c> 为 null 表示这首歌没有音节数据
    /// （<c>LyricSyncService.CalculateWordScanProgress</c> 在
    /// <c>!hasUsableSyllable</c> 时直接返回 null）。
    /// 此时退回整行高亮——把覆盖层藏起来，主行用全亮度显示。
    /// </summary>
    private void UpdateScan(LyricDisplayFrame frame)
    {
        if (frame.WordScanProgress is not { } progress || progress <= 0)
        {
            if (_isScanning)
            {
                PrimaryScanText.Visibility = Visibility.Collapsed;
                PrimaryText.Opacity = LyricsPanelMetrics.PrimaryOpacity;
                _isScanning = false;
                _lastScanProgress = -1;
            }

            return;
        }

        _isScanning = true;
        PrimaryScanText.Visibility = Visibility.Visible;

        // 底层文字压暗，让覆盖层扫过的部分显得更亮。
        PrimaryText.Opacity = LyricsPanelMetrics.SecondaryOpacity;

        SetScanProgress(Math.Clamp(progress, 0, 1));
    }

    /// <summary>
    /// 设置扫描进度。只改三个 <c>GradientStop.Offset</c>，不碰任何布局属性。
    /// </summary>
    private void SetScanProgress(double progress)
    {
        // 1% 以下的抖动直接忽略：与帧去重器的量化粒度一致，
        // 避免两边互相「追」导致多做一次无意义的绘制。
        if (Math.Abs(progress - _lastScanProgress) < 0.01)
        {
            return;
        }

        _lastScanProgress = progress;

        var width = PrimaryContainer.ActualWidth;
        if (!double.IsFinite(width) || width <= 0)
        {
            // 还没布局完成，先按 1 处理；下一次帧更新会带来正确宽度。
            width = 1;
        }

        var scanX = progress * width;
        var feather = Math.Clamp(width * ScanFeatherRatio, MinFeatherWidth, MaxFeatherWidth);

        // 羽化不能超出扫描点两端，否则短句上会看到「整行半透明」。
        feather = Math.Min(feather, Math.Min(scanX, width - scanX));

        _scanMask.StartPoint = new Point(0, 0);
        _scanMask.EndPoint = new Point(width, 0);

        _scanMask.GradientStops[0].Offset = 0;
        _scanMask.GradientStops[1].Offset = Math.Max(0, scanX - feather);
        _scanMask.GradientStops[2].Offset = Math.Min(width, scanX + feather);
    }

    /// <summary>
    /// 按当前文本宽度上报「希望多宽」。
    ///
    /// 宽度只在离散信号变化后才可能变（换行、换翻译），
    /// 所以这里用阈值过滤，避免长句滚动/逐字扫描期间反复请求重排。
    /// </summary>
    private void ReportDesiredWidth()
    {
        var desired = MeasureDesiredWidth();
        if (desired <= 0)
        {
            return;
        }

        if (_lastReportedWidth < 0 ||
            Math.Abs(desired - _lastReportedWidth) >= WidthChangeThreshold)
        {
            _lastReportedWidth = desired;
            DesiredWidthChanged?.Invoke(this, desired);
        }
    }

    /// <summary>
    /// 量出当前内容需要的宽度（取两行的最大值）。
    ///
    /// 用 <see cref="FormattedText"/> 而不是 <c>TextBlock.ActualWidth</c>：
    /// 后者在文本还没布局时是 0，而我们需要在「决定要不要重排」的
    /// 那一刻就知道宽度。
    /// </summary>
    public double MeasureDesiredWidth()
    {
        if (!_hasMetrics)
        {
            return 0;
        }

        var primaryWidth = MeasureText(PrimaryText.Text, _metrics.FontSize);
        var secondaryWidth = _metrics.IsSingleLine
            ? 0
            : MeasureText(SecondaryText.Text, _metrics.TranslationFontSize);

        return Math.Max(primaryWidth, secondaryWidth);
    }

    private double MeasureText(string? text, double fontSize)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        try
        {
            var typeface = new Typeface(
                PrimaryText.FontFamily,
                PrimaryText.FontStyle,
                PrimaryText.FontWeight,
                PrimaryText.FontStretch);

            var formatted = new FormattedText(
                text,
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection,
                typeface,
                fontSize,
                Brushes.Black,
                null,
                1);

            return formatted.Width;
        }
        catch (Exception)
        {
            // 量不出来就报 0，让宿主保持当前宽度——
            // 绝不能因为一次测量失败就把面板宽度设成 0。
            return 0;
        }
    }

    /// <summary>
    /// 应用配色。颜色必须来自系统主题（<c>WindowsThemeDetector</c>），
    /// 不能用 App 主题——任务栏配色跟系统走。
    /// </summary>
    public void ApplyPalette(Color primary, Color secondary, Color translation, Color scanOverlay)
    {
        PrimaryText.Foreground = new SolidColorBrush(primary);
        PrimaryScanText.Foreground = new SolidColorBrush(scanOverlay);
        SecondaryText.Foreground = new SolidColorBrush(translation);
    }

    /// <summary>
    /// 按系统主题设置配色。
    ///
    /// 扫描覆盖层刻意比主行更亮（而不是同色）：逐字扫描的本质是
    /// 「已唱部分比未唱部分亮」，同色的话覆盖层完全看不出来。
    /// 这与 TL 的 <c>--word-scan-overlay: rgba(255,255,255,0.75)</c>
    /// 相对 <c>--primary: 0.90</c> 的关系相反——TL 是覆盖层更暗，
    /// 因为它底层用 <c>--secondary</c> 压暗。这里底层已经压到 0.60，
    /// 覆盖层取 1.0 才能形成对比。
    /// </summary>
    public void ApplyWindowsTheme(bool isDark)
    {
        if (isDark)
        {
            ApplyPalette(
                primary: Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF),
                secondary: Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF),
                translation: Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF),
                scanOverlay: Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF));
        }
        else
        {
            // 浅色任务栏：用接近黑的文字。0xE4 而不是纯黑，
            // 与 TaskbarWidgetControl.ApplyWindowsTheme 的标题色保持一致。
            var foreground = Color.FromArgb(0xE4, 0x1C, 0x1C, 0x1C);
            ApplyPalette(
                primary: foreground,
                secondary: foreground,
                translation: foreground,
                scanOverlay: Color.FromArgb(0xFF, 0x00, 0x00, 0x00));
        }
    }

    /// <summary>
    /// 切换竖向任务栏排版。
    ///
    /// **必须用 <c>LayoutTransform</c> 而不是 <c>RenderTransform</c>**：
    /// 后者不参与布局，父级仍按未旋转的尺寸给面板排版，非正方形的
    /// 歌词区会直接叠在封面上。现有封面的反向旋转用的是
    /// <c>RenderTransform</c>，那是因为 36×36 正方形旋转后包围盒不变，
    /// 歌词区不适用这个前提。
    ///
    /// 竖向路径**默认关闭**（见 <see cref="SupportsVerticalTaskbar"/>）：
    /// FluentFlyout 今天在竖向任务栏上根本不显示文字，
    /// 打开它是行为变更，需要 P3-12 单独验收。
    /// </summary>
    public void SetVerticalMode(bool isVertical)
    {
        if (isVertical && !SupportsVerticalTaskbar)
        {
            // 开关关着：竖向任务栏上不显示歌词，行为与改动前一致。
            _isVerticalPending = true;
            UpdateEffectiveVisibility();
            return;
        }

        _isVerticalPending = false;
        _isVertical = isVertical;

        if (isVertical)
        {
            // 抵消 Widget 的 RotateTransform(90)，让字形正立。
            // 只在这里设一次——LayoutTransform 会触发 measure 遍，
            // 放进帧循环就是每帧一次重排。
            var counterRotate = new RotateTransform(-90);
            RootGrid.LayoutTransform = counterRotate;
            RootGrid.HorizontalAlignment = HorizontalAlignment.Left;
            RootGrid.VerticalAlignment = VerticalAlignment.Top;
        }
        else
        {
            RootGrid.LayoutTransform = Transform.Identity;
            RootGrid.HorizontalAlignment = HorizontalAlignment.Left;
            RootGrid.VerticalAlignment = VerticalAlignment.Center;
        }

        UpdateEffectiveVisibility();
    }

    /// <summary>
    /// 是否允许在竖向任务栏上显示歌词。
    ///
    /// 默认 false：竖向排版是 P3 新增行为，FluentFlyout 此前在竖向
    /// 任务栏上不显示任何文字。P3-12 验收通过后改成 true。
    /// </summary>
    public static bool SupportsVerticalTaskbar { get; set; }

    private bool _isVertical;
    private bool _isVerticalPending;

    /// <summary>
    /// 清空内容。切歌或停止播放时调用。
    /// </summary>
    public void Clear()
    {
        _frame = null;
        PrimaryText.Text = string.Empty;
        PrimaryScanText.Text = string.Empty;
        SecondaryText.Text = string.Empty;
        PrimaryScanText.Visibility = Visibility.Collapsed;
        SecondaryContainer.Visibility = Visibility.Collapsed;
        PrimaryText.Opacity = LyricsPanelMetrics.PrimaryOpacity;
        _isScanning = false;
        _lastScanProgress = -1;
        _lastReportedWidth = -1;
    }

    /// <summary>
    /// 切到歌词模式 / 退出歌词模式。宿主用它和标题区做互斥。
    /// </summary>
    /// <remarks>
    /// 「是否启用歌词」与「竖向任务栏能不能显示」是两个独立条件，
    /// 最终可见性 = 两者都成立。集中在这里判断，避免 SetVerticalMode
    /// 与 SetActive 互相覆盖 Visibility。
    /// </remarks>
    public void SetActive(bool active)
    {
        _isActive = active;

        if (!active)
        {
            Clear();
        }

        UpdateEffectiveVisibility();
    }

    /// <summary>宿主是否要求显示歌词。</summary>
    private bool _isActive;

    /// <summary>
    /// 计算并应用最终可见性。
    /// </summary>
    private void UpdateEffectiveVisibility()
    {
        var shouldShow = _isActive && !_isVerticalPending;
        Visibility = shouldShow ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>当前是否真正可见（同时满足「启用」与「方向支持」）。</summary>
    public bool IsShowing => Visibility == Visibility.Visible;
}