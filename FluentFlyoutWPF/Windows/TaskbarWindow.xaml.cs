// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyout.Classes.Settings;
using FluentFlyout.Controls;
using FluentFlyout.Lyrics.Layout;
using FluentFlyoutWPF;
using FluentFlyoutWPF.Classes;
using FluentFlyoutWPF.Classes.Utils;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Media.Control;
using static FluentFlyout.Classes.NativeMethods;

namespace FluentFlyout.Windows;

/// <summary>
/// Interaction logic for TaskbarWindow.xaml
/// </summary>
public partial class TaskbarWindow : Window
{
    private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

    private const double SmallTaskbarDetectionThreshold = 40;

    private readonly DispatcherTimer _timer;
    private readonly int _nativeWidgetsPadding = 216;
    private readonly double _scale = 0.9;

    /// <summary>
    /// 歌词小组件的上限（DIP 视觉长度）——**横向专用**，也用作竖向的兜底值。
    ///
    /// 横向时是宽度上限；竖向时只是「拿不到任务栏长度时」的退路，正常路径
    /// 走 <see cref="ResolveMaxLengthDip"/> 按主轴实际长度现算。
    /// 实际定义在 <see cref="TaskbarWidgetControl.MaxLyricsWidgetLength"/>，
    /// 与 <c>MeasureDesiredWidth</c> 的默认参数共用一份。
    /// </summary>
    private const double MaxLyricsWidgetWidth = TaskbarWidgetControl.MaxLyricsWidgetLength;

    /// <summary>
    /// 任务栏占用区间的缓存有效期。
    ///
    /// <c>GetTaskbarXamlElementRect</c> 走的是跨进程 UIA 查询，单次最多等
    /// 1000ms（找元素）+ 500ms（取矩形）。<c>UpdatePosition</c> 由 1.5s 定时器
    /// 和每次换歌触发，再叠加歌词换行就是高频路径——必须缓存。
    /// 500ms 与 TaskbarLyrics 的 <c>RefreshInterval</c> 一致。
    /// </summary>
    private static readonly TimeSpan OccupiedCacheLifetime = TimeSpan.FromMilliseconds(500);

    private TaskbarOccupancySnapshot? _occupiedSnapshot;

    /// <summary>
    /// 一次任务栏占用区间的快照（物理像素，主轴坐标）。
    /// </summary>
    /// <remarks>
    /// 带 <see cref="TaskbarHandle"/> 与 <see cref="TaskbarRect"/> 是为了在
    /// 换屏、任务栏尺寸变化、Explorer 重启后**自动失效**——
    /// 这三种情况下旧的物理坐标毫无意义，而调用方不需要记得主动清缓存。
    /// </remarks>
    private readonly record struct TaskbarOccupancySnapshot(
        IntPtr TaskbarHandle,
        RECT TaskbarRect,
        bool IsVertical,
        IReadOnlyList<TaskbarInterval> Occupied,
        long Timestamp);

    private IntPtr _trayHandle;
    private AutomationElement? _widgetElement;
    private AutomationElement? _trayElement;
    private AutomationElement? _taskbarFrameElement;
    // reference to main window for flyout functions
    private MainWindow? _mainWindow;

    /// <summary>
    /// 本窗口这次要显示在哪块屏上。每次 <see cref="UpdatePosition"/> 都重新解析——
    /// 显示器随时可能被拔掉或接回来，缓存一分钟就会显示在错的屏上。
    /// </summary>
    private MonitorRef? _target;

    /// <summary>
    /// 上一次解析结果的指纹（状态 + 目标路径），只用来「变了才打日志」。
    ///
    /// 不能只比状态：<c>TargetMissing</c> 下面目标会从「2 号屏」换成主屏，
    /// 状态一样但实际落点变了，这种也必须记一条。
    /// </summary>
    private string _lastResolutionLogKey = string.Empty;

    /// <summary>上一次「目标屏没有任务栏」告警对应的设备路径，用于判重（该路径每 1.5s 会被重查一次）。</summary>
    private string? _lastMissingTaskbarWarning;

    /// <summary>当前显示器数量，供「只有一条任务栏时不用找第二块」的短路判断。</summary>
    private int _monitorCount;

    /// <summary>
    /// 待写回设置的目标（迁移 / 签名恢复的产物）。
    ///
    /// **不能在解析过程中直接写**：写设置会触发 OnChanged → 立即重排 → 又回到解析，
    /// 一圈下来多跑一整轮布局。统一在定位结束后 flush 一次。
    /// </summary>
    private MonitorRef? _targetToPersist;

    /// <summary>
    /// 上一次**排完版**时的目标设备路径，用于让 UIA/托盘句柄这类缓存跟着换屏失效。
    /// </summary>
    /// <remarks>
    /// 原来存的是设置里的序号（<c>_lastSelectedMonitor</c>），序号在插拔后可能「看起来没变
    /// 但指向了另一块屏」，缓存就不会失效。改成设备路径后语义才对齐。
    /// </remarks>
    private string? _lastPositionedTargetKey;

    private IntPtr _lastTaskbarHandle;
    private bool _positionUpdateInProgress;
    private bool _isClosing;
    private readonly Dictionary<string, Task> _pendingAutomationTasks = [];

    private GlobalSystemMediaTransportControlsSessionPlaybackStatus? _lastPlaybackStatus;
    private DispatcherTimer? _autoHideTimer;

    public TaskbarWindow()
    {
        WindowHelper.SetNoActivate(this);
        InitializeComponent();
        WindowHelper.SetTopmost(this);

        // Set DataContext for bindings
        DataContext = SettingsManager.Current;

        _timer = new DispatcherTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(1500); // slow auto-update for display changes
        _timer.Tick += (s, e) => UpdatePosition();
        _timer.Start();

        Show();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        HwndSource source = (HwndSource)PresentationSource.FromDependencyObject(this);
        source.AddHook(WindowProc);
    }

    private IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg is WM_DPICHANGED or WM_DPICHANGED_AFTERPARENT)
        {
            // WPF processes WM_DPICHANGED itself. Refresh placement after that layout
            // pass; PMv2 child windows receive the AFTERPARENT variant instead.
            Dispatcher.BeginInvoke(() =>
            {
                InvalidateMeasure();
                InvalidateArrange();
                InvalidateVisual();
                UpdateLayout();
                UpdatePosition();
            }, DispatcherPriority.Loaded);
        }

        // Some interface mods may collect information from all windows associated with the taskbar,
        // causing the widget and the entire taskbar to freeze.
        // For example, Nilesoft Shell and "Click on empty taskbar space" from Windhawk.
        // Therefore, we are preventing the propagation of this message.
        // Also prevents the widget from blocking taskbar's message processing, which is another source of freezes.
        switch (msg)
        {
            case 0x003D: // WM_GETOBJECT (Sent by Microsoft UI Automation to obtain information about an accessible object contained in a server application)
            case 0x0018: // WM_SHOWWINDOW
            case 0x0046: // WM_WINDOWPOSCHANGING - Triggers during alt-tabs, window changes
            case 0x0083: // WM_NCCALCSIZE - Can trigger layout storms
            case 0x0281: // WM_IME_SETCONTEXT - IME conflicts
            case 0x0282: // WM_IME_NOTIFY
                handled = true;
                return IntPtr.Zero;

                // Handle other known harmless messages that are sent when FluentFlyout starts, Windows locks, etc.
                // Needs testing
                //case 0x0047:
                //case 0x02B1:
                //case 0x001E:
                //case 0x0164:
                //case 0xC25F:
                //    handled = true;
                //    return IntPtr.Zero;
        }

        return IntPtr.Zero;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        SetupWindow();
        _mainWindow = (MainWindow)Application.Current.MainWindow;
        Widget.SetMainWindow(_mainWindow);

        // 窗口重建（Explorer 重启 / 换屏 / 改设置）后必须重新把歌词态推回去：
        // 新建的 Widget 是全新实例，IsLyricsMode 默认 false，
        // 不推的话用户会看到「歌词设置是开的，但任务栏只剩标题」。
        //
        // **不能走 _mainWindow.ApplyLyricsSettings()**：本窗口的 Show() 在
        // 构造函数最后一行，Loaded 会在构造函数返回**之前**同步触发，
        // 那一刻 MainWindow.taskbarWindow 还是旧值（或被置成了 null），
        // 于是 ApplyLyricsSettings 里的 `taskbarWindow?.Widget` 拿到 null 直接返回。
        // 这里直接对**自己**的 Widget 操作，不依赖那个字段的赋值时机。
        var active = _mainWindow.ApplyLyricsSettingsTo(Widget);
        _mainWindow.UpdateLyricsTrayMenu(active);

        // 对齐方式切换会改变歌词的宽度策略（非左对齐时撑满可用预算），
        // 必须无条件重排一次。不能靠 1.5s 的定时器兜底：那期间用户
        // 已经把「居中」点下去了，看到的却还是左对齐，会以为设置没生效。
        Widget.WidgetWidthStrategyChanged += (_, _) =>
            Dispatcher.BeginInvoke(() => UpdatePosition(), DispatcherPriority.Background);
    }

    /// <summary>
    /// 解析「这次该显示在哪块屏」，顺便把迁移/重连的产物排进待写队列。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 每次定位都重新解析，不做缓存：显示器可能在两次 tick 之间被拔掉或接回来，
    /// 缓存住「上一次的结论」就等于把 bug 换个地方藏。
    /// </para>
    /// <para>
    /// **不在这里写设置**。写 <c>TaskbarWidgetTargetMonitorId</c> 会触发
    /// <c>OnChanged → 立即重排</c>，而此刻我们正在定位流程中间，会重入整套布局。
    /// 需要落盘的结论先放进 <see cref="_targetToPersist"/>，等这一轮排完再 flush。
    /// </para>
    /// </remarks>
    private MonitorRef? ResolveTarget()
    {
        var monitors = MonitorUtil.GetMonitorRefs();
        _monitorCount = monitors.Count;

        var settings = SettingsManager.Current;
        var resolution = MonitorTargetResolver.Resolve(
            monitors,
            settings.TaskbarWidgetTargetMonitorId,
            settings.TaskbarWidgetTargetMonitorSignature,
            settings.TaskbarWidgetSelectedMonitor);

        LogResolution(resolution);
        QueueTargetPersistIfNeeded(resolution);

        _target = resolution.Target;
        return _target;
    }

    /// <summary>
    /// 只有「解析出来的目标」和「设置里存的」不一致时才排队写回。
    /// </summary>
    /// <remarks>
    /// <see cref="MonitorResolution.ShouldPersistTarget"/> 说「这次结论值得记住」，
    /// 但值没变就没必要写——写同样的值虽然不会触发通知（<c>SetProperty</c> 会挡），
    /// 但每个 tick 都走一遍字符串比较比走一遍设置写入便宜得多。
    /// </remarks>
    private void QueueTargetPersistIfNeeded(MonitorResolution resolution)
    {
        if (!resolution.ShouldPersistTarget || resolution.Target is not { } target)
            return;

        var settings = SettingsManager.Current;
        bool changed =
            !string.Equals(settings.TaskbarWidgetTargetMonitorId, target.DevicePath, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(settings.TaskbarWidgetTargetMonitorSignature, target.Signature, StringComparison.OrdinalIgnoreCase);

        if (changed)
            _targetToPersist = target;
    }

    /// <summary>
    /// 把 <see cref="_targetToPersist"/> 落到设置里。只在「这一轮已经排完版」之后调用。
    /// </summary>
    private void FlushPendingTargetPersist()
    {
        if (_targetToPersist is not { } target)
            return;

        _targetToPersist = null;

        var settings = SettingsManager.Current;
        if (string.Equals(settings.TaskbarWidgetTargetMonitorId, target.DevicePath, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(settings.TaskbarWidgetTargetMonitorSignature, target.Signature, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Logger.Info($"Persisting taskbar widget target display: '{target.FriendlyName}' ({target.DevicePath})");

        // 先写签名再写主键：主键的 OnChanged 会立刻触发一次重排，
        // 那一刻签名必须已经就位，否则模糊重连会读到上一条记录。
        settings.TaskbarWidgetTargetMonitorSignature = target.Signature;
        settings.TaskbarWidgetTargetMonitorId = target.DevicePath;
    }

    /// <summary>解析结果变化时才打日志，避免 1.5s 一条把日志刷满。</summary>
    private void LogResolution(MonitorResolution resolution)
    {
        string path = resolution.Target?.DevicePath ?? string.Empty;
        string key = $"{resolution.Status}|{path}";

        if (string.Equals(_lastResolutionLogKey, key, StringComparison.Ordinal))
            return;

        _lastResolutionLogKey = key;

        string name = resolution.Target?.FriendlyName ?? "none";

        switch (resolution.Status)
        {
            case MonitorResolutionStatus.TargetMissing:
                // 这是「不再静默换屏」的关键：用户选的那块屏不在了，控件临时回主屏，
                // 但设置里的选择**保留着**（ShouldPersistTarget 为 false），插回来自动恢复。
                Logger.Warn($"Saved taskbar widget display is not connected; showing on '{name}' until it returns.");
                break;

            case MonitorResolutionStatus.RecoveredBySignature:
                Logger.Info($"Taskbar widget display re-identified by signature: '{name}' ({path}).");
                break;

            case MonitorResolutionStatus.MigratedFromIndex:
                Logger.Info($"Taskbar widget display migrated from legacy index to '{name}' ({path}).");
                break;

            case MonitorResolutionStatus.NoMonitors:
                Logger.Warn("No monitors reported yet; taskbar widget keeps the main taskbar.");
                break;

            default:
                Logger.Debug($"Taskbar widget display resolved to '{name}' ({resolution.Status}).");
                break;
        }
    }

    /// <summary>这条任务栏是不是在目标显示器上。</summary>
    /// <remarks>
    /// 比 <c>devicePath</c> 而不是 <c>deviceId</c>（<c>\\.\DISPLAY1</c>）：后者只是 GDI 名字，
    /// 两块同型号屏在换线/换口后可能互换，而设备接口路径带着接口实例，不会。
    /// </remarks>
    private static bool IsTaskbarOnMonitor(IntPtr taskbarHandle, MonitorRef target)
    {
        if (taskbarHandle == IntPtr.Zero)
            return false;

        var monitor = MonitorUtil.GetMonitor(taskbarHandle);
        return !string.IsNullOrEmpty(monitor.devicePath) &&
               string.Equals(monitor.devicePath, target.DevicePath, StringComparison.OrdinalIgnoreCase);
    }

    private IntPtr GetSelectedTaskbarHandle(out bool isMainTaskbarSelected)
    {
        var selectedMonitor = ResolveTarget();
        isMainTaskbarSelected = true;

        // Get the main taskbar and check if it is on the selected monitor.
        var mainHwnd = FindWindow("Shell_TrayWnd", null);
        if (selectedMonitor is null || IsTaskbarOnMonitor(mainHwnd, selectedMonitor))
            return mainHwnd;

        if (_monitorCount == 1)
            return mainHwnd;

        isMainTaskbarSelected = false;
        if (_monitorCount == 2)
        {
            var hwnd = FindWindow("Shell_SecondaryTrayWnd", null);
            if (IsTaskbarOnMonitor(hwnd, selectedMonitor))
            {
                return hwnd;
            }
            else
            {
                // 「在所有显示器上显示任务栏」被关掉时本来就没有副栏，
                // 这是系统设置而不是缺陷——但目标屏上确实没有可挂的栏，得留痕。
                WarnMissingTaskbarOnTarget(selectedMonitor);
                isMainTaskbarSelected = true;
                return mainHwnd;
            }
        }

        // If there are more than two monitors, we will need to enumerate all existing windows
        // to find all Shell_SecondaryTrayWnd among them.

        IntPtr secondHwnd = IntPtr.Zero;
        StringBuilder className = new(256); // 256 is the maximum class name length
        IntPtr checkWindowClass(IntPtr wnd)
        {
            // 必须 ToString() 比较：StringBuilder.Equals(string) 走的是 object.Equals，
            // 永远返回 false —— 旧写法在 3 块屏以上时一次都匹配不到，直接退回主屏。
            if (GetClassName(wnd, className, className.Capacity) > 0 &&
                className.ToString() == "Shell_SecondaryTrayWnd" &&
                IsTaskbarOnMonitor(wnd, selectedMonitor))
            {
                return wnd;
            }
            return IntPtr.Zero;
        }

        // Get the threadId of the main taskbar and check all windows created in the same thread.
        // This is very fast, but in some cases Shell_TrayWnd and other Shell_SecondaryTrayWnd's may be created in different threads.
        // Actually, I couldn't achieve that kind of behavior.
        if (mainHwnd != IntPtr.Zero)
        {
            uint threadId = GetWindowThreadProcessId(mainHwnd, IntPtr.Zero);
            EnumThreadWindows(threadId, (wnd, param) =>
            {
                secondHwnd = checkWindowClass(wnd);
                if (secondHwnd != IntPtr.Zero)
                    return false; // stop

                return true;
            }, IntPtr.Zero);

            if (secondHwnd != IntPtr.Zero)
                return secondHwnd;
        }

        // If for some reason the taskbars were created in different threads or simply could not be found,
        // we try to find them among all existing windows.
        EnumWindows((wnd, param) =>
        {
            secondHwnd = checkWindowClass(wnd);
            if (secondHwnd != IntPtr.Zero)
                return false; // stop

            return true;
        }, IntPtr.Zero);

        if (secondHwnd != IntPtr.Zero)
            return secondHwnd;

        WarnMissingTaskbarOnTarget(selectedMonitor);
        isMainTaskbarSelected = true;
        return mainHwnd;
    }

    /// <summary>
    /// 目标屏上没有可挂的任务栏时留一条日志，**同一块屏只留一次**。
    /// </summary>
    /// <remarks>
    /// 这条路径由 1.5s 定时器反复走到，不判重的话日志会被刷满，
    /// 真正有用的信息反而被埋掉。
    /// </remarks>
    private void WarnMissingTaskbarOnTarget(MonitorRef target)
    {
        if (string.Equals(_lastMissingTaskbarWarning, target.DevicePath, StringComparison.OrdinalIgnoreCase))
            return;

        _lastMissingTaskbarWarning = target.DevicePath;
        Logger.Warn($"No taskbar found on target display '{target.FriendlyName}'; falling back to the main taskbar.");
    }

    private void SetupWindow()
    {
        try
        {
            var interop = new WindowInteropHelper(this);
            IntPtr taskbarWindowHandle = interop.Handle;

            //Background = _hitTestTransparent; // ensures that non-content areas also trigger MouseEnter event

            IntPtr taskbarHandle = GetSelectedTaskbarHandle(out bool isMainTaskbarSelected);
            ResetTaskbarCachesIfHandleChanged(taskbarHandle);

            // This prevents the window from trying to float above the taskbar as a separate entity
            int style = GetWindowLong(taskbarWindowHandle, GWL_STYLE);
            style = (style & ~WS_POPUP) | WS_CHILD;
            SetWindowLong(taskbarWindowHandle, GWL_STYLE, style);

            SetParent(taskbarWindowHandle, taskbarHandle); // if this window is created faster than the Taskbar is loaded, then taskbarHandle will be NULL.

            CalculateAndSetPosition(taskbarHandle, taskbarWindowHandle, isMainTaskbarSelected);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Taskbar Widget error during setup");
        }
    }

    private void UpdateWindowRegion(IntPtr windowHandle, params Rect[] rects)
    {
        IntPtr rgn = CreateRectRgn(0, 0, 0, 0);
        foreach (var r in rects)
        {
            // make sure rect is not empty - happens when setting elements to collapsed
            if (r == Rect.Empty)
                continue;

            IntPtr newRgn = CreateRectRgn((int)r.Left, (int)r.Top, (int)r.Right, (int)r.Bottom);
            if (newRgn == IntPtr.Zero)
            {
                Logger.Error($"Taskbar Widget error during CreateRectRgn({(int)r.Left}, {(int)r.Top}, {(int)r.Right}, {(int)r.Bottom}).");
                goto on_error;
            }

            if (CombineRgn(rgn, rgn, newRgn, 2 /*RGN_OR*/) == 0)
            {
                Logger.Error($"Taskbar Widget error during CombineRgn. Combined regions: {string.Join(", ", rects.Select(i => $"RECT({(int)i.Left}, {(int)i.Top}, {(int)i.Right}, {(int)i.Bottom})"))}");
                DeleteObject(newRgn);
                goto on_error;
            }

            DeleteObject(newRgn);
        }

        if (SetWindowRgn(windowHandle, rgn, true) == 0)
        {
            Logger.Error($"Taskbar Widget error during SetWindowRgn.");
            goto on_error;
        }

        // Simple debugging to display the window region:
#if false
        var whiteRect = WidgetCanvas.Children.Cast<FrameworkElement>().FirstOrDefault(e => e.Name == "test_border");
        if (whiteRect == null)
        {
            whiteRect = new System.Windows.Shapes.Rectangle() { Name = "test_border", Width = 20000, Height = 20000, Fill = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Black) };
            WidgetCanvas.Children.Add(whiteRect);
            Canvas.SetLeft(whiteRect, -10000);
            Canvas.SetTop(whiteRect, -10000);
        }
#endif

        return;

on_error:

// All regions that were not sent without errors to SetWindowRgn must be destroyed manually
        DeleteObject(rgn);
        if (SetWindowRgn(windowHandle, IntPtr.Zero, true) == 0)
            Logger.Error("Taskbar Widget error during window region reset.");
    }

    private void UpdatePosition()
    {
        if (_isClosing || MainWindow.ExplorerRestarting)
        {
            // Explorer is restarting -- do NOTHING
            return;
        }

        // Check premium status before allowing widget to be displayed
        if (!SettingsManager.Current.TaskbarWidgetEnabled || !SettingsManager.Current.IsPremiumUnlocked)
            return;

        try
        {
            var interop = new WindowInteropHelper(this);
            IntPtr taskbarHandle = GetSelectedTaskbarHandle(out bool isMainTaskbarSelected);
            ResetTaskbarCachesIfHandleChanged(taskbarHandle);

            if (interop.Handle == IntPtr.Zero)
            {
                if (MainWindow.ExplorerRestarting)
                {
                    Logger.Info("Skipping TaskbarWindow recovery during Explorer restart");
                    return;
                }

                _timer.Stop();

                Dispatcher.BeginInvoke(() =>
                {
                    try
                    {
                        _mainWindow?.RecreateTaskbarWindow();
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, "Failed to signal MainWindow to recover Taskbar Widget window");
                    }
                }, DispatcherPriority.Background);

                return;
            }

            // If the Taskbar was not found during initialization or another taskbar was selected,
            // then we need to set the Taskbar as the Parent here.
            if (GetParent(interop.Handle) != taskbarHandle)
            {
                SetParent(interop.Handle, taskbarHandle);
            }

            if (taskbarHandle != IntPtr.Zero && interop.Handle != IntPtr.Zero)
            {
                Dispatcher.BeginInvoke(() =>
                {
                    CalculateAndSetPosition(taskbarHandle, interop.Handle, isMainTaskbarSelected);
                }, DispatcherPriority.Background);
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Taskbar Widget error during position update");
        }
    }

    private void ResetTaskbarCachesIfHandleChanged(IntPtr taskbarHandle)
    {
        if (_lastTaskbarHandle == taskbarHandle)
            return;

        _lastTaskbarHandle = taskbarHandle;
        _trayHandle = IntPtr.Zero;
        _widgetElement = null;
        _trayElement = null;
        _taskbarFrameElement = null;
        _pendingAutomationTasks.Clear();
    }

    /// <summary>
    /// 缓存（托盘句柄 / UIA 元素）是不是还属于「当前这个目标屏」。
    /// </summary>
    /// <remarks>
    /// 换屏后这些句柄全部失效：<c>TrayNotifyWnd</c> 是任务栏的子窗口，
    /// UIA 元素挂在 `taskbarHandle` 下面，父窗口变了旧引用就指向另一条栏。
    /// 用设备路径比较而不是设置里的序号——序号插拔后可能「看起来没变但指向了另一块屏」。
    /// </remarks>
    private bool IsPositionedForCurrentTarget()
    {
        return string.Equals(_lastPositionedTargetKey, _target?.DevicePath, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 设置里的目标显示器变了：立刻按新目标重排，不等 1.5s 定时器。
    /// </summary>
    /// <remarks>
    /// 刻意**不走** <c>MainWindow.UpdateTaskbar</c>：那条路会顺手重刷一遍媒体 UI
    /// （标题/封面/按钮），切换屏幕时完全没必要，还会在没在播放时把文字刷成 <c>-</c>。
    /// </remarks>
    public void ApplyTargetChange()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(ApplyTargetChange, DispatcherPriority.Send);
            return;
        }

        UpdatePosition();
    }

    private void CalculateAndSetPosition(IntPtr taskbarHandle, IntPtr taskbarWindowHandle, bool isMainTaskbarSelected)
    {
        // Prevent overlapping updates - if a previous update is still running
        // (e.g. waiting for an automation query timeout), skip this tick.
        if (_positionUpdateInProgress)
            return;
        _positionUpdateInProgress = true;

        try
        {
            // get DPI scaling
            double dpiScale = GetDpiForWindow(taskbarHandle) / 96.0;

            // Guard against invalid DPI (e.g. during explorer restart when handle is stale)
            if (dpiScale <= 0)
                return;

            // Get Taskbar dimensions
            RECT taskbarRect;

            if (!SettingsManager.Current.LegacyTaskbarWidthEnabled)
            {
                // first, try to find the Taskbar.TaskbarFrame element in the XAML
                // this should give us the actual bounds of the taskbar, excluding invisible margins on some Windows configurations
                (bool success, Rect result) = GetTaskbarFrameRect(taskbarHandle);
                if (success)
                {
                    taskbarRect = new RECT
                    {
                        Left = (int)result.Left,
                        Top = (int)result.Top,
                        Right = (int)result.Right,
                        Bottom = (int)result.Bottom
                    };
                }
                else
                {
                    // fallback to GetWindowRect if we fail to get the frame bounds for some reason
                    GetWindowRect(taskbarHandle, out taskbarRect);
                }
            }
            else
            {
                // legacy method - GetWindowRect on the entire taskbar, which includes invisible margins on some Windows configurations
                GetWindowRect(taskbarHandle, out taskbarRect);
            }

            int taskbarHeight = taskbarRect.Bottom - taskbarRect.Top;
            int taskbarWidth = taskbarRect.Right - taskbarRect.Left;

            // Vertical taskbar support: rotate and reposition widget when taskbar is taller than wide
            bool isVertical = taskbarHeight > taskbarWidth;
            double taskbarCrossSize = (isVertical ? taskbarWidth : taskbarHeight) / dpiScale;
            bool isSmallTaskbar = taskbarCrossSize < SmallTaskbarDetectionThreshold;
            int containerWidth = taskbarWidth;
            int containerHeight = taskbarHeight;

            Widget.SetSmallTaskbarMode(isSmallTaskbar);
            TaskbarVisualizer.SetSmallTaskbarMode(isSmallTaskbar);

            // Following SetWindowPos will set the position relative to the parent window,
            // so those coordinates need to be converted.
            POINT containerPos = new() { X = taskbarRect.Left, Y = taskbarRect.Top };
            ScreenToClient(taskbarHandle, ref containerPos);

            // Apply using SetWindowPos (Bypassing WPF layout engine).
            // HWND_TOP keeps this child window at the top of the taskbar's child z-order:
            // Explorer's taskbar XAML content bridge (Windows.UI.Composition.DesktopWindowContentBridge)
            // spans the whole taskbar and otherwise ends up above this window, hiding the widget.
            // Do NOT pass SWP_NOZORDER here, it would turn hWndInsertAfter into a no-op.
            SetWindowPos(taskbarWindowHandle, HWND_TOP,
                     containerPos.X, containerPos.Y,
                     containerWidth, containerHeight,
                     SWP_NOACTIVATE | SWP_ASYNCWINDOWPOS | SWP_SHOWWINDOW);
            var wRect = PositionWidget(taskbarHandle, taskbarRect, dpiScale, isMainTaskbarSelected, isVertical);
            var vRect = PositionVisualizer(taskbarHandle, taskbarRect, dpiScale, isMainTaskbarSelected, isVertical);

            UpdateWindowRegion(taskbarWindowHandle, wRect, vRect);

            // 这一轮已经排完版，目标才算「真正生效」——UIA/托盘句柄缓存以它为失效依据。
            _lastPositionedTargetKey = _target?.DevicePath;

            // 迁移/签名重连的产物在这里落地（不能放在解析里，会重入布局）。
            FlushPendingTargetPersist();
        }
        finally
        {
            _positionUpdateInProgress = false;
        }
    }

    private Rect PositionWidget(IntPtr taskbarHandle, RECT taskbarRect, double dpiScale, bool isMainTaskbarSelected, bool isVertical)
    {
        if (!SettingsManager.Current.TaskbarWidgetEnabled)
            return Rect.Empty;

        Widget.SetVerticalMode(isVertical);

        int taskbarHeight = taskbarRect.Bottom - taskbarRect.Top;
        int taskbarWidth = taskbarRect.Right - taskbarRect.Left;

        // 先按「理想宽度」算一遍，再去实测任务栏可用区间，
        // 用区间长度作为上限重算。见 ComputeWidgetBudget。
        var budget = ComputeWidgetBudget(taskbarHandle, taskbarRect, dpiScale, isVertical);

        // Calculate widget size
        var (logicalWidth, logicalHeight) = Widget.CalculateSize(dpiScale, budget);

        int physicalWidth = (int)(logicalWidth * dpiScale * _scale);
        int physicalHeight = (int)(logicalHeight * dpiScale);

        // Apply orientation transform
        Widget.LayoutTransform = isVertical ? new System.Windows.Media.RotateTransform(90) : null;
        Widget.RenderTransform = System.Windows.Media.Transform.Identity;

        // On a vertical taskbar the widget is rotated 90°, so the axes flip:
        //   primarySize = taskbarHeight, positioning runs along Y
        //   crossSize   = taskbarWidth,  widget is centered along X
        //   physicalWidth  = visual extent along primary axis (logical width = visual height after rotation)
        //   physicalHeight = visual extent along cross axis   (logical height = visual width after rotation)
        int primarySize = isVertical ? taskbarHeight : taskbarWidth;
        int crossSize = isVertical ? taskbarWidth : taskbarHeight;

        // Center on the cross axis; both orientations use physicalHeight for the cross dimension
        int crossPos = (crossSize - physicalHeight) / 2;

        // Primary axis position (calculated per-case below)
        int primaryPos = 0;

        switch (SettingsManager.Current.TaskbarWidgetPosition)
        {
            case 0: // near start (left for horizontal, top for vertical)
                primaryPos = 20;

                // 先定锚点，再让出频谱的宽度。**顺序不能反**：
                // 下面 padding 分支是赋值（primaryPos = …）而不是累加，
                // 先加偏移会被它覆盖掉，频谱就正好盖在 Widgets 按钮上。
                // 频谱与歌词同时打开时这条路径每次更新都会走到。
                if (SettingsManager.Current.TaskbarWidgetPadding)
                {
                    try
                    {
                        // find widget button in XAML
                        (bool found, Rect nativeWidgetRect) = GetTaskbarWidgetRect(taskbarHandle);

                        // Accept only if the native Widgets button is in the start half of the taskbar
                        bool inStartHalf = isVertical
                            ? nativeWidgetRect.Bottom < (taskbarRect.Top + taskbarRect.Bottom) / 2.0
                            : nativeWidgetRect.Right < (taskbarRect.Left + taskbarRect.Right) / 2.0;

                        if (found && inStartHalf)
                        {
                            // Convert absolute screen position to relative position within taskbar
                            primaryPos = isVertical
                                ? (int)(nativeWidgetRect.Bottom - taskbarRect.Top) + 2
                                : (int)(nativeWidgetRect.Right - taskbarRect.Left) + 2;
                        }
                    }
                    catch (Exception ex)
                    {
                        // fallback to default padding
                        Logger.Warn(ex, "Failed to get Widgets button position.");
                        primaryPos += _nativeWidgetsPadding + 2;
                    }
                }

                if (SettingsManager.Current.TaskbarVisualizerEnabled && SettingsManager.Current.TaskbarVisualizerPosition == 0)
                    primaryPos += (int)(TaskbarVisualizer.Width * dpiScale) + 4;

                break;

            case 1: // center of the taskbar
                primaryPos = (primarySize - physicalWidth) / 2;

                if (SettingsManager.Current.TaskbarVisualizerEnabled)
                    if (SettingsManager.Current.TaskbarVisualizerPosition == 0)
                        primaryPos += (int)(TaskbarVisualizer.Width * dpiScale) / 2 + 4;
                    else
                        primaryPos -= (int)(TaskbarVisualizer.Width * dpiScale) / 2 - 4;
                break;

            case 2: // near end (right for horizontal, bottom for vertical)
                try
                {
                    if (SettingsManager.Current.TaskbarVisualizerEnabled && SettingsManager.Current.TaskbarVisualizerPosition == 1)
                        primaryPos -= (int)(TaskbarVisualizer.Width * dpiScale) - 4;

                    // Horizontal only: try to position next to native Widgets button on the end side
                    if (!isVertical && SettingsManager.Current.TaskbarWidgetPadding)
                    {
                        try
                        {
                            // find widget button in XAML
                            (bool found, Rect nativeWidgetRect) = GetTaskbarWidgetRect(taskbarHandle);

                            // make sure it's on the right side, otherwise ignore (widget might be to the left)
                            if (found && nativeWidgetRect.Left > (taskbarRect.Left + taskbarRect.Right) / 2.0)
                            {
                                // Convert absolute screen position to relative position within taskbar
                                primaryPos += (int)(nativeWidgetRect.Left - taskbarRect.Left) - 1 - physicalWidth;
                                break;
                            }
                        }
                        catch (Exception ex) // catch exception when getting widget position
                        {
                            Logger.Warn(ex, "Failed to get Widgets button position.");
                        }
                    }

                    // try to position next to system tray
                    if (!isMainTaskbarSelected)
                    {
                        // find secondary tray with automation
                        (bool found, Rect trayRect) = GetSystemTrayRect(taskbarHandle);

                        if (found)
                        {
                            // Convert absolute screen position to relative position within taskbar
                            double trayOffset = isVertical
                                ? trayRect.Top - taskbarRect.Top
                                : trayRect.Left - taskbarRect.Left;
                            primaryPos += (int)trayOffset - physicalWidth - (isVertical ? 2 : 1);
                            break;
                        }
                    }
                    else
                    {
                        // Primary taskbar: for vertical, try automation first (more reliable on ExplorerPatcher)
                        if (isVertical)
                        {
                            (bool trayFound, Rect trayAutomationRect) = GetSystemTrayRect(taskbarHandle);
                            if (trayFound && trayAutomationRect.Top >= taskbarRect.Top)
                            {
                                primaryPos += (int)(trayAutomationRect.Top - taskbarRect.Top) - physicalWidth - 2;
                                break;
                            }
                        }

                        // Primary taskbar: TrayNotifyWnd (original approach for horizontal, fallback for vertical)
                        if (_trayHandle == IntPtr.Zero || !IsPositionedForCurrentTarget())
                            _trayHandle = FindWindowEx(taskbarHandle, IntPtr.Zero, "TrayNotifyWnd", null);

                        if (_trayHandle != IntPtr.Zero)
                        {
                            GetWindowRect(_trayHandle, out RECT trayWndRect);
                            // Convert absolute screen position to relative position within taskbar
                            double trayOffset = isVertical
                                ? trayWndRect.Top - taskbarRect.Top
                                : trayWndRect.Left - taskbarRect.Left;

                            // For vertical: validate the tray is in the lower half of the taskbar
                            if (!isVertical || trayOffset > taskbarHeight / 2)
                            {
                                primaryPos += (int)trayOffset - physicalWidth - (isVertical ? 2 : 6); // trayOffset isn't 100% accurate, so we subtract a few pixels
                                break;
                            }
                        }
                        else if (!isVertical)
                        {
                            // TrayNotifyWnd not found on horizontal: fallback to right alignment,
                            // since we are aligning to the right side and know the size of the taskbar.
                            primaryPos += taskbarWidth - physicalWidth - 20;
                            break;
                        }
                    }

                    // Final fallback: place near the end of the taskbar
                    primaryPos += primarySize - physicalWidth - 20;
                }
                catch (Exception ex)
                {
                    // Fallback to left alignment
                    Logger.Warn(ex, "Failed to get System Tray position.");
                    primaryPos = isVertical ? primarySize - physicalWidth - 20 : 20;
                }
                break;
        }

        primaryPos += SettingsManager.Current.TaskbarWidgetManualPadding;

        // Set widget position within canvas
        // primaryPos → left (horizontal) or top (vertical); crossPos → top (horizontal) or left (vertical)
        Canvas.SetLeft(Widget, (isVertical ? crossPos : primaryPos) / dpiScale);
        Canvas.SetTop(Widget, (isVertical ? primaryPos : crossPos) / dpiScale);
        Widget.Width = physicalWidth / dpiScale;
        Widget.Height = physicalHeight / dpiScale;

        // After 90° LayoutTransform the visual bounding rect has swapped dimensions
        double rectW = isVertical ? physicalHeight : physicalWidth;
        double rectH = isVertical ? physicalWidth : physicalHeight;
        return new Rect(Canvas.GetLeft(Widget) * dpiScale, Canvas.GetTop(Widget) * dpiScale, rectW, rectH);
    }

    /// <summary>
    /// 算出小组件这块占位「最多能用多宽」，单位是 **DIP 视觉宽度**
    /// （与 <c>_nativeWidgetsPadding = 216</c> 同一坐标系）。
    ///
    /// 这是 P3-8/P3-9 的核心：把「与原生小组件等宽」这个硬编码常量，
    /// 换成「先问自己要多大，再问任务栏给不给」的两遍流程。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 为什么是两遍：区间依赖宽度（要放多宽才知道能塞进哪个缝），
    /// 宽度又依赖区间（能塞多宽才知道放多大）。这个环不能靠迭代解，
    /// 因为第二遍只会把宽度**改小**——一旦变小，原来找到的缝只会更宽松，
    /// 不需要重新求位置。所以一遍「理想宽度 → 求缝」，一遍「缝长 → 压宽度」即可。
    /// </para>
    /// <para>
    /// 三种情况刻意分开处理，避免「为了新功能牺牲老行为」：
    /// </para>
    /// <list type="bullet">
    /// <item>非歌词模式：直接返回 <c>null</c>，<c>CalculateSize</c> 走原来的 216 常量，
    /// 行为与改动前逐像素一致。</item>
    /// <item>歌词模式但拿不到占用区间（UIA 失败 / 托盘元素不存在）：返回
    /// <see cref="MaxLyricsWidgetWidth"/>（480，与 <c>desiredDip</c> 取小），
    /// 比 216 宽但仍是个有界值。**按主轴现算的自适应上限不参与这条退路**，
    /// 理由见下方 <c>fallbackWidth</c> 的注释。</item>
    /// <item>歌词模式且拿到区间：返回实测可用长度，且**不超过**理想宽度。</item>
    /// </list>
    /// <para>
    /// <b>单位换算点只有一处</b>：交给 <see cref="TaskbarWidthBudget.Resolve"/> 前乘
    /// <c>dpiScale</c> 换成物理像素，出来后除回去。这个函数之外的所有宽度
    /// 都是 DIP 视觉宽度——包括 <see cref="MaxLyricsWidgetWidth"/>。
    /// 所有降级判定与算术都在 <see cref="TaskbarWidthBudget"/> 里，
    /// 这里只负责取数，便于单测覆盖。
    /// </para>
    /// </remarks>
    /// <returns>DIP 视觉宽度上限；<c>null</c> 表示「不参与避让，用原有常量」。</returns>
    private double? ComputeWidgetBudget(IntPtr taskbarHandle, RECT taskbarRect, double dpiScale, bool isVertical)
    {
        if (!Widget.IsLyricsMode || !double.IsFinite(dpiScale) || dpiScale <= 0)
        {
            return null;
        }

        // 任务栏主轴长度，换算到「相对任务栏左上角的物理像素」，
        // 与 UIA 返回的矩形同一坐标系。
        //
        // 必须先求它：竖向的「能占多长」上限是按它现算的，不是常量。
        var taskbarLength = isVertical
            ? taskbarRect.Bottom - taskbarRect.Top
            : taskbarRect.Right - taskbarRect.Left;

        var maxLengthDip = ResolveMaxLengthDip(taskbarLength, dpiScale, isVertical);
        var desiredDip = Math.Min(Widget.MeasureDesiredWidth(maxLengthDip), maxLengthDip);

        if (taskbarLength <= 0 || !double.IsFinite(desiredDip) || desiredDip <= 0)
        {
            return MaxLyricsWidgetWidth;
        }

        // 频谱与歌词同时开时，频谱要占的那一段必须从预算里扣掉。
        //
        // 它不是「障碍物」而是「同伴」：PositionVisualizer 永远把它贴在小组件
        // 旁边（见 PositionWidget 里 +Width+4 的偏移），所以只要向避让算法
        // 多要 84+4，扣回来就是歌词自己的宽度。若把它当固定区间塞进
        // occupied，避让算法会把「小组件自己现在占的地方」当成禁区，
        // 宽度会逐帧萎缩（详见 GetOccupiedIntervals 的 remarks）。
        var visualizerExtraDip = SettingsManager.Current.TaskbarVisualizerEnabled
            ? TaskbarVisualizer.Width + 4
            : 0;

        var occupied = GetOccupiedIntervals(taskbarHandle, taskbarRect, isVertical);

        // 进避让算法前统一换成物理像素：UIA 与 GetWindowRect 都是物理坐标，
        // 而 desiredDip / MaxLyricsWidgetWidth 是 DIP 视觉宽度。
        // 这个换算是本函数唯一的单位边界，进出各一次。
        //
        // fallbackWidth 刻意仍是 480，**不跟着竖向上限变大**：它表示
        // 「一个空隙都塞不进去 / 占用区未知」时退回的有界常量。那种情况下
        // 任务栏已经没有空间了，退回一个更大的值只会压到托盘图标上。
        // 它同样不预先扣频谱宽度——扣在下面统一做，于是退路下歌词实际拿
        // 480−88=392：频谱在场时把 480 全给歌词，加 84 就会顶到托盘。
        var budget = TaskbarWidthBudget.Resolve(
            desiredWidth: (desiredDip + visualizerExtraDip) * dpiScale,
            fallbackWidth: MaxLyricsWidgetWidth * dpiScale,
            taskbarLength: taskbarLength,
            preferredStart: ResolvePreferredStart(taskbarLength),
            occupied: occupied);

        switch (budget.Outcome)
        {
            case WidthBudgetOutcome.NoUsableGap:
                Logger.Debug(
                    "Lyrics widget has no usable taskbar gap (length={0}, desired={1}px); falling back to bounded width {2} DIP.",
                    taskbarLength, desiredDip * dpiScale, MaxLyricsWidgetWidth);
                break;

            case WidthBudgetOutcome.OccupancyUnknown:
                Logger.Debug(
                    "Taskbar occupancy unknown; lyrics widget falls back to bounded width {0} DIP.",
                    MaxLyricsWidgetWidth);
                break;
        }

        // 换回 DIP 视觉宽度。budget.Width 是物理像素，而 CalculateSize 的预算
        // 是「乘过 _scale 的视觉宽度」，所以只除 dpiScale，不除 _scale。
        //
        // 再扣掉上面多要的频谱宽度。扣完可能不足 100 DIP（任务栏挤到极限），
        // 那时歌词只剩一条细缝——这比让它压到托盘图标上要好。
        var widthDip = (budget.Width / dpiScale) - visualizerExtraDip;

        return Math.Max(widthDip, 0);
    }

    /// <summary>
    /// 歌词块「能占多长/多宽」的上限（DIP 视觉宽度）。
    ///
    /// 横向：固定 <see cref="MaxLyricsWidgetWidth"/>（480）。横向超长的句子
    /// 会走滚动（见 <c>LyricsPanel</c> 的跑马灯逻辑），不需要把块做大；
    /// 而 480 正好挡住任务栏中部的应用按钮。
    ///
    /// 竖向：**按任务栏实际主轴长度现算**（<see cref="LyricsVerticalLayout.ResolveMaxMainAxisLength"/>）。
    /// 竖排没有滚动，估算值超过上限的行会被整行裁掉——4K 竖屏主轴两千多 DIP，
    /// 继续用 480 会把一句长歌词切在半途（截图矩阵 edge-vertical-v28-font20-long-line
    /// 就是这个现象）。
    /// </summary>
    /// <param name="taskbarLength">任务栏主轴长度（物理像素）。</param>
    private static double ResolveMaxLengthDip(double taskbarLength, double dpiScale, bool isVertical)
    {
        if (!isVertical || taskbarLength <= 0 || !double.IsFinite(dpiScale) || dpiScale <= 0)
        {
            return MaxLyricsWidgetWidth;
        }

        return LyricsVerticalLayout.ResolveMaxMainAxisLength(taskbarLength / dpiScale);
    }

    /// <summary>
    /// 期望起点，用来在多个空隙里挑「最符合用户设置的那个」。
    ///
    /// <see cref="TaskbarFreeSpaceLayout.TryPlace"/> 用哨兵值表达「越靠前越好」
    /// 和「越靠后越好」，正好对应 <c>TaskbarWidgetPosition</c> 的 0 和 2；
    /// 居中则给真实中点。
    /// </summary>
    private static double ResolvePreferredStart(double taskbarLength)
    {
        return SettingsManager.Current.TaskbarWidgetPosition switch
        {
            0 => -1,                      // near start：越靠前越好
            2 => taskbarLength + 1,       // near end：越靠后越好
            _ => taskbarLength / 2,       // center
        };
    }

    /// <summary>
    /// 取任务栏上已被占用的区间（主轴坐标，相对任务栏左上角的物理像素）。
    ///
    /// 返回 <c>null</c> 表示「不知道」——与「知道且为空」是两回事，
    /// 调用方必须区别对待，否则一次 UIA 超时就会让歌词盖住托盘。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 只取两块：Widgets 按钮与系统托盘。任务栏上的应用按钮不取——
    /// 那是用户正在用的东西，UIA 逐个枚举几十个按钮的代价远超收益，
    /// 而 <see cref="TaskbarFreeSpaceLayout"/> 的 <c>safetyGap</c> 已经
    /// 保证不会贴到相邻元素上。
    /// </para>
    /// <para>
    /// <b>频谱刻意不在这里</b>，尽管它也是我们画的。避让算法只能处理
    /// 「固定障碍物」，而频谱永远被 <see cref="PositionVisualizer"/> 贴在
    /// 小组件边上——把它当障碍物会形成正反馈：本帧小组件宽 W，频谱就在
    /// 它右侧，于是下一帧的空隙变成「W − safetyGap」，再下一帧又少 8……
    /// 宽度会逐帧萎缩到下限再跳回 480，表现为一条慢速抖动。
    /// 正确做法是把它当「同伴」：长度从预算里扣（见
    /// <see cref="ComputeWidgetBudget"/> 的 <c>visualizerExtraDip</c>），
    /// 而不是作为禁区把空隙切开。
    /// </para>
    /// <para>
    /// 缓存 500ms。这两块位置的查询全部走跨进程 UIA，单次最坏 1.5s；
    /// <c>UpdatePosition</c> 由 1.5s 定时器 + 每次换歌 + 歌词换行触发，
    /// 不缓存会直接把任务栏拖死（见落点文档 3.1 节）。
    /// </para>
    /// </remarks>
    private IReadOnlyList<TaskbarInterval>? GetOccupiedIntervals(IntPtr taskbarHandle, RECT taskbarRect, bool isVertical)
    {
        var now = Stopwatch.GetTimestamp();

        if (_occupiedSnapshot is { } cached &&
            cached.TaskbarHandle == taskbarHandle &&
            cached.IsVertical == isVertical &&
            cached.TaskbarRect.Left == taskbarRect.Left &&
            cached.TaskbarRect.Top == taskbarRect.Top &&
            cached.TaskbarRect.Right == taskbarRect.Right &&
            cached.TaskbarRect.Bottom == taskbarRect.Bottom &&
            Stopwatch.GetElapsedTime(cached.Timestamp, now) < OccupiedCacheLifetime)
        {
            return cached.Occupied;
        }

        var occupied = new List<TaskbarInterval>(2);
        var anyQuerySucceeded = false;

        // Widgets 按钮
        if (TryGetOccupiedInterval(() => GetTaskbarWidgetRect(taskbarHandle), taskbarRect, isVertical, out var widgetInterval))
        {
            occupied.Add(widgetInterval);
            anyQuerySucceeded = true;
        }

        // 系统托盘
        if (TryGetOccupiedInterval(() => GetSystemTrayRect(taskbarHandle), taskbarRect, isVertical, out var trayInterval))
        {
            occupied.Add(trayInterval);
            anyQuerySucceeded = true;
        }

        if (!anyQuerySucceeded)
        {
            // 两块 UIA 查询全失败：视为「不知道」。注意**不写缓存**——
            // 下一次调用立刻重试，而不是把「未知」缓存 500ms。
            _occupiedSnapshot = null;
            return null;
        }

        _occupiedSnapshot = new TaskbarOccupancySnapshot(
            taskbarHandle, taskbarRect, isVertical, occupied, now);

        return occupied;
    }

    /// <summary>
    /// 把一次 UIA 元素查询结果转成主轴区间。
    ///
    /// 元素可能落在任务栏外（副屏任务栏上的托盘有时报出主屏坐标），
    /// 这种情况按「查询失败」处理而不是塞一个越界区间进去——
    /// <see cref="TaskbarFreeSpaceLayout"/> 会裁掉越界部分，
    /// 但裁剩的碎片会让空隙判断变得莫名其妙。
    /// </summary>
    private static bool TryGetOccupiedInterval(
        Func<(bool found, Rect rect)> query,
        RECT taskbarRect,
        bool isVertical,
        out TaskbarInterval interval)
    {
        interval = default;

        try
        {
            var (found, rect) = query();
            if (!found || rect.IsEmpty || rect.Width <= 0 || rect.Height <= 0)
            {
                return false;
            }

            if (isVertical)
            {
                var start = rect.Top - taskbarRect.Top;
                var end = rect.Bottom - taskbarRect.Top;
                if (end <= 0 || start >= taskbarRect.Bottom - taskbarRect.Top)
                {
                    return false;
                }

                interval = new TaskbarInterval(start, end);
            }
            else
            {
                var start = rect.Left - taskbarRect.Left;
                var end = rect.Right - taskbarRect.Left;
                if (end <= 0 || start >= taskbarRect.Right - taskbarRect.Left)
                {
                    return false;
                }

                interval = new TaskbarInterval(start, end);
            }

            return true;
        }
        catch (Exception ex)
        {
            // 这里不记 Warn：UIA 在任务栏刷新期间失败是常态，
            // 每 500ms 一条 Warn 会把日志刷满。
            Logger.Debug(ex, "Failed to query taskbar element bounds for occupancy.");
            return false;
        }
    }

    private Rect PositionVisualizer(IntPtr taskbarHandle, RECT taskbarRect, double dpiScale, bool isMainTaskbarSelected, bool isVertical)
    {
        if (!SettingsManager.Current.TaskbarVisualizerEnabled)
            return Rect.Empty;

        // Rotate visualizer 90° on vertical taskbar so it fits the slim width
        TaskbarVisualizer.LayoutTransform = isVertical ? new System.Windows.Media.RotateTransform(90) : null;

        int taskbarHeight = taskbarRect.Bottom - taskbarRect.Top;
        int taskbarWidth = taskbarRect.Right - taskbarRect.Left;

        // TaskbarVisualizer.Height (40) is the cross-axis extent for both orientations:
        //   horizontal: actual height = 40, centered vertically (-1 to match native element alignment)
        //   vertical:   visual width after rotation = 40, centered horizontally
        int crossSize = isVertical ? taskbarWidth : taskbarHeight;
        int crossOffset = isVertical ? 0 : -1; // -1 aligns with native taskbar elements on horizontal
        int crossPos = (crossSize - (int)(TaskbarVisualizer.Height * dpiScale)) / 2 + crossOffset;

        // TaskbarVisualizer.Width (84) is the primary-axis extent for both orientations:
        //   horizontal: actual width = 84
        //   vertical:   visual height after rotation = 84
        // Position adjacent to the widget along the primary axis
        double widgetPrimaryStart = isVertical ? Canvas.GetTop(Widget) : Canvas.GetLeft(Widget);
        int primaryPos;

        switch (SettingsManager.Current.TaskbarVisualizerPosition)
        {
            case 0: // before widget (left for horizontal, above for vertical)
                primaryPos = (int)(widgetPrimaryStart * dpiScale) - (int)(TaskbarVisualizer.Width * dpiScale);
                break;

            case 1: // after widget (right for horizontal, below for vertical)
                // Widget.Width holds the logical width; after 90° rotation its visual height = Widget.Width * dpiScale
                primaryPos = (int)(widgetPrimaryStart * dpiScale) + (int)(Widget.Width * dpiScale);
                break;

            default:
                primaryPos = 0;
                break;
        }

        // Set visualizer position within canvas
        // primaryPos → left (horizontal) or top (vertical); crossPos → top (horizontal) or left (vertical)
        Canvas.SetLeft(TaskbarVisualizer, (isVertical ? crossPos : primaryPos) / dpiScale);
        Canvas.SetTop(TaskbarVisualizer, (isVertical ? primaryPos : crossPos) / dpiScale);

        // After 90° LayoutTransform the visual bounding rect has swapped dimensions
        double rectW = isVertical ? TaskbarVisualizer.Height * dpiScale : TaskbarVisualizer.Width * dpiScale;
        double rectH = isVertical ? TaskbarVisualizer.Width * dpiScale : TaskbarVisualizer.Height * dpiScale;
        return new Rect(Canvas.GetLeft(TaskbarVisualizer) * dpiScale, Canvas.GetTop(TaskbarVisualizer) * dpiScale, rectW, rectH);
    }

    public void UpdateUi(string title, string artist, BitmapImage? icon, GlobalSystemMediaTransportControlsSessionPlaybackStatus? playbackStatus, GlobalSystemMediaTransportControlsSessionPlaybackControls? playbackControls = null)
    {
        // Check premium status - hide widget if not unlocked
        if ((!SettingsManager.Current.TaskbarWidgetEnabled || !SettingsManager.Current.IsPremiumUnlocked))
        {
            if (_timer.IsEnabled) // pause timer to save resources
                _timer.Stop();

            Dispatcher.Invoke(() =>
            {
                Visibility = Visibility.Collapsed;
            });
            return;
        }

        // Autohide - Widget hides when playback is paused
        _lastPlaybackStatus = playbackStatus;

        if ((SettingsManager.Current.TaskbarWidgetAutoHide))
        {
            if (playbackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
            {
                _autoHideTimer?.Stop();
                _autoHideTimer = null;

                Dispatcher.Invoke(() =>
                {
                    Visibility = Visibility.Visible;
                });
            }
            else
            {
                // Start delayed hide
                if (_autoHideTimer == null)
                {
                    var localTimer = new DispatcherTimer
                    {
                        Interval = TimeSpan.FromMilliseconds(750)
                    };

                    localTimer.Tick += (s, e) =>
                    {
                        localTimer.Stop();
                        if (_autoHideTimer == localTimer)
                            _autoHideTimer = null;

                        if (_lastPlaybackStatus != GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                        {
                            Dispatcher.Invoke(() =>
                            {
                                Visibility = Visibility.Collapsed;
                            });
                        }
                    };

                    _autoHideTimer = localTimer;
                    localTimer.Start();
                }
            }
        }

        if (!_timer.IsEnabled)
            _timer.Start();

        // Delegate UI update to widget control
        Widget.UpdateUi(title, artist, icon, playbackStatus, playbackControls);

        // Update position after UI change
        Dispatcher.BeginInvoke(() => UpdatePosition(), DispatcherPriority.Background);

        Dispatcher.Invoke(() =>
        {
            Visibility = Visibility.Visible;
        });
    }

    public void RefreshAppVolumeTooltip()
    {
        Widget.RefreshAppVolumeTooltip();
    }

    private (bool, Rect) GetTaskbarXamlElementRect(IntPtr taskbarHandle, ref AutomationElement? elementCache, string elementName)
    {
        if (taskbarHandle == IntPtr.Zero)
            return (false, Rect.Empty);

        try
        {
            // reset if monitor changed
            if (!IsPositionedForCurrentTarget())
                elementCache = null;

            // find widget in XAML
            if (elementCache == null)
            {
                if (_pendingAutomationTasks.TryGetValue(elementName, out var pendingTask) && !pendingTask.IsCompleted)
                    return (false, Rect.Empty);

                AutomationElement? found = null;
                var findTask = Task.Run(() =>
                {
                    var root = AutomationElement.FromHandle(taskbarHandle);
                    found = root.FindFirst(TreeScope.Descendants,
                        new PropertyCondition(AutomationElement.AutomationIdProperty, elementName));
                });
                _pendingAutomationTasks[elementName] = findTask;

                if (!findTask.Wait(1000))
                {
                    Logger.Warn("Timeout querying taskbar XAML element: " + elementName);
                    return (false, Rect.Empty);
                }

                // Propagate any exception from the background thread
                findTask.GetAwaiter().GetResult();
                elementCache = found;
            }

            if (elementCache == null) // widget most likely disabled
                return (false, Rect.Empty);

            try
            {
                if (_pendingAutomationTasks.TryGetValue(elementName, out var pendingTask) && !pendingTask.IsCompleted)
                {
                    elementCache = null;
                    return (false, Rect.Empty);
                }

                var cachedElement = elementCache;
                var boundsTask = Task.Run(() => cachedElement.Current.BoundingRectangle);
                _pendingAutomationTasks[elementName] = boundsTask;

                if (!boundsTask.Wait(500))
                {
                    Logger.Warn("Timeout getting bounds for taskbar XAML element: " + elementName);
                    elementCache = null;
                    return (false, Rect.Empty);
                }

                Rect elementRect = boundsTask.GetAwaiter().GetResult();

                if (elementRect == Rect.Empty) // widget shown before but most likely disabled now
                {
                    elementCache = null; // reset cache
                    return (false, Rect.Empty);
                }

                return (true, elementRect);
            }
            catch (ElementNotAvailableException)
            {
                // element became stale, reset cache
                Logger.Warn("Taskbar XAML element became stale, resetting cache: " + elementName);
                elementCache = null;
                return (false, Rect.Empty);
            }
        }
        catch (COMException ex)
        {
            Logger.Warn(ex, "COM error retrieving taskbar XAML element Rect: " + elementName);
            elementCache = null; // reset cache on error
            return (false, Rect.Empty);
        }
        catch (ElementNotAvailableException)
        {
            Logger.Warn("Taskbar XAML element not available, resetting cache: " + elementName);
            elementCache = null;
            return (false, Rect.Empty);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error retrieving taskbar XAML element Rect: " + elementName);
            elementCache = null; // reset cache on error
            return (false, Rect.Empty);
        }
    }

    /// <summary>
    /// Attempts to locate the Windows taskbar widgets button and retrieves its bounding rectangle.
    /// </summary>
    /// <returns>A tuple where the first value indicates whether the widgets button was found (<see langword="true"/> if found;
    /// otherwise, <see langword="false"/>), and the second value is the bounding rectangle of the button if found, or
    /// <see cref="Rect.Empty"/> if not found.</returns>
    private (bool, Rect) GetTaskbarWidgetRect(IntPtr taskbarHandle)
    {
        return GetTaskbarXamlElementRect(taskbarHandle, ref _widgetElement, "WidgetsButton");
    }

    private (bool, Rect) GetSystemTrayRect(IntPtr taskbarHandle)
    {
        return GetTaskbarXamlElementRect(taskbarHandle, ref _trayElement, "SystemTrayIcon");
    }

    private (bool, Rect) GetTaskbarFrameRect(IntPtr taskbarHandle)
    {
        return GetTaskbarXamlElementRect(taskbarHandle, ref _taskbarFrameElement, "TaskbarFrame");
    }

    protected override void OnClosed(EventArgs e)
    {
        _isClosing = true;
        _timer.Stop();
        _autoHideTimer?.Stop();
        _autoHideTimer = null;
        _widgetElement = null;
        _trayElement = null;
        _taskbarFrameElement = null;
        _pendingAutomationTasks.Clear();
        base.OnClosed(e);
    }
}