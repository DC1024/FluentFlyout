// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using CommunityToolkit.Mvvm.ComponentModel;
using FluentFlyout.Classes;
using FluentFlyout.Classes.Settings;
using FluentFlyout.Classes.Utils;
using FluentFlyout.Controls;
using FluentFlyoutWPF.Classes;
using FluentFlyoutWPF.Models;
using FluentFlyoutWPF.Windows;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Windows;
using System.Xml.Serialization;

namespace FluentFlyoutWPF.ViewModels;

/**
 * User Settings data model.
 */
public partial class UserSettings : ObservableObject
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    // List of non-XmlIgnore property names
    private static readonly HashSet<string> PersistedPropertyNames =
    [
        .. typeof(UserSettings)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.CanWrite && property.GetCustomAttribute<XmlIgnoreAttribute>() is null)
            .Select(property => property.Name)
    ];

    /// <summary>
    /// Saved width of the settings window
    /// </summary>
    [ObservableProperty]
    public partial double SettingsWindowWidth { get; set; }

    /// <summary>
    /// Saved height of the settings window
    /// </summary>
    [ObservableProperty]
    public partial double SettingsWindowHeight { get; set; }

    /// <summary>
    /// Saved horizontal position of the settings window
    /// </summary>
    [ObservableProperty]
    public partial double SettingsWindowLeft { get; set; }

    /// <summary>
    /// Saved vertical position of the settings window
    /// </summary>
    [ObservableProperty]
    public partial double SettingsWindowTop { get; set; }

    /// <summary>
    /// Saved window state of the settings window
    /// </summary>
    [ObservableProperty]
    public partial WindowState SettingsWindowState { get; set; }

    /// <summary>
    /// Use a compact layout
    /// </summary>
    [ObservableProperty]
    public partial bool CompactLayout { get; set; }

    /// <summary>
    /// Flyout Target Display
    /// </summary>
    [ObservableProperty]
    public partial int FlyoutSelectedMonitor { get; set; }

    /// <summary>
    /// Flyout position on screen
    /// </summary>
    [ObservableProperty]
    public partial int Position { get; set; }

    /// <summary>
    /// Scale for flyout animation speed
    /// </summary>
    [ObservableProperty]
    public partial int FlyoutAnimationSpeed { get; set; }

    /// <summary>
    /// Show player information in the flyout
    /// </summary>
    [ObservableProperty]
    public partial bool PlayerInfoEnabled { get; set; }

    /// <summary>
    /// Enable repeat button
    /// </summary>
    [ObservableProperty]
    public partial bool RepeatEnabled { get; set; }

    /// <summary>
    /// Enable shuffle button
    /// </summary>
    [ObservableProperty]
    public partial bool ShuffleEnabled { get; set; }

    /// <summary>
    /// Start minimized to tray when Windows starts
    /// </summary>
    [ObservableProperty]
    public partial bool Startup { get; set; }

    /// <summary>
    /// MediaFlyout Always Display
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsDurationEditable))]
    public partial bool MediaFlyoutAlwaysDisplay { get; set; }

    [XmlIgnore] public bool IsDurationEditable => !MediaFlyoutAlwaysDisplay;

    /// <summary>
    /// Flyout display duration (milliseconds)
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DurationText))]
    public partial int Duration { get; set; }

    [XmlIgnore]
    public string DurationText
    {
        get => Duration.ToString();
        set
        {
            if (int.TryParse(value, out var result))
            {
                Duration = result switch
                {
                    > 10000 => 10000,
                    < 0 => 0,
                    _ => result
                };
            }
            else
            {
                Duration = 3000;
            }

            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Enable the 'Next Up' flyout (experimental)
    /// </summary>
    [ObservableProperty]
    public partial bool NextUpEnabled { get; set; }

    /// <summary>
    /// 'Next Up' flyout display duration (milliseconds)
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NextUpDurationText))]
    public partial int NextUpDuration { get; set; }

    [XmlIgnore]
    public string NextUpDurationText
    {
        get => NextUpDuration.ToString();
        set
        {
            if (int.TryParse(value, out var result))
            {
                NextUpDuration = result switch
                {
                    > 10000 => 10000,
                    < 0 => 0,
                    _ => result
                };
            }
            else
            {
                NextUpDuration = 2000;
            }

            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Tray icon left-click behavior
    /// </summary>
    [ObservableProperty]
    [XmlElement(ElementName = "nIconLeftClick")]
    public partial int NIconLeftClick { get; set; }

    /// <summary>
    /// Center the title and artist text
    /// </summary>
    [ObservableProperty]
    public partial bool CenterTitleArtist { get; set; }

    /// <summary>
    /// Animation easing style index
    /// </summary>
    [ObservableProperty]
    public partial int FlyoutAnimationEasingStyle { get; set; }

    /// <summary>
    /// Enable lock keys flyout (shows Caps/Num/Scroll status)
    /// </summary>
    [ObservableProperty]
    public partial bool LockKeysEnabled { get; set; }

    [ObservableProperty]
    public partial bool LockKeysCapsEnabled { get; set; }

    [ObservableProperty]
    public partial bool LockKeysNumEnabled { get; set; }

    [ObservableProperty]
    public partial bool LockKeysScrollEnabled { get; set; }

    /// <summary>
    /// Lock keys flyout display duration (milliseconds)
    /// </summary>

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LockKeysDurationText))]
    public partial int LockKeysDuration { get; set; }

    [XmlIgnore]
    public string LockKeysDurationText
    {
        get => LockKeysDuration.ToString();
        set
        {
            if (int.TryParse(value, out var result))
            {
                LockKeysDuration = result switch
                {
                    > 10000 => 10000,
                    < 0 => 0,
                    _ => result
                };
            }
            else
            {
                LockKeysDuration = 2000;
            }

            OnPropertyChanged();
        }
    }

    /// <summary>
    /// App theme. 0 for default, 1 for light, 2 for dark.
    /// </summary>
    [ObservableProperty]
    public partial int AppTheme { get; set; }

    /// <summary>
    /// Enable media flyout
    /// </summary>
    [ObservableProperty]
    public partial bool MediaFlyoutEnabled { get; set; }

    /// <summary>
    /// Exclude volume keys from triggering media flyout
    /// </summary>
    [ObservableProperty]
    public partial bool MediaFlyoutVolumeKeysExcluded { get; set; }

    /// <summary>
    /// Use symbol-style tray icon
    /// </summary>
    [ObservableProperty]
    [XmlElement(ElementName = "nIconSymbol")]
    public partial bool NIconSymbol { get; set; }

    /// <summary>
    /// Hide tray icon completely
    /// </summary>
    [ObservableProperty]
    public partial bool NIconHide { get; set; }

    /// <summary>
    /// Disable flyout when a DirectX exclusive fullscreen app is detected
    /// </summary>
    [ObservableProperty]
    public partial bool DisableIfFullscreen { get; set; }

    /// <summary>
    /// Use bold symbol and font in the lock keys flyout
    /// </summary>
    [ObservableProperty]
    [XmlElement(ElementName = "LockKeysBoldUI")]
    public partial bool LockKeysBoldUi { get; set; }

    /// Selects which monitor to use for the lock keys flyout when multiple monitors are in use.
    /// 0 = Default behavior, 1 = Monitor containing the focused window, 2 = Monitor containing the cursor.
    [ObservableProperty]
    public partial int LockKeysMonitorPreference { get; set; }

    /// <summary>
    /// Determines if the user has updated to a new version
    /// </summary>
    [ObservableProperty]
    public partial string LastKnownVersion { get; set; }

    /// <summary>
    /// Show seekbar if the player supports it
    /// </summary>
    [ObservableProperty]
    public partial bool SeekbarEnabled { get; set; }

    /// <summary>
    /// Pause other media sessions when focusing a new one
    /// </summary>
    [ObservableProperty]
    public partial bool PauseOtherSessionsEnabled { get; set; }

    [ObservableProperty]
    public partial string PinnedSessionId { get; set; }

    /// <summary>
    /// Enable subtle animations for the lock keys flyout indicator
    /// </summary>
    [ObservableProperty]
    public partial bool LockKeysAnimated { get; set; }

    /// <summary>
    /// Show LockKeys flyout when the Insert key is pressed
    /// </summary>
    [ObservableProperty]
    public partial bool LockKeysInsertEnabled { get; set; }

    /// <summary>
    /// Preset for media flyout background blur styles
    /// </summary>
    [ObservableProperty]
    public partial int MediaFlyoutBackgroundBlur { get; set; }

    /// <summary>
    /// Enable acrylic blur effect on the flyout window
    /// </summary>
    [ObservableProperty]
    public partial bool MediaFlyoutAcrylicWindowEnabled { get; set; }

    /// <summary>
    /// Enable acrylic blur effect on the Next Up window
    /// </summary>
    [ObservableProperty]
    public partial bool NextUpAcrylicWindowEnabled { get; set; }

    /// <summary>
    /// Enable acrylic blur effect on the Lock Keys window
    /// </summary>
    [ObservableProperty]
    public partial bool LockKeysAcrylicWindowEnabled { get; set; }

    [ObservableProperty]
    public partial bool VolumeMixerAcrylicWindowEnabled { get; set; }

    /// <summary>
    /// User's preferred app language (e.g., "system" for system default)
    /// </summary>
    [ObservableProperty]
    public partial string AppLanguage { get; set; }

    /// <summary>
    /// Language Options
    /// </summary>
    [XmlIgnore]
    public ObservableCollection<LanguageOption> LanguageOptions { get; } = [];

    [XmlIgnore]
    [ObservableProperty]
    public partial LanguageOption SelectedLanguage { get; set; }

    [XmlIgnore]
    [ObservableProperty]
    public partial FlowDirection FlowDirection { get; set; }

    [XmlIgnore]
    [ObservableProperty]
    public partial string FontFamily { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the taskbar widget is enabled
    /// </summary>
    [ObservableProperty]
    public partial bool TaskbarWidgetEnabled { get; set; }

    /// <summary>
    /// Widget Target Display
    /// </summary>
    /// <remarks>
    /// **已降级为兼容字段**：真正的主键是 <see cref="TaskbarWidgetTargetMonitorId"/>。
    /// 这里只剩两个用途——旧配置的一次性迁移来源，以及跟随主屏时给设置页一个初始选中位置。
    /// 不要再拿它去索引 <c>MonitorUtil.GetMonitors()</c>：显示器增删后序号会整体前移，
    /// 结果是控件静默跑到另一块屏上（这是本字段被替换的原因）。
    /// </remarks>
    [ObservableProperty]
    public partial int TaskbarWidgetSelectedMonitor { get; set; }

    /// <summary>
    /// 目标显示器的设备接口路径
    /// （<c>\\?\DISPLAY#TCL2702#5&amp;2ec52e58&amp;0&amp;UID4355#{接口类GUID}</c>）。
    /// 空串表示「跟随主显示器」或「尚未从旧序号迁移」。
    /// </summary>
    /// <remarks>
    /// 用设备路径而不是序号/GDI 名字：路径里带着接口实例，换分辨率、改缩放、改相对位置、
    /// 增删其它显示器都不会变；只有在**换到另一个物理接口**时才会变，
    /// 那种情况由 <see cref="TaskbarWidgetTargetMonitorSignature"/> 兜底。
    /// </remarks>
    [ObservableProperty]
    public partial string TaskbarWidgetTargetMonitorId { get; set; }

    /// <summary>
    /// 目标显示器的模糊重连签名（<c>友好名|宽x高|left,top</c>）。
    /// 设备路径对不上时用它找回「同一块屏」。
    /// </summary>
    [ObservableProperty]
    public partial string TaskbarWidgetTargetMonitorSignature { get; set; }

    /// <summary>
    /// Autohide Widget after a few milliseconds after pause 
    /// </summary>
    [ObservableProperty]
    public partial bool TaskbarWidgetAutoHide { get; set; }

    /// <summary>
    /// Gets or sets the position of the taskbar widget, represented as an integer value.
    /// 0: Left, 1: Center, 2: Right
    /// </summary>
    [ObservableProperty]
    public partial int TaskbarWidgetPosition { get; set; }

    /// <summary>
    /// Determines whether padding should be applied to the taskbar widget for the native Windows Widgets button
    /// </summary>
    [ObservableProperty]
    public partial bool TaskbarWidgetPadding { get; set; }

    /// <summary>
    /// Manual padding value in pixels applied to the taskbar widget
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TaskbarWidgetManualPaddingText))]
    public partial int TaskbarWidgetManualPadding { get; set; }

    [XmlIgnore]
    public string TaskbarWidgetManualPaddingText
    {
        get => TaskbarWidgetManualPadding.ToString();
        set
        {
            if (int.TryParse(value, out var result))
            {
                TaskbarWidgetManualPadding = result switch
                {
                    > 9999 => 9999,
                    < -9999 => -9999,
                    _ => result
                };
            }
            else
            {
                TaskbarWidgetManualPadding = 0;
            }

            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Gets or sets a value indication whether the taskbar widget background should have a blur effect
    /// </summary>
    [ObservableProperty]
    public partial bool TaskbarWidgetBackgroundBlur { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the taskbar widget should be completely hidden from view when no media is playing.
    /// </summary>
    [ObservableProperty]
    public partial bool TaskbarWidgetHideCompletely { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the taskbar widget should always be sized at its
    /// maximum width, so right-aligned controls don't shift when the song changes.
    /// </summary>
    [ObservableProperty]
    public partial bool TaskbarWidgetFixedWidth { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the pause icon overlay should be completely hidden from view.
    /// </summary>
    [ObservableProperty]
    public partial bool TaskbarWidgetShowPauseOverlay { get; set; }

    /// <summary>
    /// Whether taskbar widget controls (pause, previous, next) are enabled.
    /// </summary>
    [ObservableProperty]
    public partial bool TaskbarWidgetControlsEnabled { get; set; }

    /// <summary>
    /// Position of the taskbar widget controls. 0: Left, 1: Right
    /// </summary>
    [ObservableProperty]
    public partial int TaskbarWidgetControlsPosition { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the taskbar widget should play animations.
    /// </summary>
    [ObservableProperty]
    public partial bool TaskbarWidgetAnimated { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the taskbar widget scrolling text (marquee) is enabled for long titles.
    /// </summary>
    [ObservableProperty]
    public partial bool TaskbarWidgetScrollingEnabled { get; set; }

    /// <summary>
    /// Controls whether scrolling over the taskbar widget adjusts master or player volume.
    /// 0: Off, 1: Master volume, 2: Player volume
    /// </summary>
    [ObservableProperty]
    public partial int TaskbarWidgetScrollVolumeMode { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the taskbar widget scrolling text should loop forever.
    /// </summary>
    [ObservableProperty]
    public partial bool TaskbarWidgetScrollingTextLoopForever { get; set; }

    /// <summary>
    /// Gets or sets the speed of the taskbar widget scrolling text.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TaskbarWidgetScrollingTextSpeedText))]
    public partial int TaskbarWidgetScrollingTextSpeed { get; set; }

    [XmlIgnore]
    public string TaskbarWidgetScrollingTextSpeedText
    {
        get => TaskbarWidgetScrollingTextSpeed.ToString();
        set
        {
            if (int.TryParse(value, out var result))
            {
                TaskbarWidgetScrollingTextSpeed = result switch
                {
                    > 100 => 100,
                    < 1 => 1,
                    _ => result
                };
            }
            else
            {
                TaskbarWidgetScrollingTextSpeed = 20;
            }

            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether the taskbar visualizer is enabled.
    /// </summary>
    /// <remarks>For now, this requires Premium and Taskbar Widget to be enabled.</remarks>
    [ObservableProperty]
    public partial bool TaskbarVisualizerEnabled { get; set; }

    // ---- 歌词（P4）----
    //
    // 为什么不按实施计划 P4-6 的设想抽成嵌套类 LyricsSettings：
    // UserSettings 的持久化完全依赖 XmlSerializer + 反射遍历公共可写属性
    // （见 PersistedPropertyNames 与 OnPropertyChangedSaveSettings）。
    // 嵌套类会序列化成嵌套元素，而「旧配置文件缺字段用默认值」这条
    // 靠的正是 XmlSerializer 对**缺失的标量元素**静默取默认值的行为——
    // 嵌套对象缺失时整个对象是 null，得额外写一堆空判断，
    // 迁移风险反而比平铺更高。平铺 + 前缀命名同样能避免字段互相污染。
    //
    // 默认值刻意是「关」。歌词会抢占任务栏空间并引入网络请求，
    // 老用户升级后不该发现自己的任务栏突然变了样。

    /// <summary>
    /// 是否在任务栏小组件里显示歌词（替换标题/艺术家两行）。
    /// </summary>
    [ObservableProperty]
    public partial bool LyricsEnabled { get; set; }

    /// <summary>
    /// 歌词字号（DIP）。实际值会被任务栏高度压缩，见 <c>LyricsPanelMetrics.Create</c>。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LyricsFontSizeText))]
    public partial int LyricsFontSize { get; set; }

    [XmlIgnore]
    public string LyricsFontSizeText
    {
        get => LyricsFontSize.ToString();
        set
        {
            if (int.TryParse(value, out var result))
            {
                LyricsFontSize = result switch
                {
                    > 20 => 20,
                    < 9 => 9,
                    _ => result
                };
            }
            else
            {
                LyricsFontSize = 13;
            }

            OnPropertyChanged();
        }
    }

    /// <summary>
    /// 是否显示翻译行。仅在双行模式下有效。
    /// </summary>
    [ObservableProperty]
    public partial bool LyricsShowTranslation { get; set; }

    /// <summary>
    /// 是否启用逐字扫描高亮。关掉后当前行整体高亮，CPU 占用更低。
    /// </summary>
    [ObservableProperty]
    public partial bool LyricsWordScanEnabled { get; set; }

    /// <summary>
    /// 是否强制单行（关掉翻译/下一行那一行）。
    /// </summary>
    /// <remarks>
    /// 与「系统自动降级单行」是两件事：后者发生在任务栏太矮装不下两行时
    /// （见 <c>LyricsMetricsPolicy</c> 的 <c>forceSingleLine</c>），
    /// 前者是用户的明确选择，在竖向任务栏上也生效。
    ///
    /// 默认 <c>false</c>（自动）：多数用户没理由主动关掉第二行，
    /// 而自动降级已经能覆盖小任务栏这个主要场景。
    /// </remarks>
    [ObservableProperty]
    public partial bool LyricsSingleLine { get; set; }

    /// <summary>
    /// 歌词主行颜色，十六进制字符串（<c>#RRGGBB</c> / <c>#AARRGGBB</c> / <c>#RGB</c>）。
    /// 空字符串表示跟随系统主题。
    /// </summary>
    /// <remarks>
    /// 存字符串而不是 <c>Color</c> 或 <c>uint</c>：<c>XmlSerializer</c> 能直接
    /// 序列化 string，而 <c>Color</c> 需要额外的类型转换器。解析交给
    /// Core 的 <c>LyricsColorScheme.Parse</c>（有单测覆盖非法输入）。
    ///
    /// 这个字段**要持久化**（与 <see cref="LyricsOffsetMilliseconds"/> 相反）：
    /// 它是稳定的用户偏好，不像偏移那样按曲目变化。
    /// </remarks>
    [ObservableProperty]
    public partial string LyricsColor { get; set; }

    /// <summary>
    /// 歌词文本对齐方式：0 = 左，1 = 居中，2 = 右。
    /// </summary>
    /// <remarks>
    /// 存 int 而不是枚举：<c>ComboBox.SelectedIndex</c> 直接绑这个值，
    /// 不需要值转换器；越界值由 <c>LyricsAlignmentPolicy.FromSetting</c> 归一化。
    /// </remarks>
    [ObservableProperty]
    public partial int LyricsAlignment { get; set; }

    /// <summary>
    /// 当前曲目的偏移量（毫秒）。正值让歌词提前，负值延后。
    /// </summary>
    /// <remarks>
    /// 这个字段只承载托盘菜单的**当前值**，真正的持久化在
    /// <c>TrackLyricOffsetStore</c> 的 SQLite 里——它按「曲目 + 歌词源」
    /// 存，一个标量字段装不下。放在这里是为了让托盘菜单能双向绑定，
    /// 切歌时由 <c>MainWindow</c> 把它同步成新曲目的值。
    ///
    /// <b>刻意加 <see cref="XmlIgnoreAttribute"/></b>：真值在 SQLite，
    /// 这个字段每次换歌都会被覆盖。如果它进了 settings.xml，
    /// 重启后 <c>CompleteInitialization</c> 之前的那次反序列化会把
    /// 上一首的偏移写进内存，随后被帧泵读到——表现为「重启后歌词整体偏了
    /// 几十毫秒，切一次歌才好」。
    /// </remarks>
    [XmlIgnore]
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LyricsOffsetText))]
    public partial int LyricsOffsetMilliseconds { get; set; }

    [XmlIgnore]
    public string LyricsOffsetText
    {
        get => LyricsOffsetMilliseconds.ToString();
        set
        {
            if (int.TryParse(value, out var result))
            {
                LyricsOffsetMilliseconds = Math.Clamp(result, -5000, 5000);
            }
            else
            {
                LyricsOffsetMilliseconds = 0;
            }

            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Returns whether app filtering is enabled or disabled.
    /// </summary>
    [ObservableProperty]
    public partial bool AppFilteringEnabled { get; set; }

    /// <summary>
    /// Returns the active filtering mode. 0 for Whitelist, 1 for Blacklist.
    /// </summary>
    [ObservableProperty]
    public partial int AppFilteringMode { get; set; }

    /// <summary>
    /// Returns a list of apps that are allowed to display media/update the taskbar.
    /// </summary>
    [ObservableProperty]
    public partial ObservableCollection<string> AllowedApps { get; set; }

    /// <summary>
    /// Returns a list of apps that are NOT allowed to display media/update the taskbar.
    /// </summary>
    [ObservableProperty]
    public partial ObservableCollection<string> BlockedApps { get; set; }

    /// <summary>
    /// Position of the visualizer, where 0 and 1 are to the left or right of the widget.
    /// </summary>
    [ObservableProperty]
    public partial int TaskbarVisualizerPosition { get; set; }

    /// <summary>
    /// Whether the visualizer is clickable to open the visualizer settings page.
    /// </summary>
    [ObservableProperty]
    public partial bool TaskbarVisualizerClickable { get; set; }

    /// <summary>
    /// Indicates whether the visualizer has content to display, and is not persisted since it's only relevant at runtime.
    /// </summary>
    [XmlIgnore]
    [ObservableProperty]
    public partial bool TaskbarVisualizerHasContent { get; set; }

    /// <summary>
    /// The number of visualizer bars to display.
    /// </summary>
    [ObservableProperty]
    public partial int TaskbarVisualizerBarCount { get; set; }

    /// <summary>
    /// Whether the visualizer should be symmetrical/mirrored.
    /// </summary>
    [ObservableProperty]
    public partial bool TaskbarVisualizerCenteredBars { get; set; }

    /// <summary>
    /// Gets or sets whether a bar baseline is shown.
    /// </summary>
    [ObservableProperty]
    public partial bool TaskbarVisualizerBaseline { get; set; }

    /// <summary>
    /// Gets or sets the audio sensitivity for the taskbar visualizer from 1 to 3, where 2 is the default.
    /// </summary>
    [ObservableProperty]
    public partial int TaskbarVisualizerAudioSensitivity { get; set; }

    /// <summary>
    /// Autohide taskbar. Only does something if TaskbarVisualizerBaseline is enabled (doesn't autohide by default).
    /// </summary>
    [ObservableProperty]
    public partial bool TaskbarVisualizerBaselineAutoHide { get; set; }

    [ObservableProperty]
    public partial bool VolumeControlEnabled { get; set; }

    [ObservableProperty]
    public partial bool VolumeFlyoutShortcutEnabled { get; set; }

    [ObservableProperty]
    public partial bool VolumeControlAboveMediaFlyout { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VolumeControlDurationText))]
    public partial int VolumeControlDuration { get; set; }

    [XmlIgnore]
    public string VolumeControlDurationText
    {
        get => VolumeControlDuration.ToString();
        set
        {
            if (int.TryParse(value, out var result))
            {
                VolumeControlDuration = result switch
                {
                    > 10000 => 10000,
                    < 0 => 0,
                    _ => result
                };
            }
            else
            {
                VolumeControlDuration = 3000;
            }

            OnPropertyChanged();
        }
    }

    [ObservableProperty]
    public partial bool VolumeMixerEnabled { get; set; }

    [ObservableProperty]
    public partial bool VolumeMixerHighlightActiveApps { get; set; }

    /// <summary>
    /// The audio peak level for the taskbar visualizer from 1 to 3.
    /// This is used to calibrate the visualizer bar height to the audio output.
    /// </summary>
    [ObservableProperty]
    public partial int TaskbarVisualizerAudioPeakLevel { get; set; }

    /// <summary>
    /// Gets whether premium features are unlocked (runtime only, not persisted)
    /// </summary>
    [XmlIgnore]
    [ObservableProperty]
    public partial bool IsPremiumUnlocked { get; set; }

    /// <summary>
    /// Gets or sets the opacity level of the acrylic blur effect.
    /// </summary>
    [ObservableProperty]
    public partial uint AcrylicBlurOpacity { get; set; }

    [ObservableProperty]
    public partial bool UseAlbumArtAsAccentColor { get; set; }

    /// <summary>
    /// Gets whether this is a Store version. Once false, always false (only if last known version was not null).
    /// </summary>
    [ObservableProperty]
    public partial bool IsStoreVersion { get; set; }

    [XmlIgnore]
    [ObservableProperty]
    public partial string PremiumPrice { get; set; }

    [XmlIgnore]
    [ObservableProperty]
    public partial string PremiumPurchaseAction { get; set; }

    /// <summary>
    /// Last time the program has sent an update notification in Unix seconds.
    /// </summary>
    [ObservableProperty]
    public partial long LastUpdateNotificationUnixSeconds { get; set; }

    /// <summary>
    /// Determines whether user will get Windows notifications when a new update is available.
    /// </summary>
    [ObservableProperty]
    public partial bool ShowUpdateNotifications { get; set; }

    /// <summary>
    /// Determines whether to use the legacy method for calculating taskbar width for widget positioning for compatibility with other taskbar mods
    /// </summary>
    [ObservableProperty]
    public partial bool LegacyTaskbarWidthEnabled { get; set; }

    [ObservableProperty]
    public partial Guid Uuid { get; set; }

    [XmlIgnore]
    [ObservableProperty]
    public partial Guid SessionId { get; set; } = Guid.NewGuid();

    [ObservableProperty]
    public partial bool AnonymousTelemetryAllowed { get; set; }

    [XmlIgnore]
    private bool _initializing = true;

    public UserSettings()
    {
        foreach (var supportedLanguage in LocalizationManager.SupportedLanguages)
        {
            LanguageOptions.Add(new LanguageOption(supportedLanguage.Key, supportedLanguage.Value));
        }

        CompactLayout = false;
        FlyoutSelectedMonitor = 0;
        Position = 0;
        FlyoutAnimationSpeed = 2;
        PlayerInfoEnabled = true;
        RepeatEnabled = false;
        ShuffleEnabled = false;
        Startup = true;
        Duration = 3000;
        NextUpEnabled = false;
        NextUpDuration = 2000;
        NIconLeftClick = 0;
        CenterTitleArtist = false;
        SettingsWindowWidth = 900;
        SettingsWindowHeight = 700;
        SettingsWindowLeft = double.NaN;
        SettingsWindowTop = double.NaN;
        SettingsWindowState = WindowState.Normal;
        FlyoutAnimationEasingStyle = 2;
        LockKeysEnabled = true;
        LockKeysCapsEnabled = true;
        LockKeysNumEnabled = true;
        LockKeysScrollEnabled = true;
        LockKeysDuration = 2000;
        AppTheme = 0;
        MediaFlyoutEnabled = true;
        MediaFlyoutAlwaysDisplay = false;
        MediaFlyoutVolumeKeysExcluded = false;
        NIconSymbol = false;
        NIconHide = false;
        DisableIfFullscreen = true;
        LockKeysBoldUi = false;
        LockKeysMonitorPreference = 0;
        LastKnownVersion = string.Empty;
        SeekbarEnabled = false;
        PauseOtherSessionsEnabled = false;
        PinnedSessionId = string.Empty;
        LockKeysAnimated = true;
        LockKeysInsertEnabled = true;
        MediaFlyoutBackgroundBlur = 0;
        AppLanguage = "system";
        FlowDirection = FlowDirection.LeftToRight;
        FontFamily = "Segoe UI Variable, Microsoft YaHei UI, Yu Gothic UI";
        MediaFlyoutAcrylicWindowEnabled = true;
        NextUpAcrylicWindowEnabled = true;
        LockKeysAcrylicWindowEnabled = true;
        VolumeMixerAcrylicWindowEnabled = true;
        TaskbarWidgetEnabled = false;
        TaskbarWidgetSelectedMonitor = 0;
        // 目标显示器：主键为空 = 跟随主屏。必须显式赋 string.Empty，
        // 否则老配置反序列化时（元素缺失）这两个字段会是 null，后续直接 NRE。
        TaskbarWidgetTargetMonitorId = string.Empty;
        TaskbarWidgetTargetMonitorSignature = string.Empty;
        TaskbarWidgetPosition = 0;
        TaskbarWidgetPadding = true;
        TaskbarWidgetManualPadding = 0;
        TaskbarWidgetBackgroundBlur = false;
        TaskbarWidgetHideCompletely = false;
        TaskbarWidgetFixedWidth = false;
        TaskbarWidgetShowPauseOverlay = true;
        TaskbarWidgetControlsEnabled = false;
        TaskbarWidgetControlsPosition = 1;
        TaskbarWidgetAnimated = true;
        TaskbarWidgetScrollingEnabled = false;
        TaskbarWidgetScrollVolumeMode = 1;
        TaskbarWidgetScrollingTextSpeed = 20;
        TaskbarWidgetScrollingTextLoopForever = false;
        TaskbarVisualizerEnabled = false;
        LyricsEnabled = false;
        LyricsFontSize = 13;
        LyricsShowTranslation = true;
        LyricsWordScanEnabled = true;
        LyricsSingleLine = false;
        LyricsColor = string.Empty;
        LyricsAlignment = 0;
        LyricsOffsetMilliseconds = 0;
        AppFilteringEnabled = false;
        AppFilteringMode = 0;
        TaskbarVisualizerPosition = 1;
        TaskbarVisualizerClickable = true;
        TaskbarVisualizerBarCount = 10;
        TaskbarVisualizerCenteredBars = false;
        TaskbarVisualizerBaseline = false;
        TaskbarVisualizerAudioSensitivity = 2;
        TaskbarVisualizerAudioPeakLevel = 3;
        TaskbarVisualizerBaselineAutoHide = false;
        VolumeControlEnabled = false;
        VolumeFlyoutShortcutEnabled = true;
        VolumeControlAboveMediaFlyout = false;
        VolumeControlDuration = 3000;
        VolumeMixerEnabled = false;
        VolumeMixerHighlightActiveApps = false;
        AcrylicBlurOpacity = 175;
        UseAlbumArtAsAccentColor = false;
        LastUpdateNotificationUnixSeconds = 0;
        ShowUpdateNotifications = true;
        LegacyTaskbarWidthEnabled = false;
        Uuid = Guid.NewGuid();
        AnonymousTelemetryAllowed = true;
        AllowedApps = [];
        BlockedApps = [];

        PropertyChanged += OnPropertyChangedSaveSettings;
    }

    [XmlIgnore]
    private CancellationTokenSource? _saveSettingsCts;

    private async void OnPropertyChangedSaveSettings(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_initializing) return;

        // Only trigger save if a persisted property changed
        if (string.IsNullOrEmpty(e.PropertyName) || !PersistedPropertyNames.Contains(e.PropertyName))
            return;

#if DEBUG
        Logger.Debug("Property '{PropertyName}' changed, scheduling settings save.", e.PropertyName);
#endif

        var newCts = new CancellationTokenSource();
        var oldCts = Interlocked.Exchange(ref _saveSettingsCts, newCts);
        oldCts?.Cancel();
        oldCts?.Dispose();

        try
        {
            await Task.Delay(500, newCts.Token);

            if (ReferenceEquals(_saveSettingsCts, newCts))
            {
                SettingsManager.SaveSettings();
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when replaced by a new property change
#if DEBUG
            Logger.Debug("Settings save canceled due to another property change.");
#endif
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "An error occurred while saving settings from property change.");
        }
        finally
        {
            if (Interlocked.CompareExchange(ref _saveSettingsCts, null, newCts) == newCts)
            {
                newCts.Dispose();
            }
        }
    }

    /// <summary>
    /// Called after deserialization to finalize initialization
    /// </summary>
    internal void CompleteInitialization()
    {
        _initializing = false;
    }

    partial void OnAppLanguageChanged(string oldValue, string newValue)
    {
        if (oldValue == newValue) return;
        SelectedLanguage = LanguageOptions.First(l => l.Tag == newValue);
    }

    partial void OnSelectedLanguageChanged(LanguageOption oldValue, LanguageOption newValue)
    {
        if (oldValue == newValue || _initializing) return;
        AppLanguage = newValue.Tag;
        LocalizationManager.ApplyLocalization();
    }

    /// <summary>
    /// Changes the application theme when the selection is changed. 0 for default, 1 for light, 2 for dark.
    /// </summary>
    partial void OnAppThemeChanged(int oldValue, int newValue)
    {
        if (oldValue == newValue || _initializing) return;
        ThemeManager.ApplyAndSaveTheme(newValue);
    }

    partial void OnNIconSymbolChanged(bool oldValue, bool newValue)
    {
        if (oldValue == newValue || _initializing) return;
        ThemeManager.UpdateTrayIcon();
    }

    partial void OnAcrylicBlurOpacityChanged(uint oldValue, uint newValue)
    {
        if (oldValue == newValue || _initializing) return;
        WindowBlurHelper.AdjustBlurOpacityForAllWindows(newValue);
    }

    partial void OnTaskbarWidgetEnabledChanged(bool oldValue, bool newValue)
    {
        if (oldValue == newValue || _initializing) return;

        // Check premium status before allowing widget to be enabled
        if (newValue && !SettingsManager.Current.IsPremiumUnlocked)
        {
            // Revert the change if premium is not unlocked
            TaskbarWidgetEnabled = false;
            return;
        }

        UpdateTaskbar();
    }

    // Update taskbar when relevant settings change
    partial void OnTaskbarWidgetPositionChanged(int oldValue, int newValue)
    {
        if (oldValue == newValue || _initializing) return;
        UpdateTaskbar();
    }

    /// <summary>
    /// 目标显示器变了（用户在设置页选了另一块屏）。
    /// </summary>
    /// <remarks>
    /// **必须立刻重排**：旧实现没有任何监听，只能等 <c>TaskbarWindow</c> 的 1.5s 定时器
    /// 发现变化。用户点完下拉框盯着一动不动的旧屏幕一秒半，只会得出「设置没生效」的结论。
    /// 这里只触发定位，不重刷媒体 UI（那条路在没播放时会把标题刷成 <c>-</c>）。
    /// </remarks>
    partial void OnTaskbarWidgetTargetMonitorIdChanged(string oldValue, string newValue)
    {
        if (oldValue == newValue || _initializing) return;

        MainWindow mainWindow = (MainWindow)Application.Current.MainWindow;
        mainWindow.taskbarWindow?.ApplyTargetChange();
    }

    partial void OnTaskbarWidgetManualPaddingChanged(int oldValue, int newValue)
    {
        if (oldValue == newValue || _initializing) return;
        UpdateTaskbar();
    }

    partial void OnTaskbarWidgetBackgroundBlurChanged(bool oldValue, bool newValue)
    {
        if (oldValue == newValue || _initializing) return;
        UpdateTaskbar();
    }

    partial void OnTaskbarWidgetHideCompletelyChanged(bool oldValue, bool newValue)
    {
        if (oldValue == newValue || _initializing) return;
        UpdateTaskbar();
    }

    partial void OnTaskbarWidgetFixedWidthChanged(bool oldValue, bool newValue)
    {
        if (oldValue == newValue || _initializing) return;
        UpdateTaskbar();
    }

    partial void OnTaskbarWidgetShowPauseOverlayChanged(bool oldValue, bool newValue)
    {
        if (oldValue == newValue || _initializing) return;
        UpdateTaskbar();
    }

    partial void OnTaskbarWidgetControlsEnabledChanged(bool oldValue, bool newValue)
    {
        if (oldValue == newValue || _initializing) return;
        UpdateTaskbar();
    }

    partial void OnTaskbarWidgetControlsPositionChanged(int oldValue, int newValue)
    {
        if (oldValue == newValue || _initializing) return;

        MainWindow mainWindow = (MainWindow)Application.Current.MainWindow;
        mainWindow.taskbarWindow?.Widget?.ReorderControls();
    }

    partial void OnTaskbarVisualizerPositionChanged(int oldValue, int newValue)
    {
        if (oldValue == newValue || _initializing) return;
        UpdateTaskbar();
    }

    partial void OnLegacyTaskbarWidthEnabledChanged(bool oldValue, bool newValue)
    {
        if (oldValue == newValue || _initializing) return;
        UpdateTaskbar();
    }

    private void UpdateTaskbar()
    {
        MainWindow mainWindow = (MainWindow)Application.Current.MainWindow;
        mainWindow.UpdateTaskbar();
    }

    partial void OnTaskbarWidgetScrollingEnabledChanged(bool oldValue, bool newValue)
    {
        if (oldValue == newValue || _initializing) return;
        UpdateTaskbarMarquees();
    }

    partial void OnTaskbarWidgetScrollingTextLoopForeverChanged(bool oldValue, bool newValue)
    {
        if (oldValue == newValue || _initializing) return;
        UpdateTaskbarMarquees();
    }

    partial void OnTaskbarWidgetScrollingTextSpeedChanged(int oldValue, int newValue)
    {
        if (oldValue == newValue || _initializing) return;
        UpdateTaskbarMarquees();
    }

    private void UpdateTaskbarMarquees()
    {
        MainWindow mainWindow = (MainWindow)Application.Current.MainWindow;
        var widget = mainWindow.taskbarWindow?.Widget;
        if (widget == null) return;
        widget.Dispatcher.Invoke(widget.UpdateMarquees);
    }

    /// <summary>
    /// 歌词开关变化：启停取帧泵 + 让任务栏窗口重建布局。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 与频谱可视化**可以同时开启**：两块并排占位，由
    /// <c>TaskbarWindow.ComputeWidgetBudget</c> 通过
    /// <c>GetOccupiedIntervals</c> 互相避让（频谱区间本来就在占用列表里）。
    /// </para>
    /// <para>
    /// 这里曾经有一条互斥规则（实施计划 P4-6：开歌词就强制关频谱），
    /// 理由是「两者都占紧邻小组件的那一块，同时开会挤满任务栏」。
    /// 实测这个理由不成立：频谱固定 84 DIP、歌词块按内容收缩，
    /// 2560 宽的任务栏上合计只占 22%，且避让算法保证两块不重叠。
    /// 代价却是实实在在的——用户在频谱页打开开关，回到歌词页发现歌词
    /// 开关自己变成了关，完全不知道是谁关的。为省 84 DIP 让用户丢掉一个
    /// 正在用的功能，不值得。
    /// </para>
    /// </remarks>
    partial void OnLyricsEnabledChanged(bool oldValue, bool newValue)
    {
        if (oldValue == newValue || _initializing) return;

        // 歌词依赖任务栏小组件这块地方。小组件没开时开歌词没有任何显示位置，
        // 直接拒绝比「开了但看不见」更容易让用户理解。
        if (newValue && !TaskbarWidgetEnabled)
        {
            LyricsEnabled = false;
            return;
        }

        // 这里**刻意没有** !IsPremiumUnlocked 的短路回滚，
        // 与 OnTaskbarWidgetEnabledChanged 不同——这是决定不是遗漏。
        // 上面那条判断已经把未解锁用户拦住了：TaskbarWidgetEnabled 在
        // 未解锁时恒为 false（由它自己的钩子回滚），所以走不到这里。
        // 若将来给歌词加独立门控，必须在这里补一条对称的回滚，
        // 否则会出现「开关开着但没歌词」。完整清单见
        // Pages/LyricsPage.xaml.cs 的类级 remarks。

        MainWindow mainWindow = (MainWindow)Application.Current.MainWindow;
        mainWindow.ApplyLyricsSettings();
        UpdateTaskbar();
    }

    /// <summary>
    /// 频谱开关变化：启停音频采集 + 让任务栏窗口重建布局。
    ///
    /// 与歌词**不再互斥**，理由见 <see cref="OnLyricsEnabledChanged"/> 的
    /// remarks。这个钩子只负责频谱自己那半边。
    /// </summary>
    partial void OnTaskbarVisualizerEnabledChanged(bool oldValue, bool newValue)
    {
        if (oldValue == newValue || _initializing) return;

        TaskbarVisualizerControl.OnTaskbarVisualizerEnabledChanged(newValue);
        UpdateTaskbar();
    }

    /// <summary>
    /// 字号/翻译/逐字扫描变化：只影响排版，不需要重新检索歌词。
    /// </summary>
    partial void OnLyricsFontSizeChanged(int oldValue, int newValue)
    {
        if (oldValue == newValue || _initializing) return;
        ApplyLyricsPresentation();
    }

    partial void OnLyricsShowTranslationChanged(bool oldValue, bool newValue)
    {
        if (oldValue == newValue || _initializing) return;
        ApplyLyricsPresentation();
    }

    partial void OnLyricsWordScanEnabledChanged(bool oldValue, bool newValue)
    {
        if (oldValue == newValue || _initializing) return;
        ApplyLyricsPresentation();
    }

    /// <summary>
    /// 单/双行切换：走 <see cref="ApplyLyricsPresentation"/>，
    /// 因为它改变的是度量（<c>forceSingleLine</c>），而不只是文本内容。
    /// </summary>
    partial void OnLyricsSingleLineChanged(bool oldValue, bool newValue)
    {
        if (oldValue == newValue || _initializing) return;
        ApplyLyricsPresentation();
    }

    /// <summary>
    /// 颜色变化：只换画笔，不重排、不重取歌词。
    /// </summary>
    /// <remarks>
    /// 颜色不需要 <c>RefreshLyricsMetrics</c>——它不影响字号与行高，
    /// 只影响三个 <c>Foreground</c>。所以这里不调
    /// <see cref="ApplyLyricsPresentation"/>（那个会顺带刷度量），
    /// 直接让控件重涂一次。
    /// </remarks>
    partial void OnLyricsColorChanged(string oldValue, string newValue)
    {
        if (oldValue == newValue || _initializing) return;
        RefreshLyricsVisuals();
    }

    /// <summary>
    /// 对齐变化：只改位移/对齐，不影响度量。
    /// </summary>
    partial void OnLyricsAlignmentChanged(int oldValue, int newValue)
    {
        if (oldValue == newValue || _initializing) return;
        RefreshLyricsVisuals();
    }

    /// <summary>
    /// 让控件重涂一次配色与对齐。
    ///
    /// 与 <see cref="ApplyLyricsPresentation"/> 的区别：这里不碰度量，
    /// 所以不会触发 <c>ApplyMetrics</c> 里的重排。改个颜色不该让
    /// 整块任务栏重新 <c>SetWindowPos</c>。
    /// </summary>
    private void RefreshLyricsVisuals()
    {
        MainWindow mainWindow = (MainWindow)Application.Current.MainWindow;
        var widget = mainWindow.taskbarWindow?.Widget;
        if (widget == null) return;

        widget.Dispatcher.Invoke(widget.RefreshLyricsAppearance);
    }

    /// <summary>
    /// 偏移滑块变化：写回 <c>TrackLyricOffsetStore</c>（按曲目 + 歌词源存）。
    ///
    /// 这个字段本身**不是**持久化目标——它只是滑块的双向绑定载体。
    /// 真正的值在 SQLite 里，切歌时由 <c>MainWindow</c> 把它刷成新曲目的已存值，
    /// 所以这次变更不该被 <c>XmlSerializer</c> 当成用户设置存进 settings.xml。
    /// </summary>
    partial void OnLyricsOffsetMillisecondsChanged(int oldValue, int newValue)
    {
        if (oldValue == newValue || _initializing) return;

        // 切歌时由 SyncLyricsOffsetFromTrack 写进来的值，不该再存回去。
        if (_syncingLyricsOffset) return;

        MainWindow mainWindow = (MainWindow)Application.Current.MainWindow;
        mainWindow.lyricsService?.SaveCurrentOffsetMilliseconds(newValue);
    }

    /// <summary>
    /// 正在把「新曲目的已存偏移」同步到 <see cref="LyricsOffsetMilliseconds"/>。
    ///
    /// 用途是打断 <see cref="OnLyricsOffsetMillisecondsChanged"/> 里的回写——
    /// 否则每次换歌都会把 A 歌的偏移写到 B 歌头上。
    /// </summary>
    private bool _syncingLyricsOffset;

    /// <summary>
    /// 换歌后把偏移滑块刷成该曲目的已存值。
    /// </summary>
    internal void SyncLyricsOffsetFromTrack(int offsetMilliseconds)
    {
        _syncingLyricsOffset = true;
        try
        {
            LyricsOffsetMilliseconds = offsetMilliseconds;
        }
        finally
        {
            _syncingLyricsOffset = false;
        }
    }

    /// <summary>
    /// 把纯表现类设置刷到任务栏控件上。
    ///
    /// 与 <see cref="ApplyLyricsSettings"/> 的分工：这里**不**碰取帧泵，
    /// 也不重建窗口——改个字号不该让歌词重新检索一遍。
    /// </summary>
    private void ApplyLyricsPresentation()
    {
        MainWindow mainWindow = (MainWindow)Application.Current.MainWindow;
        var widget = mainWindow.taskbarWindow?.Widget;
        if (widget == null) return;

        widget.Dispatcher.Invoke(() =>
        {
            widget.RefreshLyricsMetrics();
            widget.RefreshLyricsOptions();
        });
    }

    partial void OnTaskbarVisualizerBarCountChanged(int oldValue, int newValue)
    {
        if (oldValue == newValue || _initializing) return;
        Visualizer.ResizeBarList(newValue);
    }

    partial void OnTaskbarVisualizerBaselineChanged(bool oldValue, bool newValue)
    {
        if (oldValue == newValue || _initializing || newValue == false) return;
        TaskbarVisualizerHasContent = true;
    }

    partial void OnTaskbarVisualizerBaselineAutoHideChanged(bool oldValue, bool newValue)
    {
        if (oldValue == newValue || _initializing) return;
        // If newValue is true, refresh the visualizer by hiding it:
        // if audio is playing, it will be shown again, if not, it will remain hidden.
        TaskbarVisualizerHasContent = !newValue;
    }

    partial void OnUseAlbumArtAsAccentColorChanged(bool oldValue, bool newValue)
    {
        if (oldValue == newValue || _initializing) return;
        BitmapHelper.GetDominantColors(1);
    }

    partial void OnAppFilteringEnabledChanged(bool oldValue, bool newValue)
    {
        if (oldValue == newValue || _initializing) return;

        MainWindow mainWindow = (MainWindow)Application.Current.MainWindow;
        mainWindow?.RefreshFilteredMedia();
    }

    partial void OnAppFilteringModeChanged(int oldValue, int newValue)
    {
        if (oldValue == newValue || _initializing) return;

        MainWindow mainWindow = (MainWindow)Application.Current.MainWindow;
        mainWindow?.RefreshFilteredMedia();
    }

    partial void OnVolumeMixerHighlightActiveAppsChanged(bool oldValue, bool newValue)
    {
        if (oldValue == newValue || _initializing) return;

        // Check premium status before allowing highlight to be enabled
        if (newValue && !SettingsManager.Current.IsPremiumUnlocked)
        {
            VolumeMixerHighlightActiveApps = false;
            return;
        }
    }

    partial void OnVolumeControlEnabledChanged(bool oldValue, bool newValue)
    {
        if (newValue == true || oldValue == newValue || _initializing) return;

        // re-enable native volume flyout
        VolumeMixerWindow.ShowVolumeOsd();
    }
}