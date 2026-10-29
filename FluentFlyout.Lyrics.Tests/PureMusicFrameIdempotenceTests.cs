// Copyright (c) 2026 anync
// SPDX-License-Identifier: MIT
//
// 本文件为本仓库新增，不源自 TaskbarLyrics。

using System.Windows;
using System.Windows.Controls;
using FluentFlyout.Controls;
using FluentFlyout.Lyrics.Layout;
using FluentFlyout.Lyrics.Models;
using Xunit;

namespace FluentFlyout.Lyrics.Tests;

/// <summary>
/// 纯音乐帧的幂等性：同一帧喂两次，面板内容必须仍然为空。
/// </summary>
/// <remarks>
/// <para>
/// 缺陷原状：<c>ApplyFrame</c> 先把 <c>frame.CurrentLine</c> 无条件写进
/// <c>PrimaryText</c>，之后才调 <c>UpdatePureMusicState</c>；而后者是**边沿触发**的
/// ——<c>IsPureMusic</c> 没有变化就早退。于是第二帧纯音乐到达时，清空分支不再执行，
/// 「纯音乐，请欣赏」这行占位文字被留在了面板上。
/// </para>
/// <para>
/// 生产里看不见它：<c>TaskbarWidgetControl.UpdateTextRegionVisibility</c> 在纯音乐态
/// 把整个歌词面板让位给频谱。但面板的**状态**是错的——只要有别的路径把它露出来
/// （截图探针补喂一帧、将来改成并列显示、切换可见性的那一瞬），就会画出一行
/// 本不该存在的歌词。看不见的错状态仍然是错状态。
/// </para>
/// <para>
/// 为什么这条测试落在单测而不是只靠截图矩阵：矩阵里「纯音乐态无墨迹」那条反向断言
/// 确实能抓到它（补喂一帧就会变红），但那要跑几十秒、还要能启动 WPF 渲染。
/// 这里三行断言、几百毫秒，失败信息直接指向 <c>ApplyFrame</c>。
/// </para>
/// <para>
/// 只走公开入口（<c>ApplyMetrics</c> / <c>SetVerticalMode</c> / <c>ApplyFrame</c> /
/// <c>FindName</c>）：<c>FluentFlyoutWPF</c> 没有对本测试程序集开
/// <c>InternalsVisibleTo</c>（只有 <c>FluentFlyout.Lyrics</c> 开了），
/// 直接读 x:Name 生成的 internal 字段编译不过。
/// </para>
/// </remarks>
public sealed class PureMusicFrameIdempotenceTests
{
    /// <summary>帧里的纯音乐占位行，与截图矩阵夹具、<c>LyricDocument</c> 的启发式判定同源。</summary>
    private const string PureMusicPlaceholder = "纯音乐，请欣赏";

    /// <summary>一句普通歌词，用来确认「面板确实会画字」这个前提成立。</summary>
    private const string NormalLine = "所以暂时将你眼睛闭了起来";

    [Fact]
    public void RepeatedPureMusicFrameKeepsPanelEmpty()
    {
        var (afterFirst, afterSecond, stillPureMusic, scanText, secondaryText) = RunOnStaThread(() =>
        {
            var (panel, primary, primaryScan, secondary) = CreatePanel();
            var frame = PureMusicFrame();

            panel.ApplyFrame(frame);
            var first = primary.Text;

            // 第二帧与第一帧逐字段相同。生产里 30fps 就是这么喂的：内容不变，
            // 只有 LineProgress 之类在动；截图探针则是在布局跑完后原样再喂一次。
            panel.ApplyFrame(frame);

            return (first, primary.Text, panel.IsPureMusic, primaryScan.Text, secondary.Text);
        });

        Assert.Equal(string.Empty, afterFirst);
        Assert.Equal(string.Empty, afterSecond);
        Assert.Equal(string.Empty, scanText);
        Assert.Equal(string.Empty, secondaryText);

        // 内容归零不等于退出纯音乐态：宿主靠这个标志决定把面板让位给频谱，
        // 归一化时把它一起清掉的话，频谱会在纯音乐段整段消失。
        Assert.True(stillPureMusic, "重复喂纯音乐帧之后 IsPureMusic 必须仍为 true。");
    }

    [Fact]
    public void PureMusicFrameErasesTheLineBeforeIt()
    {
        var (before, after, scanVisibility, secondaryVisibility) = RunOnStaThread(() =>
        {
            var (panel, primary, primaryScan, _) = CreatePanel();

            panel.ApplyFrame(NormalFrame());
            var shown = primary.Text;

            panel.ApplyFrame(PureMusicFrame());

            var secondaryContainer = Find<FrameworkElement>(panel, "SecondaryContainer");
            return (shown, primary.Text, primaryScan.Visibility, secondaryContainer.Visibility);
        });

        // 前提先立住：同一套夹具在纯音乐帧之前确实写出了字。
        // 少了这一条，下面三个断言在「面板根本不画字」时也会全绿。
        Assert.Equal(NormalLine, before);

        Assert.Equal(string.Empty, after);
        Assert.Equal(Visibility.Collapsed, scanVisibility);
        Assert.Equal(Visibility.Collapsed, secondaryVisibility);
    }

    [Fact]
    public void LeavingPureMusicPutsTextBack()
    {
        var after = RunOnStaThread(() =>
        {
            var (panel, primary, _, _) = CreatePanel();

            // 连喂两帧纯音乐：第二帧正是原先会把占位文字写回去的那一帧。
            panel.ApplyFrame(PureMusicFrame());
            panel.ApplyFrame(PureMusicFrame());

            panel.ApplyFrame(NormalFrame());

            return primary.Text;
        });

        // 归一化只该作用在纯音乐帧的**副本**上。若它顺手改了面板的持久状态
        // （例如把 _frame 里留下一个「永远空」的标记），纯音乐结束后的第一句歌词
        // 就再也画不出来了——那是把一个隐形缺陷换成另一个显形缺陷。
        Assert.Equal(NormalLine, after);
    }

    [Fact]
    public void VerticalGlyphTreeStaysEmptyAcrossRepeatedPureMusicFrames()
    {
        var (glyphsForNormalLine, glyphsAfterPureMusic) = RunOnStaThread(() =>
        {
            var (panel, primary, _, _) = CreatePanel(isVertical: true);

            // 竖排的字不在 PrimaryText 里，而是一列独立的字形块（见
            // LyricsPanel.Vertical.cs）。短边由宿主写进 Height，这里照做——
            // 读不到短边时 EnsureVerticalLayout 会整个早退，断言就变成空转。
            panel.SetVerticalMode(true);
            panel.Height = 37.5;

            panel.ApplyFrame(NormalFrame());
            var drawn = Find<Panel>(panel, "VerticalContainer").Children.Count;

            panel.ApplyFrame(PureMusicFrame());
            panel.ApplyFrame(PureMusicFrame());

            return (drawn, Find<Panel>(panel, "VerticalContainer").Children.Count);
        });

        // 同上：先证明建树这条路径活着，再断言它被清空。
        Assert.True(glyphsForNormalLine > 0,
            $"普通歌词在竖排下应当产生字形块，实为 {glyphsForNormalLine} 个——" +
            "为 0 说明短边没被读到或建树没跑，这条断言就不成立。");

        Assert.Equal(0, glyphsAfterPureMusic);
    }

    /// <summary>
    /// 造一个面板，并按生产的顺序设好度量。
    /// </summary>
    /// <remarks>
    /// <c>ApplyMetrics</c> 必须早于 <c>ApplyFrame</c>：它会清掉宽度缓存、
    /// 写字号与行高，<c>ApplyFrame</c> 里读的 <c>_hasMetrics</c> 也由它置位。
    /// 顺序反了不会抛异常，只会让「文本宽度」这类断言读到 0。
    /// </remarks>
    private static (LyricsPanel Panel, TextBlock Primary, TextBlock PrimaryScan, TextBlock Secondary) CreatePanel(
        bool isVertical = false)
    {
        var panel = new LyricsPanel();
        panel.ApplyMetrics(LyricsMetricsPolicy.Create(
            isVertical: isVertical,
            isSmallTaskbar: false));

        return (
            panel,
            Find<TextBlock>(panel, "PrimaryText"),
            Find<TextBlock>(panel, "PrimaryScanText"),
            Find<TextBlock>(panel, "SecondaryText"));
    }

    private static LyricDisplayFrame PureMusicFrame() => new(
        CurrentLine: PureMusicPlaceholder,
        NextLine: string.Empty,
        Title: "测试曲目",
        IsPureMusic: true);

    private static LyricDisplayFrame NormalFrame() => new(
        CurrentLine: NormalLine,
        NextLine: "黑暗之中漂浮我的期待",
        Title: "测试曲目");

    private static T Find<T>(FrameworkElement root, string name)
        where T : class =>
        root.FindName(name) as T
        ?? throw new InvalidOperationException(
            $"在 {root.GetType().Name} 的命名域里找不到 \"{name}\"，测试夹具与 XAML 已经不同步。");

    /// <summary>
    /// 在共享的 STA 线程上跑一段 WPF 代码并把结果带回来。
    /// </summary>
    /// <remarks>
    /// 复用 <see cref="WpfStaHost"/>：同一个进程里只能有一个 <see cref="Application"/>，
    /// 每个用例各起一个会直接抛异常。
    /// </remarks>
    private static T RunOnStaThread<T>(Func<T> action) => WpfStaHost.Invoke(action);
}
