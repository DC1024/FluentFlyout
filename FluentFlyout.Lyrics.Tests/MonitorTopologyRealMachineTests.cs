// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyoutWPF.Classes.Utils;
using Xunit;
using Xunit.Abstractions;

namespace FluentFlyout.Lyrics.Tests;

/// <summary>
/// 真机拓扑测试：直接用**生产代码**（<see cref="MonitorUtil"/> + <see cref="MonitorTargetResolver"/>）
/// 读这台机器的显示器和任务栏，验证「设备路径当主键」这条链在真实 Win32 上成立。
/// </summary>
/// <remarks>
/// <para>
/// 纯函数那部分（<see cref="MonitorTargetResolverTests"/>）证不了的一件事是：
/// <c>EnumDisplayDevices(EDD_GET_DEVICE_INTERFACE_NAME)</c> 到底返不返回路径。
/// 写错标志位或结构体，编译期、单元测试都抓不到，只会表现为「主键永远退化成
/// <c>\\.\DISPLAY1</c>」——功能看着正常，插拔一次就开始漂。
/// </para>
/// <para>
/// 单显示器 / 无显示器的 CI 机器上这些断言会自动退化，不会误报。
/// </para>
/// </remarks>
public class MonitorTopologyRealMachineTests
{
    private readonly ITestOutputHelper _output;

    public MonitorTopologyRealMachineTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void EveryMonitorExposesADistinctDevicePath()
    {
        var monitors = MonitorUtil.GetMonitorRefs();

        foreach (var monitor in monitors)
        {
            _output.WriteLine($"{monitor.FriendlyName,-14} {monitor.DeviceId,-14} {monitor.Signature}");
            _output.WriteLine($"    {monitor.DevicePath}");
        }

        if (monitors.Count == 0)
        {
            // 无头 CI：没有显示器可枚举，这条断言没有意义。
            return;
        }

        Assert.All(monitors, m => Assert.False(string.IsNullOrWhiteSpace(m.DevicePath)));

        // 同一型号的两块屏在设备类实例路径上会完全同名（MONITOR\默认监视器\...\0001），
        // 带上 EDD_GET_DEVICE_INTERFACE_NAME 之后才带接口实例，才可能互不相同。
        Assert.Equal(monitors.Count, monitors.Select(m => m.DevicePath).Distinct(StringComparer.OrdinalIgnoreCase).Count());

        // 友好名不该退化成一串「两块钱都叫一样的名字」——那种情况下设置页无法区分。
        Assert.All(monitors, m => Assert.False(string.IsNullOrWhiteSpace(m.FriendlyName)));
    }

    [Fact]
    public void SavedTargetSurvivesResolutionOnTheRealMachine()
    {
        var monitors = MonitorUtil.GetMonitorRefs();
        if (monitors.Count < 2)
        {
            return;
        }

        foreach (var saved in monitors)
        {
            var resolution = MonitorTargetResolver.Resolve(monitors, saved.DevicePath, saved.Signature, legacyIndex: 0);

            Assert.Equal(MonitorResolutionStatus.TargetFound, resolution.Status);
            Assert.Equal(saved.DevicePath, resolution.Target!.DevicePath);
        }
    }

    /// <summary>
    /// 把「副屏被拔掉」真正模拟一遍：目标仍在设置里，但当前列表里已经没有它。
    /// </summary>
    [Fact]
    public void UnpluggedTargetFallsBackToPrimaryWithoutLosingTheChoice()
    {
        var monitors = MonitorUtil.GetMonitorRefs();
        var primary = monitors.FirstOrDefault(m => m.IsPrimary);
        if (primary is null || monitors.Count < 2)
        {
            return;
        }

        var secondaryIndex = -1;
        for (int i = 0; i < monitors.Count; i++)
        {
            if (!monitors[i].IsPrimary)
            {
                secondaryIndex = i;
                break;
            }
        }

        if (secondaryIndex < 0)
        {
            return;
        }

        var secondary = monitors[secondaryIndex];
        var remaining = monitors.Where((_, i) => i != secondaryIndex).ToList();

        var resolution = MonitorTargetResolver.Resolve(
            remaining,
            secondary.DevicePath,
            secondary.Signature,
            legacyIndex: secondaryIndex);

        Assert.Equal(MonitorResolutionStatus.TargetMissing, resolution.Status);
        Assert.Equal(primary.DevicePath, resolution.Target!.DevicePath);

        // 这是整个修复的要点：**不能**把回退结果写回设置，否则副屏插回来也回不去了。
        Assert.False(resolution.ShouldPersistTarget);
    }

    /// <summary>
    /// 任务栏的归属也是按设备路径判定的，顺手确认真实的两条任务栏落在两块不同的屏上。
    /// </summary>
    [Fact]
    public void RealTaskbarsMapToDistinctMonitors()
    {
        IntPtr mainTaskbar = FindWindow("Shell_TrayWnd", null);
        if (mainTaskbar == IntPtr.Zero)
        {
            return;
        }

        var mainMonitor = MonitorUtil.GetMonitor(mainTaskbar);
        _output.WriteLine($"Shell_TrayWnd → {mainMonitor.deviceName} ({mainMonitor.devicePath})");

        IntPtr secondaryTaskbar = FindWindow("Shell_SecondaryTrayWnd", null);
        if (secondaryTaskbar == IntPtr.Zero)
        {
            // 关掉「在所有显示器上显示任务栏」时本来就没有副栏——不是缺陷。
            return;
        }

        var secondaryMonitor = MonitorUtil.GetMonitor(secondaryTaskbar);
        _output.WriteLine($"Shell_SecondaryTrayWnd → {secondaryMonitor.deviceName} ({secondaryMonitor.devicePath})");

        Assert.NotEqual(mainMonitor.devicePath, secondaryMonitor.devicePath, StringComparer.OrdinalIgnoreCase);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);
}
