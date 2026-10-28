// Copyright (c) 2026 anync
// SPDX-License-Identifier: MIT
//
// 本文件为本仓库新增，不源自 TaskbarLyrics。

using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using FluentFlyout.Controls;
using Xunit;

namespace FluentFlyout.Lyrics.Tests;

/// <summary>
/// 竖向任务栏歌词的**装配链**测试：走生产的 <c>CalculateSize</c>，
/// 而不是像截图矩阵那样由夹具直接把宽度写进面板。
///
/// 为什么必须有这一层：竖向下「一个字都不显示」的缺陷（面板宽度恒为 0、
/// 文字被 <c>ClipToBounds</c> 剪光）在 72 张截图矩阵里**全绿**——因为那套
/// 夹具自己给面板写了宽度，绕过了生产路径里唯一真正取宽度的地方。
/// 夹具通过 ≠ 生产路径正确，这一课值得用一个测试长期钉住。
///
/// 断言都落在公开可见的状态上（<see cref="FrameworkElement.Width"/>），
/// 不碰 internal：面板宽度就是那条断掉后再也补不回来的链环。
/// </summary>
public sealed class TaskbarVerticalLyricsWiringTests
{
    /// <summary>标准任务栏短边（DIP），与 <c>DefaultTaskbarWidgetHeight</c> 同源。</summary>
    private const double DefaultCrossSize = 40;

    /// <summary>小任务栏短边（DIP），与 <c>SmallTaskbarWidgetHeight</c> 同源。</summary>
    private const double SmallCrossSize = 28;

    [Fact]
    public void VerticalLyricsModeWritesWrapWidthIntoPanel()
    {
        var width = RunOnStaThread(() =>
        {
            var (control, panel) = CreateVerticalLyricsControl();

            control.CalculateSize(dpiScale: 1.0, availableWidth: 800);

            return panel.Width;
        });

        Assert.False(double.IsNaN(width), "竖向歌词模式下 LyricsPanel.Width 不能被留成 NaN：宽度为 Auto 时" +
                                          "面板的排列宽度是内容期望宽度 0，文字会被 ClipToBounds 整块剪掉。");
        Assert.Equal(DefaultCrossSize, width, precision: 3);
    }

    [Fact]
    public void SmallTaskbarUsesSmallCrossSize()
    {
        var width = RunOnStaThread(() =>
        {
            var (control, panel) = CreateVerticalLyricsControl();
            control.SetSmallTaskbarMode(true);

            control.CalculateSize(dpiScale: 1.0, availableWidth: 800);

            return panel.Width;
        });

        Assert.Equal(SmallCrossSize, width, precision: 3);
    }

    [Fact]
    public void SwitchingBackToHorizontalReleasesTheWidth()
    {
        var (widthAfterSwitchBack, widthAfterHorizontalLayout) = RunOnStaThread(() =>
        {
            var (control, panel) = CreateVerticalLyricsControl();
            control.CalculateSize(dpiScale: 1.0, availableWidth: 800);

            // 竖向写成 40 之后切回横向：面板必须回到 Auto，否则横向第一帧
            // 会按 40 宽折行，长句被切掉。
            control.SetVerticalMode(false);
            var afterSwitchBack = panel.Width;

            control.CalculateSize(dpiScale: 1.0, availableWidth: 800);
            return (afterSwitchBack, panel.Width);
        });

        Assert.True(double.IsNaN(widthAfterSwitchBack),
            "切回横向后 LyricsPanel.Width 应被清回 NaN（Auto），实为 " +
            $"{widthAfterSwitchBack.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)}。");

        // 横向分支自己会重写一个有限宽度（值取决于内容预算，这里不断言具体数值，
        // 只要求「不是竖向留下的 40、也不是 NaN」）。
        Assert.False(double.IsNaN(widthAfterHorizontalLayout), "横向 CalculateSize 之后宽度应被重新写入。");
        Assert.NotEqual(DefaultCrossSize, widthAfterHorizontalLayout);
    }

    [Fact]
    public void RepeatedLayoutKeepsWidthStable()
    {
        // CalculateSize 被 1.5s 定时器、换歌、每次歌词换行反复调用。
        // 重复调用必须幂等：宽度不变（写入有值比较，不会每轮触发布局遍）。
        var widths = RunOnStaThread(() =>
        {
            var (control, panel) = CreateVerticalLyricsControl();

            var samples = new double[3];
            for (var i = 0; i < samples.Length; i++)
            {
                control.CalculateSize(dpiScale: 1.0, availableWidth: 800);
                samples[i] = panel.Width;
            }

            return samples;
        });

        Assert.All(widths, w => Assert.Equal(DefaultCrossSize, w, precision: 3));
    }

    /// <summary>
    /// 造一个「竖向 + 歌词模式」的控件。
    /// </summary>
    /// <remarks>
    /// 顺序刻意与生产一致：<c>SetLyricsMode</c> → <c>SetVerticalMode</c> → <c>CalculateSize</c>
    /// （<c>TaskbarWindow.PositionWidget</c> 就是先切方向、再问尺寸）。
    /// </remarks>
    private static (TaskbarWidgetControl Control, FrameworkElement Panel) CreateVerticalLyricsControl()
    {
        var control = new TaskbarWidgetControl();
        control.SetLyricsMode(true);
        control.SetVerticalMode(true);

        var panel = control.FindName("LyricsPanel") as FrameworkElement;
        Assert.NotNull(panel);

        return (control, panel);
    }

    /// <summary>
    /// 在共享的 STA 线程上跑一段 WPF 代码并把结果带回来。
    /// </summary>
    /// <remarks>
    /// xunit 的测试线程是 MTA，WPF 控件必须 STA；这里不引第三方 STA 扩展包，
    /// 手工起线程反而更清楚。异常要显式捕获后重抛——否则会变成
    /// 「测试通过但什么都没跑」。
    /// </remarks>
    private static T RunOnStaThread<T>(Func<T> action) => WpfStaHost.Invoke(action);
}

/// <summary>
/// 测试进程内唯一的 WPF 宿主线程。
/// </summary>
/// <remarks>
/// <para>
/// 两个硬约束决定了它必须是「一个线程 + 一个 Application」而不是每个用例各起一个：
/// </para>
/// <list type="number">
/// <item>同一个 AppDomain 里只能有一个 <see cref="Application"/> 实例，
/// 第二次 <c>new Application()</c> 会直接抛异常。</item>
/// <item><see cref="Application.Current"/> 是线程亲和的，
/// 资源查找必须在创建它的那个线程上做。</item>
/// </list>
/// <para>
/// <c>TaskbarWidgetControl.xaml</c> 里有一处
/// <c>Style="{StaticResource UiContextMenu}"</c>，这个键来自 WPF-UI 的
/// <c>ControlsDictionary</c>。测试里没有真实的 App.xaml，所以补一个等价的
/// 最小 <see cref="ContextMenu"/> 样式——本测试只关心宽度数值，
/// 不关心右键菜单长什么样。
/// </para>
/// </remarks>
internal static class WpfStaHost
{
    private static readonly Lazy<Dispatcher> HostDispatcher = new(Start, LazyThreadSafetyMode.ExecutionAndPublication);

    public static T Invoke<T>(Func<T> action) => HostDispatcher.Value.Invoke(action);

    private static Dispatcher Start()
    {
        Dispatcher? dispatcher = null;
        Exception? startupFailure = null;
        using var ready = new ManualResetEventSlim(false);

        var thread = new Thread(() =>
        {
            try
            {
                var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                application.Resources["UiContextMenu"] = new Style(typeof(ContextMenu));
                dispatcher = Dispatcher.CurrentDispatcher;
            }
            catch (Exception ex)
            {
                startupFailure = ex;
            }
            finally
            {
                ready.Set();
            }

            if (startupFailure is null)
            {
                Dispatcher.Run();
            }
        })
        {
            IsBackground = true,
            Name = "WPF test host",
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait(TimeSpan.FromSeconds(30));

        if (startupFailure is not null)
        {
            throw new InvalidOperationException("初始化 WPF 测试宿主失败。", startupFailure);
        }

        return dispatcher ?? throw new InvalidOperationException("WPF 测试宿主没有返回 Dispatcher。");
    }
}
