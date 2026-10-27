// Copyright (c) 2026 anync
// SPDX-License-Identifier: MIT
//
// 本文件源自 TaskbarLyrics (https://github.com/ANYNC/TaskbarLyrics)
// 的 TaskbarLyrics.App/TaskbarFreeSpaceLayout.cs，原始许可为 MIT
// （见仓库根目录 LICENSE-TaskbarLyrics）。
// 本仓库整体按 GPL-3.0-or-later 分发。

namespace FluentFlyout.Lyrics.Layout;

/// <summary>
/// 任务栏主轴上的一个区间，单位是设备无关像素（DIP），原点是任务栏起点。
///
/// 「主轴」指任务栏的长边方向：横向任务栏是 X 轴，竖向任务栏是 Y 轴。
/// 用 <see cref="Start"/>/<see cref="End"/> 而不是 Left/Right，
/// 是为了让同一套算法能同时服务两种方向——竖向任务栏上
/// Start 对应 Top、End 对应 Bottom。
/// </summary>
public readonly record struct TaskbarInterval(double Start, double End)
{
    /// <summary>区间长度。可能为负（构造了非法区间时），调用方需自行校验。</summary>
    public double Length => End - Start;
}
