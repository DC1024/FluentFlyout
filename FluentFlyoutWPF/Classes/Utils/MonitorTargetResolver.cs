// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using System.Globalization;

namespace FluentFlyoutWPF.Classes.Utils;

/// <summary>
/// 一块显示器的**稳定身份**。刻意不含任何 WPF 类型，便于脱离 UI 线程单测。
/// </summary>
/// <param name="DevicePath">
/// 监视器设备接口路径，形如
/// <c>\\?\DISPLAY#TCL2702#5&amp;2ec52e58&amp;0&amp;UID4355#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}</c>
/// ——这是唯一适合持久化的主键。取不到时会退回 GDI 设备名（<c>\\.\DISPLAY1</c>），
/// 那种情况下稳定性降级为「中」，但仍然比序号强。
/// </param>
/// <param name="DeviceId">GDI 设备名（<c>\\.\DISPLAY1</c>），只用于比较与日志。</param>
/// <param name="FriendlyName">
/// 友好名（型号，如 <c>TCL2702</c>），用于 UI 与签名。
/// 从 <paramref name="DevicePath"/> 里解析，而不是取 <c>DeviceString</c>——
/// 后者在本机对两块不同型号的屏都返回 <c>Generic PnP Monitor</c>。
/// </param>
/// <param name="Signature">模糊重连用的签名，见 <see cref="MonitorTargetResolver.BuildSignature"/>。</param>
/// <param name="IsPrimary">是否主显示器。</param>
public sealed record MonitorRef(
    string DevicePath,
    string DeviceId,
    string FriendlyName,
    string Signature,
    bool IsPrimary);

/// <summary>一次「我该显示在哪块屏」的解析结果。</summary>
public enum MonitorResolutionStatus
{
    /// <summary>保存的目标按设备路径精确命中。</summary>
    TargetFound,

    /// <summary>设备路径变了（换接口/换线），但签名（型号+分辨率+位置）对上了。</summary>
    RecoveredBySignature,

    /// <summary>升级后第一次：从旧的序号字段迁移而来。</summary>
    MigratedFromIndex,

    /// <summary>保存的目标当前不存在（被拔掉/关闭），本次回退到主屏。</summary>
    TargetMissing,

    /// <summary>用户从未指定过目标（或指定的就是序号 0）——跟随主显示器。</summary>
    FollowPrimary,

    /// <summary>当前一块屏都没有（极早期启动、显示驱动重载）。</summary>
    NoMonitors,
}

/// <summary>
/// 解析结果。<see cref="Target"/> 为 <c>null</c> 只会在 <see cref="MonitorResolutionStatus.NoMonitors"/> 时出现。
/// </summary>
/// <param name="Target">本次要显示在哪块屏。</param>
/// <param name="Status">为什么是这块屏。</param>
public readonly record struct MonitorResolution(MonitorRef? Target, MonitorResolutionStatus Status)
{
    /// <summary>
    /// 是否应该把 <see cref="Target"/> 写回设置。
    /// </summary>
    /// <remarks>
    /// 只有「精确命中」「签名恢复」「从序号迁移」三种情况写：
    /// <list type="bullet">
    /// <item><see cref="MonitorResolutionStatus.TargetMissing"/> **绝不能写**——写了就等于
    /// 把用户的选择永久改成主屏，显示器插回来也回不去了。</item>
    /// <item><see cref="MonitorResolutionStatus.FollowPrimary"/> 也不写——保持「跟随主屏」的语义，
    /// 用户换主屏时控件跟着走，和升级前的行为一致。</item>
    /// </list>
    /// </remarks>
    public bool ShouldPersistTarget => Status is
        MonitorResolutionStatus.TargetFound or
        MonitorResolutionStatus.RecoveredBySignature or
        MonitorResolutionStatus.MigratedFromIndex;

    /// <summary>保存的目标当前不可用（控件正临时显示在别的屏上）。</summary>
    public bool TargetUnavailable => Status == MonitorResolutionStatus.TargetMissing;
}

/// <summary>
/// 把「用户保存的目标」解析成「这次实际要显示在哪块屏」。
/// </summary>
/// <remarks>
/// <para>
/// 这个类存在的唯一理由是**替换掉按序号取值**：
/// <c>monitors[Clamp(savedIndex, 0, count - 1)]</c> 在显示器数量变化时会静默把控件挪到另一块屏，
/// 用户完全无法察觉。换成设备路径后，最坏情况也只是「暂时回退到主屏」，而原来的选择被保留，
/// 显示器接回来就自动恢复。
/// </para>
/// <para>
/// 纯函数、无副作用、不读全局单例——设置读写由调用方（<c>TaskbarWindow</c>）负责。
/// </para>
/// </remarks>
public static class MonitorTargetResolver
{
    /// <summary>
    /// 从设备接口路径里抠出型号：<c>\\?\DISPLAY#TCL2702#5&amp;2ec52e58&amp;0&amp;UID4355#{接口类GUID}</c>
    /// → <c>TCL2702</c>。
    /// </summary>
    /// <returns>不是设备接口路径（例如只有 GDI 名 <c>\\.\DISPLAY1</c>）时返回 <c>null</c>。</returns>
    /// <remarks>
    /// 这个方法是给友好名用的：<c>EnumDisplayDevices</c> 的 <c>DeviceString</c> 在有些机器上
    /// 对所有屏都返回 <c>Generic PnP Monitor</c>（本机实测两块不同型号的屏就是如此），
    /// 于是设置页两项无法区分、签名里的型号位也失去意义。路径里的这段是 Windows 从 EDID
    /// 记下的型号，免一次 P/Invoke，也没有失败分支。
    /// </remarks>
    public static string? TryGetModelFromDevicePath(string? devicePath)
    {
        const string prefix = @"\\?\DISPLAY#";

        if (string.IsNullOrEmpty(devicePath) ||
            !devicePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        int start = prefix.Length;
        int end = devicePath.IndexOf('#', start);
        if (end <= start)
        {
            return null;
        }

        string model = devicePath[start..end];
        return string.IsNullOrWhiteSpace(model) ? null : model;
    }

    /// <summary>
    /// 构造模糊重连签名：<c>友好名|宽x高|left,top</c>。
    /// </summary>
    /// <remarks>
    /// 用「型号 + 分辨率 + 相对位置」而不是 DPI 或刷新率：
    /// 前者在换接口、改缩放时都不变，后者会变。左/上坐标能把两块同型号屏区分开
    /// （它们不可能占同一个位置）。
    /// </remarks>
    public static string BuildSignature(string friendlyName, int width, int height, int left, int top)
    {
        return string.Create(CultureInfo.InvariantCulture,
            $"{friendlyName}|{width}x{height}|{left},{top}");
    }

    /// <summary>
    /// 解析目标显示器。
    /// </summary>
    /// <param name="monitors">当前所有显示器，顺序须与设置页展示顺序一致（主屏优先、再按 Left）。</param>
    /// <param name="savedPath">保存的设备路径；空表示尚未迁移或用户从未指定。</param>
    /// <param name="savedSignature">保存的签名；空表示没有可用的模糊重连依据。</param>
    /// <param name="legacyIndex">旧的序号字段，仅用于一次性迁移。</param>
    public static MonitorResolution Resolve(
        IReadOnlyList<MonitorRef> monitors,
        string? savedPath,
        string? savedSignature,
        int legacyIndex)
    {
        ArgumentNullException.ThrowIfNull(monitors);

        if (monitors.Count == 0)
        {
            return new MonitorResolution(null, MonitorResolutionStatus.NoMonitors);
        }

        var primary = FindPrimary(monitors);

        if (string.IsNullOrWhiteSpace(savedPath))
        {
            return ResolveWithoutSavedPath(monitors, primary, legacyIndex);
        }

        var exact = FindByPath(monitors, savedPath);
        if (exact is not null)
        {
            return new MonitorResolution(exact, MonitorResolutionStatus.TargetFound);
        }

        if (!string.IsNullOrWhiteSpace(savedSignature))
        {
            var recovered = FindBySignature(monitors, savedSignature);
            if (recovered is not null)
            {
                return new MonitorResolution(recovered, MonitorResolutionStatus.RecoveredBySignature);
            }
        }

        return new MonitorResolution(primary, MonitorResolutionStatus.TargetMissing);
    }

    private static MonitorResolution ResolveWithoutSavedPath(
        IReadOnlyList<MonitorRef> monitors,
        MonitorRef primary,
        int legacyIndex)
    {
        // 序号 <= 0 视为「跟随主屏」：0 既是默认值，也是旧版「第一块屏」，
        // 在旧配置里无法区分。选这个方向是因为升级前 0 本来就等于主屏，
        // 只有真的选过 1..n 的用户才会被迁移成固定目标。
        if (legacyIndex <= 0)
        {
            return new MonitorResolution(primary, MonitorResolutionStatus.FollowPrimary);
        }

        if (legacyIndex < monitors.Count)
        {
            return new MonitorResolution(monitors[legacyIndex], MonitorResolutionStatus.MigratedFromIndex);
        }

        // 旧配置指向的序号已经超出范围 —— 正是「拔了一块屏」的典型现场。
        return new MonitorResolution(primary, MonitorResolutionStatus.TargetMissing);
    }

    private static MonitorRef FindPrimary(IReadOnlyList<MonitorRef> monitors)
    {
        foreach (var monitor in monitors)
        {
            if (monitor.IsPrimary)
            {
                return monitor;
            }
        }

        return monitors[0];
    }

    private static MonitorRef? FindByPath(IReadOnlyList<MonitorRef> monitors, string savedPath)
    {
        foreach (var monitor in monitors)
        {
            if (string.Equals(monitor.DevicePath, savedPath, StringComparison.OrdinalIgnoreCase))
            {
                return monitor;
            }
        }

        return null;
    }

    private static MonitorRef? FindBySignature(IReadOnlyList<MonitorRef> monitors, string savedSignature)
    {
        foreach (var monitor in monitors)
        {
            if (!string.IsNullOrEmpty(monitor.Signature) &&
                string.Equals(monitor.Signature, savedSignature, StringComparison.OrdinalIgnoreCase))
            {
                return monitor;
            }
        }

        return null;
    }
}