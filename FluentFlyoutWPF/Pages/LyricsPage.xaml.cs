// Copyright (c) 2024-2026 The FluentFlyout Authors
// SPDX-License-Identifier: GPL-3.0-or-later

using FluentFlyout.Classes.Settings;
using System.Windows;
using System.Windows.Controls;

namespace FluentFlyoutWPF.Pages;

/// <summary>
/// 歌词设置页。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么整页没有 Premium 门控</b>（这是一个决定，不是遗漏）
/// </para>
///
/// <para>
/// 频谱页（<c>TaskbarVisualizerPage</c>）有完整的 Premium 包装：
/// 未购买时整页半透明、控件禁用、顶部挂 <c>PremiumPurchaseButton</c>。
/// 歌词页刻意<b>不</b>照做，理由是两者的商业含义不同：
/// </para>
///
/// <list type="number">
/// <item><b>没有独立的显示位置。</b>歌词只画在任务栏小组件里，而
/// <c>TaskbarWidgetEnabled</c> 本身就有 Premium 门控（见
/// <c>UserSettings.OnTaskbarWidgetEnabledChanged</c> 的短路回滚）。
/// 未购买的用户连小组件都开不了，歌词自然也进不去——再包一层
/// 不会多拦住任何一个用户。</item>
///
/// <item><b>频谱有独立的「试看」价值。</b>它有自己的参数体系
/// （位置、柱数、居中、基线、灵敏度），未购买时让用户看到
/// 「有这些可调项」才有转化意义。歌词页的 8 项全是小组件的附属配置，
/// 小组件打不开时整页拿出来展示没有任何引导作用。</item>
///
/// <item><b>少一层门控就少一个 bug 面。</b>见下面的「若要加」清单。</item>
/// </list>
///
/// <para>
/// 补充一个容易被忽略的事实：<c>LicenseManager.InitializeAsync</c> 里，
/// <c>GITHUB_RELEASE</c> 编译符号下会直接 <c>_isPremiumUnlocked = true</c>；
/// 非 Store 版（自编译）同样无条件解锁。也就是说<b>只有 Microsoft Store 版
/// 才存在「未解锁」这个状态</b>，本地开发怎么测都测不出差异——
/// 这一点也解释了为什么门控问题一直没被上游发现。
/// </para>
///
/// <para>
/// <b>如果将来要加门控，必须同时做这三件事，缺一项都会留隐患：</b>
/// </para>
///
/// <list type="number">
/// <item>
/// <c>MainWindow.ApplyLyricsSettingsTo</c> 里的 <c>active</c> 加上
/// <c>&amp;&amp; settings.IsPremiumUnlocked</c>。
/// <b>这一项最容易漏，后果也最实在</b>：只做 UI 层时，用户订阅过期后
/// 窗口不再显示，但 <c>LyricsService</c> 的帧泵照跑不误——
/// 每 33ms 一次跨进程 COM 采样，纯烧 CPU 且零可见输出。
/// </item>
/// <item>
/// <c>UserSettings.OnLyricsEnabledChanged</c> 里加与
/// <c>OnTaskbarWidgetEnabledChanged</c> 对称的短路回滚
/// （<c>if (newValue &amp;&amp; !IsPremiumUnlocked) { LyricsEnabled = false; return; }</c>），
/// 否则开关能被打开但实际不生效，用户看到的是「开关开着但没歌词」。
/// </item>
/// <item>
/// 本页加 <c>IsStoreVersion &amp;&amp; !IsPremiumUnlocked</c> 的三态 InfoBar
/// 与 <c>PremiumPurchaseButton</c>（可照抄
/// <c>TaskbarVisualizerPage.xaml</c> 顶部的三个 InfoBar 写法），
/// 否则未购买用户看不到任何「为什么用不了」的说明。
/// </item>
/// </list>
/// </remarks>
public partial class LyricsPage : Page
{
    public LyricsPage()
    {
        InitializeComponent();
        DataContext = SettingsManager.Current;
    }

    /// <summary>
    /// 「关闭频谱并启用歌词」：把任务栏小组件从频谱切回歌词。
    ///
    /// 顺序不能反。<see cref="UserSettings.OnTaskbarVisualizerEnabledChanged"/>
    /// 会在频谱被打开时把 <c>LyricsEnabled</c> 置为 false，
    /// 所以必须<b>先关频谱、再开歌词</b>；倒过来写会被那条互斥逻辑立刻打回。
    ///
    /// 这两次赋值各自会触发一次 <c>ApplyLyricsSettings</c>（都是幂等的），
    /// 不必在这里手动调用。
    /// </summary>
    private void OnSwitchFromVisualizerClick(object sender, RoutedEventArgs e)
    {
        var settings = SettingsManager.Current;
        settings.TaskbarVisualizerEnabled = false;
        settings.LyricsEnabled = true;
    }
}