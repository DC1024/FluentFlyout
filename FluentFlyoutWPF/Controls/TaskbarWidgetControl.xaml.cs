// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyout.Classes.Settings;
using FluentFlyout.Classes.Utils;
using FluentFlyout.Lyrics.Layout;
using FluentFlyout.Lyrics.Models;
using FluentFlyoutWPF;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Windows.Media.Control;
using Wpf.Ui.Controls;

namespace FluentFlyout.Controls;

/// <summary>
/// Interaction logic for TaskbarWidgetControl.xaml
/// </summary>
public partial class TaskbarWidgetControl : UserControl
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    // Constants for default and small taskbar widget sizes
    private const double DefaultTaskbarWidgetHeight = 40;
    private const double SmallTaskbarWidgetHeight = 28;

    // Constants for cover image and control button sizes
    private const double DefaultCoverImageSize = 36;
    private const double SmallCoverImageSize = 24;
    private const double DefaultCoverImageMargin = 55;
    private const double SmallCoverImageMargin = 43;
    private const double DefaultPlaceholderIconSize = 24;
    private const double SmallPlaceholderIconSize = 18;
    private const double DefaultControlButtonSize = 32;
    private const double SmallControlButtonSize = 24;
    private const float TaskbarVolumeStep = 0.02f;

    private readonly double _scale = 0.9;
    private readonly int _nativeWidgetsPadding = 216;

    /// <summary>
    /// 歌词模式下这块占位「最多能占多大」的默认上限（DIP 视觉长度）。
    ///
    /// 横向时是宽度上限；竖向时是**主轴长度**上限。同一常量两种含义，
    /// 因为两者都是「沿可协商方向能占多少」——横向能协商宽度，
    /// 竖向能协商的只有长度（宽度被任务栏本身钉死）。
    ///
    /// 取值与理由见 <see cref="LyricsVerticalLayout.DefaultMaxMainAxisLength"/>：
    /// 480 是 1080p 的经验值。**竖向实际用的上限不是它**——那一条由
    /// <c>TaskbarWindow</c> 按任务栏真实主轴长度经
    /// <see cref="LyricsVerticalLayout.ResolveMaxMainAxisLength"/> 现算后传进来，
    /// 见 <see cref="MeasureDesiredWidth"/>。这里保留常量是为了两件事：
    /// 横向宽度的上限，以及竖向下「拿不到任务栏尺寸」时的兜底。
    /// </summary>
    public const double MaxLyricsWidgetLength = LyricsVerticalLayout.DefaultMaxMainAxisLength;

    // Cached width calculations
    private string _cachedTitleText = string.Empty;
    private string _cachedArtistText = string.Empty;
    private double _cachedTitleWidth = 0;
    private double _cachedArtistWidth = 0;
    private double _cachedTitleContainerWidth = -1;
    private double _cachedArtistContainerWidth = -1;
    private readonly int _extraMarginForText = 6; // additional margin to avoid text clipping

    private double _cachedTitleOpacityMaskWidth = -1;
    private double _cachedArtistOpacityMaskWidth = -1;
    private LinearGradientBrush? _cachedTitleOpacityMask;
    private LinearGradientBrush? _cachedArtistOpacityMask;

    private string _actualTitle = string.Empty;
    private string _actualArtist = string.Empty;
    private string _songInfoTooltip = string.Empty;
    private float? _appVolume;

    // reference to main window for flyout functions
    private MainWindow? _mainWindow;
    private bool _isPaused;
    private bool _isVertical;
    private bool _isSmallTaskbar;

    /// <summary>
    /// 歌词模式是否开启。开启后 LyricsPanel 取代 SongInfoStackPanel，
    /// 且 UpdateMarquees 不再驱动标题滚动动画。
    /// </summary>
    private bool _isLyricsMode;

    public TaskbarWidgetControl()
    {
        InitializeComponent();

        // Apply Windows theme colors (independent of the app theme setting)
        ApplyWindowsTheme();

        // Set DataContext for bindings
        DataContext = SettingsManager.Current;

        MainBorder.SizeChanged += (s, e) =>
        {
            var rect = new RectangleGeometry(new Rect(0, 0, MainBorder.ActualWidth, MainBorder.ActualHeight), 6, 6);
            MainBorder.Clip = rect;
        };

        // for hover animation
        if (MainBorder.Background is not SolidColorBrush)
        {
            MainBorder.Background = new SolidColorBrush(Colors.Transparent);
            MainBorder.Background.Opacity = 0;
        }

        Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));

        // Initialize control order
        ReorderControls();

        // 面板内容宽度变了（换歌定下整首宽度、字号变化）要立刻重排一次。
        //
        // 不能靠 TaskbarWindow 的 1.5s 定时器兜底：那期间胶囊还停在旧宽度上，
        // 长句会先按旧视口开始滚动，等定时器到点才变宽、句子不再溢出，
        // 那次滚动白跑一趟——真机验收报的正是这个。
        // 阈值过滤在面板里做，这里不再判：能到这里说明宽度确实变了。
        LyricsPanel.DesiredWidthChanged += (_, _) =>
            WidgetWidthStrategyChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ReorderControls()
    {
        // Remove ControlsStackPanel from MainStackPanel
        MainStackPanel.Children.Remove(ControlsStackPanel);

        // Reorder based on position setting
        if (SettingsManager.Current.TaskbarWidgetControlsPosition == 0)
        {
            // Left: Controls, Image, Info
            MainStackPanel.Children.Insert(0, ControlsStackPanel);
            ControlsStackPanel.Margin = new Thickness(2, 0, 6, 0); // for some reason margins are weird on left side
        }
        else
        {
            // Right: Image, Info, Controls
            MainStackPanel.Children.Add(ControlsStackPanel);
            ControlsStackPanel.Margin = new Thickness(8, 0, 0, 0);
        }
    }

    /// <summary>
    /// 切换竖向任务栏模式。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>TaskbarWindow.PositionWidget</c> 每次 <c>UpdatePosition</c> 都会调到这里
    /// （1.5s 定时器 + 每次换歌 + 每次歌词换行）。方法体本身是幂等的，
    /// 但有两件事不能无条件做：
    /// </para>
    /// <list type="bullet">
    /// <item><b>重算歌词度量</b>——<see cref="LyricsPanel.ApplyMetrics"/>
    /// 会改字号与行高，等于定时触发一次重排。</item>
    /// <item><b>新建变换对象</b>——每次 new 一个 <see cref="RotateTransform"/>
    /// 赋给 <c>RenderTransform</c>，会让 WPF 丢弃旧的渲染缓存重新合成；
    /// 4 个元素 × 每 1.5s 一次，在低端机上是可以量到的抖动。</item>
    /// </list>
    /// <para>两者都用 <c>changed</c> 把关。</para>
    /// </remarks>
    public void SetVerticalMode(bool isVertical)
    {
        var changed = _isVertical != isVertical;
        _isVertical = isVertical;

        // 歌词面板的竖向排版与标题区不同：标题区在竖向任务栏上是直接隐藏，
        // 歌词必须显示。这里只切面板自身的 Orientation，具体排版由它负责。
        // 它自己也有方向未变时的早退，重复调用无代价。
        LyricsPanel.SetVerticalMode(isVertical);

        UpdateTextRegionVisibility();

        if (changed)
        {
            // 变换只在方向真的翻转时重建。横向时赋 Transform.Identity 而不是
            // null，避免 RenderTransform 在 null 与非 null 之间来回切也触发合成失效。
            //
            // 每个元素单独 new 一份而不是共用同一个实例：共用在 WPF 里是合法的
            // （Freezable 支持多父级），但一旦将来有人想给某个按钮单独加角度，
            // 共用会静默地把另外三个一起改掉。这里方向切换最多每秒一次，
            // 四份对象的代价可以忽略。
            void ApplyCounterRotation(FrameworkElement element)
            {
                element.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
                element.RenderTransform = isVertical ? new RotateTransform(-90) : Transform.Identity;
            }

            ApplyCounterRotation(SongImageBorder);

            foreach (var button in new Wpf.Ui.Controls.Button[] { PreviousButton, PlayPauseButton, NextButton })
            {
                ApplyCounterRotation(button);
            }

            RefreshLyricsMetrics();
        }
    }

    /// <summary>
    /// 切换小任务栏模式（任务栏短边 &lt; 40 DIP）。
    /// </summary>
    /// <remarks>
    /// 小任务栏会把歌词压成单行（28px 装不下两行），所以这里也参与度量推导，
    /// 同样要 <c>changed</c> 把关。判定用的是 DIP 高度，与 DPI 无关——
    /// 150% 下物理 60px 的任务栏仍是 40 DIP，不会被误判成小任务栏。
    /// </remarks>
    public void SetSmallTaskbarMode(bool isSmallTaskbar)
    {
        var changed = _isSmallTaskbar != isSmallTaskbar;
        _isSmallTaskbar = isSmallTaskbar;
        UpdateTextRegionVisibility();

        double coverImageSize = isSmallTaskbar ? SmallCoverImageSize : DefaultCoverImageSize;
        SongImageBorder.Width = coverImageSize;
        SongImageBorder.Height = coverImageSize;
        SongImagePlaceholder.FontSize = isSmallTaskbar ? SmallPlaceholderIconSize : DefaultPlaceholderIconSize;

        double controlButtonSize = isSmallTaskbar ? SmallControlButtonSize : DefaultControlButtonSize;
        foreach (var button in new Wpf.Ui.Controls.Button[] { PreviousButton, PlayPauseButton, NextButton })
        {
            button.Width = controlButtonSize;
            button.Height = controlButtonSize;
        }

        if (changed)
        {
            RefreshLyricsMetrics();
        }
    }

    /// <summary>
    /// 按当前的「方向 + 是否小任务栏」重新推导歌词度量。
    ///
    /// 只在影响度量的输入真的变化时调用（<see cref="SetVerticalMode"/>、
    /// <see cref="SetSmallTaskbarMode"/>、<see cref="SetLyricsMode"/>）。
    /// <b>不要放进帧循环，也不要每次 UpdatePosition 都走一遍</b>——
    /// <see cref="LyricsPanel.ApplyMetrics"/> 会改字号与行高，触发整块重排。
    ///
    /// 为什么需要它：<see cref="BuildLyricsMetrics"/> 同时依赖
    /// <c>_isVertical</c> 与 <c>_isSmallTaskbar</c>。改动前它只在
    /// <see cref="SetLyricsMode"/> 里被调用一次，于是用户把任务栏从横向
    /// 改成竖向（或换成小任务栏）后，歌词面板还挂着旧的字号与行高：
    /// 竖向时本该「不强制单行、靠换行吃满主轴长度」，却仍按横向的度量排版。
    /// </remarks>
    public void RefreshLyricsMetrics()
    {
        if (!_isLyricsMode)
        {
            return;
        }

        LyricsPanel.ApplyMetrics(BuildLyricsMetrics());
    }

    /// <summary>
    /// 把「纯偏好类」歌词设置刷到面板上：翻译行开关、逐字扫描开关。
    ///
    /// 与 <see cref="RefreshLyricsMetrics"/> 分开是因为代价不同：
    /// 这个方法只写两个 bool，不触发重排；度量会改字号与行高，
    /// 必然重排一次。设置界面改字号时两个都要调，改开关时只调这个。
    /// </summary>
    public void RefreshLyricsOptions()
    {
        if (!_isLyricsMode)
        {
            return;
        }

        var settings = SettingsManager.Current;
        LyricsPanel.ShowTranslation = settings.LyricsShowTranslation;
        LyricsPanel.WordScanEnabled = settings.LyricsWordScanEnabled;

        // 翻译开关可能让次要行整块出现或消失，竖向折行高度必须重量。
        // 横向不需要：行高是固定的，只是文本变空。
        LyricsPanel.RefreshSecondaryLineLayout();
    }

    /// <summary>
    /// 只刷「外观」类歌词设置：主行颜色、对齐。
    ///
    /// 与 <see cref="RefreshLyricsMetrics"/> 的区别：这两项都不影响字号与行高，
    /// 所以**不触发重排**——改个颜色不该让整块任务栏重新 <c>SetWindowPos</c>。
    /// 与 <see cref="RefreshLyricsOptions"/> 的区别：那两个开关会改变次要行
    /// 是否显示（进而改变竖向折行高度），这两个不会。
    /// </summary>
    public void RefreshLyricsAppearance()
    {
        if (!_isLyricsMode)
        {
            return;
        }

        var settings = SettingsManager.Current;
        var previousAlignment = LyricsPanel.Alignment;

        LyricsPanel.SetUserPrimaryColor(ResolveUserLyricsColor(settings.LyricsColor));
        LyricsPanel.SetAlignment(LyricsAlignmentPolicy.FromSetting(settings.LyricsAlignment));

        // 对齐方式变化会改变小组件的宽度策略（见 CalculateLyricsWidth：
        // 非左对齐要撑满预算）。宽度变了就必须重新走一次
        // CalculateSize，否则面板还挂在旧宽度上，居中看起来仍是左对齐。
        //
        // 这里直接通知宿主重排，而不是走 LyricsPanel.DesiredWidthChanged：
        // 那个事件的语义是「内容宽度变了」，宿主的处理器会拿宽度值做
        // 阈值比较；对齐切换时内容宽度可能一个字都没变，但**容器**宽度
        // 必须变，阈值判断会把它当成噪声丢掉。
        if (previousAlignment != LyricsPanel.Alignment)
        {
            WidgetWidthStrategyChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// 歌词的宽度策略变了（对齐方式切换），需要宿主重新算一次尺寸。
    ///
    /// 与「内容宽度变了」是两件事：后者可以按阈值节流（差 4 DIP 以内不值得动窗口），
    /// 前者必须无条件重排——宽度策略决定了 <c>CalculateSize</c> 走哪条分支。
    /// </summary>
    public event EventHandler? WidgetWidthStrategyChanged;

    /// <summary>
    /// 把设置里的十六进制字符串转成 <c>Color</c>。<c>null</c> = 跟随主题。
    /// </summary>
    private static Color? ResolveUserLyricsColor(string? hex)
    {
        var scheme = LyricsColorScheme.Parse(hex);
        if (!scheme.IsExplicit)
        {
            return null;
        }

        var argb = scheme.Argb;
        return Color.FromArgb(
            (byte)((argb >> 24) & 0xFF),
            (byte)((argb >> 16) & 0xFF),
            (byte)((argb >> 8) & 0xFF),
            (byte)(argb & 0xFF));
    }

    /// <summary>
    /// 统一决定「标题区还是歌词区」显示哪个。
    ///
    /// 改动前这段逻辑分散在 SetVerticalMode / SetSmallTaskbarMode / UpdateUi
    /// 三处，各自直接写 Visibility，加进歌词后必然互相覆盖。集中到一处，
    /// 三处都改成调用它。
    /// </summary>
    private void UpdateTextRegionVisibility()
    {
        if (_isLyricsMode)
        {
            // 歌词模式下标题区一律隐藏——不管是显示歌词还是让位给频谱。
            SongInfoStackPanel.Visibility = Visibility.Collapsed;
            SongArtistContainer.Visibility = Visibility.Collapsed;

            // 纯音乐态：歌词面板让位给频谱。频谱是独立控件，
            // 由 TaskbarWindow.PositionVisualizer 定位，不在这里管。
            LyricsPanel.SetActive(!LyricsPanel.IsPureMusic);
            return;
        }

        LyricsPanel.SetActive(false);
        SongInfoStackPanel.Visibility = _isVertical ? Visibility.Collapsed : Visibility.Visible;
        SongArtistContainer.Visibility = !_isSmallTaskbar && !_isVertical && !string.IsNullOrEmpty(_actualArtist)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    /// <summary>
    /// 切换歌词模式。
    /// </summary>
    /// <param name="enabled">是否启用歌词。</param>
    /// <param name="metrics">
    /// 歌词面板度量。传 null 表示沿用当前度量（若从未设置过则用默认值推导）。
    /// </param>
    public void SetLyricsMode(bool enabled, LyricsPanelMetrics? metrics = null)
    {
        if (_isLyricsMode == enabled && (metrics is null || !enabled))
        {
            return;
        }

        var wasLyricsMode = _isLyricsMode;
        _isLyricsMode = enabled;

        if (enabled)
        {
            LyricsPanel.ApplyMetrics(metrics ?? BuildLyricsMetrics());

            // 只在「从关到开」的边沿订阅。
            //
            // 改动前这里无条件 `+=`，而 P4 的 ApplyLyricsSettings 会被
            // 「窗口重建 / 设置变化 / 启动」反复调用——每次都传 enabled=true
            // 且 metrics 为 null，恰好绕过上面那个短路。结果是同一个处理器
            // 被挂 N 次，一次纯音乐态切换会跑 N 遍 UpdateTextRegionVisibility。
            // 现在短路条件允许「已开启 + 无 metrics」通过（那是为了让
            // 别的调用点刷新可见性），所以订阅必须自己判边沿。
            if (!wasLyricsMode)
            {
                LyricsPanel.PureMusicChanged += OnLyricsPureMusicChanged;
            }
        }
        else
        {
            LyricsPanel.PureMusicChanged -= OnLyricsPureMusicChanged;
        }

        UpdateTextRegionVisibility();

        // 进歌词模式时把外观类设置刷一遍。
        //
        // 必须在 ApplyWindowsTheme 之后：那个方法会把主题色写进面板，
        // 若顺序反了，用户设的自定义颜色会在下一次主题切换时被覆盖掉。
        // 这里放在 SetLyricsMode 里而不是只在 RefreshLyricsAppearance 里，
        // 是因为「刚开启歌词」这条路径不会经过设置变更钩子。
        if (enabled)
        {
            RefreshLyricsAppearance();
        }

        // 进歌词模式时停掉标题滚动动画。隐藏的 TextBlock 上跑着
        // RepeatBehavior.Forever 的动画仍然消耗渲染时钟，
        // 而且切回来时动画状态是脏的。
        if (enabled)
        {
            StopMarqueeAnimations();
        }
        else
        {
            UpdateMarquees();
        }
    }

    /// <summary>
    /// 纯音乐态变化。纯音乐没有歌词可显示，隐藏歌词面板，
    /// 让频谱接管这块空间。
    /// </summary>
    /// <remarks>
    /// 互斥规则（与实施计划的 P3-6 一致）：**歌词模式优先**，
    /// 频谱只在纯音乐态作为降级显示。这样用户开了歌词就不会同时
    /// 看到两套可视化在抢任务栏空间。
    /// </remarks>
    private void OnLyricsPureMusicChanged(object? sender, bool isPureMusic)
    {
        UpdateTextRegionVisibility();
    }

    /// <summary>歌词模式是否开启。</summary>
    public bool IsLyricsMode => _isLyricsMode;

    /// <summary>
    /// 歌词模式下「完全不受任务栏空间约束时」想占的整块宽度，
    /// 单位是 **DIP 视觉宽度**（与 <c>_nativeWidgetsPadding = 216</c> 同一坐标系）。
    ///
    /// 供 <c>TaskbarWindow</c> 做「两遍布局」的第一遍：先问要多大，
    /// 再拿这个值去问任务栏「放得下吗、放哪」，最后用实际区间长度回压。
    ///
    /// **单位必须与 216 对齐**：`CalculateSize` 的 <c>maxLogicalWidth</c>
    /// 是 `预算 / _scale`，而 <c>physicalWidth</c> 是
    /// <c>logicalWidth * dpiScale * _scale</c>——也就是说 216 这个常量
    /// 从来就是「DIP 视觉宽度」，在 150% DPI 下它仍占 216 DIP。
    /// 若这里返回物理像素，非 100% DPI 时预算会凭空多出 dpiScale 倍。
    ///
    /// <b>竖向时返回值语义不同</b>：不是宽度而是**主轴长度**（屏幕纵向）。
    /// 竖向任务栏宽度固定（通常 40–60 DIP），能协商的只有沿主轴占多长。
    /// </summary>
    /// <param name="maxMainAxisLength">
    /// 竖向时可占的主轴长度上限（DIP 视觉长度）。横向忽略此参数。
    ///
    /// 由调用方按任务栏实际主轴长度现算（<c>TaskbarWindow</c> →
    /// <see cref="LyricsVerticalLayout.ResolveMaxMainAxisLength"/>）。
    /// 用参数而不是常量，是因为竖排**没有滚动**：估算值一旦超过上限，
    /// 超出的行会被整行裁掉，屏幕上表现为「句子没写完」。4K 竖屏的主轴
    /// 有两千多 DIP，跟 1080p 用同一个 480 会把长句切在半途。
    /// </param>
    public double MeasureDesiredWidth(double maxMainAxisLength = MaxLyricsWidgetLength)
    {
        if (!_isLyricsMode)
        {
            return 0;
        }

        if (_isVertical)
        {
            // 竖向：一句歌词必然折成多行，需要的主轴长度 = 行数 × 行高。
            // 估算逻辑在 Core 的 LyricsVerticalLayout 里（可单测）。
            //
            // **必须传两行内容**（MeasureVerticalContent），不能只传主行宽度：
            // 当前行与翻译/下一行各自独立折行，只按主行估会漏掉次要行整块。
            // 截图矩阵实测：双行状态按主行估 29.9 DIP、实际要 63.4 DIP，
            // 次要行被 ClipToBounds 整行裁掉。
            //
            // 上限由调用方给：横向时它是 480 DIP 宽度，竖向时是
            // 「最多占主轴多少」（按任务栏长度现算，4K 竖屏会比 480 大）。
            // 对 1080p 屏来说主轴约 1040 DIP，一半即 520，不到中点，
            // 不会盖住任务栏中部的应用按钮——这条不变量在
            // LyricsVerticalLayout.ResolveMaxMainAxisLength 里写成常量。
            var crossSize = _isSmallTaskbar
                ? LyricsMetricsPolicy.SmallTaskbarCrossSize
                : LyricsMetricsPolicy.DefaultTaskbarCrossSize;

            var estimate = LyricsVerticalLayout.EstimateMainAxisLength(
                content: LyricsPanel.MeasureVerticalContent(),
                crossSize: crossSize,
                maxLength: maxMainAxisLength);

            // 用面板的**实测内容高度**兜住估算偏小的一侧。
            //
            // 估算是按宽度除法推的近似值，而实际折行由 TextBlock 的断行算法
            // 决定：中文标点禁则会让实际行数比估算多一行。估算偏小的后果是
            // 外层 Widget.Height 不够、最后一行被 ClipToBounds 整行裁掉，
            // 而截图上看起来只是「字少了点」，很难发现。
            //
            // 只取较大者、不做双向对齐：估算偏大是宿主主动申请的占位，
            // 缩回去会让任务栏留空白，且下一帧估算又撑起来，形成抖动。
            var measured = LyricsPanel.VerticalContentHeight;
            if (double.IsFinite(measured) && measured > estimate)
            {
                return Math.Min(measured, maxMainAxisLength);
            }

            return estimate;
        }

        var coverImageMargin = _isSmallTaskbar ? SmallCoverImageMargin : DefaultCoverImageMargin;
        var contentWidth = LyricsPanel.DesiredContentWidth;

        if (contentWidth <= 0)
        {
            // 首帧之前没有内容宽度，按最小可用宽度估。
            // 报 0 会让避让算法认为「什么都不需要」，直接塞进最窄的缝里。
            contentWidth = TaskbarFreeSpaceLayout.MinimumDisplayLength;
        }

        var logicalWidth = contentWidth + coverImageMargin + _extraMarginForText + MeasureControlsWidth();

        return logicalWidth * _scale;
    }

    /// <summary>
    /// 播放控制按钮占的逻辑宽度（DIP，未乘 <c>_scale</c>）。
    ///
    /// 单独拿出来是因为它在 <see cref="CalculateSize"/> 里是**加在宽度上限之后**的
    /// （`logicalWidth += controlsWidth` 在 clamp 之后），所以上限必须先把
    /// 这部分扣掉，否则整块占位会超出实测可用区间一个按钮组的宽度。
    /// </summary>
    private double MeasureControlsWidth()
    {
        if (!SettingsManager.Current.TaskbarWidgetControlsEnabled ||
            ControlsStackPanel.Visibility != Visibility.Visible)
        {
            return 0;
        }

        var controlsWidth = PreviousButton.Width + PlayPauseButton.Width + NextButton.Width;
        if (!_isVertical)
        {
            controlsWidth += ControlsStackPanel.Margin.Left + ControlsStackPanel.Margin.Right;
        }

        return controlsWidth;
    }

    /// <summary>
    /// 按当前任务栏形态推导歌词度量。
    ///
    /// 决策本身在 Core 的 <see cref="LyricsMetricsPolicy"/> 里（可用 2×2 矩阵单测锁死），
    /// 这里只负责把 WPF 侧的两个标志位传进去。
    /// </summary>
    private LyricsPanelMetrics BuildLyricsMetrics()
    {
        var settings = SettingsManager.Current;

        return LyricsMetricsPolicy.Create(
            isVertical: _isVertical,
            isSmallTaskbar: _isSmallTaskbar,
            preferredFontSize: settings.LyricsFontSize,
            userSingleLine: settings.LyricsSingleLine);
    }

    /// <summary>
    /// 换歌时把整首歌词交给面板，用来定下这首歌的固定宽度。
    /// </summary>
    /// <remarks>
    /// 宽度按「整首最宽的一句」定，而不是按当前行：胶囊才不会随每句胀缩，
    /// 长句也不会出现「先滚动、一秒后变宽」的抖动。详见
    /// <see cref="LyricsDocumentWidth"/>。
    ///
    /// 调用顺序要求：**先给文档、再给这一帧**（装配层在同一次
    /// <c>FrameReady</c> 里按这个顺序调用），否则第一句会先按旧宽度排一次版。
    /// </remarks>
    public void ApplyLyricDocument(IReadOnlyList<LyricLine>? lines)
    {
        LyricsPanel.ApplyDocument(lines);
    }

    /// <summary>
    /// 更新一帧歌词。这是热路径（最多 30 次/秒），
    /// 只改文本与扫描遮罩，<b>不触发重排、不做文本测量</b>。
    /// </summary>
    /// <remarks>
    /// 宽度不在这里返回：<see cref="LyricsPanel.MeasureDesiredWidth"/>
    /// 内部要构造 <c>FormattedText</c>，那是每帧几十微秒的固定开销，
    /// 而宽度只在换行时才可能变。改由面板的
    /// <c>DesiredWidthChanged</c> 事件推送（带 4 DIP 阈值）。
    /// </remarks>
    public void UpdateLyricsFrame(LyricDisplayFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        if (!_isLyricsMode)
        {
            return;
        }

        // 纯音乐态下歌词面板是隐藏的，不必更新它的可视树——
        // 但**必须**仍然调用 ApplyFrame：IsPureMusic 状态与
        // PureMusicChanged 事件都从那里来，跳过就永远退不出纯音乐态。
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        LyricsPanel.ApplyFrame(frame);
        stopwatch.Stop();

        if (!LyricsPanel.IsShowing)
        {
            // 隐藏时不喂帧耗时：隐藏元素不渲染，量到的时间没有意义，
            // 喂进去只会误触发降级。
            return;
        }

        // 把本帧耗时喂给自适应降级器。连续多帧超预算会降档，
        // 但不自动升回——避免在临界点抖动。
        LyricsPanel.ReportFrameTime(stopwatch.Elapsed.TotalMilliseconds);
    }

    /// <summary>
    /// 停掉两条标题滚动动画。歌词模式下调用，避免后台空转。
    /// </summary>
    private void StopMarqueeAnimations()
    {
        if (SongTitle.RenderTransform is TranslateTransform titleTransform)
        {
            titleTransform.BeginAnimation(TranslateTransform.XProperty, null);
            titleTransform.X = 0;
        }

        if (SongArtist.RenderTransform is TranslateTransform artistTransform)
        {
            artistTransform.BeginAnimation(TranslateTransform.XProperty, null);
            artistTransform.X = 0;
        }

        // 渐变遮罩上的颜色动画也要停，否则 OpacityMask 仍挂着动画时钟。
        _cachedTitleOpacityMask?.GradientStops[0].BeginAnimation(GradientStop.ColorProperty, null);
        _cachedTitleOpacityMask?.GradientStops[3].BeginAnimation(GradientStop.ColorProperty, null);
        _cachedArtistOpacityMask?.GradientStops[0].BeginAnimation(GradientStop.ColorProperty, null);
        _cachedArtistOpacityMask?.GradientStops[3].BeginAnimation(GradientStop.ColorProperty, null);
    }

    public void SetMainWindow(MainWindow mainWindow)
    {
        _mainWindow = mainWindow;
    }

    public void ApplyWindowsTheme()
    {
        WindowsThemeDetector.GetWindowsTheme(out _, out var systemTheme);
        bool isDark = systemTheme == WindowsThemeDetector.ThemeMode.Dark;

        var foreground = new SolidColorBrush(isDark
            ? Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF)
            : Color.FromArgb(0xE4, 0x1C, 0x1C, 0x1C));

        SongTitle.Foreground = foreground;
        SongArtist.Foreground = foreground;
        PreviousButton.Foreground = foreground;
        PlayPauseButton.Foreground = foreground;
        NextButton.Foreground = foreground;

        // 歌词面板的配色必须跟系统主题走，不能用 App 主题——
        // 任务栏是系统区域，用户把 App 设成浅色但系统深色时，
        // 用 App 配色会出现浅底白字。
        LyricsPanel.ApplyWindowsTheme(isDark);
    }

    private void Grid_MouseEnter(object sender, MouseEventArgs e)
    {
        if (string.IsNullOrEmpty(SongTitle.Text + SongArtist.Text)) return;

        RefreshAppVolumeTooltip();

        SolidColorBrush targetBackgroundBrush;
        // hover effects with animations, hard-coded colors because I can't find the resource brushes
        WindowsThemeDetector.GetWindowsTheme(out _, out var systemTheme);
        bool isDark = systemTheme == WindowsThemeDetector.ThemeMode.Dark;

        if (isDark)
        { // dark mode
            targetBackgroundBrush = new SolidColorBrush(Color.FromArgb(197, 255, 255, 255)) { Opacity = 0.075 };
            TopBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(93, 255, 255, 255)) { Opacity = 0.25 };
        }
        else
        { // light mode
            targetBackgroundBrush = new SolidColorBrush(Color.FromArgb(255, 255, 255, 255)) { Opacity = 0.6 };
            TopBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(93, 255, 255, 255)) { Opacity = 1 };
        }

        // Animate background
        var backgroundAnimation = new ColorAnimation
        {
            To = targetBackgroundBrush.Color,
            Duration = TimeSpan.FromMilliseconds(200),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        var backgroundOpacityAnimation = new DoubleAnimation
        {
            To = targetBackgroundBrush.Opacity,
            Duration = TimeSpan.FromMilliseconds(200),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        // rare case where background is not a SolidColorBrush after SetupWindow
        if (MainBorder.Background is not SolidColorBrush)
        {
            MainBorder.Background = new SolidColorBrush(Colors.Transparent);
            MainBorder.Background.Opacity = 0;
        }

        MainBorder.Background.BeginAnimation(SolidColorBrush.ColorProperty, backgroundAnimation);
        MainBorder.Background.BeginAnimation(SolidColorBrush.OpacityProperty, backgroundOpacityAnimation);
    }

    private void Grid_MouseLeave(object sender, MouseEventArgs e)
    {
        if (string.IsNullOrEmpty(SongTitle.Text + SongArtist.Text)) return;

        // Animate back to transparent
        var backgroundAnimation = new ColorAnimation
        {
            To = Colors.Transparent,
            Duration = TimeSpan.FromMilliseconds(200),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };

        var backgroundOpacityAnimation = new DoubleAnimation
        {
            To = 0,
            Duration = TimeSpan.FromMilliseconds(200),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };

        MainBorder.Background?.BeginAnimation(SolidColorBrush.ColorProperty, backgroundAnimation);
        MainBorder.Background?.BeginAnimation(SolidColorBrush.OpacityProperty, backgroundOpacityAnimation);

        TopBorder.BorderBrush = Brushes.Transparent;
    }

    private void Grid_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_mainWindow == null) return;

        // toggle main flyout when clicked
        _mainWindow.ShowMediaFlyout(toggleMode: true, forceShow: true, withVolume: true);
    }

    private void MainBorder_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (SettingsManager.Current.TaskbarWidgetScrollVolumeMode != 0 && _mainWindow != null)
        {
            float volumeDelta = Math.Clamp(e.Delta / 120f * TaskbarVolumeStep, -1f, 1f);
            _mainWindow.AdjustTaskbarVolume(volumeDelta);
        }

        e.Handled = true;
    }

    public (double logicalWidth, double logicalHeight) CalculateSize(double dpiScale, double? availableWidth = null)
    {
        double coverImageMargin = _isSmallTaskbar ? SmallCoverImageMargin : DefaultCoverImageMargin;

        // calculate widget width - use cached values if text hasn't changed
        string currentTitle = _actualTitle;
        string currentArtist = _actualArtist;

        bool textChanged = false;

        if (!string.Equals(currentTitle, _cachedTitleText, StringComparison.Ordinal))
        {
            _cachedTitleWidth = Math.Round(StringWidth.GetStringWidth(currentTitle, 400), 2);
            _cachedTitleText = currentTitle;
            textChanged = true;
        }
        if (!string.Equals(currentArtist, _cachedArtistText, StringComparison.Ordinal))
        {
            _cachedArtistWidth = Math.Round(StringWidth.GetStringWidth(currentArtist, 400), 2);
            _cachedArtistText = currentArtist;
            textChanged = true;
        }

        // 宽度上限，单位是「布局 DIP」（即乘过 _scale 之前的那个数）。
        //
        // availableWidth 由 TaskbarWindow 通过 TaskbarFreeSpaceLayout.TryPlace
        // 实测得出，传的是 **DIP 视觉宽度**（与 _nativeWidgetsPadding 同一坐标系）；
        // 传 null 时退回原来的硬编码 216。
        //
        // 单位链条（改动前就存在，容易看错，写清楚）：
        //   216 ── 视觉 DIP，恒为 216，不随 DPI 变
        //   /_scale ── 布局 DIP，240
        //   ×dpiScale×_scale ── 占屏物理像素
        // 所以 216 从来不是「物理像素预算」。把 UIA 的物理坐标直接
        // 当成预算传进来，150% DPI 下会凭空多出 1.5 倍宽度。
        //
        // 播放按钮组是**加在 clamp 之后**的（见下方 logicalWidth += controlsWidth），
        // 所以实测预算必须先把按钮扣掉再当上限用，否则整块占位会
        // 超出可用区间整整一个按钮组的宽度。传 null 时保持原有语义：
        // 216 只是文本部分的预算，按钮另算——这是 FF 一直以来的行为，
        // 不在本次改动里改。
        double controlsWidth = MeasureControlsWidth();

        // 竖向下不扣按钮宽度：竖向的预算方向是主轴（屏幕纵向），
        // 而按钮组经反向旋转后是竖排的，占的也是主轴方向——但它的
        // 高度由 TaskbarWindow 的 crossPos 居中处理，不在这个预算里。
        // 扣掉会让可用主轴长度凭空少一个按钮组。
        double budgetDeduction = _isVertical ? 0 : controlsWidth;

        double maxLogicalWidth = availableWidth is { } budget
            ? Math.Max((budget / _scale) - budgetDeduction, 0)
            : _nativeWidgetsPadding / _scale;

        double logicalWidth;

        if (_isVertical && _isLyricsMode)
        {
            // 竖向任务栏上的歌词。
            //
            // 坐标系有两层，容易绕进去，写清楚：
            //   1. TaskbarWindow.PositionWidget 给整个控件挂 LayoutTransform(+90)，
            //      所以控件的「逻辑宽度」在屏幕上表现为**纵向延伸**（主轴长度），
            //      「逻辑高度」表现为**横向宽度**（任务栏宽度）。
            //   2. LyricsPanel 内部再挂一个 LayoutTransform(-90) 把旋转抵消掉，
            //      让字形正立（见 LyricsPanel.SetVerticalMode 的探针数据）。
            //
            // 所以这里的 logicalWidth 要的是「沿主轴占多长」，而
            // maxLogicalWidth 正是 TaskbarWindow 传下来的主轴预算
            // （竖向时它算的是任务栏高度方向的可用区间），直接用。
            //
            // 不扣 controlsWidth：播放按钮组在竖向时被反向旋转成竖排，
            // 占的是主轴方向的一小段，已经由 MeasureDesiredWidth 的
            // 折行估算隐含包含在内容里；再扣一次会让可用长度凭空少一截。
            logicalWidth = maxLogicalWidth;
        }
        else if (_isVertical)
        {
            // 竖向 + 标题模式：FF 原有行为，竖向不显示文字，只要封面宽度。
            logicalWidth = coverImageMargin;
        }
        else if (_isLyricsMode)
        {
            logicalWidth = CalculateLyricsWidth(coverImageMargin, maxLogicalWidth);
        }
        else if (SettingsManager.Current.TaskbarWidgetFixedWidth)
        {
            // pin to maximum width so right-aligned controls don't shift between songs
            logicalWidth = maxLogicalWidth;
        }
        else
        {
            double contentWidth = _isSmallTaskbar ? _cachedTitleWidth : Math.Max(_cachedTitleWidth, _cachedArtistWidth);
            logicalWidth = contentWidth + coverImageMargin + _extraMarginForText; // add margin for cover image
            logicalWidth = Math.Min(logicalWidth, maxLogicalWidth);
        }

        double newTitleContainerWidth = Math.Max(logicalWidth - coverImageMargin, 0);
        double newArtistContainerWidth = Math.Max(logicalWidth - coverImageMargin, 0);
        bool widthChanged = false;

        if (_cachedTitleContainerWidth != newTitleContainerWidth)
        {
            SongTitleContainer.Width = newTitleContainerWidth;
            _cachedTitleContainerWidth = newTitleContainerWidth;
            widthChanged = true;
        }

        if (_cachedArtistContainerWidth != newArtistContainerWidth)
        {
            SongArtistContainer.Width = newArtistContainerWidth;
            _cachedArtistContainerWidth = newArtistContainerWidth;
            widthChanged = true;
        }

        // 歌词模式下容器宽度同时决定歌词视口宽度，必须一起更新，
        // 否则长句滚动会按旧的视口宽度算，滚出边界。
        //
        // 竖向时不设：竖向下 LyricsPanel 的宽度由「任务栏宽度」决定，
        // 与这里的 logicalWidth（主轴长度）不是同一个量。竖向下
        // logicalWidth 对应屏幕纵向延伸，写进 LyricsPanel.Width 会让面板
        // 变成一条极长的横条，换行完全失效。竖向的折行宽度由
        // LyricsPanel.UpdateVerticalWrapLayout 从面板自身的实际宽度取。
        if (_isLyricsMode && !_isVertical && widthChanged)
        {
            LyricsPanel.Width = newTitleContainerWidth;
        }

        // Refresh animations if layout bounds or text contents change
        if (textChanged || widthChanged)
        {
            UpdateMarquees();
        }

        // 播放按钮组占的空间。竖向下**不加**：竖向的 logicalWidth 是主轴长度
        // （屏幕纵向），按钮组经反向旋转后是竖排的，占的是同一个方向，
        // 但它的高度已经由 TaskbarWindow 的 crossPos 居中处理，
        // 在这里加会让整块占位沿主轴多出一截空白。
        if (controlsWidth > 0 && !_isVertical)
        {
            logicalWidth += controlsWidth;
        }

        // 竖向时 logicalHeight 是屏幕上的**横向宽度**，也就是任务栏宽度——
        // TaskbarWindow 会把它写成 Widget.Height（physicalHeight / dpiScale）。
        //
        // 这里仍然返回 40 / 28（任务栏短边的标准值）：TaskbarWindow 的
        // crossPos 计算是 `(crossSize - physicalHeight) / 2`，若两边取同一来源
        // （都来自 SettingsManager 的短边判定），居中的结果才稳定。
        // 真正的折行宽度由 LyricsPanel.UpdateVerticalWrapLayout 从
        // 面板自身的实际宽度取，不依赖这个返回值。
        double logicalHeight = _isSmallTaskbar ? SmallTaskbarWidgetHeight : DefaultTaskbarWidgetHeight;

        return (logicalWidth, logicalHeight);
    }

    /// <summary>
    /// 歌词模式下的宽度。
    ///
    /// 与标题模式的差别：标题是「内容多宽就多宽」，歌词需要
    /// **留出滚动余量**——一句长歌词如果刚好卡在视口宽度上，
    /// 每次换行都会因为多一两个像素触发重排。余量来自
    /// <c>_extraMarginForText</c>（6 DIP）：它只进 <c>logicalWidth</c>、
    /// 不进 <c>contentWidth</c>，所以最终的视口宽度
    /// （<c>logicalWidth - coverImageMargin</c>）比文本宽 6 DIP。
    /// </summary>
    private double CalculateLyricsWidth(double coverImageMargin, double maxLogicalWidth)
    {
        // 居中和右对齐需要「富余宽度」才有意义。
        //
        // 左对齐（默认）时小组件宽度按内容收缩，这是 FF 一贯的行为：
        // 短句就该占得少，不然任务栏上白占一块。但居中/右对齐下，
        // 如果宽度也收缩到内容宽度，富余恒为 0，三种对齐的渲染结果
        // 完全相同——用户会以为设置没生效。
        //
        // 所以非左对齐时直接吃满可用预算。代价是短句旁边会留白，
        // 但那正是用户选居中的目的（在固定区域里居中显示）。
        if (GetEffectiveLyricsAlignment() != LyricsTextAlignment.Left)
        {
            return maxLogicalWidth;
        }

        var contentWidth = LyricsPanel.DesiredContentWidth;

        if (contentWidth <= 0)
        {
            // 还没收到帧（刚切到歌词模式），先给一个中等宽度，
            // 避免先渲染成 0 宽再跳变。
            contentWidth = maxLogicalWidth * 0.5;
        }

        var logicalWidth = contentWidth + coverImageMargin + _extraMarginForText;

        // 歌词同样受任务栏可用区间约束，只缩不放。
        return Math.Min(logicalWidth, maxLogicalWidth);
    }

    /// <summary>
    /// 当前生效的歌词对齐方式。
    ///
    /// 竖向时强制左对齐：竖向的文本宽度被钉成视口宽度（换行需要有限约束），
    /// 对齐由 <c>TextAlignment</c> 负责，而竖向任务栏的宽度只有 40–60 DIP，
    /// 一行往往刚好占满，居中与右对齐在视觉上与左对齐没有区别。
    /// 强行让竖向也撑满预算会把宽度算错（竖向的 maxLogicalWidth 是主轴长度）。
    /// </summary>
    private LyricsTextAlignment GetEffectiveLyricsAlignment()
    {
        if (_isVertical)
        {
            return LyricsTextAlignment.Left;
        }

        return LyricsAlignmentPolicy.FromSetting(SettingsManager.Current.LyricsAlignment);
    }

    public void UpdateMarquees()
    {
        // 歌词模式下标题区是隐藏的，别再驱动它的滚动动画：
        // 隐藏元素上的 Forever 动画仍占渲染时钟，切回来时状态也是脏的。
        if (_isLyricsMode)
        {
            return;
        }

        double titleAvailableWidth = double.IsNaN(SongTitleContainer.Width) ? 0 : SongTitleContainer.Width;
        double artistAvailableWidth = double.IsNaN(SongArtistContainer.Width) ? 0 : SongArtistContainer.Width;

        bool isScrollingEnabled = SettingsManager.Current.TaskbarWidgetScrollingEnabled;

        UpdateMarquee(SongTitle, SongTitleContainer, _cachedTitleWidth, titleAvailableWidth, isScrollingEnabled);
        UpdateMarquee(SongArtist, SongArtistContainer, _cachedArtistWidth, artistAvailableWidth, isScrollingEnabled);
    }

    private void UpdateMarquee(System.Windows.Controls.TextBlock textBlock, Canvas container, double textWidth, double availableWidth, bool isEnabled)
    {
        if (textBlock.RenderTransform as TranslateTransform is not { } transform) return;

        int speed = SettingsManager.Current.TaskbarWidgetScrollingTextSpeed;
        bool loopForever = SettingsManager.Current.TaskbarWidgetScrollingTextLoopForever;
        bool isTitle = textBlock == SongTitle;
        double containerWidth = container.Width;

        // references moved outside so they may be called in the else block later
        ref double cachedMaskWidth = ref (isTitle ? ref _cachedTitleOpacityMaskWidth : ref _cachedArtistOpacityMaskWidth);
        ref LinearGradientBrush? cachedMask = ref (isTitle ? ref _cachedTitleOpacityMask : ref _cachedArtistOpacityMask);

        if (isEnabled && textWidth > availableWidth && containerWidth > 0 && !double.IsNaN(containerWidth))
        {
            textBlock.Width = double.NaN;
            textBlock.TextTrimming = TextTrimming.None;

            string origText = isTitle ? _actualTitle : _actualArtist;

            if (cachedMask == null || Math.Abs(containerWidth - cachedMaskWidth) > 0.5)
            {
                // 12.0 is the width in pixels of the gradient fade on the left and right hand edges of the 
                // text container.
                double fadeFraction = 12.0 / containerWidth;
                if (fadeFraction > 0.5) fadeFraction = 0.5;

                cachedMask = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(containerWidth, 0),
                    MappingMode = BrushMappingMode.Absolute
                };

                cachedMask.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.0));
                cachedMask.GradientStops.Add(new GradientStop(Color.FromArgb(255, 255, 255, 255), fadeFraction));
                cachedMask.GradientStops.Add(new GradientStop(Color.FromArgb(255, 255, 255, 255), 1.0 - fadeFraction));
                cachedMask.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 1.0));
                cachedMaskWidth = containerWidth;
            }

            container.OpacityMask = cachedMask;

            if (loopForever)
            {
                // continuous looping should have the fades constantly active (as its infinite)
                cachedMask.GradientStops[0].BeginAnimation(GradientStop.ColorProperty, null);
                cachedMask.GradientStops[3].BeginAnimation(GradientStop.ColorProperty, null);
                cachedMask.GradientStops[0].Color = Color.FromArgb(0, 255, 255, 255);
                cachedMask.GradientStops[3].Color = Color.FromArgb(0, 255, 255, 255);

                // \u00A0 are non-breaking spaces, which prevents WPF from collapsing and/or trimming
                // them
                string spacer = "\u00A0\u00A0\u00A0\u00A0\u00A0";
                textBlock.Text = origText + spacer + origText;

                double spacerWidth = StringWidth.GetStringWidth(spacer, 400);
                double scrollDistance = textWidth + spacerWidth;

                double durationToScroll = scrollDistance / speed;
                var animation = new DoubleAnimation
                {
                    From = 0,
                    To = -scrollDistance,
                    Duration = TimeSpan.FromSeconds(durationToScroll),
                    RepeatBehavior = RepeatBehavior.Forever
                };

                transform.BeginAnimation(TranslateTransform.XProperty, animation);
            }
            else
            {
                // Adding 10 pixels gives extra padding so the text scrolls past the container's edge before
                // resetting or reversing; this prevents abrupt cutoffs
                double scrollDistance = textWidth - containerWidth + 10;
                textBlock.Text = origText;

                double durationSeconds = scrollDistance / speed;
                double pauseDuration = 2.0; // wait 2 seconds at the start and end of the scroll
                double tWaitStart = pauseDuration;
                double tScrollEnd = tWaitStart + durationSeconds;
                double tWaitEnd = tScrollEnd + pauseDuration;
                double tScrollBackEnd = tWaitEnd + durationSeconds;
                double tTotalCycle = tScrollBackEnd + pauseDuration;

                var animation = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
                animation.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                animation.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(tWaitStart))));
                animation.KeyFrames.Add(new LinearDoubleKeyFrame(-scrollDistance, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(tScrollEnd))));
                animation.KeyFrames.Add(new LinearDoubleKeyFrame(-scrollDistance, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(tWaitEnd))));
                animation.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(tScrollBackEnd))));
                animation.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(tTotalCycle))));

                // sync fades with the "ping pong" movement
                Color transparentWhite = Color.FromArgb(0, 255, 255, 255);
                Color solidWhite = Color.FromArgb(255, 255, 255, 255);

                // 300 ms is the capped duration for the fade transition; we clamp it so that the fade animation
                // doesn't overlap with the scroll animation on certain shorter texts
                TimeSpan fadeTime = TimeSpan.FromMilliseconds(Math.Min(300, durationSeconds * 1000 / 2.0));

                var leftColorAnim = new ColorAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
                leftColorAnim.KeyFrames.Add(new DiscreteColorKeyFrame(solidWhite, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                leftColorAnim.KeyFrames.Add(new LinearColorKeyFrame(solidWhite, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(tWaitStart))));
                leftColorAnim.KeyFrames.Add(new LinearColorKeyFrame(transparentWhite, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(tWaitStart) + fadeTime)));
                leftColorAnim.KeyFrames.Add(new LinearColorKeyFrame(transparentWhite, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(tScrollBackEnd) - fadeTime)));
                leftColorAnim.KeyFrames.Add(new LinearColorKeyFrame(solidWhite, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(tScrollBackEnd))));
                leftColorAnim.KeyFrames.Add(new LinearColorKeyFrame(solidWhite, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(tTotalCycle))));

                var rightColorAnim = new ColorAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
                rightColorAnim.KeyFrames.Add(new DiscreteColorKeyFrame(transparentWhite, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                rightColorAnim.KeyFrames.Add(new LinearColorKeyFrame(transparentWhite, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(tScrollEnd) - fadeTime)));
                rightColorAnim.KeyFrames.Add(new LinearColorKeyFrame(solidWhite, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(tScrollEnd))));
                rightColorAnim.KeyFrames.Add(new LinearColorKeyFrame(solidWhite, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(tWaitEnd))));
                rightColorAnim.KeyFrames.Add(new LinearColorKeyFrame(transparentWhite, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(tWaitEnd) + fadeTime)));
                rightColorAnim.KeyFrames.Add(new LinearColorKeyFrame(transparentWhite, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(tTotalCycle))));

                cachedMask.GradientStops[0].BeginAnimation(GradientStop.ColorProperty, leftColorAnim);
                cachedMask.GradientStops[3].BeginAnimation(GradientStop.ColorProperty, rightColorAnim);

                transform.BeginAnimation(TranslateTransform.XProperty, animation);
            }
        }
        else
        {
            if (cachedMask != null)
            {
                // Prevent memory leaks and/or unwanted behavior by clearing the color animations when the mask is hidden
                cachedMask.GradientStops[0].BeginAnimation(GradientStop.ColorProperty, null);
                cachedMask.GradientStops[3].BeginAnimation(GradientStop.ColorProperty, null);
            }

            transform.BeginAnimation(TranslateTransform.XProperty, null);
            transform.X = 0;
            textBlock.Text = isTitle ? _actualTitle : _actualArtist;
            textBlock.Width = containerWidth;
            textBlock.TextTrimming = TextTrimming.CharacterEllipsis;
            container.OpacityMask = null;
        }
    }

    public void UpdateUi(string title, string artist, BitmapImage? icon, GlobalSystemMediaTransportControlsSessionPlaybackStatus? playbackStatus, GlobalSystemMediaTransportControlsSessionPlaybackControls? playbackControls = null)
    {
        if (title == "-" && artist == "-")
        {
            // No media playing, hide UI
            Dispatcher.Invoke(() =>
            {
                _actualTitle = string.Empty;
                _actualArtist = string.Empty;
                _songInfoTooltip = string.Empty;
                _appVolume = null;

                if (SettingsManager.Current.TaskbarWidgetHideCompletely)
                {
                    Visibility = Visibility.Collapsed;
                    return;
                }

                ControlsStackPanel.Visibility = Visibility.Collapsed;
                SongTitle.Text = string.Empty;
                SongArtist.Text = string.Empty;
                SongInfoStackPanel.Visibility = Visibility.Collapsed;
                SongInfoStackPanel.ToolTip = string.Empty;
                SongImagePlaceholder.Symbol = SymbolRegular.MusicNote220;
                SongImagePlaceholder.Visibility = Visibility.Visible;
                SongImage.ImageSource = null;
                BackgroundImage.Source = null;
                SongImageBorder.Margin = new Thickness(0, 0, 0, -3); // align music note better when no cover

                MainBorder.Background = new SolidColorBrush(Colors.Transparent);
                MainBorder.Background.Opacity = 0;
                TopBorder.BorderBrush = Brushes.Transparent;

                Visibility = Visibility.Visible;
            });
            return;
        }

        _isPaused = false;
        if (playbackStatus != GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
        {
            _isPaused = true;
        }

        // adjust UI based on available controls
        Dispatcher.Invoke(() =>
        {
            if (SettingsManager.Current.TaskbarWidgetControlsEnabled && playbackControls != null)
            {
                PreviousButton.IsHitTestVisible = playbackControls.IsPreviousEnabled;
                PlayPauseButton.IsHitTestVisible = playbackControls.IsPauseEnabled || playbackControls.IsPlayEnabled;
                NextButton.IsHitTestVisible = playbackControls.IsNextEnabled;

                PreviousButton.Opacity = playbackControls.IsPreviousEnabled ? 1 : 0.5;
                PlayPauseButton.Opacity = (playbackControls.IsPauseEnabled || playbackControls.IsPlayEnabled) ? 1 : 0.5;
                NextButton.Opacity = playbackControls.IsNextEnabled ? 1 : 0.5;
            }
            else
            {
                PreviousButton.IsHitTestVisible = false;
                PlayPauseButton.IsHitTestVisible = false;
                NextButton.IsHitTestVisible = false;

                PreviousButton.Opacity = 0.5;
                NextButton.Opacity = 0.5;
                PlayPauseButton.Opacity = 0.5;
            }
        });

        Dispatcher.Invoke(() =>
        {
            string newTitle = !string.IsNullOrEmpty(title) ? title : "-";
            string newArtist = artist ?? string.Empty;

            if (_actualTitle != newTitle || _actualArtist != newArtist)
            {
                // changed info
                if (SettingsManager.Current.TaskbarWidgetAnimated)
                {
                    AnimateEntrance();
                }

                _actualTitle = newTitle;
                _actualArtist = newArtist;

                SongTitle.Text = _actualTitle;
                SongArtist.Text = _actualArtist;
            }

            // Update tooltip with song info and the active app volume
            _songInfoTooltip = string.Empty;
            _songInfoTooltip += !string.IsNullOrEmpty(title) ? title : string.Empty;
            _songInfoTooltip += !string.IsNullOrEmpty(artist) ? "\n\n" + artist : string.Empty;
            _appVolume = _mainWindow?.GetActiveMediaAppVolume();
            UpdateSongInfoTooltip();

            if (SettingsManager.Current.TaskbarWidgetControlsEnabled)
            {
                PlayPauseButton.Icon = _isPaused ? new SymbolIcon(SymbolRegular.Play24, filled: true) : new SymbolIcon(SymbolRegular.Pause24, filled: true);
            }

            // change color of icon
            SolidColorBrush brush = BitmapHelper.SavedDominantColors.Count > 0 ?
                BitmapHelper.SavedDominantColors.Last()
                : (SolidColorBrush)Application.Current.TryFindResource("MicaWPF.Brushes.SystemAccentColorTertiary");
            SongImagePlaceholder.Foreground = brush;

            if (icon != null)
            {
                if (_isPaused && SettingsManager.Current.TaskbarWidgetShowPauseOverlay)
                { // show pause icon overlay
                    SongImagePlaceholder.Symbol = SymbolRegular.Pause24;
                    SongImagePlaceholder.Visibility = Visibility.Visible;
                    SongImage.Opacity = 0.4;
                }
                else
                {
                    SongImagePlaceholder.Visibility = Visibility.Collapsed;
                    SongImage.Opacity = 1;
                }
                SongImage.ImageSource = icon;
                BackgroundImage.Source = icon;
                SongImageBorder.Margin = new Thickness(0, 0, 0, -2); // align image better when cover is present
            }
            else
            {
                SongImagePlaceholder.Symbol = SymbolRegular.MusicNote220;
                SongImagePlaceholder.Visibility = Visibility.Visible;
                SongImage.ImageSource = null;
                BackgroundImage.Source = null;
            }

            SongTitle.Visibility = Visibility.Visible;

            // 标题区 / 歌词区的可见性统一由 UpdateTextRegionVisibility 决定，
            // 这里不再直接写 SongArtistContainer / SongInfoStackPanel。
            UpdateTextRegionVisibility();

            BackgroundImage.Visibility = SettingsManager.Current.TaskbarWidgetBackgroundBlur ? Visibility.Visible : Visibility.Collapsed;

            // on top of XAML visibility binding (XAML binding only hides when disabled in settings)
            ControlsStackPanel.Visibility = SettingsManager.Current.TaskbarWidgetControlsEnabled
                ? Visibility.Visible
                : Visibility.Collapsed;

            Visibility = Visibility.Visible;
        });
    }

    public void RefreshAppVolumeTooltip()
    {
        Dispatcher.Invoke(() =>
        {
            _appVolume = _mainWindow?.GetActiveMediaAppVolume();
            UpdateSongInfoTooltip();
        });
    }

    private void UpdateSongInfoTooltip()
    {
        SongInfoStackPanel.ToolTip = _songInfoTooltip;
        if (_appVolume is float appVolume)
            SongInfoStackPanel.ToolTip += $" ({appVolume:P0})";
    }

    private async void AnimateEntrance()
    {
        try
        {
            int msDuration = MainWindow.getDuration();

            // opacity and left to right animation for SongInfoStackPanel
            DoubleAnimation opacityAnimation = new()
            {
                From = 0.0,
                To = 1.0,
                Duration = TimeSpan.FromMilliseconds(msDuration),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };

            DoubleAnimation translateAnimation = new()
            {
                From = -10,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(msDuration),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };

            // Apply animations
            SongInfoStackPanel.BeginAnimation(OpacityProperty, opacityAnimation);
            TranslateTransform translateTransform = new();
            SongInfoStackPanel.RenderTransform = translateTransform;
            translateTransform.BeginAnimation(TranslateTransform.XProperty, translateAnimation);

            // don't play ControlsStackPanel animation if it's not enabled
            if (!SettingsManager.Current.TaskbarWidgetControlsEnabled)
                return;

            ControlsStackPanel.BeginAnimation(OpacityProperty, opacityAnimation);
            TranslateTransform translateTransform2 = new();
            ControlsStackPanel.RenderTransform = translateTransform2;
            translateTransform2.BeginAnimation(TranslateTransform.XProperty, translateAnimation);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Taskbar Widget error during entrance animation");
        }
    }

    // event handlers for media control buttons
    private async void Previous_Click(object sender, RoutedEventArgs e)
    {
        if (_mainWindow == null) return;
        await _mainWindow.TrySkipPreviousAsync();
    }

    private async void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        if (_mainWindow == null) return;
        await _mainWindow.TryTogglePlayPauseAsync();
    }

    private async void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_mainWindow == null) return;
        await _mainWindow.TrySkipNextAsync();
    }

    // Event handlers for context menu items
    private async void ContextMenuMediaPlayer_Click(object sender, RoutedEventArgs e)
    {
        if (_mainWindow == null) return;

        _ = _mainWindow.TryOpenMediaPlayerAsync();
    }

    private void TaskbarContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        _mainWindow?.PopulateSessionSwitcherMenu(ContextMenuSessionSwitcher);
    }

    private void ContextMenuSettings_Click(object sender, RoutedEventArgs e)
    {
        SettingsWindow.ShowInstance();
    }
}