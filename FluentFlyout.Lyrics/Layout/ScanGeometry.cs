// Copyright (c) 2026 anync
// SPDX-License-Identifier: MIT
//
// 本文件为本仓库新增，不源自 TaskbarLyrics。

namespace FluentFlyout.Lyrics.Layout;

/// <summary>
/// 逐字扫描的渲染档位。
///
/// 这不是「平台能力」检测的结果——WPF 里 <c>LinearGradientBrush</c>
/// 永远可用，不存在不支持的情况。它是**性能降级**档位：
/// 渐变遮罩最费，硬边裁剪次之，整行高亮最省。
/// </summary>
public enum ScanRenderMode
{
    /// <summary>
    /// 渐变遮罩：已唱部分与未唱部分之间有一段羽化过渡。
    /// 视觉最好，成本最高（每帧改 3 个 GradientStop.Offset）。
    /// </summary>
    Gradient = 0,

    /// <summary>
    /// 硬边裁剪：用 <c>RectangleGeometry</c> 裁出已唱部分。
    /// 没有羽化，边界是硬的，但成本比渐变低。
    /// </summary>
    Clip = 1,

    /// <summary>
    /// 整行高亮：不做逐字，整行统一用主色。
    /// 用于帧里没有 <c>WordScanProgress</c> 的情况（无音节数据）。
    /// </summary>
    WholeLine = 2
}

/// <summary>
/// 扫描遮罩的几何参数。
///
/// 单独抽出来是因为这块算错了表现很隐蔽：羽化宽度超出扫描点两端时，
/// 短句上会看到「整行半透明」，而不是明显的位置错误。
/// 抽成纯函数后可以精确断言。
/// </summary>
public readonly record struct ScanGeometry
{
    /// <summary>扫描位置（DIP，相对行首）。</summary>
    public required double ScanX { get; init; }

    /// <summary>羽化半宽（DIP）。为 0 时退化成硬边。</summary>
    public required double Feather { get; init; }

    /// <summary>已唱部分的终点（DIP），= ScanX - Feather。</summary>
    public double SolidEnd => Math.Max(0, ScanX - Feather);

    /// <summary>未唱部分的起点（DIP），= ScanX + Feather。</summary>
    public double FadeStart => ScanX + Feather;

    /// <summary>
    /// 羽化宽度相对行宽的比例。0.12 取自 TL 的
    /// <c>--word-scan-feather-width: 0.12em</c>。
    /// </summary>
    public const double FeatherRatio = 0.12;

    /// <summary>羽化宽度的下限（DIP）。太窄看不到过渡，等于硬边。</summary>
    public const double MinFeather = 2;

    /// <summary>羽化宽度的上限（DIP）。太宽会让整行看起来都在渐变。</summary>
    public const double MaxFeather = 24;

    /// <summary>
    /// 按行宽与进度计算几何。
    /// </summary>
    /// <param name="progress">扫描进度 0..1。越界会被夹取。</param>
    /// <param name="lineWidth">行宽（DIP）。非法值按 1 处理。</param>
    /// <remarks>
    /// 羽化宽度必须同时受两个约束：
    /// 1. 绝对范围 [MinFeather, MaxFeather]——避免极短/极长句下比例失真；
    /// 2. 不超过扫描点到两端的距离——否则 <see cref="SolidEnd"/> 会被
    ///    <see cref="Math.Max"/> 夹到 0、<see cref="FadeStart"/> 会超出
    ///    行宽，短句上表现为「整行都在渐变」。
    /// </remarks>
    public static ScanGeometry Create(double progress, double lineWidth)
    {
        if (!double.IsFinite(lineWidth) || lineWidth <= 0)
        {
            lineWidth = 1;
        }

        if (!double.IsFinite(progress))
        {
            progress = 0;
        }

        progress = Math.Clamp(progress, 0, 1);

        var scanX = progress * lineWidth;
        var feather = Math.Clamp(lineWidth * FeatherRatio, MinFeather, MaxFeather);

        // 关键约束：羽化不能越过扫描点两侧的可用空间。
        feather = Math.Min(feather, Math.Min(scanX, lineWidth - scanX));

        return new ScanGeometry
        {
            ScanX = scanX,
            Feather = Math.Max(0, feather)
        };
    }
}
