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

    public void SetVerticalMode(bool isVertical)
    {
        _isVertical = isVertical;

        // 歌词面板的竖向排版与标题区不同：标题区在竖向任务栏上是直接隐藏，
        // 歌词必须显示。这里只切面板自身的 Orientation，具体排版由它负责。
        LyricsPanel.SetVerticalMode(isVertical);

        UpdateTextRegionVisibility();

        var counterRotate = isVertical ? new RotateTransform(-90) : null;

        SongImageBorder.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
        SongImageBorder.RenderTransform = (Transform?)counterRotate ?? Transform.Identity;

        foreach (var button in new Wpf.Ui.Controls.Button[] { PreviousButton, PlayPauseButton, NextButton })
        {
            button.RenderTransformOrigin = new System.Windows.Point(0.5, 0.5);
            button.RenderTransform = (Transform?)counterRotate ?? Transform.Identity;
        }
    }

    public void SetSmallTaskbarMode(bool isSmallTaskbar)
    {
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

        _isLyricsMode = enabled;

        if (enabled)
        {
            LyricsPanel.ApplyMetrics(metrics ?? BuildLyricsMetrics());
            LyricsPanel.PureMusicChanged += OnLyricsPureMusicChanged;
        }
        else
        {
            LyricsPanel.PureMusicChanged -= OnLyricsPureMusicChanged;
        }

        UpdateTextRegionVisibility();

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
    /// </summary>
    public double MeasureDesiredWidth()
    {
        if (!_isLyricsMode)
        {
            return 0;
        }

        // 竖向任务栏上不旋转排版（见 LyricsPanel.SetVerticalMode 的实测说明），
        // 文本沿主轴换行，所以「想要多宽」实际是「想要多长」：
        // 竖向时这个方法的返回值会被 TaskbarWindow 当成主轴长度用。
        //
        // 竖向的视觉宽度由任务栏宽度决定（不可协商），所以这里报的是
        // 主轴方向的理想长度——按「一行大概放得下 4 个汉字」估，
        // 取一个能让两行歌词完整显示的量。
        if (_isVertical)
        {
            return (LyricsPanel.Metrics.LineHeight * 4) * _scale;
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

    /// <summary>按当前任务栏尺寸推导歌词度量。</summary>
    private LyricsPanelMetrics BuildLyricsMetrics()
    {
        double height = _isSmallTaskbar ? SmallTaskbarWidgetHeight : DefaultTaskbarWidgetHeight;

        // 竖向任务栏不旋转排版（文本沿主轴换行），行高与横向保持一致，
        // 所以这里不区分方向——换行后的行数由主轴长度决定，不是由度量决定。
        //
        // 唯一的方向差异：小任务栏在竖向时不必强制单行。竖向的物理约束是
        // 「宽度窄」，而宽度窄正好靠换行解决，不需要再压成一行。
        return LyricsPanelMetrics.Create(
            availableHeight: height,
            preferredFontSize: 13,
            forceSingleLine: _isSmallTaskbar && !_isVertical);
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

        double maxLogicalWidth = availableWidth is { } budget
            ? Math.Max((budget / _scale) - controlsWidth, 0)
            : _nativeWidgetsPadding / _scale;

        double logicalWidth;

        if (_isVertical && _isLyricsMode)
        {
            // 竖向任务栏上的歌词：旋转 90° 后，`logicalWidth` 就是**屏幕上的高度**
            // （沿任务栏主轴的延伸量），`logicalHeight` 是屏幕上的宽度。
            //
            // 竖向不旋转排版（见 LyricsPanel.SetVerticalMode 的实测说明），
            // 所以这里要的是「主轴能给多长」而不是「内容多宽」：
            // 任务栏越短，能换的行越少。maxLogicalWidth 已经是主轴预算
            // （TaskbarWindow 对竖向传的是任务栏高度方向的区间），直接用。
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
        if (_isLyricsMode && widthChanged)
        {
            LyricsPanel.Width = newTitleContainerWidth;
        }

        // Refresh animations if layout bounds or text contents change
        if (textChanged || widthChanged)
        {
            UpdateMarquees();
        }

        // add space for playback controls if enabled and visible
        if (controlsWidth > 0)
        {
            logicalWidth += controlsWidth;
        }

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