// Copyright (c) 2026 anync
// SPDX-License-Identifier: MIT
//
// 本文件为本仓库新增，不源自 TaskbarLyrics。

using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using FluentFlyout.Controls;
using FluentFlyout.Lyrics.Models;
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

    /// <summary>
    /// 小组件内容区相对短边的内缩（DIP）：<c>TopBorder</c> 上边框 1.25 +
    /// 下外边距 1.25。折行宽度必须是「可用短边」= 短边 − 内缩，
    /// 与 <c>TaskbarWidgetControl.WidgetCrossInset</c> 同源。
    /// </summary>
    private const double CrossInset = 2.5;

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

        // 期望值是「可用短边」而不是短边本身：按 40 写会让每行文本比视口宽
        // 2.5 DIP，右边缘被切掉一条（不报错、不裁整行，肉眼几乎判不出来）。
        Assert.Equal(DefaultCrossSize - CrossInset, width, precision: 3);
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

        Assert.Equal(SmallCrossSize - CrossInset, width, precision: 3);
    }

    [Fact]
    public void SwitchingBackToHorizontalReleasesTheSize()
    {
        var (widthAfterSwitchBack, heightAfterSwitchBack, widthAfterHorizontalLayout) = RunOnStaThread(() =>
        {
            var (control, panel) = CreateVerticalLyricsControl();
            control.CalculateSize(dpiScale: 1.0, availableWidth: 800);

            // 竖向写成「37.5 宽 + 主轴长的高」之后切回横向：两个尺寸都必须回到
            // Auto，否则横向第一帧会按 37.5 折行（长句被切），或按竖向的高度
            // 留一条空白。
            control.SetVerticalMode(false);
            var afterSwitchBack = (panel.Width, panel.Height);

            control.CalculateSize(dpiScale: 1.0, availableWidth: 800);
            return (afterSwitchBack.Width, afterSwitchBack.Height, panel.Width);
        });

        Assert.True(double.IsNaN(widthAfterSwitchBack),
            "切回横向后 LyricsPanel.Width 应被清回 NaN（Auto），实为 " +
            $"{widthAfterSwitchBack.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)}。");
        Assert.True(double.IsNaN(heightAfterSwitchBack),
            "切回横向后 LyricsPanel.Height 应被清回 NaN（Auto），实为 " +
            $"{heightAfterSwitchBack.ToString("F3", System.Globalization.CultureInfo.InvariantCulture)}。");

        // 横向分支自己会重写一个有限宽度（值取决于内容预算，这里不断言具体数值，
        // 只要求「不是竖向留下的那个、也不是 NaN」）。
        Assert.False(double.IsNaN(widthAfterHorizontalLayout), "横向 CalculateSize 之后宽度应被重新写入。");
        Assert.NotEqual(DefaultCrossSize - CrossInset, widthAfterHorizontalLayout);
    }

    [Fact]
    public void WrapWidthEqualsAvailableCrossSize()
    {
        // 折行宽度不是控件高度本身：TopBorder 的上边框与下外边距吃掉 2.5 DIP，
        // 40 的高度只剩 37.5 给内容。这条测试用**真实布局量出的可用短边**校验
        // 宿主的常量，XAML 里改边框/外边距而忘了改常量时会红。
        var (writtenWidth, availableFromLayout) = RunOnStaThread(() =>
        {
            var (control, _) = CreateVerticalLyricsControl();

            var host = new Canvas { Width = 48, Height = 1440 };
            host.Children.Add(control);
            control.Width = 240;
            control.Height = DefaultCrossSize;
            host.Measure(new Size(48, 1440));
            host.Arrange(new Rect(0, 0, 48, 1440));
            host.UpdateLayout();

            var topBorder = (Border)control.FindName("TopBorder");
            var available = topBorder.ActualHeight - topBorder.BorderThickness.Top;

            control.CalculateSize(dpiScale: 1.0, availableWidth: 800);
            var panel = (FrameworkElement)control.FindName("LyricsPanel");
            return (panel.Width, available);
        });

        Assert.Equal(availableFromLayout, writtenWidth, precision: 3);
    }

    [Fact]
    public void LongLineGetsFullMainAxisLengthAndIsNotClipped()
    {
        // 真机缺陷（左侧竖排任务栏）：9 个字的「被爱的深夜我在想念」只显示
        // 「被爱的 / 深夜我」——每列 3 字、上限 6 字，第 7 个字往后静默消失。
        //
        // 成因：面板挂在横向 MainStackPanel 里，交叉轴约束映射到面板的**宽度**
        // （折行宽度），映射不到高度；而面板高度撑不起来（内容在 Canvas 里，
        // Canvas 的期望尺寸恒为 0）。于是折行结果只写进了 Canvas 高度，
        // 传导不到面板自身，被 ClipToBounds 剪掉。
        //
        // 因此这里断言的不是「宽度写对了」，而是「面板沿主轴的长度撑到了
        // 内容的真实高度」，并且「小组件比 StackPanel 要的更宽」。
        const string LongLine = "如果这场雨一直下到明天清晨我们是不是就能一直站在这里不分开";

        var (panelHeight, contentHeight, stackWidth, widgetWidth) = RunOnStaThread(() =>
        {
            var (control, _) = CreateVerticalLyricsControl();

            var host = new Canvas { Width = 48, Height = 1440 };
            host.Children.Add(control);

            // 先排一遍：让控件的字体/样式落定，否则第一次量到的行高来自
            // 应用默认字体，会比真实字体少一行。
            control.Width = 240;
            control.Height = DefaultCrossSize;
            control.Measure(new Size(240, DefaultCrossSize));
            control.Arrange(new Rect(0, 0, 240, DefaultCrossSize));
            control.UpdateLayout();

            control.ApplyLyricDocument(new[] { new LyricLine(TimeSpan.Zero, LongLine) });
            control.UpdateLyricsFrame(new LyricDisplayFrame(LongLine, string.Empty, "标题", 0.3));

            var (logicalWidth, logicalHeight) = control.CalculateSize(dpiScale: 1.0, availableWidth: 800);
            control.Width = logicalWidth;
            control.Height = logicalHeight;
            control.Measure(new Size(logicalWidth, logicalHeight));
            control.Arrange(new Rect(0, 0, logicalWidth, logicalHeight));
            control.UpdateLayout();

            var panel = (FrameworkElement)control.FindName("LyricsPanel");
            var lyricsPanel = (LyricsPanel)panel;
            var stack = (FrameworkElement)control.FindName("MainStackPanel");

            return (panel.Height, lyricsPanel.VerticalContentHeight, stack.ActualWidth, control.ActualWidth);
        });

        Assert.InRange(contentHeight, 60, 2000);
        Assert.True(panelHeight >= contentHeight - 0.5,
            $"竖向歌词面板沿主轴只拿到 {panelHeight:F1} DIP，内容需要 {contentHeight:F1} DIP——" +
            "差额会被 ClipToBounds 吃掉，屏幕上表现为「句子没写完」。");

        Assert.True(stackWidth <= widgetWidth + 0.5,
            $"MainStackPanel 要 {stackWidth:F1} DIP，小组件只有 {widgetWidth:F1} DIP——" +
            "超出部分会被 MainBorder 裁掉。");
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

        Assert.All(widths, w => Assert.Equal(DefaultCrossSize - CrossInset, w, precision: 3));
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
