// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyoutWPF.Classes.Utils;
using Xunit;

namespace FluentFlyout.Lyrics.Tests;

/// <summary>
/// 目标显示器解析测试。
/// </summary>
/// <remarks>
/// 这个文件锁的是**一个已经发生过的现场**：设置里存的是
/// <c>EnumDisplayMonitors</c> 排序后的序号，取用时 <c>Math.Clamp</c> 静默兜底。
/// 拔掉一块屏后「2 号」变成「1 号」，小组件悄悄跑到另一块屏上，用户完全无法察觉。
///
/// 所以下面每条断言的重点都不是「能不能找到屏」，而是
/// **找不到的时候到底发生了什么**：是静默换一块屏（旧行为），
/// 还是保留用户的选择、临时回退、等屏插回来自动恢复（新行为）。
/// </remarks>
public class MonitorTargetResolverTests
{
    private static MonitorRef Monitor(
        string path,
        string name,
        int left,
        bool primary = false,
        int width = 2560,
        int height = 1440)
    {
        return new MonitorRef(
            path,
            DeviceId: path,
            name,
            MonitorTargetResolver.BuildSignature(name, width, height, left, 0),
            primary);
    }

    /// <summary>主屏 + 左侧副屏，顺序与 <c>MonitorUtil.GetMonitors</c> 一致（主屏优先，再按 Left）。</summary>
    private static List<MonitorRef> DualMonitors() =>
    [
        Monitor(@"\\?\DISPLAY#MAIN#1&aaa&0&UID1", "MAIN27", left: 0, primary: true),
        Monitor(@"\\?\DISPLAY#SIDE#2&bbb&0&UID2", "SIDE27", left: -2560),
    ];

    // ---------- 精确命中 ----------

    [Fact]
    public void Resolve_ReturnsMonitoredTarget_WhenPathStillMatches()
    {
        var monitors = DualMonitors();

        var result = MonitorTargetResolver.Resolve(monitors, monitors[1].DevicePath, monitors[1].Signature, 1);

        Assert.Equal(MonitorResolutionStatus.TargetFound, result.Status);
        Assert.Equal(monitors[1], result.Target);
        Assert.True(result.ShouldPersistTarget);
        Assert.False(result.TargetUnavailable);
    }

    /// <summary>
    /// 显示器顺序变了（主屏被改成了另一块），设备路径仍然精确命中。
    /// 旧实现按序号取，这一下就会取到另一块屏。
    /// </summary>
    [Fact]
    public void Resolve_FollowsDevicePath_NotPositionInList()
    {
        var side = Monitor(@"\\?\DISPLAY#SIDE#2&bbb&0&UID2", "SIDE27", left: -2560);
        var main = Monitor(@"\\?\DISPLAY#MAIN#1&aaa&0&UID1", "MAIN27", left: 0, primary: true);

        var reordered = new List<MonitorRef> { side, main };

        var result = MonitorTargetResolver.Resolve(reordered, side.DevicePath, side.Signature, legacyIndex: 0);

        Assert.Equal(MonitorResolutionStatus.TargetFound, result.Status);
        Assert.Equal(side, result.Target);
    }

    // ---------- 签名模糊重连 ----------

    [Fact]
    public void Resolve_RecoversBySignature_WhenDevicePathChangedButPanelDidNot()
    {
        var monitors = DualMonitors();
        var saved = monitors[1];

        // 换了一个物理接口：设备路径变了，型号/分辨率/位置没变。
        var replugged = new MonitorRef(
            @"\\?\DISPLAY#SIDE#9&ccc&0&UID9",
            @"\\.\DISPLAY2",
            saved.FriendlyName,
            saved.Signature,
            IsPrimary: false);

        var afterReplug = new List<MonitorRef> { monitors[0], replugged };

        var result = MonitorTargetResolver.Resolve(afterReplug, saved.DevicePath, saved.Signature, legacyIndex: 1);

        Assert.Equal(MonitorResolutionStatus.RecoveredBySignature, result.Status);
        Assert.Equal(replugged, result.Target);
        // 恢复出来的新路径要写回去，否则每次都靠签名猜。
        Assert.True(result.ShouldPersistTarget);
    }

    /// <summary>
    /// 两块同型号屏：签名必须能把它们分开，否则「恢复」会指到另一块去。
    /// 靠的是相对坐标——两块屏不可能占同一个位置。
    /// </summary>
    [Fact]
    public void BuildSignature_KeepsSameModelMonitorsDistinct()
    {
        var left = MonitorTargetResolver.BuildSignature("SAME27", 2560, 1440, -2560, 0);
        var right = MonitorTargetResolver.BuildSignature("SAME27", 2560, 1440, 0, 0);

        Assert.NotEqual(left, right);
    }

    [Fact]
    public void Resolve_DoesNotGuess_WhenSignatureAlsoMissing()
    {
        var monitors = DualMonitors();

        var result = MonitorTargetResolver.Resolve(
            monitors,
            @"\\?\DISPLAY#GONE#7&ddd&0&UID7",
            savedSignature: null,
            legacyIndex: 1);

        // 只剩一块主屏时最坏情况也只能回主屏，但**不能**把选择改掉。
        Assert.Equal(MonitorResolutionStatus.TargetMissing, result.Status);
        Assert.Equal(monitors[0], result.Target);
        Assert.False(result.ShouldPersistTarget);
        Assert.True(result.TargetUnavailable);
    }

    // ---------- 目标不存在：这是本 bug 的核心 ----------

    [Fact]
    public void Resolve_FallsBackToPrimary_WithoutOverwritingSavedChoice_WhenTargetUnplugged()
    {
        var monitors = new List<MonitorRef> { DualMonitors()[0] };   // 副屏被拔掉

        var result = MonitorTargetResolver.Resolve(
            monitors,
            @"\\?\DISPLAY#SIDE#2&bbb&0&UID2",
            MonitorTargetResolver.BuildSignature("SIDE27", 2560, 1440, -2560, 0),
            legacyIndex: 1);

        Assert.Equal(MonitorResolutionStatus.TargetMissing, result.Status);
        Assert.Equal(monitors[0], result.Target);

        // 关键：不写设置。写了就等于把用户的选择永久改成主屏，
        // 显示器插回来也回不去了——旧实现（Clamp）没有「选择」这个概念，
        // 所以它「看起来能用」，实际上每次插拔都在覆盖。
        Assert.False(result.ShouldPersistTarget);
    }

    /// <summary>
    /// 旧配置（只有序号）指向一根已经不存在的屏：正是「拔屏后静默跳屏」的现场。
    /// 迁移不能把它当成「用户选了主屏」写下去。
    /// </summary>
    [Fact]
    public void Resolve_LegacyIndexOutOfRange_ReportsMissingInsteadOfSilentlyClamping()
    {
        var monitors = new List<MonitorRef> { DualMonitors()[0] };

        var result = MonitorTargetResolver.Resolve(monitors, savedPath: null, savedSignature: null, legacyIndex: 1);

        Assert.Equal(MonitorResolutionStatus.TargetMissing, result.Status);
        Assert.Equal(monitors[0], result.Target);
        Assert.False(result.ShouldPersistTarget);
    }

    // ---------- 一次性迁移 ----------

    [Fact]
    public void Resolve_MigratesLegacyIndex_ToDevicePath()
    {
        var monitors = DualMonitors();

        var result = MonitorTargetResolver.Resolve(monitors, savedPath: null, savedSignature: null, legacyIndex: 1);

        Assert.Equal(MonitorResolutionStatus.MigratedFromIndex, result.Status);
        Assert.Equal(monitors[1], result.Target);
        Assert.True(result.ShouldPersistTarget);
    }

    /// <summary>
    /// 旧版默认值 0 既可能表示「第一块屏」也可能表示「从没选过」。
    /// 选「跟随主屏」这个方向，是因为旧代码在 0 的情况下确实会落到主屏
    /// （<c>GetMonitors()</c> 主屏优先 + <c>FirstOrDefault</c> 语义），
    /// 只有真的选过 1..n 的用户才会被迁移成固定目标。
    /// </summary>
    [Fact]
    public void Resolve_LegacyIndexZero_MeansFollowPrimary_AndIsNotPersisted()
    {
        var monitors = DualMonitors();

        var result = MonitorTargetResolver.Resolve(monitors, savedPath: null, savedSignature: null, legacyIndex: 0);

        Assert.Equal(MonitorResolutionStatus.FollowPrimary, result.Status);
        Assert.Equal(monitors[0], result.Target);
        Assert.False(result.ShouldPersistTarget);
    }

    [Fact]
    public void Resolve_EmptySavedPath_FollowsWhicheverMonitorIsPrimaryNow()
    {
        // 空路径 + 用户从没指定过：主屏从「左边那块」变成「右边那块」时，控件应该跟着走。
        var mainNowRight = new List<MonitorRef>
        {
            Monitor(@"\\?\DISPLAY#RIGHT#1&eee&0&UID1", "RIGHT27", left: 0, primary: true),
            Monitor(@"\\?\DISPLAY#LEFT#2&fff&0&UID2", "LEFT27", left: -2560),
        };

        var result = MonitorTargetResolver.Resolve(mainNowRight, string.Empty, string.Empty, legacyIndex: 0);

        Assert.Equal(MonitorResolutionStatus.FollowPrimary, result.Status);
        Assert.Equal(mainNowRight[0], result.Target);
    }

    // ---------- 边界 ----------

    [Fact]
    public void Resolve_ReportsNoMonitors_InsteadOfThrowing()
    {
        var result = MonitorTargetResolver.Resolve([], @"\\?\DISPLAY#ANY#1", "sig", 0);

        Assert.Equal(MonitorResolutionStatus.NoMonitors, result.Status);
        Assert.Null(result.Target);
        Assert.False(result.ShouldPersistTarget);
    }

    /// <summary>
    /// 一块屏都没有主屏标记（极少数驱动/虚拟屏场景）：不能返回 null 让调用方空引用，
    /// 退到列表第一项。
    /// </summary>
    [Fact]
    public void Resolve_FallsBackToFirstMonitor_WhenNoPrimaryFlagExists()
    {
        var monitors = new List<MonitorRef>
        {
            Monitor(@"\\?\DISPLAY#A#1&111&0&UID1", "A", left: 0),
            Monitor(@"\\?\DISPLAY#B#2&222&0&UID2", "B", left: 2560),
        };

        var result = MonitorTargetResolver.Resolve(monitors, @"\\?\DISPLAY#GONE#3", "sig", legacyIndex: 0);

        Assert.Equal(MonitorResolutionStatus.TargetMissing, result.Status);
        Assert.Equal(monitors[0], result.Target);
    }

    // ---------- 设备路径 → 型号 ----------

    /// <summary>
    /// 本机实测的真实路径形状（注意结尾还有一段 <c>#{接口类GUID}</c>，
    /// 早期文档里漏了这一段，照着写会以为路径对不上）。
    /// </summary>
    [Theory]
    [InlineData(@"\\?\DISPLAY#TCL2702#5&2ec52e58&0&UID4355#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}", "TCL2702")]
    [InlineData(@"\\?\DISPLAY#SKG27C5#5&2ec52e58&0&UID4357#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}", "SKG27C5")]
    [InlineData(@"\\?\display#dell u2720q#1&1&0&uid0", "dell u2720q")]
    [InlineData(@"\\?\DISPLAY#TCL2702#5&2ec52e58&0&UID4355", "TCL2702")]
    public void TryGetModelFromDevicePath_ExtractsModelSegment(string path, string expected)
    {
        Assert.Equal(expected, MonitorTargetResolver.TryGetModelFromDevicePath(path));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(@"\\.\DISPLAY1")]              // 只有 GDI 名：没有型号可抠
    [InlineData("MONITOR\\TCL2702\\{4d36e96e-e325-11ce-bfc1-08002be10318}\\0011")] // 设备类实例路径
    [InlineData(@"\\?\DISPLAY#TCL2702")]       // 没有第二个 '#'
    [InlineData(@"\\?\DISPLAY##5&1&0&UID0")]   // 型号段为空
    public void TryGetModelFromDevicePath_ReturnsNull_WhenNoModelAvailable(string? path)
    {
        // 返回 null 的语义是「调用方自己去 EnumDisplayDevices」，
        // 不是「没有名字」——不能让上层把 null 直接当型号显示。
        Assert.Null(MonitorTargetResolver.TryGetModelFromDevicePath(path));
    }
}
