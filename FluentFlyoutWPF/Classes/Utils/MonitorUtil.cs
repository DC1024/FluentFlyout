// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyout.Classes;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using static FluentFlyout.Classes.NativeMethods;

namespace FluentFlyoutWPF.Classes.Utils;

public static class MonitorUtil
{
    public struct MonitorInfo
    {
        public Rect monitorArea;
        public Rect workArea;
        public bool isPrimary;
        public uint dpiX;
        public uint dpiY;
        public string deviceId;

        /// <summary>
        /// 监视器设备接口路径（<c>\\?\DISPLAY#TCL2702#5&amp;2ec52e58&amp;0&amp;UID4355#{接口类GUID}</c>）。
        /// 取不到时等于 <see cref="deviceId"/>，**不会为空**。
        /// </summary>
        /// <remarks>
        /// 这是唯一适合存进设置的显示器标识：<see cref="deviceId"/> 只是 GDI 设备名
        /// （<c>\\.\DISPLAY1</c>），换显卡接口、换线、装驱动都可能变；枚举顺序更是一次插拔就会变。
        /// 值保持 API 原样（含结尾那段 <c>#{e6f07b5f-…}</c>），只做比较与展示，不做规范化——
        /// 拆解会让「同一个 API 两次返回是否相同」这种判断多一层自造规则。
        /// </remarks>
        public string devicePath;

        public string deviceName;
    }


    public static MonitorInfo GetSelectedMonitor(int index = 0)
    {
        var monitors = GetMonitors();
        return monitors[Math.Clamp(index, 0, monitors.Count - 1)];
    }

    private static MonitorInfo GetMonitorInfoInternal(IntPtr hMonitor)
    {
        var info = new NativeMethods.MONITORINFOEX();
        info.cbSize = Marshal.SizeOf<NativeMethods.MONITORINFOEX>();

        if (GetMonitorInfo(hMonitor, ref info))
        {
            // 先取设备路径：友好名优先从路径里解析型号（原因见 GetMonitorFriendlyName）。
            string devicePath = GetMonitorDevicePath(info.szDevice);

            MonitorInfo new_info = new MonitorInfo
            {
                monitorArea = new Rect(info.rcMonitor.Left, info.rcMonitor.Top, info.rcMonitor.Right - info.rcMonitor.Left, info.rcMonitor.Bottom - info.rcMonitor.Top),
                workArea = new Rect(info.rcWork.Left, info.rcWork.Top, info.rcWork.Right - info.rcWork.Left, info.rcWork.Bottom - info.rcWork.Top),
                isPrimary = (info.dwFlags & MONITORINFOF_PRIMARY) != 0,
                deviceId = info.szDevice,
                devicePath = devicePath,
                deviceName = GetMonitorFriendlyName(info.szDevice, devicePath)
            };

            if (GetDpiForMonitor(hMonitor, MonitorDpiType.MDT_EFFECTIVE_DPI, out uint dpiX, out uint dpiY) == S_OK)
            {
                new_info.dpiX = dpiX;
                new_info.dpiY = dpiY;
            }
            else
            {
                new_info.dpiX = 96;
                new_info.dpiY = 96;
            }

            return new_info;
        }

        return new MonitorInfo();  // Defaults will be empty/null strings for new fields
    }


    public static MonitorInfo GetMonitor(IntPtr hwnd, MonitorFromWindowFlags flag = MonitorFromWindowFlags.DEFAULTTONEAREST)
    {
        var hMonitor = MonitorFromWindow(hwnd, (int)flag);
        return GetMonitorInfoInternal(hMonitor);
    }

    public static MonitorInfo GetMonitor(Window window, MonitorFromWindowFlags flag = MonitorFromWindowFlags.DEFAULTTONEAREST)
    {
        return GetMonitor(new WindowInteropHelper(window).Handle, flag);
    }

    internal static MonitorInfo GetMonitor(POINT point, MonitorFromWindowFlags flag = MonitorFromWindowFlags.DEFAULTTONEAREST)
    {
        var hMonitor = MonitorFromPoint(point, flag);
        return GetMonitorInfoInternal(hMonitor);
    }

    public static Rect GetWorkAreaDips(double left, double top)
    {
        if (double.IsNaN(left) || double.IsNaN(top))
        {
            return SystemParameters.WorkArea;
        }

        var monitor = GetMonitor(new POINT { X = (int)left, Y = (int)top });
        if (monitor.workArea.IsEmpty)
        {
            return SystemParameters.WorkArea;
        }

        double dpiX = monitor.dpiX > 0 ? monitor.dpiX : 96.0;
        double dpiY = monitor.dpiY > 0 ? monitor.dpiY : 96.0;

        return new Rect(
            monitor.workArea.Left * 96.0 / dpiX,
            monitor.workArea.Top * 96.0 / dpiY,
            monitor.workArea.Width * 96.0 / dpiX,
            monitor.workArea.Height * 96.0 / dpiY);
    }

    public static IReadOnlyList<MonitorInfo> GetMonitors()
    {
        List<MonitorInfo> result = [];

        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero,
            (hMonitor, _, ref _, _) =>
            {
                result.Add(GetMonitorInfoInternal(hMonitor));
                return true;
            },
            IntPtr.Zero
        );


        return result
            .OrderByDescending(m => m.isPrimary)
            .ThenBy(m => m.monitorArea.Left)
            .ToList();
    }
    /// <summary>
    /// 显示器友好名（型号）。优先从设备接口路径里解析，其次才问 <see cref="EnumDisplayDevices"/>。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 为什么不信 <c>DeviceString</c>：本机实测两块不同型号的屏
    /// （TCL2702 / SKG27C5）在这个字段上**都返回 <c>Generic PnP Monitor</c>**。
    /// 于是设置页两项长得一模一样，模糊重连签名里的「型号」位也不携带任何信息。
    /// </para>
    /// <para>
    /// 设备接口路径的第二段就是 Windows 从 EDID 记下的型号：
    /// <c>\\?\DISPLAY#TCL2702#5&amp;2ec52e58&amp;0&amp;UID4355#{接口类GUID}</c> → <c>TCL2702</c>。
    /// 免掉一次 P/Invoke，也没有失败分支。
    /// </para>
    /// </remarks>
    private static string GetMonitorFriendlyName(string deviceId, string devicePath)
    {
        if (MonitorTargetResolver.TryGetModelFromDevicePath(devicePath) is { } model)
        {
            return model;
        }

        var displayDevice = new NativeMethods.DISPLAY_DEVICE
        {
            cb = Marshal.SizeOf<NativeMethods.DISPLAY_DEVICE>()
        };

        //Enumurate all display devices to find display given by deviceId
        if (EnumDisplayDevices(deviceId, 0, ref displayDevice, 0))
        {
            return displayDevice.DeviceString.Trim(); // "Eg: Dell U2720Q"
        }
        return "Unknown Monitor";
    }

    /// <summary>
    /// 取监视器设备接口路径（<c>\\?\DISPLAY#…</c>）；取不到时退回 GDI 设备名。
    /// </summary>
    /// <remarks>
    /// 必须带 <c>EDD_GET_DEVICE_INTERFACE_NAME</c>：不加这个标志，<c>DeviceID</c> 是
    /// <c>MONITOR\默认监视器\{4d36e96e-…}\0001</c> 这种**设备类实例路径**，
    /// 同一型号的两块屏会完全相同，做不了主键。
    /// </remarks>
    private static string GetMonitorDevicePath(string deviceId)
    {
        var displayDevice = new NativeMethods.DISPLAY_DEVICE
        {
            cb = Marshal.SizeOf<NativeMethods.DISPLAY_DEVICE>()
        };

        if (EnumDisplayDevices(deviceId, 0, ref displayDevice, EDD_GET_DEVICE_INTERFACE_NAME) &&
            !string.IsNullOrWhiteSpace(displayDevice.DeviceID))
        {
            return displayDevice.DeviceID;
        }

        return deviceId;
    }

    /// <summary>
    /// 当前所有显示器，按设置页展示顺序（主屏优先，再按 Left）排列。
    /// </summary>
    /// <remarks>
    /// 顺序必须和 <see cref="GetMonitors"/> 完全一致：旧的序号字段是按这个顺序解释的，
    /// 迁移时错一位就会把用户的目标指到另一块屏上。
    /// </remarks>
    public static IReadOnlyList<MonitorRef> GetMonitorRefs()
    {
        return ToMonitorRefs(GetMonitors());
    }

    /// <summary>把一批 <see cref="MonitorInfo"/> 转成可持久化的身份（不重新枚举）。</summary>
    /// <remarks>
    /// 调用方手里已经有一份列表时用这个：重复枚举会让「下拉框里的第 i 项」和
    /// 「写设置时算出来的第 i 项」来自两次不同的快照，中间正好插拔显示器就会指错屏。
    /// </remarks>
    public static IReadOnlyList<MonitorRef> ToMonitorRefs(IReadOnlyList<MonitorInfo> monitors)
    {
        var refs = new List<MonitorRef>(monitors.Count);

        foreach (var monitor in monitors)
        {
            refs.Add(ToMonitorRef(monitor));
        }

        return refs;
    }

    /// <summary>把 <see cref="MonitorInfo"/> 转成可持久化的身份。</summary>
    public static MonitorRef ToMonitorRef(MonitorInfo monitor)
    {
        return new MonitorRef(
            monitor.devicePath,
            monitor.deviceId,
            monitor.deviceName,
            BuildSignature(monitor),
            monitor.isPrimary);
    }

    /// <summary>模糊重连签名，见 <see cref="MonitorTargetResolver.BuildSignature"/>。</summary>
    public static string BuildSignature(MonitorInfo monitor)
    {
        return MonitorTargetResolver.BuildSignature(
            monitor.deviceName,
            (int)monitor.monitorArea.Width,
            (int)monitor.monitorArea.Height,
            (int)monitor.monitorArea.X,
            (int)monitor.monitorArea.Y);
    }

    /// <summary>
    /// 按「设备路径」填充显示器下拉框，返回最终选中的下标。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="UpdateMonitorList(ComboBox, Func{int}, Action{int})"/> 的区别：
    /// 这里**只负责展示**，不回调写设置。原因是「保存的目标当前不存在」时不能把
    /// 选中项回写成主屏——那等于把用户的选择抹掉，显示器插回来也恢复不了。
    /// 写入只应该发生在用户真的动手选的时候。
    /// </remarks>
    public static int FillMonitorComboBox(ComboBox comboBox, IReadOnlyList<MonitorInfo> monitors, string? selectedPath)
    {
        ArgumentNullException.ThrowIfNull(comboBox);
        ArgumentNullException.ThrowIfNull(monitors);

        comboBox.Items.Clear();

        int selectedIndex = 0;
        bool found = false;

        for (int i = 0; i < monitors.Count; i++)
        {
            var monitor = monitors[i];
            string name = $"{i + 1} ({monitor.deviceName})";
            string contentName = monitor.isPrimary ? $"* {name}" : name;

            comboBox.Items.Add(new ComboBoxItem { Content = contentName });

            if (!found &&
                !string.IsNullOrWhiteSpace(selectedPath) &&
                string.Equals(monitor.devicePath, selectedPath, StringComparison.OrdinalIgnoreCase))
            {
                selectedIndex = i;
                found = true;
            }
        }

        comboBox.SelectedIndex = monitors.Count == 0 ? -1 : selectedIndex;
        return comboBox.SelectedIndex;
    }

    public static void UpdateMonitorList(ComboBox comboBox, Func<int> getSelectedIndex, Action<int> setSelectedIndex)
    {
        var monitors = GetMonitors();
        comboBox.Items.Clear();

        int savedIndex = getSelectedIndex();

        bool resetToPrimary =
            savedIndex >= monitors.Count ||
            savedIndex < 0;

        int selectedMonitor = savedIndex;

        for (int i = 0; i < monitors.Count; i++)
        {
            var monitor = monitors[i];

            string name = $"{i + 1} ({monitor.deviceName})";
            string contentName = monitor.isPrimary ? $"* {name}" : $"{name}";

            var cb = new ComboBoxItem
            {
                Content = contentName
            };

            if (resetToPrimary && monitor.isPrimary)
                selectedMonitor = i;

            comboBox.Items.Add(cb);
        }

        comboBox.SelectedIndex = selectedMonitor;
        setSelectedIndex(selectedMonitor);
    }

    public static MonitorInfo GetMonitorWithCursor()
    {
        bool gotCursorPos = GetCursorPos(out POINT cursorPos);
        if (gotCursorPos)
        {
            // Find monitor containing cursor position
            IntPtr hMonitor = MonitorFromPoint(cursorPos, MonitorFromWindowFlags.DEFAULTTONEAREST);
            // Return monitor info
            return GetMonitorInfoInternal(hMonitor);
        }

        // Fallback: use primary monitor if available, otherwise first monitor
        var monitors = GetMonitors();
        if (monitors.Count > 0)
        {
            foreach (var monitor in monitors)
            {
                if (monitor.isPrimary)
                {
                    return monitor;
                }
            }
            // No primary flagged; return first monitor
            return monitors[0];
        }
        // As a last resort, return default monitor info
        return default;
    }

    public static MonitorInfo GetMonitorWithFocusedWindow()
    {
        IntPtr foregroundWindow = GetForegroundWindow();
        if (foregroundWindow != IntPtr.Zero)
        {
            return GetMonitor(foregroundWindow, MonitorFromWindowFlags.DEFAULTTONEAREST);
        }

        // Fallback to cursor monitor if no focused window
        return GetMonitorWithCursor();
    }
}