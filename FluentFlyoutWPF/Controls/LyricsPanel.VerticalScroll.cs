// Copyright (c) 2026 anync
// SPDX-License-Identifier: MIT
//
// 本文件为本仓库新增，不源自 TaskbarLyrics。

using FluentFlyout.Lyrics.Layout;
using FluentFlyout.Lyrics.Models;
using System.Windows.Media;

namespace FluentFlyout.Controls;

/// <summary>
/// 竖排（任务栏在屏幕左/右）的超长句滚动。
///
/// <para>
/// <b>为什么不截断。</b>一句 51 个字排在主轴上是 663 DIP，而任务栏能给的
/// 主轴长度常被钉在 480 DIP 上下（见 <c>LyricsVerticalLayout.DefaultMaxMainAxisLength</c>）。
/// 超出 143 DIP 的那一截正好是「还没唱的半句」——竖排一列到底的排法
/// 让溢出全部落在尾部，截断等于把歌词变成半句。加省略号、按句缩字号、
/// 折进第二列这三条路都试过，前两条读不全、第三条第二列已被译文占用。
/// </para>
///
/// <para>
/// <b>为什么不新写一套滚动。</b><see cref="ScrollGeometry.Create"/> 只认
/// 「文本长度 / 视口长度 / 行内进度」，与排版方向无关：横向滚 x，
/// 竖向滚的也是面板坐标系的 x（宿主再挂 +90 把它转成屏幕纵向）。
/// <c>lineDurationSeconds: 0</c> 走「未知时长」分支按进度线性推进，
/// 两个方向口径完全一致。自己再算一遍只会多一套需要同步修改的边界条件。
/// </para>
///
/// <para>
/// <b>为什么位移挂在整块画布上。</b>平移要一次性作用于几十个字形块。
/// 逐块改 <c>Canvas.SetLeft</c> 是 50 次属性写 + 50 次重排；
/// 给 <c>VerticalContainer</c> 挂一个 <see cref="TranslateTransform"/>，
/// 每帧最多一次属性写，且不触发任何 measure/arrange。
/// </para>
///
/// <para>
/// <b>渲染上的坑（踩过）。</b><see cref="Visual.RenderTransform"/> 会把元素
/// <b>自己的裁剪框一起搬走</b>。原先 <c>VerticalContainer</c> 上有
/// <c>ClipToBounds="True"</c>，于是平移到哪儿、裁剪窗口就跟到哪儿，
/// 尾巴永远出不来——位移生效了，画面上却毫无变化。修法是把裁剪上移到
/// 面板根元素（<c>UserControl.ClipToBounds</c>）：它在变换之外，窗口固定不动。
/// 现在 <c>VerticalContainer</c> 上**没有** ClipToBounds，是刻意的。
/// </para>
/// </summary>
public partial class LyricsPanel
{
    /// <summary>位移变化小于这个值就不写属性（double 精度级别，不影响视觉）。</summary>
    private const double VerticalScrollWriteEpsilon = 1e-6;

    /// <summary>
    /// 最近一帧给的行内进度。
    ///
    /// <b>与位移分开记。</b>字形树会在 <see cref="ApplyFrame"/> 之外被重建
    /// （宿主写短边、换 DPI、切方向、换行），重建后画布上还挂着旧位移；
    /// 只在帧循环里算位移的话，那些路径要么漏算、要么要等下一帧（33ms 的错位）。
    /// 记账每帧无条件发生，应用则由 <see cref="EnsureVerticalLayout"/>
    /// 与 <c>SizeChanged</c> 补一次——与 <c>LyricsPanel.VerticalScan.cs</c>
    /// 的点灯是同一套分工。
    /// </summary>
    private double _verticalLineProgress;

    /// <summary>
    /// 竖排画布当前的主轴位移（DIP，负值表示已往前滚）。
    ///
    /// 给探针与单测读：位移是纯几何量，能从渲染树里读出来就不必再猜
    /// 「滚动到底有没有作用到画面上」——那个判断靠肉眼看截图太容易搞错
    /// （长句与短句的截图差别只有开头几个字）。
    /// </summary>
    public double VerticalScrollOffset =>
        VerticalContainer.RenderTransform is TranslateTransform transform ? transform.X : 0;

    /// <summary>
    /// 记下这一帧的行内进度。
    ///
    /// <b>横向也要记。</b>进度是帧的属性，与排版方向无关；而竖向的画布会
    /// 在一堆 <see cref="ApplyFrame"/> 之外的路径上重建。只在竖排分支里记的话，
    /// 「先建树、后收到帧」的时序（截图探针就是）会整句停在开头。
    /// </summary>
    private void TrackVerticalScroll(LyricDisplayFrame frame) => _verticalLineProgress = frame.LineProgress;

    /// <summary>
    /// 按 <see cref="_verticalLineProgress"/> 写画布位移。
    ///
    /// <b>必须在字形树建好之后调用</b>：重建会清空画布并重新排字，
    /// 顺带把内容长度换掉，位移要按新长度重算。
    /// </summary>
    private void ApplyVerticalScroll()
    {
        if (!_isVertical)
        {
            ResetVerticalScroll();
            return;
        }

        if (VerticalContainer.RenderTransform is not TranslateTransform transform)
        {
            return;
        }

        // 传的这一句的长度，**不是** <see cref="VerticalContentLength"/>。
        //
        // 后者返回「整首歌最长的一列」，是给宿主定小组件尺寸用的（歌内恒定，
        // 否则胶囊会随每句伸缩）。拿它当滚动长度会让短句在长歌里也跟着滑——
        // 明明 20 个字一屏放得下，却因为同专辑有句 50 字的歌词而滚动。
        var geometry = ScrollGeometry.Create(
            lineProgress: _verticalLineProgress,
            textWidth: _verticalContentLength,
            viewportWidth: ResolveVerticalViewport(),
            // 帧里没有行时长，按进度线性滚（ScrollGeometry 的未知时长分支）。
            // 竖排的读法是提词器式的「跟着进度往下走」，线性反而更贴：
            // 有首尾静止的档位在竖排上会让长句的后半段永远来不及露出来。
            lineDurationSeconds: 0,
            enabled: FluentFlyout.Classes.Settings.SettingsManager.Current.TaskbarWidgetScrollingEnabled);

        var offset = geometry.OffsetX;

        // 值没变就不写：每写一次都会让整块画布（几十个子项）重新合成。
        if (Math.Abs(transform.X - offset) > VerticalScrollWriteEpsilon)
        {
            transform.X = offset;
        }
    }

    /// <summary>
    /// 竖排的视口长度（DIP），即面板主轴长度。
    ///
    /// <para>
    /// 取 <see cref="FrameworkElement.Width"/> 优先、<c>ActualWidth</c> 兜底，
    /// 顺序与 <see cref="ResolveVerticalCross"/> 相同且不能反：宿主在
    /// <c>CalculateSize</c> 里写完宽度之后**立刻**就要读内容长度建树，
    /// 那一刻 <c>ActualWidth</c> 还是上一轮的旧值。反过来的话，
    /// 换方向后的第一帧会按旧宽度判断「要不要滚」，短句被判成溢出、
    /// 或者长句被判成装得下。
    /// </para>
    /// <para>
    /// 返回 0 表示尺寸还没由宿主写入。<see cref="ScrollGeometry.Create"/>
    /// 对非正的视口返回 <see cref="ScrollGeometry.None"/>（位移 0），
    /// 正好就是「先不动，等尺寸到了再说」。
    /// </para>
    /// </summary>
    private double ResolveVerticalViewport()
    {
        var width = Width;

        if (!double.IsFinite(width) || width <= 0)
        {
            width = ActualWidth;
        }

        return double.IsFinite(width) && width > 0 ? width : 0;
    }

    /// <summary>
    /// 位移与进度归零。切回横向、清空内容时调用。
    ///
    /// 画布切到 Collapsed 之后位移留着也不会画出来，但切回来会带着上一次的
    /// 偏移量——那一帧的字会整体错位几十 DIP，看起来像「歌词突然跑到面板外」。
    /// </summary>
    private void ResetVerticalScroll()
    {
        _verticalLineProgress = 0;

        if (VerticalContainer.RenderTransform is TranslateTransform transform &&
            Math.Abs(transform.X) > VerticalScrollWriteEpsilon)
        {
            transform.X = 0;
        }
    }
}