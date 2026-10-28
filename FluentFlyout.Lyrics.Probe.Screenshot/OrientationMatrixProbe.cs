// Copyright (c) 2026 anync
// SPDX-License-Identifier: MIT
//
// 本文件为本仓库新增，不源自 TaskbarLyrics。

using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentFlyout.Controls;
using FluentFlyout.Lyrics.Layout;
using FluentFlyout.Lyrics.Models;

namespace FluentFlyout.Lyrics.Probe.Screenshot;

/// <summary>
/// 四种任务栏方向 × 三档 DPI × 深浅主题的**位置矩阵**：24 张图。
///
/// <para>
/// 这是「手工矩阵」里唯一能自动化的那一半。另外一半（把任务栏真的摆到左/右/上）
/// 在 Win11 上做不到——代码里判方向靠 <c>taskbarHeight &gt; taskbarWidth</c>，
/// 而系统只允许任务栏贴底边（文档 7.2.6）。所以这里换了个做法：**不摆任务栏，
/// 而是把任务栏的矩形交给生产函数**，让它算出小组件该落在哪。
/// 位置与旋转全部来自 <see cref="WidgetPlacement.Resolve"/> 与
/// <c>TaskbarWidgetControl.SetVerticalMode</c>，探针只提供「任务栏有多大」。
/// </para>
///
/// <para>
/// 为什么值得单独一支：位置类缺陷在像素上看不出来。小组件画得一模一样，
/// 只是**整块挪到了任务栏外面**或**次轴没居中**，截图矩阵里仍然是「一片清楚的歌词」。
/// 所以这一支的判据全部是几何量（是否越界、是否居中、朝向是否对），
/// 外加一条「顶底同构」的像素相等断言。
/// </para>
/// </summary>
internal static class OrientationMatrixProbe
{
    /// <summary>任务栏主轴长度（DIP）。取 1080p 横屏宽度，与真机常见配置一致。</summary>
    private const double TaskbarLengthDip = 1920;

    /// <summary>任务栏次轴长度（DIP）。48 是 Win11 默认任务栏高度。</summary>
    private const double TaskbarCrossDip = 48;

    /// <summary>
    /// 主轴起点（物理像素）。与生产的 <c>case 0</c> 一致取 20 附近。
    ///
    /// 刻意不用 0：贴边时「越界 1px」和「正好对齐」在数值上难以区分，
    /// 而留出一点起点后，越界必然是负数或超出任务栏长度，判据才敏感。
    /// </summary>
    private const int PrimaryPositionPx = 24;

    private enum Edge
    {
        Bottom,
        Top,
        Left,
        Right,
    }

    /// <summary>跑一遍位置矩阵，返回进程退出码（0 = 全过）。</summary>
    public static int Run()
    {
        // TaskbarWidgetControl.xaml 里有 Style="{StaticResource UiContextMenu}"，
        // 这个键来自 WPF-UI 的 ControlsDictionary，而探针没有 App.xaml。
        if (Application.Current is null)
        {
            var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            application.Resources["UiContextMenu"] = new Style(typeof(ContextMenu));
        }

        var outputDirectory = Path.Combine(AppContext.BaseDirectory, "orientations");
        if (Directory.Exists(outputDirectory))
        {
            Directory.Delete(outputDirectory, recursive: true);
        }

        Directory.CreateDirectory(outputDirectory);

        Console.WriteLine("=== 四方向任务栏 × 3 DPI × 深浅主题：位置矩阵 ===");
        Console.WriteLine();

        // 长句：横向会长到接近上限、竖向会一眼看出「哪一条是主轴」。
        var frame = new LyricDisplayFrame(
            CurrentLine: "如果这场雨一直下到明天清晨我们是不是就能一直站在这里",
            NextLine: "而你说好要陪我走完这条街",
            Title: "测试曲目",
            LineProgress: 0.3);

        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var isDark in new[] { false, true })
        {
            var themeLabel = isDark ? "dark" : "light";

            foreach (var dpiScale in new[] { 1.0, 1.5, 2.0 })
            {
                var dpiLabel = $"{dpiScale * 100:F0}";

                foreach (var edge in new[] { Edge.Bottom, Edge.Top, Edge.Left, Edge.Right })
                {
                    var name = $"{edge.ToString().ToLowerInvariant()}-{themeLabel}-{dpiLabel}";
                    var shot = Render(isDark, edge, dpiScale, frame);
                    VerticalWidgetProbe.SavePng(shot.Bitmap, Path.Combine(outputDirectory, name + ".png"));
                    hashes[name] = PixelHash(shot.Bitmap);

                    CheckInsideTaskbar(name, shot);
                    CheckCenteredOnCrossAxis(name, shot);
                    CheckOrientation(name, edge, shot);
                    CheckColumnSide(name, edge, shot);
                }
            }
        }

        Console.WriteLine();
        Console.WriteLine("--- 顶部与底部同构（同一套坐标，像素必须一致）---");
        CheckTopMatchesBottom(hashes);

        Console.WriteLine();
        Console.WriteLine("--- 左侧与右侧必须镜像（镜像没接上时两张会一模一样）---");
        CheckLeftMirrorsRight(hashes);

        Console.WriteLine();
        Console.WriteLine(VerticalWidgetProbe.Failures == 0
            ? "位置矩阵全部通过"
            : $"{VerticalWidgetProbe.Failures} 项失败");

        return VerticalWidgetProbe.Failures == 0 ? 0 : 1;
    }

    /// <summary>小组件必须整块落在任务栏的主轴范围内。</summary>
    private static void CheckInsideTaskbar(string name, Shot shot)
    {
        var isVertical = shot.Edge is Edge.Left or Edge.Right;

        var start = (isVertical ? shot.Placement.CanvasTop : shot.Placement.CanvasLeft) * shot.DpiScale;
        var extent = isVertical ? shot.Placement.RectHeight : shot.Placement.RectWidth;
        var length = isVertical ? shot.TaskbarHeightPx : shot.TaskbarWidthPx;

        VerticalWidgetProbe.Check(
            $"{name}：整块在任务栏内",
            start >= -0.5 && start + extent <= length + 0.5,
            $"起点={start:F1}px，长度={extent:F1}px，任务栏={length}px");
    }

    /// <summary>次轴上必须居中：40 DIP 的小组件在 48 DIP 的任务栏里两边各留 4 DIP。</summary>
    private static void CheckCenteredOnCrossAxis(string name, Shot shot)
    {
        var isVertical = shot.Edge is Edge.Left or Edge.Right;

        var start = (isVertical ? shot.Placement.CanvasLeft : shot.Placement.CanvasTop) * shot.DpiScale;
        var extent = isVertical ? shot.Placement.RectWidth : shot.Placement.RectHeight;
        var cross = isVertical ? shot.TaskbarWidthPx : shot.TaskbarHeightPx;

        var expected = (cross - extent) / 2;
        VerticalWidgetProbe.Check(
            $"{name}：次轴居中",
            Math.Abs(start - expected) <= 0.5,
            $"起点={start:F1}px，期望={expected:F1}px，次轴={cross}px");
    }

    /// <summary>朝向：竖向的墨迹必须「高而窄」，横向必须「宽而扁」。</summary>
    private static void CheckOrientation(string name, Edge edge, Shot shot)
    {
        var isVertical = edge is Edge.Left or Edge.Right;

        VerticalWidgetProbe.Check(
            $"{name}：有墨迹",
            shot.Ink.Pixels > 0,
            $"墨迹={shot.Ink.Pixels}px，包围盒={shot.Ink.Bounds.Width:F0}×{shot.Ink.Bounds.Height:F0}");

        VerticalWidgetProbe.Check(
            $"{name}：朝向正确（{(isVertical ? "高>宽" : "宽>高")}）",
            shot.Ink.Pixels > 0 &&
            (isVertical
                ? shot.Ink.Bounds.Height > shot.Ink.Bounds.Width
                : shot.Ink.Bounds.Width > shot.Ink.Bounds.Height),
            $"包围盒={shot.Ink.Bounds.Width:F0}×{shot.Ink.Bounds.Height:F0}，" +
            $"图={shot.Bitmap.PixelWidth}×{shot.Bitmap.PixelHeight}");
    }

    /// <summary>
    /// 主列在哪半边：贴左侧时主列要贴屏幕边缘（左半），贴右侧时在右半。
    ///
    /// 判据与装配链探针的 <c>CheckMirrorSide</c> 同口径——左右墨迹量按**小组件自身**
    /// 中线劈开比较，不按整张图画布。镜像没接上时两侧墨迹量会掉个个儿，
    /// 而这个差异在缩略图上几乎看不出来（两列都是竖排汉字）。
    /// </summary>
    private static void CheckColumnSide(string name, Edge edge, Shot shot)
    {
        if (edge is not (Edge.Left or Edge.Right))
        {
            return;
        }

        var mainOnLeftHalf = shot.Ink.LeftPixels > shot.Ink.RightPixels;
        var expectedMainOnLeftHalf = edge == Edge.Left;

        VerticalWidgetProbe.Check(
            $"{name}：主列贴在{(expectedMainOnLeftHalf ? "左" : "右")}半（镜像方向）",
            mainOnLeftHalf == expectedMainOnLeftHalf,
            $"左半={shot.Ink.LeftPixels}px，右半={shot.Ink.RightPixels}px");
    }

    /// <summary>
    /// 顶部与底部：同一尺寸、同一帧 → 像素必须**逐字节相同**。
    ///
    /// 这条不是凑数：代码里没有「顶部」分支，顶部能用正是因为坐标只相对于
    /// 任务栏自己的左上角。哪天有人给顶部加偏移（很自然的一步），这里会先醒，
    /// 而不是等真机反馈「顶部时位置偏了」。
    /// </summary>
    private static void CheckTopMatchesBottom(Dictionary<string, string> hashes)
    {
        foreach (var isDark in new[] { false, true })
        {
            foreach (var dpiLabel in new[] { "100", "150", "200" })
            {
                var theme = isDark ? "dark" : "light";
                var bottom = hashes[$"bottom-{theme}-{dpiLabel}"];
                var top = hashes[$"top-{theme}-{dpiLabel}"];

                VerticalWidgetProbe.Check(
                    $"top-{theme}-{dpiLabel}：与底部逐像素相同",
                    string.Equals(bottom, top, StringComparison.Ordinal),
                    $"bottom={bottom[..8]} top={top[..8]}");
            }
        }
    }

    /// <summary>
    /// 左侧与右侧：镜像接上时两张图必须**不同**。
    ///
    /// 只判「不同」，不判「镜像关系」：两列的宽度本来就不一样，包围盒不满足
    /// 严格镜像。真正的镜像判据在装配链探针里（<c>CheckMirrorSide</c> 量的是
    /// 主列在哪半边）。
    /// </summary>
    private static void CheckLeftMirrorsRight(Dictionary<string, string> hashes)
    {
        foreach (var isDark in new[] { false, true })
        {
            foreach (var dpiLabel in new[] { "100", "150", "200" })
            {
                var theme = isDark ? "dark" : "light";
                var left = hashes[$"left-{theme}-{dpiLabel}"];
                var right = hashes[$"right-{theme}-{dpiLabel}"];

                VerticalWidgetProbe.Check(
                    $"left/right-{theme}-{dpiLabel}：镜像生效（两张不同）",
                    !string.Equals(left, right, StringComparison.Ordinal),
                    $"left={left[..8]} right={right[..8]}");
            }
        }
    }

    /// <summary>渲染一张：任务栏由 <see cref="WidgetPlacement.Resolve"/> 算出的位置摆放。</summary>
    private static Shot Render(bool isDark, Edge edge, double dpiScale, LyricDisplayFrame frame)
    {
        var isVertical = edge is Edge.Left or Edge.Right;

        // 任务栏矩形：竖向是「窄而高」，横向是「宽而扁」。
        var taskbarLengthPx = (int)Math.Round(TaskbarLengthDip * dpiScale);
        var taskbarCrossPx = (int)Math.Round(TaskbarCrossDip * dpiScale);
        var taskbarWidthPx = isVertical ? taskbarCrossPx : taskbarLengthPx;
        var taskbarHeightPx = isVertical ? taskbarLengthPx : taskbarCrossPx;

        var control = new TaskbarWidgetControl();
        control.SetLyricsMode(true);

        // 与生产同一顺序：先喂帧，再定方向，最后算尺寸。
        var panel = (LyricsPanel)control.FindName("LyricsPanel")!;
        panel.ApplyWindowsTheme(isDark);
        panel.ApplyFrame(frame);
        panel.SetActive(true);

        control.SetVerticalMode(isVertical);

        // 贴左侧时主列要贴屏幕边缘（真机反馈的「左侧方向不对」）。
        control.SetVerticalMirror(isVertical && edge == Edge.Left);

        // 生产的两遍：先问想要多长（MeasureDesiredWidth），再按任务栏长度回压
        // （ResolveMaxLengthDip 的上限）。探针没有避让区间，第二遍只能拿
        // 「任务栏有多长」当上限——这正是 ComputeWidgetBudget 里 desiredDip 的算法。
        var maxLengthDip = isVertical
            ? LyricsVerticalLayout.ResolveMaxMainAxisLength(taskbarLengthPx / dpiScale)
            : TaskbarWidgetControl.MaxLyricsWidgetLength;

        var desired = control.MeasureDesiredWidth(maxLengthDip);
        if (!double.IsFinite(desired) || desired <= 0)
        {
            desired = 200;
        }

        var (logicalWidth, logicalHeight) =
            control.CalculateSize(dpiScale, availableWidth: Math.Min(desired, maxLengthDip));

        var background = new SolidColorBrush(
            isDark ? Color.FromRgb(0x20, 0x20, 0x20) : Color.FromRgb(0xF3, 0xF3, 0xF3));

        var canvasWidth = taskbarWidthPx / dpiScale;
        var canvasHeight = taskbarHeightPx / dpiScale;
        var canvas = new Canvas { Width = canvasWidth, Height = canvasHeight, Background = background };

        // 生产里旋转挂在控件上（LayoutTransform），画布是任务栏本身。
        control.LayoutTransform = isVertical ? new RotateTransform(90) : null;
        control.RenderTransform = Transform.Identity;

        canvas.Children.Add(control);

        var (placement, physicalWidth, physicalHeight) = Place(
            taskbarWidthPx, taskbarHeightPx, isVertical,
            logicalWidth, logicalHeight, dpiScale);

        ApplyPlacement(control, placement, physicalWidth, physicalHeight, dpiScale);
        VerticalWidgetProbe.Arrange(canvas, canvasWidth, canvasHeight);

        // 第二拍：面板拿到槽位之后主轴长度才收敛，与生产 1.5s 定时器同构。
        panel.ApplyFrame(frame);
        var reread = control.MeasureDesiredWidth(maxLengthDip);
        if (double.IsFinite(reread) && reread > 0)
        {
            (logicalWidth, logicalHeight) =
                control.CalculateSize(dpiScale, availableWidth: Math.Min(reread, maxLengthDip));

            (placement, physicalWidth, physicalHeight) = Place(
                taskbarWidthPx, taskbarHeightPx, isVertical,
                logicalWidth, logicalHeight, dpiScale);

            ApplyPlacement(control, placement, physicalWidth, physicalHeight, dpiScale);
            VerticalWidgetProbe.Arrange(canvas, canvasWidth, canvasHeight);
        }

        var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(canvasWidth * dpiScale),
            (int)Math.Ceiling(canvasHeight * dpiScale),
            dpiScale * 96,
            dpiScale * 96,
            PixelFormats.Pbgra32);
        bitmap.Render(canvas);

        // 墨迹统计限制在小组件矩形内：整机里还有封面与播放按钮。
        var widgetRect = new Rect(
            placement.CanvasLeft,
            placement.CanvasTop,
            placement.RectWidth / dpiScale,
            placement.RectHeight / dpiScale);

        var ink = VerticalWidgetProbe.MeasureInk(bitmap, background, widgetRect, dpiScale);

        return new Shot(
            bitmap,
            edge,
            dpiScale,
            placement,
            ink,
            taskbarWidthPx,
            taskbarHeightPx);
    }

    /// <summary>位置与尺寸全部交给生产函数算，探针不自己写公式。</summary>
    private static (WidgetPlacement Placement, int PhysicalWidth, int PhysicalHeight) Place(
        int taskbarWidthPx,
        int taskbarHeightPx,
        bool isVertical,
        double logicalWidth,
        double logicalHeight,
        double dpiScale)
    {
        var physicalWidth = (int)Math.Round(logicalWidth * dpiScale);
        var physicalHeight = (int)Math.Round(logicalHeight * dpiScale);

        var placement = WidgetPlacement.Resolve(
            taskbarWidthPx,
            taskbarHeightPx,
            isVertical,
            physicalWidth,
            physicalHeight,
            PrimaryPositionPx,
            dpiScale);

        return (placement, physicalWidth, physicalHeight);
    }

    private static void ApplyPlacement(
        TaskbarWidgetControl control,
        WidgetPlacement placement,
        int physicalWidth,
        int physicalHeight,
        double dpiScale)
    {
        Canvas.SetLeft(control, placement.CanvasLeft);
        Canvas.SetTop(control, placement.CanvasTop);
        control.Width = physicalWidth / dpiScale;
        control.Height = physicalHeight / dpiScale;
    }

    /// <summary>像素指纹：用来判「两张图是否逐像素相同」。</summary>
    private static string PixelHash(BitmapSource bitmap)
    {
        var stride = bitmap.PixelWidth * 4;
        var buffer = new byte[stride * bitmap.PixelHeight];
        bitmap.CopyPixels(buffer, stride, 0);

        var hash = System.Security.Cryptography.SHA256.HashData(buffer);
        return Convert.ToHexString(hash);
    }

    /// <summary>一张图的渲染结果。</summary>
    private sealed record Shot(
        BitmapSource Bitmap,
        Edge Edge,
        double DpiScale,
        WidgetPlacement Placement,
        VerticalWidgetProbe.InkStats Ink,
        int TaskbarWidthPx,
        int TaskbarHeightPx);
}
