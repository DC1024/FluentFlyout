// Copyright (c) 2026 anync
// SPDX-License-Identifier: MIT
//
// 本文件为本仓库新增，不源自 TaskbarLyrics。

using FluentFlyout.Lyrics.Layout;
using FluentFlyout.Lyrics.Models;
using System.Windows.Controls;

namespace FluentFlyout.Controls;

/// <summary>
/// 竖排（任务栏在屏幕左/右）的逐字点亮。
///
/// <para>
/// <b>为什么与横向是两套实现。</b>横向是一整块 <c>TextBlock</c>，
/// 逐字只能靠一层遮罩（<c>PrimaryScanText</c> + <c>OpacityMask</c>）去裁；
/// 竖排本来就是几十个独立的字形块（见 LyricsPanel.Vertical.cs），
/// 逐字点亮就是「改这些块的不透明度」——不需要遮罩、不需要几何、
/// 没有羽化边界，每帧最多写两三个属性。
/// </para>
///
/// <para>
/// <b>为什么用不透明度而不是换画笔。</b>字形块的 <c>Foreground</c> 是
/// 从 <c>PrimaryText</c> 抄过来的同一支画笔，换主题时
/// <see cref="SyncVerticalPalette"/> 会把整列重涂一遍——如果点亮状态也存在
/// <c>Foreground</c> 里，那次重涂就会把已唱/未唱一起抹平。存在
/// <c>Opacity</c> 里则互不干扰：配色走画笔，点亮走不透明度，两条路正交。
/// </para>
///
/// <para>
/// <b>与横向的视觉口径。</b>横向是「底色压到 0.60 + 覆盖层 1.0」，
/// 竖排取同样的 0.60 / 1.0，于是两个方向的对比度一致。差别只在颜色：
/// 横向的覆盖层是比主行更亮（或更暗）的另一支画笔，竖排沿用主行本色。
/// 这条是有意为之——竖排要换色就得在每次主题变化时重算两支画笔，
/// 而收益只有「已唱那半句略微更白一点」。
/// </para>
/// </summary>
public partial class LyricsPanel
{
    /// <summary>已唱格的格内进度低于这个值就不写属性（约等于一帧的抖动）。</summary>
    private const double VerticalScanWriteEpsilon = 0.001;

    /// <summary>
    /// 已唱部分的不透明度。与横向覆盖层的 1.0 对齐：
    /// 若取常亮的 0.90，暗色主题下「点亮」几乎看不出来（0.90 与 1.0 都是白）。
    /// </summary>
    private const double VerticalSungOpacity = 1.0;

    /// <summary>
    /// 每一格的字素数，与 <see cref="_verticalPrimaryBlocks"/> 一一对应。
    ///
    /// 存字素数而不是宽度：进度本身是「已唱字数 / 总字数」
    /// （见 <see cref="GlyphScanCursor"/> 的说明）。
    /// </summary>
    private readonly List<int> _verticalPrimaryRunLengths = new();

    /// <summary>
    /// 上一次真正写进字形块的 <c>Opacity</c>，用于跳过重复写入。
    ///
    /// 每写一次 <c>Opacity</c> 都会让该元素重新合成，30fps 下几十个块
    /// 全量重写是纯浪费；正常只该有「跨过一格」的那一次真正变化。
    /// </summary>
    private readonly List<double> _verticalAppliedOpacity = new();

    /// <summary>上一次点亮到第几格。-1 表示当前不是点亮态（整列常亮）。</summary>
    private int _verticalLitCount = -1;

    /// <summary>上一次应用过的进度，用于 1% 抖动抑制（与帧去重器同粒度）。</summary>
    private double _verticalLastProgress = -1;

    /// <summary>
    /// 最近一帧给出的扫描进度。<c>null</c> = 这一帧不该点亮（没有音节数据或被用户关掉）。
    ///
    /// <b>与 <see cref="_verticalLastProgress"/> 的区别</b>：这个是「帧说了什么」，
    /// 那个是「已经写进块里的是什么」。字形树会在 <see cref="ApplyFrame"/> 之外
    /// 被重建（宿主写短边、换 DPI、切方向），重建后必须能凭这一份把灯补上，
    /// 否则要等下一帧——30fps 下就是 33ms 的整列全亮，看得见。
    /// </summary>
    private double? _verticalScanProgress;

    /// <summary>
    /// 记下这一帧该点亮到哪儿。
    ///
    /// <b>横向也要记。</b>进度是帧的属性，与排版方向无关；而竖向的字形树会被
    /// 一堆 <see cref="ApplyFrame"/> 之外的路径重建（宿主写短边、换 DPI、
    /// 切方向），重建时必须能凭这一份把灯补上。只在竖排分支里记的话，
    /// 「先建树、后收到帧」的时序（截图探针就是）会整列常亮。
    /// </summary>
    private void TrackVerticalScan(LyricDisplayFrame frame)
    {
        _verticalScanProgress = WordScanEnabled ? frame.WordScanProgress : null;
    }

    /// <summary>
    /// 按 <see cref="_verticalScanProgress"/> 点亮字形块。
    ///
    /// <b>必须在字形树建好之后调用</b>：换行会整棵树重建，重建出来的块是常亮态，
    /// 先点灯会被下一次重建冲掉——表现是「新的一句前半秒全亮，然后才暗下去又亮起来」。
    /// </summary>
    private void ApplyVerticalScan()
    {
        if (_verticalScanProgress is not { } value ||
            value <= 0 ||
            _verticalPrimaryBlocks.Count == 0 ||
            _verticalPrimaryRunLengths.Count != _verticalPrimaryBlocks.Count)
        {
            ResetVerticalScan();
            return;
        }

        // 1% 以内的抖动直接忽略：帧去重器的量化粒度就是 0.01，
        // 这里再细也拿不到更细的输入，只会多做一次无意义的属性写入。
        // 首帧（_verticalLitCount < 0）不受此限，否则刚换行时会停在常亮态。
        if (_verticalLitCount >= 0 && Math.Abs(value - _verticalLastProgress) < 0.01)
        {
            return;
        }

        _verticalLastProgress = value;

        var cursor = GlyphScanCursor.Create(value, _verticalPrimaryRunLengths);
        _verticalLitCount = cursor.LitCount;

        for (var index = 0; index < _verticalPrimaryBlocks.Count; index++)
        {
            double opacity;

            if (index < cursor.LitCount)
            {
                opacity = VerticalSungOpacity;
            }
            else if (index == cursor.LitCount && !cursor.IsComplete)
            {
                // 正在唱的这一格：从暗淡到点亮之间线性过渡。
                // 取 1.0 而不是直接翻牌，是为了让长句（尤其是整格一个拉丁词）
                // 也有连续感——汉字一格一翻已经足够离散了。
                opacity = LyricsPanelMetrics.SecondaryOpacity +
                    ((VerticalSungOpacity - LyricsPanelMetrics.SecondaryOpacity) * cursor.CrossingFraction);
            }
            else
            {
                opacity = LyricsPanelMetrics.SecondaryOpacity;
            }

            SetVerticalBlockOpacity(index, opacity);
        }
    }

    /// <summary>
    /// 退出点亮态：整列恢复常亮。
    ///
    /// 只在确实处于点亮态时才逐块写回（<see cref="_verticalLitCount"/> &gt;= 0）；
    /// 否则每帧都会把几十个块重写一遍同值属性。
    /// </summary>
    private void ResetVerticalScan()
    {
        if (_verticalLitCount < 0)
        {
            return;
        }

        _verticalLitCount = -1;
        _verticalLastProgress = -1;

        for (var index = 0; index < _verticalPrimaryBlocks.Count; index++)
        {
            SetVerticalBlockOpacity(index, LyricsPanelMetrics.PrimaryOpacity);
        }
    }

    /// <summary>
    /// 写一个字形块的不透明度，值没变就跳过。
    /// </summary>
    private void SetVerticalBlockOpacity(int index, double opacity)
    {
        if (index < _verticalAppliedOpacity.Count &&
            Math.Abs(_verticalAppliedOpacity[index] - opacity) < VerticalScanWriteEpsilon)
        {
            return;
        }

        // 字形块与这两个列表是同一轮建树里同增同清的，
        // 正常情况下长度恒等；这里的兜底只为「列表被外部清空」这种不该发生的情况。
        if (index >= _verticalPrimaryBlocks.Count)
        {
            return;
        }

        while (_verticalAppliedOpacity.Count <= index)
        {
            _verticalAppliedOpacity.Add(double.NaN);
        }

        _verticalAppliedOpacity[index] = opacity;
        _verticalPrimaryBlocks[index].Opacity = opacity;
    }

    /// <summary>
    /// 记录一格的字素数并登记它当前的常亮态，供 <see cref="ApplyVerticalScan"/> 使用。
    /// </summary>
    private void RegisterVerticalPrimaryRun(string runText)
    {
        _verticalPrimaryRunLengths.Add(runText.Length);
        _verticalAppliedOpacity.Add(LyricsPanelMetrics.PrimaryOpacity);
    }

    /// <summary>
    /// 字形树重建/清空时，把点亮相关的记账一并作废。
    ///
    /// 不在这里逐块写回常亮：重建出来的块本来就是按
    /// <see cref="LyricsPanelMetrics.PrimaryOpacity"/> 建的，写回是多余的一次绘制。
    /// </summary>
    private void ResetVerticalScanBookkeeping()
    {
        _verticalPrimaryRunLengths.Clear();
        _verticalAppliedOpacity.Clear();
        _verticalLitCount = -1;
        _verticalLastProgress = -1;
    }
}