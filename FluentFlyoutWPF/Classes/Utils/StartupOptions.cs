// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

namespace FluentFlyoutWPF.Classes.Utils;

/// <summary>
/// 命令行启动参数。目前只有一个开关：<see cref="MinimizedFlag"/>。
///
/// <para>
/// 带这个标记启动表示「这次是被开机自启拉起来的」，此时**不弹任何窗口**：
/// 不弹设置窗口、不弹引导窗口，只留托盘图标。
/// </para>
///
/// <para>
/// 为什么必须靠命令行、而不能只读 <c>Settings.Startup</c>：那个设置只说明
/// 「下次登录要自启」，它区分不了「用户此刻双击了图标」和「登录时被系统拉起」。
/// 同一台 <c>Startup=true</c> 的机器上，用户主动开应用时必须正常显示窗口，
/// 而自启时必须安静——只有每条启动路径各自声明意图才能把两者分开。
/// </para>
/// </summary>
internal static class StartupOptions
{
    /// <summary>自启标记，写在注册表启动项的 exe 路径之后。</summary>
    internal const string MinimizedFlag = "--minimized";

    /// <summary>
    /// 缓存一次即可：命令行在进程生命周期内不会变，而这几个属性会被
    /// 构造函数、单例分支、引导分支反复读。
    /// </summary>
    private static readonly bool isMinimized = HasFlag(Environment.GetCommandLineArgs());

    /// <summary>本次进程是否由开机自启拉起。</summary>
    internal static bool IsMinimized => isMinimized;

    /// <summary>
    /// 判断参数表里是否出现自启标记。大小写不敏感、容忍外层引号——
    /// 注册表里的值经 <c>CreateProcess</c> 解析后引号行为在不同路径下不一致，
    /// 与其猜不如两种都收。
    /// </summary>
    internal static bool HasFlag(IEnumerable<string> arguments)
    {
        foreach (string argument in arguments)
        {
            string candidate = argument.Trim().Trim('"');
            if (string.Equals(candidate, MinimizedFlag, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// 拼出写进注册表的自启命令行：带引号的路径 + 标记。
    ///
    /// <para>
    /// 引号不是凑数的：一旦后面跟了参数，含空格的路径（<c>C:\Program Files\...</c>）
    /// 不整体加引号就会被 <c>CreateProcess</c> 拆成「exe = C:\Program，参数 = Files\...」，
    /// 自启静默失败。旧代码只写裸路径不写参数，恰好绕过了这个坑，
    /// 加了标记就必须补上引号。
    /// </para>
    /// </summary>
    internal static string BuildAutostartCommandLine(string executablePath) =>
        $"\"{executablePath}\" {MinimizedFlag}";
}