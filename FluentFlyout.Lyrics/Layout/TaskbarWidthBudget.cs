// Copyright (c) 2026 anync
// SPDX-License-Identifier: MIT
//
// 本文件源自 TaskbarLyrics (https://github.com/ANYNC/TaskbarLyrics)
// 的 TaskbarLyrics.App/TaskbarFreeSpaceLayout.cs 的调用约定，原始许可为 MIT
// （见仓库根目录 LICENSE-TaskbarLyrics）。
// 本仓库整体按 GPL-3.0-or-later 分发。

namespace FluentFlyout.Lyrics.Layout;

/// <summary>
/// 宽度预算的求解结果。
/// </summary>
/// <param name="Width">最终可用的视觉宽度（DIP，乘过 <c>_scale</c> 之前的语义见调用方）。</param>
/// <param name="Outcome">为什么是这个宽度。调用方据此决定要不要记日志。</param>
/// <param name="Placement">区间避让算出的落点；<see cref="Outcome"/> 非 <see cref="WidthBudgetOutcome.Placed"/> 时为默认值。</param>
public readonly record struct TaskbarWidthBudgetResult(
    double Width,
    WidthBudgetOutcome Outcome,
    TaskbarInterval Placement);

/// <summary>宽度预算的求解结果分类。</summary>
public enum WidthBudgetOutcome
{
    /// <summary>在实测空隙里找到了位置，宽度就是空隙能给的长度。</summary>
    Placed,

    /// <summary>
    /// 拿不到占用区间（UIA 查询失败 / 任务栏元素不存在）。
    /// 此时**不能**当作「全空」——那会让内容压到托盘图标上，
    /// 应退回一个有界常量。
    /// </summary>
    OccupancyUnknown,

    /// <summary>
    /// 所有空隙都比 <see cref="TaskbarFreeSpaceLayout.MinimumDisplayLength"/> 窄。
    /// 挤进去也只能显示几个字，且会贴住相邻元素，应退回原有常量行为。
    /// </summary>
    NoUsableGap,

    /// <summary>理想宽度本身不合法（0 / 负数 / NaN），或任务栏长度不合法。</summary>
    InvalidInput,
}

/// <summary>
/// 把「理想宽度 + 任务栏占用区间」解成「最终可用宽度」。
///
/// 这是 P3-8/P3-9 的可测试核心：<c>TaskbarWindow</c> 只负责取数
/// （UIA 坐标、DPI、设置项），所有算术与降级判定都在这里，
/// 于是「150% DPI」「托盘图标全开」「UIA 超时」这些场景
/// 都能用纯函数单测覆盖，不需要真机。
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么是两遍而不是迭代</b>：区间依赖宽度（要放多宽才知道能塞进哪个缝），
/// 宽度又依赖区间（能塞多宽才知道放多大）。这个环不需要迭代解，
/// 因为第二遍只会把宽度**改小**——一旦变小，原来找到的缝只会更宽松，
/// 不需要重新求位置。所以一遍「理想宽度 → 求缝」，一遍「缝长 → 压宽度」即可。
/// </para>
/// <para>
/// <b>为什么必须单向 clamp</b>：如果允许「缝比理想宽度大就用缝长」，
/// 短歌词（例如「啊」）会撑满整个空隙，看起来像一块空白面板。
/// 只缩不放是唯一正确的方向。
/// </para>
/// </remarks>
public static class TaskbarWidthBudget
{
    /// <summary>
    /// 求解宽度预算。
    /// </summary>
    /// <param name="desiredWidth">理想宽度，与返回值的单位一致（由调用方保证）。</param>
    /// <param name="fallbackWidth">
    /// 降级时使用的常量宽度。改动前 FluentFlyout 用 216，
    /// 歌词模式下用更宽的常量——由调用方决定，这里不猜。
    /// </param>
    /// <param name="taskbarLength">任务栏主轴总长度，与 <paramref name="desiredWidth"/> 同单位。</param>
    /// <param name="preferredStart">
    /// 期望起点。小于 0 表示「越靠前越好」，大于 <paramref name="taskbarLength"/>
    /// 表示「越靠后越好」，与 <see cref="TaskbarFreeSpaceLayout.TryPlace"/> 一致。
    /// </param>
    /// <param name="occupied">
    /// 已占用区间。<c>null</c> 表示「不知道」——与「知道且为空」严格区分：
    /// 前者降级到 <paramref name="fallbackWidth"/>，后者才允许占满整条任务栏。
    /// </param>
    public static TaskbarWidthBudgetResult Resolve(
        double desiredWidth,
        double fallbackWidth,
        double taskbarLength,
        double preferredStart,
        IReadOnlyList<TaskbarInterval>? occupied)
    {
        // 理想宽度不合法：直接用兜底值。这种情况通常是「首帧还没到」，
        // 报 0 会让上层把面板压成 0 宽。
        if (!double.IsFinite(desiredWidth) || desiredWidth <= 0)
        {
            return new TaskbarWidthBudgetResult(fallbackWidth, WidthBudgetOutcome.InvalidInput, default);
        }

        if (!double.IsFinite(taskbarLength) || taskbarLength <= 0)
        {
            return new TaskbarWidthBudgetResult(fallbackWidth, WidthBudgetOutcome.InvalidInput, default);
        }

        // 「不知道」不能当「空」。这是整个模块最重要的一条：
        // UIA 在任务栏刷新、Explorer 重启、第三方任务栏工具介入时
        // 都会返回失败，把它当成「全空」就会把歌词画到托盘图标上。
        if (occupied is null)
        {
            return new TaskbarWidthBudgetResult(
                Math.Min(desiredWidth, fallbackWidth),
                WidthBudgetOutcome.OccupancyUnknown,
                default);
        }

        if (!TaskbarFreeSpaceLayout.TryPlace(
                taskbarLength,
                desiredWidth,
                preferredStart,
                occupied,
                out var placement))
        {
            return new TaskbarWidthBudgetResult(fallbackWidth, WidthBudgetOutcome.NoUsableGap, default);
        }

        // 只缩不放。placement.Length 已经 ≤ desiredWidth
        // （TryPlace 内部取 min(requestedLength, gapLength)），
        // 这里再 clamp 一次是为了防御未来 TryPlace 的实现变更。
        return new TaskbarWidthBudgetResult(
            Math.Min(placement.Length, desiredWidth),
            WidthBudgetOutcome.Placed,
            placement);
    }
}
