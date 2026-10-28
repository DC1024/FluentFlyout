// Copyright (c) 2026 anync
// SPDX-License-Identifier: MIT
//
// 本文件为本仓库新增，不源自 TaskbarLyrics。

namespace FluentFlyout.Lyrics.Layout;

/// <summary>
/// 竖排逐字点亮的游标：把「这一行唱到几成」换算成「前几格已点亮、当前这格点亮了几成」。
///
/// <para>
/// <b>为什么不能复用 <see cref="ScanGeometry"/>。</b>横向的扫描是一块遮罩，
/// 位置 = 进度 × 行宽，是一维的像素坐标；竖排的每一格是独立
/// <c>TextBlock</c>（见 LyricsPanel.Vertical.cs），
/// 点亮单位是「格」而不是像素。硬把像素坐标映射过去，会在拉丁整词
/// （一格六个字母、宽度是汉字的三倍）上明显跑偏。
/// </para>
///
/// <para>
/// <b>为什么按字素数分配而不是按宽度。</b>
/// <c>LyricSyncService.CalculateWordScanProgress</c> 算出来的进度本身就是
/// <c>已唱字数 / 总字数</c>（它用 <c>line.Text.IndexOf(syllableText)</c> 得字符位置），
/// 所以竖排按字素数换算才是**同一把尺子**。按宽度换算会让宽格（拉丁词）
/// 独占时间，两种方向的节奏就对不上了。
/// </para>
///
/// <para>
/// 与 <see cref="ScanGeometry"/> 一样抽成纯函数：这块算错的表现很隐蔽
/// （整列偏亮/偏暗、长句唱到末尾还剩三格不亮），有 <c>Create</c> 就能精确断言。
/// </para>
/// </summary>
public readonly record struct GlyphScanCursor
{
    /// <summary>参与点亮的格数（= 传入的字素数列表长度）。</summary>
    public required int TotalCount { get; init; }

    /// <summary>已**完全**点亮的格数。下标小于它的格都要按已唱处理。</summary>
    public required int LitCount { get; init; }

    /// <summary>
    /// 正在跨越的那一格内的进度 0..1。<see cref="LitCount"/> 指向这一格；
    /// <see cref="LitCount"/> 等于 <see cref="TotalCount"/> 时无意义恒为 0。
    /// </summary>
    public required double CrossingFraction { get; init; }

    /// <summary>整行已唱完（所有格都按已唱处理）。</summary>
    public bool IsComplete => TotalCount > 0 && LitCount >= TotalCount;

    /// <summary>
    /// 按字素数切分进度。
    /// </summary>
    /// <param name="progress">扫描进度 0..1。非有限值按 0 处理，越界会被夹取。</param>
    /// <param name="runLengths">
    /// 每一格的字素数（UTF-16 长度，与 <c>CalculateWordScanProgress</c> 同口径）。
    /// 为 null 或空表示这一行没有可点亮的格。
    /// </param>
    /// <remarks>
    /// 用 UTF-16 长度而不是「用户感知的字数」：进度那边用的就是
    /// <c>string.Length</c> 与 <c>IndexOf</c>，单位必须一致；emoji 在两边
    /// 都算 2，比例仍然对得上。
    /// </remarks>
    public static GlyphScanCursor Create(double progress, IReadOnlyList<int>? runLengths)
    {
        var count = runLengths?.Count ?? 0;
        if (count == 0)
        {
            return new GlyphScanCursor
            {
                TotalCount = 0,
                LitCount = 0,
                CrossingFraction = 0
            };
        }

        var total = 0;
        for (var index = 0; index < count; index++)
        {
            total += Math.Max(0, runLengths![index]);
        }

        if (total <= 0)
        {
            // 全是零长格（理论上切分器不会产出）：点亮它也没有可见意义，
            // 直接当成「一格都没亮」的初始态。
            return new GlyphScanCursor
            {
                TotalCount = count,
                LitCount = 0,
                CrossingFraction = 0
            };
        }

        if (!double.IsFinite(progress))
        {
            progress = 0;
        }

        progress = Math.Clamp(progress, 0, 1);

        // 已唱掉的字数。允许小数：这一格点亮几成，就靠它的小数部分换算。
        var scanned = progress * total;
        var consumed = 0d;

        for (var index = 0; index < count; index++)
        {
            var length = Math.Max(0, runLengths![index]);
            if (length == 0)
            {
                // 零长格不占时间，直接算作已点亮（index 继续前进，
                // 于是 LitCount 天然把它包含在内）。
                continue;
            }

            var next = consumed + length;
            if (scanned < next)
            {
                return new GlyphScanCursor
                {
                    TotalCount = count,
                    LitCount = index,
                    CrossingFraction = Math.Clamp((scanned - consumed) / length, 0, 1)
                };
            }

            consumed = next;
        }

        return new GlyphScanCursor
        {
            TotalCount = count,
            LitCount = count,
            CrossingFraction = 0
        };
    }
}
