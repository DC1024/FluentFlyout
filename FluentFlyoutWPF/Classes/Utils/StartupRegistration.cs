// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using Microsoft.Win32;
using System.IO;
using Windows.ApplicationModel;

namespace FluentFlyoutWPF.Classes.Utils;

/// <summary>
/// 「开机自启」登记项的读写。
///
/// <para>
/// 全仓只有这里碰 <c>HKCU\...\Run</c>。原先 <c>MainWindow</c> 与 <c>SystemPage</c>
/// 各写一份、写法还不一致（一个只写不清、一个写前查路径），是同一个缺陷的两个入口。
/// </para>
/// </summary>
internal static class StartupRegistration
{
    private const string RunKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "FluentFlyout";

    /// <summary>
    /// 当前进程是否跑在 MSIX 包里。打包环境下 <c>Package.Current</c> 可用，
    /// 未打包会抛 <see cref="InvalidOperationException"/>（0x80073D54）——这是判断
    /// 「是否打包」最可靠的方式，比看 exe 路径里有没有 <c>WindowsApps</c> 稳。
    /// </summary>
    private static readonly bool isPackaged = DetectPackaged();

    internal static bool IsPackaged => isPackaged;

    /// <summary>
    /// 登记开机自启。返回 <c>false</c> 表示**确实想写但没写成**，<paramref name="error"/> 带原因。
    ///
    /// <para>
    /// 打包版直接返回 <c>true</c> 且**不写** Run 键：MSIX 清单里已经声明了
    /// <c>windows.startupTask</c>（TaskId=<c>FluentFlyoutStartup</c>），系统会在登录时拉起它。
    /// 再从 Run 键补一条，等于让同一个应用登录时启动两次——第二份抢单例互斥量失败后
    /// 会请求已有实例「打开设置窗口」，这就是「开机莫名弹出设置窗口」的成因。
    /// </para>
    /// </summary>
    internal static bool TryEnable(out string? error)
    {
        error = null;

        if (isPackaged)
        {
            // 打包版由 windows.startupTask 负责，不用 Run 键。顺手把旧版本
            // （2.16.0.0 及更早）留下的那条清掉——它指向 WindowsApps 下的
            // exe，被直接拉起时是「未打包」身份，走的是另一份 %APPDATA%，
            // 与 startupTask 拉起的那份抢同一个单例互斥量，正是弹窗的来源。
            _ = TryDisable(out _);
            return true;
        }

        string? executablePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(executablePath) || !File.Exists(executablePath))
        {
            error = "Application executable not found";
            return false;
        }

        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key == null)
            {
                error = @"Cannot open HKCU\Software\Microsoft\Windows\CurrentVersion\Run";
                return false;
            }

            key.SetValue(ValueName, StartupOptions.BuildAutostartCommandLine(executablePath));
            return true;
        }
        catch (Exception exception)
        {
            error = exception.Message;
            return false;
        }
    }

    /// <summary>
    /// 撤销开机自启。
    ///
    /// <para>
    /// 打包版也照旧清一遍 Run 键：旧版本（含 2.16.0.0）在打包环境下也会写这一条，
    /// 留着就是一条多余的启动入口，正好借用户关开关时清掉。
    /// </para>
    /// </summary>
    internal static bool TryDisable(out string? error)
    {
        error = null;

        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key == null)
            {
                error = @"Cannot open HKCU\Software\Microsoft\Windows\CurrentVersion\Run";
                return false;
            }

            if (key.GetValue(ValueName) != null)
                key.DeleteValue(ValueName, throwOnMissingValue: false);

            return true;
        }
        catch (Exception exception)
        {
            error = exception.Message;
            return false;
        }
    }

    private static bool DetectPackaged()
    {
        try
        {
            // 注意别写成 Windows.ApplicationModel.Package：本文件的命名空间在
            // FluentFlyoutWPF 之下，`Windows` 会先命中 FluentFlyoutWPF.Windows
            // 这个自身命名空间（CS0234）。走 using 指令解析才不会被截胡。
            _ = Package.Current.Id;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}