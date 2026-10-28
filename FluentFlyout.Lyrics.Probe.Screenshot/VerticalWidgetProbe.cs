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
/// 竖向任务栏的**整机装配链**探针：渲染真实的 <see cref="TaskbarWidgetControl"/>，
/// 面板尺寸由生产方法 <c>CalculateSize</c> 写入，而不是由夹具代写。
/// </summary>
/// <remarks>
/// <para>
/// 与主截图矩阵的区别只有一条，而它正是历史上漏掉缺陷的那一条：主矩阵直接给
/// <c>LyricsPanel</c> 写 <c>Width</c>/<c>Height</c>，绕过了生产里唯一真正决定
/// 这两个数的地方。本探针走
/// <c>SetLyricsMode → SetVerticalMode → MeasureDesiredWidth → CalculateSize</c>，
/// 面板拿到多少长度只能来自宿主。**夹具通过 ≠ 生产路径正确。**
/// </para>
/// <para>
/// 为什么这不是「真机截图」而是它的替代品：Win11 的任务栏只能贴在屏幕底边
/// （本机 build 26300 实测 <c>StuckRects3</c> 的 edge 字节恒为 3），没有竖排任务栏
/// 就摆不出竖向的小组件。能自动化的最近似物就是这条装配链——除了任务栏本身
/// （<c>TaskbarWindow.PositionWidget</c> 的定位与吸附），其余每一层都是生产代码。
/// </para>
/// <para>
/// 判据全部是像素统计 + 一个几何量，不依赖人眼：
/// </para>
/// <list type="bullet">
/// <item><b>短边写进了面板</b>——<c>panel.Height</c> 落在 (0, logicalHeight] 里。</item>
/// <item><b>主轴长度由内容驱动</b>——<c>panel.Width ≥ min(内容长度, logicalWidth)</c>。
/// 旧缺陷正是这里：内容要 224 DIP，面板只有 37.5，尾部整列被裁。</item>
/// <item><b>面板内有墨迹</b>——文字没被 <c>ClipToBounds</c> 剪光。</item>
/// <item><b>墨迹纵向延伸 &gt; 横向</b>——竖排生效，不是被压成一行。</item>
/// </list>
/// </remarks>
internal static class VerticalWidgetProbe
{
    /// <summary>主轴长度上限（DIP）。竖向时它就是 Widget 在屏幕上的纵向长度。</summary>
    private const double MaxMainAxisLength = 400;

    private static int _failures;

    /// <summary>跑一遍整机装配链，返回失败数（0 = 全过）。</summary>
    public static int Run()
    {
        // TaskbarWidgetControl.xaml 里有 Style="{StaticResource UiContextMenu}"，
        // 这个键来自 WPF-UI 的 ControlsDictionary，而探针没有 App.xaml。
        // 补一个最小 ContextMenu 样式即可——这里只关心尺寸与墨迹，不关心右键菜单。
        if (Application.Current is null)
        {
            var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            application.Resources["UiContextMenu"] = new Style(typeof(ContextMenu));
        }

        var outputDirectory = Path.Combine(AppContext.BaseDirectory, "assembly");
        if (Directory.Exists(outputDirectory))
        {
            Directory.Delete(outputDirectory, recursive: true);
        }

        Directory.CreateDirectory(outputDirectory);

        Console.WriteLine("=== 竖向任务栏整机装配链（尺寸来自 CalculateSize）===");
        Console.WriteLine();

        // 长句是最不利的输入：内容长度远超预算，任何「宽度没写进去」都会立刻
        // 在像素上表现为「一个字都没有」或「只有头几个字」。
        var frame = new LyricDisplayFrame(
            CurrentLine: "如果这场雨一直下到明天清晨我们是不是就能一直站在这里不分开",
            NextLine: "而你说好要陪我走完这条街",
            Title: "测试曲目",
            LineProgress: 0.3);

        foreach (var isDark in new[] { false, true })
        {
            var themeLabel = isDark ? "dark" : "light";

            foreach (var crossSize in new[]
                     {
                         LyricsMetricsPolicy.DefaultTaskbarCrossSize,
                         LyricsMetricsPolicy.SmallTaskbarCrossSize,
                     })
            {
                var sizeLabel = crossSize >= LyricsMetricsPolicy.DefaultTaskbarCrossSize ? "40" : "28";

                foreach (var dpiScale in new[] { 1.0, 2.0 })
                {
                    var name = $"{themeLabel}-v{sizeLabel}-{dpiScale * 100:F0}%";

                    var shot = Render(isDark, crossSize, dpiScale, frame);
                    SavePng(shot.Bitmap, Path.Combine(outputDirectory, name + ".png"));

                    Check(
                        $"{name}：短边写进了面板",
                        shot.PanelHeight > 0 && shot.PanelHeight <= shot.LogicalHeight + 0.01,
                        $"面板 Height={shot.PanelHeight:F1}，CalculateSize 返回的短边={shot.LogicalHeight:F1}");

                    // 内容长度 > 预算时被 clamp 是**预期行为**（竖排没有滚动），
                    // 所以下界取两者中较小的那个：真正要防的是「塌成 0」。
                    var expectedMainAxis = Math.Min(shot.ContentLength, shot.LogicalWidth);

                    Check(
                        $"{name}：主轴长度由内容驱动",
                        shot.PanelWidth >= expectedMainAxis - 1,
                        $"面板 Width={shot.PanelWidth:F1}，内容长度={shot.ContentLength:F1}，" +
                        $"logicalWidth={shot.LogicalWidth:F1}");

                    Check(
                        $"{name}：面板内有墨迹（文字没被裁空）",
                        shot.InkPixels > 0,
                        $"墨迹={shot.InkPixels}px，面板矩形={Describe(shot.PanelRect)}");

                    Check(
                        $"{name}：墨迹纵向延伸 > 横向（竖排生效）",
                        shot.InkPixels > 0 && shot.InkBounds.Height > shot.InkBounds.Width,
                        $"包围盒 {shot.InkBounds.Width:F0}×{shot.InkBounds.Height:F0}，" +
                        $"图 {shot.Bitmap.PixelWidth}×{shot.Bitmap.PixelHeight}");
                }
            }
        }

        Console.WriteLine();
        Console.WriteLine(_failures == 0 ? "整机装配链全部通过" : $"{_failures} 项失败");
        return _failures;
    }

    private static string Describe(Rect rect) => rect.IsEmpty
        ? "空"
        : $"({rect.Left:F0},{rect.Top:F0}) {rect.Width:F0}×{rect.Height:F0}";

    /// <summary>渲染一张：真控件 + 生产取尺寸 + 生产那层 +90 旋转。</summary>
    private static Shot Render(bool isDark, double crossSize, double dpiScale, LyricDisplayFrame frame)
    {
        var control = new TaskbarWidgetControl();

        if (crossSize < LyricsMetricsPolicy.DefaultTaskbarCrossSize)
        {
            control.SetSmallTaskbarMode(true);
        }

        control.SetLyricsMode(true);

        // 帧必须**先**喂，再谈尺寸与方向。
        //
        // 生产里的次序就是如此：帧泵不断送帧，1.5s 定时器/换歌时才调
        // PositionWidget → SetVerticalMode → CalculateSize。反过来（先算尺寸
        // 再喂帧）会让内容长度按「空文本」算出来，长句只排出一小段。
        var panel = (LyricsPanel)control.FindName("LyricsPanel")!;
        panel.ApplyWindowsTheme(isDark);
        panel.ApplyFrame(frame);
        panel.SetActive(true);

        control.SetVerticalMode(true);

        // 生产是「两遍布局」：先问想要多长（MeasureDesiredWidth），
        // 再拿任务栏实测区间回压（CalculateSize 的 availableWidth）。
        // 探针没有任务栏，就用前者的值当预算——语义与生产一致。
        //
        // 单位陷阱：MeasureDesiredWidth 返回的已经是**视觉 DIP**（内部乘过 _scale），
        // 而 CalculateSize 的 availableWidth 收的正是视觉 DIP（它自己再除 _scale）。
        // 早先的探针在这里又乘了一次 0.9，预算凭空少 11%，长句被误判成「被裁」。
        var desired = control.MeasureDesiredWidth(MaxMainAxisLength);
        if (!double.IsFinite(desired) || desired <= 0)
        {
            desired = 200;
        }

        var (logicalWidth, logicalHeight) = control.CalculateSize(dpiScale: 1.0, availableWidth: desired);

        // 复刻 TaskbarWindow.PositionWidget：尺寸写在控件自身（Width=主轴、
        // Height=短边），旋转也挂在控件上，外面只包一层画布当任务栏。
        // 不要用 Border 间接定尺寸——那会让控件变成 Auto 尺寸、被父级压扁，
        // 面板拿到的槽位就不是主轴长度了（早先的探针第一版正是如此）。
        var background = new SolidColorBrush(
            isDark ? Color.FromRgb(0x20, 0x20, 0x20) : Color.FromRgb(0xF3, 0xF3, 0xF3));

        control.Width = logicalWidth;
        control.Height = logicalHeight;
        control.LayoutTransform = new RotateTransform(90);
        control.RenderTransform = Transform.Identity;

        var canvas = new Canvas { Width = logicalHeight, Height = logicalWidth, Background = background };
        canvas.Children.Add(control);
        Canvas.SetLeft(control, 0);
        Canvas.SetTop(control, 0);

        Arrange(canvas, logicalHeight, logicalWidth);

        // 第二拍：生产里帧泵每 1.5s（或换帧）都会重排一次，主轴长度正是靠这
        // 第二拍才收敛的——首拍时面板还没有实际槽位，内容长度可能还没算出来。
        panel.ApplyFrame(frame);
        Arrange(canvas, logicalHeight, logicalWidth);

        // 再走一遍生产的那两步：短边 → 读长度 → 写长度。真机上这一步发生在
        // 定时器里，且必须早于渲染，否则截到的是「上一轮的列数 + 这一轮的长度」。
        var reread = control.MeasureDesiredWidth(MaxMainAxisLength);
        if (double.IsFinite(reread) && reread > 0)
        {
            (logicalWidth, logicalHeight) = control.CalculateSize(dpiScale: 1.0, availableWidth: reread);
            control.Width = logicalWidth;
            control.Height = logicalHeight;
            canvas.Width = logicalHeight;
            canvas.Height = logicalWidth;
            Arrange(canvas, logicalHeight, logicalWidth);
        }

        var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(logicalHeight * dpiScale),
            (int)Math.Ceiling(logicalWidth * dpiScale),
            dpiScale * 96,
            dpiScale * 96,
            PixelFormats.Pbgra32);
        bitmap.Render(canvas);

        // 面板在画布上的实际矩形（已含旋转），用于把墨迹统计限制在歌词区域内：
        // 整机里还有封面与播放按钮，把它们算进墨迹会让「有没有文字」失效。
        var panelRect = Rect.Empty;
        if (panel.ActualWidth > 0 && panel.ActualHeight > 0)
        {
            try
            {
                panelRect = panel.TransformToAncestor(canvas)
                    .TransformBounds(new Rect(0, 0, panel.ActualWidth, panel.ActualHeight));
            }
            catch (InvalidOperationException)
            {
                // 不在同一棵可视树上（理论上不会发生）——退化成整图统计。
                panelRect = new Rect(0, 0, logicalHeight, logicalWidth);
            }
        }

        var (ink, bounds) = MeasureInk(bitmap, background, panelRect, dpiScale);

        return new Shot(
            bitmap,
            ink,
            bounds,
            panelRect,
            panel.Width,
            panel.Height,
            panel.VerticalContentLength,
            logicalWidth,
            logicalHeight);
    }

    private static void Arrange(Canvas canvas, double width, double height)
    {
        for (var pass = 0; pass < 2; pass++)
        {
            canvas.InvalidateMeasure();
            canvas.Measure(new Size(width, height));
            canvas.Arrange(new Rect(0, 0, width, height));
            canvas.UpdateLayout();
        }
    }

    /// <summary>统计指定区域内的「非背景」像素及其包围盒。</summary>
    /// <remarks>
    /// 阈值取 24：主题色与背景差得更远，抗锯齿边缘只有个位数差异；取小会把
    /// 整张图算成墨迹，取大会漏掉细笔画（13px 汉字的横只有 1px）。
    ///
    /// 与主矩阵一致，先反预乘再比亮度：<c>RenderTargetBitmap</c> 的目标格式是
    /// <c>Pbgra32</c>，半透明边界像素的 RGB 已被 alpha 压过，直接比会误判。
    /// </remarks>
    private static (int Pixels, Rect Bounds) MeasureInk(
        BitmapSource bitmap, SolidColorBrush background, Rect panelRect, double dpiScale)
    {
        var width = bitmap.PixelWidth;
        var height = bitmap.PixelHeight;
        var stride = width * 4;
        var pixels = new byte[stride * height];
        bitmap.CopyPixels(pixels, stride, 0);

        var bg = background.Color;

        var left = panelRect.IsEmpty ? 0 : (int)Math.Floor(panelRect.Left * dpiScale);
        var right = panelRect.IsEmpty ? width - 1 : (int)Math.Ceiling(panelRect.Right * dpiScale) - 1;
        var top = panelRect.IsEmpty ? 0 : (int)Math.Floor(panelRect.Top * dpiScale);
        var bottom = panelRect.IsEmpty ? height - 1 : (int)Math.Ceiling(panelRect.Bottom * dpiScale) - 1;
        left = Math.Max(left, 0);
        top = Math.Max(top, 0);
        right = Math.Min(right, width - 1);
        bottom = Math.Min(bottom, height - 1);

        var count = 0;
        var minX = int.MaxValue;
        var minY = int.MaxValue;
        var maxX = int.MinValue;
        var maxY = int.MinValue;

        for (var y = top; y <= bottom; y++)
        {
            for (var x = left; x <= right; x++)
            {
                var offset = (y * stride) + (x * 4);
                var alpha = pixels[offset + 3];

                if (alpha == 0)
                {
                    continue;
                }

                var scale = 255.0 / alpha;
                var b = pixels[offset] * scale;
                var g = pixels[offset + 1] * scale;
                var r = pixels[offset + 2] * scale;

                var delta = Math.Abs(b - bg.B) + Math.Abs(g - bg.G) + Math.Abs(r - bg.R);
                if (delta <= 24)
                {
                    continue;
                }

                count++;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }
        }

        var bounds = count == 0
            ? Rect.Empty
            : new Rect(minX, minY, maxX - minX + 1, maxY - minY + 1);

        return (count, bounds);
    }

    private static void SavePng(BitmapSource bitmap, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static void Check(string name, bool condition, string detail)
    {
        if (condition)
        {
            Console.WriteLine($"  [PASS] {name}（{detail}）");
            return;
        }

        _failures++;
        Console.WriteLine($"  [FAIL] {name}（{detail}）");
    }

    /// <summary>一张图的渲染结果。</summary>
    private sealed record Shot(
        BitmapSource Bitmap,
        int InkPixels,
        Rect InkBounds,
        Rect PanelRect,
        double PanelWidth,
        double PanelHeight,
        double ContentLength,
        double LogicalWidth,
        double LogicalHeight);
}
