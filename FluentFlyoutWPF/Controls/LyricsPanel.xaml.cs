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
    private readonly LinearGradientBrush _scanMask;

    private LyricDisplayFrame? _frame;
    private LyricsPanelMetrics _metrics;
    private bool _hasMetrics;

    /// <summary>当前是否处于逐字扫描态（帧里带 WordScanProgress）。</summary>
    private bool _isScanning;

    /// <summary>最近一次设置的扫描进度，避免重复写同一批 Offset。</summary>
    private double _lastScanProgress = -1;

    /// <summary>
    /// 当前渲染档位。
    ///
    /// 默认 <see cref="ScanRenderMode.Gradient"/>。自适应降级由
    /// <see cref="ReportFrameTime"/> 驱动：连续多帧超预算就降一档，
    /// 且**不会自动升回来**——避免在临界点来回抖动
    /// （降级→变快→升级→又变慢，用户会看到高亮方式反复切换）。
    /// 只有换行时才重新评估一次。
    /// </summary>
    private ScanRenderMode _scanMode = ScanRenderMode.Gradient;

    /// <summary>
    /// 单帧渲染预算（毫秒）。30fps 的预算是 33ms，
    /// 这里取一半作为「还有余量」的门槛。
    /// </summary>
    public const double FrameBudgetMilliseconds = 8;

    /// <summary>连续超预算多少帧后降级。取 5 约等于 165ms，用户还来不及察觉。</summary>
    private const int FramesBeforeDowngrade = 5;

    private int _slowFrameCount;

    /// <summary>
    /// 当前主行文本的宽度缓存（DIP）。
    ///
    /// 只在文本或字号变化时重新测量。滚动与宽度上报都要用，
    /// 但两者都在热路径上——每帧构造 <c>FormattedText</c> 是
    /// 几十微秒的固定开销，30fps 下就是 1% 的 CPU 白烧。
    /// </summary>
    private double _cachedPrimaryTextWidth;

    private string _cachedMeasuredText = string.Empty;
    private double _cachedMeasuredFontSize = -1;

    /// <summary>累积的降级次数，供诊断与设置界面展示。</summary>
    public int DowngradeCount { get; private set; }

    private bool _showTranslation = true;

    /// <summary>
    /// 用户是否开启翻译行（设置项）。默认开启。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="LyricsPanelMetrics.ShowTranslation"/> 的分工：
    /// 后者是**度量层**的判断（单行模式装不下翻译，硬件上不允许），
    /// 这里是**用户层**的偏好（双行模式装得下，但用户不想看）。
    /// 两者取与：度量说不行就是不行，度量说行还要用户同意。
    ///
    /// <para>
    /// 赋值会重算「整首歌的宽度」：翻译行参与宽度测量，关掉后宽度必须收回去，
    /// 否则胶囊会一直为一行看不见的翻译留白。值没变时直接返回，所以
    /// 设置页每次重刷（<c>ApplyLyricsSettings</c>）不会反复量整首歌词。
    /// </para>
    /// </remarks>
    public bool ShowTranslation
    {
        get => _showTranslation;
        set
        {
            if (_showTranslation == value)
            {
                return;
            }

            _showTranslation = value;
            RecalculateDocumentWidth();
            ReportDesiredWidth();
        }
    }

    /// <summary>
    /// 用户是否开启逐字扫描（设置项）。默认开启。
    ///
    /// 关掉后不进入扫描态，当前行整体高亮。这是给低端机 / 电池模式
    /// 留的省电路径——渐变遮罩每帧改三个 GradientStop，是最费的一档。
    /// </summary>
    public bool WordScanEnabled { get; set; } = true;

    /// <summary>
    /// 用户显式指定的主行颜色。null 表示跟随系统主题。
    /// </summary>
    /// <remarks>
    /// 只覆盖**主行与翻译行的文字色**，不覆盖扫描覆盖层：
    /// 扫描层的语义是「已唱部分更亮」，它的颜色必须相对主行有对比度。
    /// 如果让用户自定义成和主行同色，逐字扫描就完全看不出来了。
    /// 所以这里按主行颜色的亮度自动推一个更亮的版本（见
    /// <see cref="ApplyUserColor"/>）。
    /// </remarks>
    public Color? UserPrimaryColor { get; set; }

    /// <summary>
    /// 用户指定的对齐方式。竖向排版走 <c>TextAlignment</c>，
    /// 横向走 <see cref="LyricsAlignmentPolicy"/> 算出的位移。
    /// </summary>
    public LyricsTextAlignment Alignment { get; set; } = LyricsTextAlignment.Left;

    /// <summary>
    /// 上一次「跟随主题」时算出的主行色。
    ///
    /// 用户清空自定义颜色后要能退回主题色，而 <see cref="ApplyWindowsTheme"/>
    /// 只在系统主题切换时被调用。把上次的主题色记下来，就能在
    /// 「从自定义切回跟随」的那一刻直接复用，不必等下一次主题变化。
    /// </summary>
    private Color _themePrimaryColor = Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);

    /// <summary>上一次「跟随主题」时算出的扫描覆盖层色。</summary>
    private Color _themeScanColor = Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);

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

    /// <summary>
    /// 整首歌词里最宽一句的宽度（DIP），0 表示还没有文档。
    ///
    /// <para>
    /// 小组件的宽度按它定，**歌内不再变**。按当前行量宽度的做法会让胶囊
    /// 随每一句胀缩，长句还会「先滚动、一秒后变宽、滚动白跑」——
    /// 这两点都是真机验收报上来的问题。理由详见
    /// <see cref="LyricsDocumentWidth"/>。
    /// </para>
    /// </summary>
    private double _documentMaxWidth;

    /// <summary>
    /// 当前文档的行。字号变化要重新量一遍宽度，所以原始文本得留着。
    /// </summary>
    private IReadOnlyList<LyricLine>? _documentLines;

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

        // 竖向模式下折行宽度/高度要跟着面板宽度走。
        //
        // 订阅**面板自身**的 SizeChanged 而不是 PrimaryContainer 的：
        // 后者的高度正是由 UpdateVerticalWrapLayout 写进去的，
        // 用它当触发源会形成自触发循环（写高度 → SizeChanged →
        // 再写高度），而面板宽度是宿主给的、外部输入，没有这个问题。
        SizeChanged += (_, _) =>
        {
            if (_isVertical)
            {
                UpdateVerticalWrapLayout();
            }
        };

        // 面板默认不可见，由宿主在切到歌词模式时打开。
        // 走 UpdateEffectiveVisibility 而不是直接写 Visibility，
        // 保证可见性只有一个来源。
        UpdateEffectiveVisibility();
    }

    /// <summary>当前是否显示歌词。宿主据此决定是否显示标题/艺术家。</summary>
    public bool IsActive => Visibility == Visibility.Visible;

    /// <summary>
    /// 内容需要的宽度（DIP），供宿主在 <c>CalculateSize</c> 里使用。
    ///
    /// <para>
    /// 优先返回**整首歌最宽一句**的宽度（见 <see cref="_documentMaxWidth"/>），
    /// 只有文档还没到（刚切到歌词模式、纯音乐、没搜到词）时才退回当前行宽度。
    /// 这条优先级是「胶囊不随换行胀缩」的落点：宿主拿到的宽度在同一首歌里恒定。
    /// </para>
    ///
    /// <para>
    /// 与 <see cref="MeasureDesiredWidth"/> 的区别：这个属性读缓存，
    /// 可以在任意时刻廉价查询；后者会真的去量文本。
    /// </para>
    /// </summary>
    public double DesiredContentWidth
    {
        get
        {
            if (_documentMaxWidth > 0)
            {
                return _documentMaxWidth;
            }

            if (_cachedPrimaryTextWidth > 0)
            {
                return _cachedPrimaryTextWidth;
            }

            // 缓存还没建立（首帧之前）：退回量一次。
            UpdatePrimaryTextWidth();
            return _cachedPrimaryTextWidth;
        }
    }

    /// <summary>
    /// 换歌时把整首歌词交给面板，用来定下「这首歌的宽度」。
    /// </summary>
    /// <remarks>
    /// 必须在**这首歌的第一帧之前**调用，否则第一句会先按旧宽度排一次版。
    /// 装配层是在同一次 <c>FrameReady</c> 里先给文档再给帧，满足这个顺序。
    /// 传 null 表示没有歌词（纯音乐 / 搜不到 / 停止播放），宽度退回按当前行量。
    /// </remarks>
    public void ApplyDocument(IReadOnlyList<LyricLine>? lines)
    {
        _documentLines = lines;
        RecalculateDocumentWidth();
        ReportDesiredWidth();
    }

    /// <summary>
    /// 按当前字号把整首歌词重新量一遍，取最宽的一句。
    ///
    /// <para>
    /// 竖向不算：竖向的宽度被任务栏宽度钉死，能协商的是主轴长度，
    /// 走的是 <see cref="MeasureVerticalContent"/>，这里量了也用不上。
    /// </para>
    /// </summary>
    private void RecalculateDocumentWidth()
    {
        if (!_hasMetrics || _isVertical || _documentLines is not { Count: > 0 })
        {
            _documentMaxWidth = 0;
            return;
        }

        _documentMaxWidth = LyricsDocumentWidth.MeasureMaxLineWidth(
            _documentLines,
            text => MeasureText(text, _metrics.FontSize),
            // 单行模式装不下翻译、或用户关掉了翻译：都不该算进宽度，
            // 否则胶囊会为一行根本不画的文本留白。
            _metrics.IsSingleLine || !ShowTranslation
                ? null
                : text => MeasureText(text, _metrics.TranslationFontSize));
    }

    /// <summary>当前度量，供宿主查询所需宽度/高度。</summary>
    public LyricsPanelMetrics Metrics => _metrics;

    /// <summary>
    /// 竖向估算主轴长度所需的两行实测内容。
    ///
    /// <b>为什么不能只给 <see cref="DesiredContentWidth"/></b>：竖向有
    /// 当前行 + 翻译/下一行两行**各自独立折行**的文本，只按主行估长度
    /// 会漏掉次要行整块。截图矩阵实测双行状态估 29.9 DIP、实际要 63.4 DIP，
    /// 少算一半，次要行被裁掉一整行。
    ///
    /// 行高给的是 <b>FormattedText 实测值</b>而不是 <c>Metrics.LineHeight</c>：
    /// 后者是「字号 × 1.15」的名义值（13px → 14.95），而 WPF 实际行高
    /// 是 16.5 DIP，差 10%。用名义值估算会让每行都少算 1.5 DIP。
    /// </summary>
    public LyricsVerticalContent MeasureVerticalContent()
    {
        if (!_hasMetrics)
        {
            return default;
        }

        var primaryText = PrimaryText.Text ?? string.Empty;
        var primaryWidth = primaryText.Length > 0
            ? MeasureText(primaryText, _metrics.FontSize)
            : 0;

        // 次要行只在「真的会画」时才算。单行模式下 SecondaryContainer
        // 是 Collapsed，把它算进去会让面板白占一行高度。
        var secondaryText = SecondaryText.Text ?? string.Empty;
        var hasSecondary = !_metrics.IsSingleLine &&
            SecondaryContainer.Visibility == Visibility.Visible &&
            !string.IsNullOrWhiteSpace(secondaryText);

        var secondaryWidth = hasSecondary
            ? MeasureText(secondaryText, _metrics.TranslationFontSize)
            : 0;

        return new LyricsVerticalContent(
            PrimaryWidth: primaryWidth,
            PrimaryLineHeight: MeasureLineHeight(_metrics.FontSize),
            SecondaryWidth: secondaryWidth,
            SecondaryLineHeight: hasSecondary
                ? MeasureLineHeight(_metrics.TranslationFontSize)
                : 0,
            Gap: hasSecondary ? _metrics.Gap : 0);
    }

    /// <summary>
    /// 量出该字号下单行文本的实际高度（DIP）。
    ///
    /// 用一个固定的单字符量：行高由字体度量（ascent+descent+lineGap）决定，
    /// 与内容无关，所以量一个字就够，且不会被空文本或超长文本干扰。
    /// </summary>
    private double MeasureLineHeight(double fontSize)
    {
        var height = MeasureWrappedHeight("字", fontSize, double.PositiveInfinity);

        // FormattedText 在 MaxTextWidth 为无穷时正常返回；
        // 万一异常返回 0，退回名义行高兜底。
        return height > 0 ? height : fontSize * LyricsPanelMetrics.LineHeightFactor;
    }

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

        if (_isVertical)
        {
            // 竖向时高度不能钉死：换行后行数由「视口宽度 ÷ 折行点」决定，
            // 不是度量能预知的。具体高度由 UpdateVerticalWrapLayout
            // 按实测折行结果写回（见该方法里关于 Canvas 塌成 0 高的说明）。
            PrimaryContainer.Height = double.NaN;
            SecondaryContainer.Height = double.NaN;
        }
        else
        {
            PrimaryContainer.Height = metrics.LineHeight;
            SecondaryContainer.Height = metrics.LineHeight;
        }

        if (metrics.IsSingleLine)
        {
            SecondaryRow.Height = new GridLength(0);
            SecondaryContainer.Visibility = Visibility.Collapsed;
        }
        else
        {
            // 竖向的行高由折行结果决定（Auto），横向是固定 LineHeight。
            SecondaryRow.Height = _isVertical
                ? GridLength.Auto
                : new GridLength(metrics.LineHeight);
            SecondaryContainer.Margin = new Thickness(0, metrics.Gap, 0, 0);
        }

        // 度量变了，之前记的宽度作废，下次帧更新会重新上报。
        _lastReportedWidth = -1;

        // 字号变了文本宽度也会变，缓存必须失效。
        _cachedMeasuredFontSize = -1;
        UpdatePrimaryTextWidth();

        // 整首歌的宽度是按字号量出来的，字号变了必须重量——
        // 否则用户把字号从 13 调到 20，胶囊还停在旧宽度上，长句会被裁。
        RecalculateDocumentWidth();

        // 竖向时字号变化会改变折行位置：视口宽度没变，但折行点与行数都变了，
        // 必须重新量一次折行高度。SizeChanged 不会触发（宽度确实没变）。
        if (_isVertical)
        {
            UpdateVerticalWrapLayout();
        }
    }

    /// <summary>
    /// 当前是否处于纯音乐态（帧里 <c>IsPureMusic</c> 为 true）。
    /// 宿主据此决定是否把频谱控件显示出来。
    /// </summary>
    public bool IsPureMusic { get; private set; }

    /// <summary>
    /// 纯音乐态发生变化。宿主订阅它来切换频谱的可见性。
    /// </summary>
    /// <remarks>
    /// 为什么由面板上报而不是宿主自己判断：<c>IsPureMusic</c> 来自
    /// <c>LyricDocument</c> 的启发式判定（只有一行且含「纯音乐」），
    /// 宿主拿不到。面板已经是帧的唯一消费者，顺手转发成本最低。
    /// </remarks>
    public event EventHandler<bool>? PureMusicChanged;

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

        var lineChanged = !string.Equals(
            _frame?.CurrentLine,
            frame.CurrentLine,
            StringComparison.Ordinal);

        _frame = frame;

        var primary = frame.CurrentLine ?? string.Empty;
        var primaryChanged = !string.Equals(PrimaryText.Text, primary, StringComparison.Ordinal);

        if (primaryChanged)
        {
            PrimaryText.Text = primary;
            PrimaryScanText.Text = primary;
            UpdatePrimaryTextWidth();
        }

        // 换行时给一次重新评估渲染档位的机会。
        // 只在真的换行时做——每帧都重置等于降级从未生效。
        if (lineChanged)
        {
            ResetScanModeForNewLine();
        }

        UpdatePureMusicState(frame.IsPureMusic);
        var secondaryChanged = UpdateSecondaryLine(frame);
        UpdateScan(frame);
        UpdateScroll(frame);

        // 竖向时行数由文本决定：换行/换翻译都会改变折行高度，
        // 必须重量一次并写回 Canvas 高度，否则新行会被裁掉。
        //
        // 只在文本真的变了时做。这里是 30fps 热路径，
        // 每次 FormattedText 折行测量是几十微秒的固定开销，
        // 无条件调用等于把「不重排」的设计全部作废。
        if (_isVertical && (primaryChanged || secondaryChanged))
        {
            UpdateVerticalWrapLayout();
        }

        ReportDesiredWidth();
    }

    /// <summary>
    /// 更新纯音乐态。只在真的变化时触发事件，
    /// 避免宿主每帧都被通知一次。
    /// </summary>
    private void UpdatePureMusicState(bool isPureMusic)
    {
        if (IsPureMusic == isPureMusic)
        {
            return;
        }

        IsPureMusic = isPureMusic;

        if (isPureMusic)
        {
            // 纯音乐没有歌词可显示，清掉文本避免残留上一首的内容。
            // 但**不清空可见性**——面板本身仍要占位，由宿主决定
            // 是把面板藏起来换频谱，还是两者并列。
            PrimaryText.Text = string.Empty;
            PrimaryScanText.Text = string.Empty;
            SecondaryText.Text = string.Empty;
            PrimaryScanText.Visibility = Visibility.Collapsed;
            SecondaryContainer.Visibility = Visibility.Collapsed;
            PrimaryText.Opacity = LyricsPanelMetrics.PrimaryOpacity;
            _isScanning = false;
            _lastScanProgress = -1;
            _cachedPrimaryTextWidth = 0;
            _cachedMeasuredText = string.Empty;
        }

        PureMusicChanged?.Invoke(this, isPureMusic);
    }

    /// <summary>
    /// 更新长句滚动。
    ///
    /// 只在文本真的超出视口时才滚：短句滚动会让整句一直在动，
    /// 读起来比截断更累。这是与 <see cref="ScrollGeometry"/> 一致的判断。
    /// </summary>
    /// <remarks>
    /// 文本宽度走 <see cref="_cachedPrimaryTextWidth"/> 缓存，不在每帧
    /// 重新 <see cref="MeasureText"/>——文本只在换行时变，宽度也只在那时变。
    /// </remarks>
    private void UpdateScroll(LyricDisplayFrame frame)
    {
        if (PrimaryText.RenderTransform is not TranslateTransform transform)
        {
            return;
        }

        // 竖向模式下文本是换行的，横向偏移毫无意义——会把换行后的
        // 整块内容推出视口。这里直接归零并返回。
        //
        // 注意：这里说的「竖向」是**面板自身被反向旋转抵消之后**的状态。
        // 抵消之后坐标系回到屏幕坐标，PrimaryContainer 的宽度对应屏幕上的
        // 纵向延伸；换行已经让内容完整可见，再横向滚动只会把行推出去。
        if (_isVertical)
        {
            if (Math.Abs(transform.X) > 1e-6)
            {
                transform.X = 0;
            }

            // 竖向的对齐由 TextAlignment 负责（宽度被钉成视口宽度），
            // 覆盖层的位移也要一起归零，否则会与 TextAlignment 叠加成双倍偏移。
            SyncScanTransform(0);

            return;
        }

        var viewportWidth = PrimaryContainer.ActualWidth;
        if (!double.IsFinite(viewportWidth) || viewportWidth <= 0)
        {
            // 还没布局完成，下一次帧更新会带来正确宽度。
            return;
        }

        var geometry = ScrollGeometry.Create(
            lineProgress: frame.LineProgress,
            textWidth: _cachedPrimaryTextWidth,
            viewportWidth: viewportWidth,
            // 帧里没有行时长，只能按进度线性滚（ScrollGeometry 的
            // 未知时长分支）。有音节数据时逐字扫描已经给出了时间感，
            // 这里再精确也不会更准。
            lineDurationSeconds: 0,
            enabled: FluentFlyout.Classes.Settings.SettingsManager.Current.TaskbarWidgetScrollingEnabled);

        // 需要滚动时位置由滚动决定，对齐不参与（否则两者相加会把文本推出视口）；
        // 不需要滚动时按对齐算位移——横向的 TextBlock 宽度是 Auto，
        // TextAlignment 在这种宽度下不起作用，只能靠位移。
        var offsetX = geometry.NeedsScroll
            ? geometry.OffsetX
            : LyricsAlignmentPolicy.GetOffset(Alignment, viewportWidth, _cachedPrimaryTextWidth);

        // 只有偏移真的变了才写属性，避免每帧触发 TranslateTransform 的
        // 失效传播。1e-6 是 double 精度级别，不影响视觉。
        if (Math.Abs(transform.X - offsetX) > 1e-6)
        {
            transform.X = offsetX;
        }

        // 扫描覆盖层必须与主行完全同步位移。
        //
        // 这一步此前是缺的：PrimaryScanText 的 TranslateTransform 从来没被写过，
        // 于是长句滚动时主行往左移、覆盖层留在原地，高亮位置整块错开。
        // 短句不滚动时看不出问题，但「长句 + 逐字扫描」恰恰是最常见的组合。
        // 加了对齐之后更明显——不滚动也会错位。
        SyncScanTransform(offsetX);
    }

    /// <summary>
    /// 让扫描覆盖层跟上主行的位移。
    /// </summary>
    /// <remarks>
    /// 覆盖层的 <c>OpacityMask</c> 是挂在自身上的，<c>RenderTransform</c>
    /// 会连同遮罩一起移动，所以只要 X 一致，扫描边界就始终对得上文本。
    /// </remarks>
    private void SyncScanTransform(double offsetX)
    {
        if (PrimaryScanText.RenderTransform is not TranslateTransform scanTransform)
        {
            return;
        }

        if (Math.Abs(scanTransform.X - offsetX) > 1e-6)
        {
            scanTransform.X = offsetX;
        }
    }

    /// <summary>
    /// 决定次要行显示什么：
    /// - 单行模式 → 不显示；
    /// - 有翻译且用户开着翻译 → 显示当前行的翻译（用户开翻译就是想看它）；
    /// - 其余 → 显示下一行预览，用次要色区分。
    /// </summary>
    /// <returns>次要行文本是否变化。竖向折行需要据此决定要不要重新量高度。</returns>
    private bool UpdateSecondaryLine(LyricDisplayFrame frame)
    {
        if (_hasMetrics && _metrics.IsSingleLine)
        {
            return SetSecondaryText(string.Empty, LyricsPanelMetrics.TranslationOpacity, visible: false);
        }

        // 用户关掉翻译时，翻译一律不显示，退回「下一行预览」。
        //
        // 注意这里不能直接 return「不显示」：双行模式下第二行本来就该
        // 显示下一行，翻译只是它的一个可选替代。关翻译应该回到
        // 「看下一行」，而不是留一块空白。
        if (ShowTranslation && !string.IsNullOrWhiteSpace(frame.CurrentTranslation))
        {
            // 翻译行用更低的透明度，与 TL 的 --translation: 0.70 一致。
            return SetSecondaryText(frame.CurrentTranslation, LyricsPanelMetrics.TranslationOpacity, visible: true);
        }

        var next = frame.NextLine ?? string.Empty;
        if (string.IsNullOrWhiteSpace(next))
        {
            return SetSecondaryText(string.Empty, LyricsPanelMetrics.SecondaryOpacity, visible: false);
        }

        // 下一行预览比翻译更淡：它只是「预告」，不是当前内容。
        return SetSecondaryText(next, LyricsPanelMetrics.SecondaryOpacity, visible: true);
    }

    /// <summary>
    /// 写次要行文本并返回「文本是否真的变了」。
    ///
    /// 提取出来是因为 <see cref="UpdateSecondaryLine"/> 有三个分支，
    /// 每个分支都要做同样的「比较-赋值-记变化」三步；散在四处
    /// 极易漏掉一处，而漏掉的后果是竖向折行高度不更新（行被裁掉）。
    /// </summary>
    private bool SetSecondaryText(string text, double opacity, bool visible)
    {
        var changed = !string.Equals(SecondaryText.Text, text, StringComparison.Ordinal);

        if (changed)
        {
            SecondaryText.Text = text;
        }

        SecondaryText.Opacity = opacity;
        SecondaryContainer.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

        return changed;
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
        // 用户关掉逐字扫描：不进扫描态，当前行整体高亮。
        //
        // 这条分支必须在进度判断**之前**：否则关掉开关时如果刚好
        // 有一帧带进度，就会先进扫描态再退出来，闪一下。
        if (!WordScanEnabled)
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
    /// 设置扫描进度。
    ///
    /// 三档渲染路径的差别只在「怎么裁出已唱部分」：
    /// - <see cref="ScanRenderMode.Gradient"/>：改三个 <c>GradientStop.Offset</c>；
    /// - <see cref="ScanRenderMode.Clip"/>：改一个 <c>RectangleGeometry.Rect</c>；
    /// - <see cref="ScanRenderMode.WholeLine"/>：不裁，覆盖层直接藏起来。
    ///
    /// 三条路径都不碰布局属性。
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

        if (_scanMode == ScanRenderMode.WholeLine)
        {
            // 降级到整行高亮：覆盖层不需要了，主行恢复全亮度。
            PrimaryScanText.Visibility = Visibility.Collapsed;
            PrimaryText.Opacity = LyricsPanelMetrics.PrimaryOpacity;
            return;
        }

        var width = PrimaryContainer.ActualWidth;
        if (!double.IsFinite(width) || width <= 0)
        {
            // 还没布局完成，先按 1 处理；下一次帧更新会带来正确宽度。
            width = 1;
        }

        // 几何计算抽到 Core 里（ScanGeometry），可以精确单测边界。
        var geometry = ScanGeometry.Create(progress, width);

        if (_scanMode == ScanRenderMode.Clip)
        {
            ApplyClipScan(geometry);
            return;
        }

        ApplyGradientScan(geometry, width);
    }

    /// <summary>
    /// 渐变遮罩：已唱部分不透明 → 羽化 → 未唱部分透明。
    /// 视觉最好，成本也最高。
    /// </summary>
    private void ApplyGradientScan(ScanGeometry geometry, double width)
    {
        PrimaryScanText.OpacityMask = _scanMask;

        // 从 Clip 档升回 Gradient 时清掉残留的裁剪区，
        // 否则覆盖层会同时受 Clip 与 OpacityMask 限制，看起来像「缺一块」。
        if (PrimaryScanText.Clip is not null)
        {
            PrimaryScanText.Clip = null;
        }

        // StartPoint/EndPoint 只在宽度变化时改；宽度没变就不动，
        // 减少 WPF 对画刷的失效标记。
        if (_scanMask.EndPoint.X != width)
        {
            _scanMask.StartPoint = new Point(0, 0);
            _scanMask.EndPoint = new Point(width, 0);
        }

        _scanMask.GradientStops[0].Offset = 0;
        _scanMask.GradientStops[1].Offset = geometry.SolidEnd;
        _scanMask.GradientStops[2].Offset = geometry.FadeStart;
    }

    /// <summary>
    /// 硬边裁剪：没有羽化，边界是硬的，但成本比渐变低。
    ///
    /// 用 <c>UIElement.Clip</c> 而不是 <c>OpacityMask</c>：
    /// <c>OpacityMask</c> 的类型是 <see cref="Brush"/>，塞不进
    /// <c>RectangleGeometry</c>；要硬边遮罩就得包一层
    /// <c>DrawingBrush</c>，反而比直接 <c>Clip</c> 更贵。
    /// 每帧只改一个 <c>Rect</c>。
    /// </summary>
    private void ApplyClipScan(ScanGeometry geometry)
    {
        if (PrimaryScanText.Clip is not RectangleGeometry rect)
        {
            rect = new RectangleGeometry();
            PrimaryScanText.Clip = rect;
        }

        // 用 FadeStart 而不是 ScanX：硬边模式下把羽化区也算进已唱部分，
        // 否则边界会看起来比渐变模式「慢半拍」。
        rect.Rect = new Rect(0, 0, Math.Max(0, geometry.FadeStart), PrimaryContainer.ActualHeight);
    }

    /// <summary>
    /// 上报一帧的实际耗时，用于自适应降级。
    ///
    /// **注意这是托管侧耗时的近似值**：它测的是
    /// <see cref="ApplyFrame"/> 从头到尾的时间，包含文本赋值与
    /// 画刷属性写入，但**不含** WPF 之后在渲染线程合成渐变遮罩的成本。
    /// 真实帧时间要在 <c>CompositionTarget.Rendering</c> 上量。
    ///
    /// 用它做降级判据的理由：托管侧耗时异常升高时，渲染侧几乎必然
    /// 也在恶化（文本赋值触发 <c>TextBlock</c> 重新排版，是主因）。
    /// 作为「预警指标」足够，作为精确性能数据不够。
    ///
    /// 连续 <see cref="FramesBeforeDowngrade"/> 帧超过
    /// <see cref="FrameBudgetMilliseconds"/> 就降一档。
    /// 刻意不做「变快就升回来」——那会在临界点来回抖动，
    /// 用户看到的是高亮方式反复切换，比一直用低档更难受。
    /// </summary>
    public void ReportFrameTime(double milliseconds)
    {
        if (!double.IsFinite(milliseconds) || milliseconds < 0)
        {
            return;
        }

        if (milliseconds <= FrameBudgetMilliseconds)
        {
            _slowFrameCount = 0;
            return;
        }

        _slowFrameCount++;
        if (_slowFrameCount < FramesBeforeDowngrade)
        {
            return;
        }

        _slowFrameCount = 0;

        if (_scanMode == ScanRenderMode.Gradient)
        {
            _scanMode = ScanRenderMode.Clip;
            DowngradeCount++;
            _lastScanProgress = -1; // 强制下一次重画
        }
        else if (_scanMode == ScanRenderMode.Clip)
        {
            _scanMode = ScanRenderMode.WholeLine;
            DowngradeCount++;
        }
    }

    /// <summary>
    /// 重置渲染档位。换行时调用，给一次重新评估的机会。
    /// </summary>
    private void ResetScanModeForNewLine()
    {
        // 只在「上一轮降过级」时才重置，否则正常播放中每句都重置
        // 等于降级从未生效。
        if (DowngradeCount == 0)
        {
            return;
        }

        _scanMode = ScanRenderMode.Gradient;
        _slowFrameCount = 0;
        _lastScanProgress = -1;
    }

    /// <summary>
    /// 按当前文本宽度上报「希望多宽」。
    ///
    /// 宽度只在离散信号变化后才可能变（换歌、换翻译、字号/对齐变化），
    /// 所以这里用阈值过滤，避免长句滚动/逐字扫描期间反复请求重排。
    /// </summary>
    private void ReportDesiredWidth()
    {
        // 有整首文档时用「全曲最宽句」——宽度在歌内恒定，换行不再触发重排。
        // 没有文档（首帧之前、纯音乐、没搜到词）才退回按当前行量。
        var desired = _documentMaxWidth > 0 ? _documentMaxWidth : MeasureDesiredWidth();
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
                // pixelsPerDip 只影响**字形栅格化的取整**，不改变返回值单位
                // （Width 始终是 DIP）。传 1 与传真实 DPI 的差异在 0.1 DIP 量级，
                // 远小于 WidthChangeThreshold = 4，所以这里不值得每帧去查 DPI。
                //
                // 但要写清楚为什么不用 1 以外的值：面板会被挂到不同 DPI 的
                // 任务栏窗口上（混合 DPI 多屏），若把 DPI 混进度量，同一句歌词
                // 在两块屏上会算出不同宽度，进而让 DesiredWidthChanged 反复触发，
                // 表现为「拖动窗口到另一块屏后任务栏小组件宽度来回跳」。
                // 度量必须与 DPI 无关，物理像素由 WPF 统一换算。
                pixelsPerDip: 1);

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
    /// 刷新主行文本宽度缓存。文本或字号变化时调用，不进帧循环。
    /// </summary>
    private void UpdatePrimaryTextWidth()
    {
        var fontSize = _hasMetrics ? _metrics.FontSize : PrimaryText.FontSize;

        // 文本与字号都没变就跳过——调用方可能只是防御性地调了一下。
        if (string.Equals(_cachedMeasuredText, PrimaryText.Text, StringComparison.Ordinal) &&
            Math.Abs(_cachedMeasuredFontSize - fontSize) < 0.01)
        {
            return;
        }

        _cachedMeasuredText = PrimaryText.Text ?? string.Empty;
        _cachedMeasuredFontSize = fontSize;
        _cachedPrimaryTextWidth = MeasureText(_cachedMeasuredText, fontSize);
    }

    /// <summary>
    /// 应用配色。颜色必须来自系统主题（<c>WindowsThemeDetector</c>），
    /// 不能用 App 主题——任务栏配色跟系统走。
    /// </summary>
    /// <remarks>
    /// 生产代码请用 <see cref="ApplyWindowsTheme"/> 或
    /// <see cref="SetUserPrimaryColor"/>，不要直接调这个：它们负责
    /// 「用户显式色 vs 主题色」的取舍，直接调会绕开那层判断。
    /// 保留 public 是给视觉探针用的——探针要指定任意配色验证对比度。
    /// </remarks>
    public void ApplyPalette(Color primary, Color secondary, Color translation, Color scanOverlay)
    {
        PrimaryText.Foreground = new SolidColorBrush(primary);
        PrimaryScanText.Foreground = new SolidColorBrush(scanOverlay);
        SecondaryText.Foreground = new SolidColorBrush(translation);
    }

    /// <summary>
    /// 设置用户显式指定的主行颜色。<c>null</c> 表示回到跟随系统主题。
    ///
    /// 只改画笔，不重排——颜色不影响字号与行高。
    /// </summary>
    public void SetUserPrimaryColor(Color? color)
    {
        UserPrimaryColor = color;
        ApplyResolvedPalette();
    }

    /// <summary>
    /// 设置文本对齐方式。
    ///
    /// 竖向与横向的实现方式不同，原因见 <see cref="LyricsAlignmentPolicy"/>：
    /// 竖向的 <c>TextBlock.Width</c> 被钉成视口宽度（换行需要），
    /// 所以直接设 <c>TextAlignment</c>；横向宽度是 Auto，只能靠位移。
    /// </summary>
    public void SetAlignment(LyricsTextAlignment alignment)
    {
        if (Alignment == alignment)
        {
            return;
        }

        Alignment = alignment;
        ApplyAlignmentToTextBlocks();

        // 横向的位移由 UpdateScroll 每帧算，这里只触发一次重算：
        // 用当前帧重放一遍即可（帧内容没变，只是对齐变了）。
        // 竖向没有待处理的位移，SetVerticalMode 的 Width 已经就位。
        if (!_isVertical && _frame is { } frame)
        {
            UpdateScroll(frame);
        }
    }

    /// <summary>
    /// 按当前方向把对齐写到三个 <c>TextBlock</c> 上。
    ///
    /// <b>横向一律写 Left，竖向才用 <see cref="Alignment"/></b>。
    /// 两个方向必须分工，不能都设：
    /// <list type="bullet">
    /// <item>横向的 <c>TextBlock.Width</c> 是 Auto（等于文本宽度），
    /// <c>TextAlignment</c> 在这种宽度下没有可见效果；位置全靠
    /// <see cref="LyricsAlignmentPolicy"/> 算出的位移。</item>
    /// <item>竖向的 <c>Width</c> 被钉成视口宽度，<c>TextAlignment</c> 才真正生效，
    /// 而位移恒为 0。</item>
    /// </list>
    /// 若横向也设成 Center/Right，一旦某条路径把 <c>Width</c> 撑开
    /// （例如后续给长句滚动加固定视口宽度），就会与位移叠加成双倍偏移——
    /// 那种 bug 只在特定文本长度下出现，极难复现。这里从源头杜绝。
    /// </summary>
    private void ApplyAlignmentToTextBlocks()
    {
        var textAlignment = _isVertical
            ? Alignment switch
            {
                LyricsTextAlignment.Center => TextAlignment.Center,
                LyricsTextAlignment.Right => TextAlignment.Right,
                _ => TextAlignment.Left,
            }
            : TextAlignment.Left;

        PrimaryText.TextAlignment = textAlignment;
        PrimaryScanText.TextAlignment = textAlignment;
        SecondaryText.TextAlignment = textAlignment;
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
        Color primary, scanOverlay;

        if (isDark)
        {
            primary = Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);
            scanOverlay = Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);
        }
        else
        {
            // 浅色任务栏：用接近黑的文字。0xE4 而不是纯黑，
            // 与 TaskbarWidgetControl.ApplyWindowsTheme 的标题色保持一致。
            primary = Color.FromArgb(0xE4, 0x1C, 0x1C, 0x1C);
            scanOverlay = Color.FromArgb(0xFF, 0x00, 0x00, 0x00);
        }

        // 记住主题色：用户之后清空自定义颜色时要能立刻退回，
        // 不能等下一次系统主题变化。
        _themePrimaryColor = primary;
        _themeScanColor = scanOverlay;

        ApplyResolvedPalette();
    }

    /// <summary>
    /// 按「用户显式色优先，否则跟随系统主题」重涂一次。
    ///
    /// 所有配色入口最终都收敛到这里，保证「谁生效」只有一处判断。
    /// </summary>
    private void ApplyResolvedPalette()
    {
        if (UserPrimaryColor is not { } custom)
        {
            ApplyPalette(
                primary: _themePrimaryColor,
                secondary: _themePrimaryColor,
                translation: _themePrimaryColor,
                scanOverlay: _themeScanColor);
            return;
        }

        // 翻译行与主行同色，靠 Opacity 区分层次（与跟随主题时一致）。
        ApplyPalette(
            primary: custom,
            secondary: custom,
            translation: custom,
            scanOverlay: GetScanOverlayFor(custom));
    }

    /// <summary>
    /// 为自定义主行色推一个扫描覆盖层色。
    ///
    /// 规则：主行越暗，覆盖层越亮；主行越亮，覆盖层越暗。
    /// 逐字扫描的本质是「已唱部分比未唱部分亮」，而这里底层主行
    /// 会被压到 <see cref="LyricsPanelMetrics.SecondaryOpacity"/>（0.60），
    /// 覆盖层只要明显高于它就能形成对比。
    ///
    /// 用相对亮度而不是简单的反色：反色会让红色歌词的扫描层变成青色，
    /// 视觉上完全不像「同一句在扫」。这里改成「往白色方向提亮」或
    /// 「往黑色方向压暗」，保持色相不变。
    /// </summary>
    private static Color GetScanOverlayFor(Color primary)
    {
        // Rec.601 亮度，够用且不需要线性化。
        var luminance = ((0.299 * primary.R) + (0.587 * primary.G) + (0.114 * primary.B)) / 255.0;

        // 已经足够亮（> 0.6）的歌词，覆盖层往白走会看不清，
        // 改成往黑压——这样压暗后的主行与覆盖层仍有对比。
        var target = luminance > 0.6 ? Colors.Black : Colors.White;

        // 0.75 与 TL 的 --word-scan-overlay 强度一致；
        // 同时保留主行色的 alpha，用户设了半透明就跟着半透明。
        return Color.FromArgb(
            primary.A,
            BlendChannel(primary.R, target.R),
            BlendChannel(primary.G, target.G),
            BlendChannel(primary.B, target.B));
    }

    private static byte BlendChannel(byte from, byte to) =>
        (byte)Math.Clamp(from + ((to - from) * 0.75), 0, 255);

    /// <summary>
    /// 切换竖向任务栏排版：<b>反向旋转抵消宿主旋转 + 沿主轴换行</b>。
    ///
    /// 竖向任务栏的宽度通常只有 40–60 DIP，横排一行放不下一个句子，
    /// 所以文本要沿主轴方向换行。但换行本身不足以让字形正立——
    /// 这一点是**第二轮探针实测出来的，推翻了第一轮的结论**。
    ///
    /// <c>TaskbarWindow.PositionWidget</c> 对竖向任务栏会**无条件**给整个
    /// <c>TaskbarWidgetControl</c> 挂 <c>LayoutTransform = RotateTransform(90)</c>。
    /// 第一轮探针只测了「Widget 不旋转 + 歌词换行」（G/H 方案，墨迹 541px），
    /// 据此得出「竖向不需要任何旋转」——但那个前提在真机上不存在。
    /// 补测真实场景后：
    ///
    /// <list type="table">
    /// <listheader><term>方案</term><description>实测结果</description></listheader>
    /// <item><term>I：Widget(+90) + panel 不旋转</term>
    /// <description>墨迹仅 182px（完整显示的 34%），文字整体躺倒。
    /// 单字符 <c>—</c> 的包围盒 1×20（宽高比 0.05）证实是竖线。</description></item>
    /// <item><term>J：Widget(+90) + panel LayoutTransform(−90)</term>
    /// <description>墨迹 553px（与完整显示的 541px 相当），文字正立。
    /// 单字符包围盒 20×1（宽高比 20.0）证实是横线。</description></item>
    /// </list>
    ///
    /// 所以正确做法是「<b>抵消</b>宿主旋转 + 换行」，而不是「不旋转」。
    /// 与第一轮 C 方案（panel 挂 −90 但尺寸硬编码 185）的区别在尺寸：
    /// C 之所以墨迹 0px，是因为 panel 硬编码宽度 185 超出父级可用空间后
    /// 被排到 x = −135 画布外。这里 panel 宽度由 Grid 布局给出（不硬编码），
    /// 旋转后父级拿到的是真实尺寸，不会溢出。
    ///
    /// 顺带一个好处：抵消之后坐标系回到「屏幕坐标」，扫描渐变
    /// （沿 PrimaryContainer 宽度方向）与长句滚动都天然正确，
    /// 不需要为竖向单独调方向。
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

        if (_isVertical == isVertical)
        {
            // 方向没变，别重复设变换与 TextWrapping——两者都会触发 measure 遍，
            // 而这个方法被 UpdatePosition 每 1.5s 调一次。
            UpdateEffectiveVisibility();
            return;
        }

        _isVertical = isVertical;

        // 抵消宿主的 RotateTransform(90)。
        //
        // 用 LayoutTransform 而不是 RenderTransform：后者只改绘制、不改布局，
        // 父级仍按未旋转的尺寸排版，结果就是第一轮 D 方案那样被裁（墨迹 64px）。
        //
        // 横向时用 Transform.Identity 而不是 null，避免 RenderTransform 在
        // null 与非 null 之间切换也触发一次合成失效。
        LayoutTransform = isVertical ? new RotateTransform(-90) : Transform.Identity;

        // 换行策略：竖向时打开。抵消旋转后，这里的「宽度」对应屏幕上的
        // **纵向**延伸，所以 Wrap 折的是「任务栏宽度」这条边。
        var wrapping = isVertical ? TextWrapping.Wrap : TextWrapping.NoWrap;

        PrimaryText.TextWrapping = wrapping;
        PrimaryScanText.TextWrapping = wrapping;
        SecondaryText.TextWrapping = wrapping;

        // 对齐的实现方式与方向绑定（横向靠位移、竖向靠 TextAlignment），
        // 所以方向一变必须重新写一遍。写在 TextWrapping 之后：
        // 竖向的 Width 由下面的 UpdateVerticalWrapLayout 写入，
        // 而 TextAlignment 要等宽度就位才有意义——但设置本身无副作用，
        // 提前写不会错，放在这里只是让「与换行一起变」这件事更明显。
        ApplyAlignmentToTextBlocks();

        // 竖向时两行的高度不再固定——换行后行数由内容决定，
        // 交给容器 Auto 高度，由主轴方向的可用长度约束。
        if (isVertical)
        {
            UpdateVerticalWrapLayout();
        }
        else
        {
            // 横向时 TextBlock 宽度必须回到 Auto：
            // 否则会保留竖向时写进去的固定宽度，长句滚动算错。
            PrimaryText.Width = double.NaN;
            PrimaryScanText.Width = double.NaN;
            SecondaryText.Width = double.NaN;

            if (_hasMetrics)
            {
                PrimaryContainer.Height = _metrics.LineHeight;
                SecondaryContainer.Height = _metrics.LineHeight;
                SecondaryRow.Height = new GridLength(_metrics.LineHeight);
            }
        }

        UpdateEffectiveVisibility();
    }

    /// <summary>
    /// 竖向模式下把两行文本钉到折行宽度，并**显式写回 Canvas 的高度**。
    ///
    /// <b>这一步不能省，而且有两个坑。</b>
    ///
    /// <para>
    /// 坑一：<c>TextWrapping.Wrap</c> 只有在元素拿到「有限宽度约束」时才生效，
    /// 而 <c>Canvas</c> 给子元素的是**无限宽度**（Canvas 的设计就是不约束子元素）。
    /// 所以光设 <c>TextWrapping</c> 不够，必须显式写 <c>Width</c>。
    /// </para>
    ///
    /// <para>
    /// 坑二（截图矩阵实测踩到）：<c>Canvas.MeasureOverride</c> 对任何内容都返回
    /// <c>(0,0)</c>，所以 <c>Height="Auto"</c> 的 Canvas 会塌成 0 高，
    /// 叠在 <c>ClipToBounds="True"</c> 上就是「文字全部消失」。横向没事是因为
    /// XAML 把高度写死成 19；竖向一旦放开高度约束（换行后行数不定），
    /// 就必须自己量出折行后的高度再写回去。
    /// </para>
    ///
    /// <para>
    /// 折行宽度取**面板自身**的宽度，不能取 <c>PrimaryContainer.ActualWidth</c>：
    /// 后者的高度正是由本方法写进去的，读它会形成「先有鸡还是先有蛋」，
    /// 首次布局时读到 0 就再也不更新了（生产代码里面板宽度由
    /// <c>TaskbarWidgetControl.CalculateSize</c> 写入，是唯一可靠来源）。
    /// </para>
    /// </summary>
    /// <summary>
    /// 次要行的显示条件变了（翻译开关、单双行切换）之后，让布局跟上。
    ///
    /// 只做两件事：
    /// <list type="number">
    /// <item>按当前帧重新算一次次要行该显示什么——开关变了但帧没变，
    /// 不重算就还挂着上一次的内容。</item>
    /// <item>竖向时重量折行高度（次要行整块出现/消失会改变总高）。</item>
    /// </list>
    /// 横向不需要重量：行高固定，只是文本变空。
    /// </summary>
    public void RefreshSecondaryLineLayout()
    {
        if (_frame is { } frame)
        {
            UpdateSecondaryLine(frame);
        }

        if (_isVertical)
        {
            UpdateVerticalWrapLayout();
        }
    }

    private void UpdateVerticalWrapLayout()
    {
        if (!_isVertical || !_hasMetrics)
        {
            return;
        }

        var viewport = ResolveVerticalViewport();
        if (viewport <= 0)
        {
            // 还没布局完成（面板宽度尚未由宿主写入）。SizeChanged 会再来一次。
            return;
        }

        _lastWrapViewport = viewport;

        PrimaryText.Width = viewport;
        PrimaryScanText.Width = viewport;
        SecondaryText.Width = viewport;

        // 高度按折行结果实测：一行还是五行由内容决定，度量预知不了。
        PrimaryContainer.Height = MeasureWrappedHeight(
            PrimaryText.Text, _metrics.FontSize, viewport);

        SecondaryContainer.Height = MeasureWrappedHeight(
            SecondaryText.Text, _metrics.TranslationFontSize, viewport);

        // 次要行在竖向也是 Auto：高度由上面写进 Canvas 的值决定，
        // 不能沿用横向的固定 LineHeight（那会把折行后的翻译行裁成一行）。
        SecondaryRow.Height = GridLength.Auto;
    }

    /// <summary>
    /// 竖向折行宽度：面板自身的宽度。
    ///
    /// <c>ActualWidth</c> 在首次布局前是 0，此时退回显式 <c>Width</c>
    /// （宿主在 <c>CalculateSize</c> 里刚写进去、还没走完布局）。
    /// </summary>
    private double ResolveVerticalViewport()
    {
        var width = ActualWidth;
        if (!double.IsFinite(width) || width <= 0)
        {
            width = Width;
        }

        return double.IsFinite(width) && width > 0 ? width : 0;
    }

    /// <summary>
    /// 量出文本在给定宽度下折行后的高度（DIP）。
    ///
    /// 用 <see cref="FormattedText"/> 而不是 <c>TextBlock.ActualHeight</c>：
    /// 后者要等一轮布局才有值，而我们需要在**决定 Canvas 高度**的那一刻
    /// 就知道它——否则第一次布局永远是 0 高，永远看不到文字。
    ///
    /// 与 <see cref="MeasureText"/> 同源（同一个文本引擎、同样固定
    /// <c>pixelsPerDip: 1</c>），所以折行点与 TextBlock 一致。
    /// </summary>
    /// <param name="maxWidth">
    /// 折行宽度。传 <see cref="double.PositiveInfinity"/> 表示**不折行**，
    /// 只量单行高度——<c>FormattedText.MaxTextWidth</c> 不接受无穷，
    /// 所以这里显式跳过赋值（默认就是不限制）。
    /// </param>
    private double MeasureWrappedHeight(string? text, double fontSize, double maxWidth)
    {
        if (string.IsNullOrEmpty(text) || fontSize <= 0 || maxWidth <= 0)
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
                pixelsPerDip: 1)
            {
                TextAlignment = TextAlignment.Left,
            };

            // MaxTextWidth 默认就是「不限制」；只有拿到有限宽度才需要折行。
            if (double.IsFinite(maxWidth))
            {
                formatted.MaxTextWidth = maxWidth;
            }

            return formatted.Height;
        }
        catch (Exception)
        {
            // 量不出来就退回单行高度：宁可少显示几行，也不要 0 高把文字全裁掉。
            return _metrics.LineHeight;
        }
    }

    /// <summary>
    /// 竖向模式下，当前内容**实测**需要的主轴长度（DIP）。
    ///
    /// <b>为什么需要它。</b> 宿主用 <see cref="LyricsVerticalLayout"/> 估算
    /// 主轴长度，再写进 <c>Widget.Height</c>。但估算是按宽度除法推的近似值，
    /// 而实际折行由 <c>TextBlock</c> 的断行算法决定：中文标点禁则
    ///（标点不能出现在行首）会让实际行数比估算多一行。估算偏小的后果是
    /// 外层容器高度不够、最后一行被 <c>ClipToBounds</c> 整行裁掉，
    /// 而截图上看起来只是「字少了点」，很难发现。
    ///
    /// 宿主取 <c>max(估算, 本属性)</c> 即可兜住偏小的一侧。
    ///
    /// 返回值是 0 表示「还没布局完」或「不在竖向」，调用方应忽略它。
    /// </summary>
    public double VerticalContentHeight
    {
        get
        {
            if (!_isVertical || !_hasMetrics)
            {
                return 0;
            }

            // 先把两行的高度按实测折行结果写对，RootGrid 才会撑到真实高度。
            UpdateVerticalWrapLayout();

            var height = RootGrid.ActualHeight;
            return double.IsFinite(height) && height > 0 ? height : 0;
        }
    }
    ///
    /// 默认 true。初版设计是「默认关闭，等 #12 验收后打开」，但探针证明
    /// 竖向路径只是「不旋转 + 换行 + 换主轴」，成本与风险都远低于原方案。
    /// 保留开关的唯一价值是「真机上万一有问题可以一键退回不显示歌词」。
    /// </summary>
    public static bool SupportsVerticalTaskbar { get; set; } = true;

    private bool _isVertical;
    private bool _isVerticalPending;

    /// <summary>
    /// 上次竖向折行用的视口宽度（DIP）。仅用于诊断与「宽度没变就不重量」的短路，
    /// 不参与正确性判断——正确性只依赖 <see cref="UpdateVerticalWrapLayout"/> 被调用。
    /// </summary>
    private double _lastWrapViewport = -1;

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