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
    /// 用户指定的对齐方式。只作用于横向排版（由
    /// <see cref="LyricsAlignmentPolicy"/> 换算成位移）；竖排的列位置固定，
    /// 宿主在竖向也强制左对齐。
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

        // 竖向时列数由可用短边决定：宿主写面板 Height 会走到这里。
        //
        // 订阅**面板自身**的 SizeChanged 而不是 VerticalContainer 的：
        // 画布的子项是字形块，建树本身不会改变面板尺寸（Canvas 的
        // MeasureOverride 恒返回 0），所以这里不存在自触发循环。
        SizeChanged += (_, _) =>
        {
            if (_isVertical)
            {
                EnsureVerticalLayout();

                // 主轴长度（Width）是滚动几何的视口，换了就必须重算位移。
                // EnsureVerticalLayout 内部也会调一次，但它在「输入没变」时早退，
                // 而宽度变化不会改变建树输入（建树只看短边）——
                // 「拉长任务栏后长句还在按旧视口滚」就是这么来的。
                ApplyVerticalScroll();
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
    /// 竖向不算：竖排是逐字沿主轴排的，短边被任务栏钉死，
    /// 能协商的是主轴长度（走 <see cref="VerticalContentLength"/>），
    /// 这里量出来的宽度在竖向没有任何去处。
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

        // 横向两行的高度是度量的名义行高；竖向的 RootGrid 整块 Collapsed，
        // 这里写什么都不会被排到，竖排的字形块按列距自己定高
        // （见 LyricsPanel.Vertical.cs）。
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

        // 字号变了文本宽度也会变，缓存必须失效。
        _cachedMeasuredFontSize = -1;
        UpdatePrimaryTextWidth();

        // 整首歌的宽度是按字号量出来的，字号变了必须重量——
        // 否则用户把字号从 13 调到 20，胶囊还停在旧宽度上，长句会被裁。
        RecalculateDocumentWidth();

        // 竖向的列数、列距、每格步进全部由字号推出来：字号一变整棵树作废。
        if (_isVertical)
        {
            EnsureVerticalLayout();
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

        // 纯音乐态：这一帧的**内容**恒为空。
        //
        // 不能只靠 UpdatePureMusicState 的「首次进入时清一次」——那是边沿触发的：
        // 第二次喂同一个纯音乐帧时 IsPureMusic 已经是 true，清空分支早退，
        // 而主行赋值排在它前面，文字于是被写回面板（见 p3-landing-points.md 8.3.1）。
        // 生产里被 UpdateTextRegionVisibility 藏住看不见，截图探针补喂一帧就现形。
        //
        // 在这里归一化一次，比在每个写入点各加一条 if 可靠：扫描、次行、滚动、
        // 竖向建树读的都是这个对象，漏改一处就漏一条路径。IsPureMusic 本身必须保留
        // ——宿主靠它决定把面板让位给频谱（TaskbarWidgetControl.UpdateTextRegionVisibility）。
        var effective = frame.IsPureMusic
            ? frame with
            {
                CurrentLine = string.Empty,
                NextLine = string.Empty,
                CurrentTranslation = string.Empty,
                NextTranslation = string.Empty,
                WordScanProgress = null,
                LineProgress = 0,
            }
            : frame;

        var lineChanged = !string.Equals(
            _frame?.CurrentLine,
            effective.CurrentLine,
            StringComparison.Ordinal);

        // 存归一化后的那一帧：它才是屏幕上真正的内容。
        // 改对齐 / 关翻译时重放用的是 _frame（见 ApplyAlignment 与
        // RefreshSecondaryLineLayout），存原始帧会把纯音乐的字又写回去。
        _frame = effective;

        var primary = effective.CurrentLine ?? string.Empty;
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

        UpdatePureMusicState(effective.IsPureMusic);
        UpdateSecondaryLine(effective);

        // 进度与排版方向无关，横竖都要记：竖向字形树随时可能被重建，
        // 重建后要凭这一份把灯补上（见 LyricsPanel.VerticalScan.cs）。
        TrackVerticalScan(effective);

        // 长句滚动同理：位移也由重建后的内容长度算出来，
        // 而重建发生在 ApplyFrame 之外（见 LyricsPanel.VerticalScroll.cs）。
        TrackVerticalScroll(effective);

        if (_isVertical)
        {
            // 横向那套遮罩在竖向整块不可见（RootGrid 是 Collapsed），
            // 但它的状态仍会被写——先归零，省掉每帧对隐藏元素的画刷失效。
            ResetHorizontalScan();
        }
        else
        {
            UpdateScan(effective);
        }

        UpdateScroll(effective);

        // 竖向要跟着文本重建字形树。
        //
        // 无条件调用而不是「只在文本变了时」：文本不只由上面那段赋值改变——
        // Clear、RefreshSecondaryLineLayout、宿主写短边都会改输入，
        // 靠布尔标记判断容易漏一次（漏的后果是歌词停在上一句，截图上看不出来）。
        // EnsureVerticalLayout 自己用输入做键，没变就是一次字符串比较。
        if (_isVertical)
        {
            EnsureVerticalLayout();

            // 点灯必须校在重建之后：换行会整棵树重来，先点灯会被冲掉。
            ApplyVerticalScan();
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

        // 竖排的位移不在这个 TextBlock 上，而在 VerticalContainer 整块画布上：
        // 竖排是几十个独立字形块，逐块改 Canvas.SetLeft 是 50 次属性写 + 50 次
        // 重排，挂在画布上的 TranslateTransform 每帧最多一次属性写。
        //
        // 这里仍要先把横向那支位移归零：它此刻不可见（RootGrid 整块 Collapsed），
        // 但状态留着会在切回横向时把整句推出面板。
        // 竖向自己的滚动见 LyricsPanel.VerticalScroll.cs。
        if (_isVertical)
        {
            if (Math.Abs(transform.X) > 1e-6)
            {
                transform.X = 0;
            }

            // 覆盖层此时不可见（RootGrid 整块 Collapsed），一并归零，
            // 免得切回横向时带着上一次的偏移。
            SyncScanTransform(0);

            ApplyVerticalScroll();
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
    /// <returns>次要行文本是否变化（供 <see cref="RefreshSecondaryLineLayout"/> 之类的诊断路径用）。</returns>
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
    /// 极易漏一处，而漏的后果是某个分支忘了改可见性（竖排于是多排一列空列）。
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
    /// 退出横向扫描态：藏起覆盖层、主行恢复常亮。
    ///
    /// 只在确实处于扫描态时才动属性——<see cref="ApplyFrame"/> 在竖向每帧
    /// 都要调一次它（横向那套在竖向不可见，但状态不该留着）。
    /// </summary>
    private void ResetHorizontalScan()
    {
        if (!_isScanning)
        {
            return;
        }

        PrimaryScanText.Visibility = Visibility.Collapsed;
        PrimaryText.Opacity = LyricsPanelMetrics.PrimaryOpacity;
        _isScanning = false;
        _lastScanProgress = -1;
    }

    /// <summary>
    /// 更新横向的逐字扫描（竖向走 <c>LyricsPanel.VerticalScan.cs</c> 的字形块点亮）。
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
            ResetHorizontalScan();
            return;
        }

        if (frame.WordScanProgress is not { } progress || progress <= 0)
        {
            ResetHorizontalScan();
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
    /// <remarks>
    /// <para>
    /// <b>竖向报的不是宽度，是主轴长度。</b>竖排的短边被任务栏钉死、没有协商余地，
    /// 能协商的只有沿主轴的长度（<see cref="VerticalContentLength"/>）。
    /// 早先竖向也走横向那套判据，而竖向的 <c>_documentMaxWidth</c> 恒为 0
    /// （见 <c>RecalculateDocumentWidth</c>），于是每一帧都退回去量当前行——
    /// 结果是<b>每一句换行都请求一次整窗重排</b>：60s 播放 20 次，而每次重排都带
    /// 跨进程 UIA 查询与 <c>SetWindowPos</c>（见落点文档 3.1 与 R1）。
    /// </para>
    /// <para>
    /// 换用主轴长度后，有文档的竖向在整首歌内长度恒定 → 一次都不请求；
    /// 没文档时长度确实跟着当前行走，换行重排仍然是必要的，行为不变。
    /// </para>
    /// </remarks>
    private void ReportDesiredWidth()
    {
        // 有整首文档时用「全曲最宽句」——宽度在歌内恒定，换行不再触发重排。
        // 没有文档（首帧之前、纯音乐、没搜到词）才退回按当前行量。
        var desired = _isVertical
            ? VerticalContentLength
            : _documentMaxWidth > 0 ? _documentMaxWidth : MeasureDesiredWidth();

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

    private double MeasureText(string? text, double fontSize) =>
        MeasureText(text, fontSize, PrimaryText.FontWeight);

    /// <summary>
    /// 按指定字重量文本宽度（DIP）。
    ///
    /// <para>
    /// 字重是单独一个参数的唯一原因：竖排里主行是 Medium、译文行是 Normal，
    /// 而每格沿主轴的步进就是这一格的文本宽度。用主行的字重量译文，
    /// 拉丁词会差半个字的宽度，几十格累下来尾部会被裁掉一截。
    /// </para>
    /// </summary>
    private double MeasureText(string? text, double fontSize, FontWeight fontWeight)
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
                fontWeight,
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

        // 竖排的字形块各自持有画笔引用，换主题时必须一起重涂——
        // 它们不会自动跟着源 TextBlock 走（见 LyricsPanel.Vertical.cs）。
        SyncVerticalPalette();
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
    /// 竖向与横向的实现方式不同：横向的 <c>TextBlock.Width</c> 是 Auto，
    /// <c>TextAlignment</c> 不起作用，位置只能靠位移；横向一律左对齐。
    ///
    /// <para>
    /// 竖向这套已经用不上了——逐字竖排的字形块各占一格，主列居右、译文列在左，
    /// 对齐由列的位置决定（见 <c>LyricsPanel.Vertical.cs</c>），
    /// 宿主对竖向也强制左对齐。这里保留写入只是让三个源 TextBlock 的状态
    /// 与 <see cref="Alignment"/> 保持一致，切回横向时不需要再补一次。
    /// </para>
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
        // 竖向没有待处理的位移。
        if (!_isVertical && _frame is { } frame)
        {
            UpdateScroll(frame);
        }
    }

    /// <summary>
    /// 把三个源 <c>TextBlock</c> 的对齐一律写成 Left。
    ///
    /// <para>
    /// 横向的 <c>Width</c> 是 Auto（等于文本宽度），<c>TextAlignment</c> 在这种
    /// 宽度下没有可见效果；位置全靠 <see cref="LyricsAlignmentPolicy"/> 算出的
    /// 位移。若这里也设成 Center/Right，一旦某条路径把 <c>Width</c> 撑开
    /// （例如长句滚动给固定视口宽度），就会与位移叠加成双倍偏移——
    /// 那种 bug 只在特定文本长度下出现，极难复现。这里从源头杜绝。
    /// </para>
    /// <para>
    /// 竖向用的是逐字字形树，对齐由列的位置决定（见
    /// <c>LyricsPanel.Vertical.cs</c>），这三行文本在竖向根本不参与排版。
    /// </para>
    /// </summary>
    private void ApplyAlignmentToTextBlocks()
    {
        PrimaryText.TextAlignment = TextAlignment.Left;
        PrimaryScanText.TextAlignment = TextAlignment.Left;
        SecondaryText.TextAlignment = TextAlignment.Left;
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
    /// 切换竖向任务栏排版：<b>逐字沿主轴排，字形各自抵消宿主旋转</b>。
    ///
    /// <para>
    /// 这里不再给面板挂 <c>LayoutTransform(-90)</c>。历史上那个做法
    /// （「抵消旋转 + 沿主轴折行」）确实能让字形正立，代价是面板内部又回到
    /// 横向排版：短边 40 DIP 抵消后只剩 37.5 DIP 一行，<b>只放两个汉字</b>，
    /// 一句 28 字的歌词要折 14 行——就是「竖向歌词只显示 6 个字」那个缺陷。
    /// 逐字排把一列装到 44 字，这才是竖排的意义。
    /// </para>
    ///
    /// <para>
    /// 旋转下放到每个字形（<c>RenderTransform(-90)</c>），且只有方块字需要；
    /// 拉丁词整词侧躺沿列向下读。机制、容量账与两列的几何约束都写在
    /// <c>LyricsPanel.Vertical.cs</c> 的文件注释里。
    /// </para>
    ///
    /// <para>
    /// 两套内容互斥：横向用 <c>RootGrid</c> 的两行，竖向用
    /// <c>VerticalContainer</c> 的逐字画布。横向那两行的 <c>TextBlock</c>
    /// 在竖向仍然保留——它们是「文本 / 配色 / 不透明度」的唯一真相，
    /// 字形树照着它建，不另存一份状态。
    /// </para>
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
            // 方向没变，别重复设变换与可见性——两者都会触发 measure 遍，
            // 而这个方法被 UpdatePosition 每 1.5s 调一次。
            UpdateEffectiveVisibility();
            return;
        }

        _isVertical = isVertical;

        // 一律 Identity：面板与宿主那个 +90 同处一个坐标系，不再整体抵消
        // （抵消会把排版退回「一行两个字」，见方法注释）。
        LayoutTransform = Transform.Identity;

        // 两套内容互斥切换。用 Collapsed 而不是 Hidden：Collapsed 的子树
        // 不测量、不绘制、不参与命中测试，逐字路径上那两行的 TextBlock
        // 于是零成本。
        RootGrid.Visibility = isVertical ? Visibility.Collapsed : Visibility.Visible;
        VerticalContainer.Visibility = isVertical ? Visibility.Visible : Visibility.Collapsed;

        // 换行只在横向有意义：横向的 TextBlock 宽度是 Auto，靠位移对齐。
        PrimaryText.TextWrapping = TextWrapping.NoWrap;
        PrimaryScanText.TextWrapping = TextWrapping.NoWrap;
        SecondaryText.TextWrapping = TextWrapping.NoWrap;

        // 对齐的实现方式与方向绑定（横向靠位移），方向一变必须重新写一遍。
        ApplyAlignmentToTextBlocks();

        if (isVertical)
        {
            // 短边此刻通常还没写入（宿主要到 CalculateSize 才写），
            // 这里会早退；宿主写完 Height 会触发 SizeChanged 再进来一次。
            EnsureVerticalLayout();
        }
        else
        {
            // 竖向的显式尺寸与字形树都要清掉，否则切回横向后：
            //   - 面板宽度停在「主轴长度」上，长句滚动按错宽度算；
            //   - 面板高度停在「可用短边」上，留一条空白。
            Width = double.NaN;
            Height = double.NaN;
            ResetVerticalTree();

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
    /// 次要行的显示条件变了（翻译开关、单双行切换）之后，让布局跟上。
    ///
    /// 只做两件事：
    /// <list type="number">
    /// <item>按当前帧重新算一次次要行该显示什么——开关变了但帧没变，
    /// 不重算就还挂着上一次的内容。</item>
    /// <item>竖向重建一次字形树（关掉翻译要少排一列）。</item>
    /// </list>
    /// 横向不需要动布局：行高固定，只是文本变空。
    /// </summary>
    public void RefreshSecondaryLineLayout()
    {
        if (_frame is { } frame)
        {
            UpdateSecondaryLine(frame);
        }

        if (_isVertical)
        {
            EnsureVerticalLayout();
        }
    }

    /// <summary>
    /// 竖向任务栏上是否显示歌词。
    ///
    /// 默认 true。初版设计是「默认关闭，等 #12 验收后打开」，但探针证明
    /// 竖向路径的成本与风险都远低于原方案。保留开关的唯一价值是
    /// 「真机上万一有问题可以一键退回不显示歌词」。
    /// </summary>
    public static bool SupportsVerticalTaskbar { get; set; } = true;

    private bool _isVertical;
    private bool _isVerticalPending;

    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

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

        // 竖排的字形树立刻清空：清空是「停止播放 / 切歌」，不是换行，
        // 让它等到下一次读 VerticalContentLength 才回收没有意义。
        ResetVerticalTree();
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