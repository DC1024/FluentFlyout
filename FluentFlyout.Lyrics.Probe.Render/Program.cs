// Copyright (c) 2026 anync
// SPDX-License-Identifier: MIT
//
// 竖向任务栏几何的离屏渲染探针。
//
// 目的：**实证**而不是推理。第一版探针证明了落点文档 4 节的结论是错的
// （panel 再加 LayoutTransform(-90) 后内容完全消失），这一版把几种候选
// 方案并排渲染，用像素判据挑出真正可行的那个。
//
// 判据（全部是几何量，不受抗锯齿影响）：
//   1. 墨迹存在（内容没被裁掉）
//   2. 字形正立：墨迹包围盒宽 > 高（一行横向文字的特征）
//   3. 墨迹完全落在画布内且不贴边
//
// 输出 PNG 到 out/，可直接目视复核。

using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FluentFlyout.Lyrics.Probe.Render;

internal static class Program
{
    private const int CanvasWidth = 60;
    private const int CanvasHeight = 320;

    private static int _failures;

    [STAThread]
    private static int Main()
    {
        var outputDirectory = Path.Combine(AppContext.BaseDirectory, "out");
        if (Directory.Exists(outputDirectory))
        {
            Directory.Delete(outputDirectory, recursive: true);
        }

        Directory.CreateDirectory(outputDirectory);

        Console.WriteLine("=== 竖向任务栏几何探针 v2 ===");
        Console.WriteLine();

        var cases = new (string Name, RotationPlan Plan)[]
        {
            ("A-baseline-horizontal", RotationPlan.Horizontal),
            ("B-widget90-only", RotationPlan.WidgetOnly),
            ("C-widget90-panelLayout-90", RotationPlan.PanelLayoutCounterRotate),
            ("D-widget90-panelRender-90", RotationPlan.PanelRenderCounterRotate),
            ("E-widget90-panelLayout-90-auto", RotationPlan.PanelLayoutCounterRotateAutoSize),
            ("F-widgetNeg90-panelNone", RotationPlan.WidgetNeg90),
            ("G-no-rotation-tall-box", RotationPlan.NoRotationTallBox),
            ("H-no-rotation-wrapped", RotationPlan.NoRotationWrapped),
        };

        var results = new Dictionary<string, RenderResult>();
        foreach (var (name, plan) in cases)
        {
            var result = Render(plan);
            results[name] = result;
            SavePng(result.Bitmap, name);
            Console.WriteLine(
                $"{name,-32} 墨迹={result.InkPixels,6}px  包围盒={result.InkBounds}  " +
                $"宽高比={result.AspectRatio,6:F2}  裁剪={!result.FullyInside}  " +
                $"panel={result.PanelRenderSize} @ {result.PanelOrigin}");
        }

        Console.WriteLine();
        Console.WriteLine("--- 断言 ---");

        var baseline = results["A-baseline-horizontal"];

        Check(
            "基准横向时字形是横排（宽>高）",
            baseline.AspectRatio > 1.2,
            $"宽高比={baseline.AspectRatio:F2}");

        var widgetOnly = results["B-widget90-only"];
        Check(
            "只转 Widget 时字形躺倒（宽<高）——这就是要修的问题",
            widgetOnly.AspectRatio < 0.9 && widgetOnly.InkPixels > 0,
            $"宽高比={widgetOnly.AspectRatio:F2}, 墨迹={widgetOnly.InkPixels}px");

        // 逐个候选方案判定「可用」：有墨迹 + 不贴边裁剪 + 单行宽高比。
        //
        // 只对「单行显示」的候选套用宽高比判据。C / D / F 是**故意保留的
        // 反例**（它们本来就该失败，用于证明原推导不成立），H 是多行方案
        // （包围盒天然接近正方形，判据在下面单独给）。
        foreach (var name in new[]
                 {
                     "E-widget90-panelLayout-90-auto",
                     "G-no-rotation-tall-box",
                 })
        {
            var result = results[name];
            var usable = result.InkPixels > 0 && result.FullyInside;

            // 正立 = 包围盒宽高比接近基准（同一段文本，正立时比例应该相似）
            var upright = result.AspectRatio > 1.0;

            Check(
                $"{name} 可用（有墨迹 + 未裁剪 + 正立）",
                usable && upright,
                $"墨迹={result.InkPixels}px, 未裁剪={result.FullyInside}, 宽高比={result.AspectRatio:F2}");
        }

        // 单独评估 G：它不追求「一行显示完整句子」，而是靠主轴长度
        // 换行显示——判据不同，所以单独给结论。
        var tall = results["G-no-rotation-tall-box"];
        Check(
            "G 竖条方案：文字横排可读（宽高比接近基准，说明没被旋转）",
            tall.AspectRatio > 1.0,
            $"宽高比={tall.AspectRatio:F2}（基准 {baseline.AspectRatio:F2}），墨迹={tall.InkPixels}px");

        // H 的判据不能用宽高比：多行文本的包围盒天然接近正方形。
        // 改用「墨迹量」——换行成功的话，同样内容会占更多行，
        // 墨迹像素数应当显著高于单行的 G。
        var wrapped = results["H-no-rotation-wrapped"];
        Check(
            "H 换行方案：内容完整显示（墨迹显著多于单行）",
            wrapped.InkPixels > tall.InkPixels * 1.5 && wrapped.FullyInside,
            $"H={wrapped.InkPixels}px vs G={tall.InkPixels}px（比值 {(double)wrapped.InkPixels / Math.Max(1, tall.InkPixels):F2}），未裁剪={wrapped.FullyInside}");

        Check(
            "H 换行方案：字形未旋转（包围盒宽度与基准相当）",
            Math.Abs(wrapped.InkBounds.Width - baseline.InkBounds.Width) < baseline.InkBounds.Width * 0.3,
            $"H 宽={wrapped.InkBounds.Width:F0}，基准宽={baseline.InkBounds.Width:F0}");

        // 回归护栏：C 方案（初版文档主张的做法）必须保持「内容消失」。
        // 如果哪天它突然有内容了，说明 WPF 的布局行为变了，
        // 文档里的结论需要重新验证。
        var deadCase = results["C-widget90-panelLayout-90"];
        Check(
            "回归护栏：C 方案（嵌套 LayoutTransform 硬编码尺寸）仍然不可用",
            deadCase.InkPixels == 0 || !deadCase.FullyInside,
            $"墨迹={deadCase.InkPixels}px, 未裁剪={deadCase.FullyInside}");

        Console.WriteLine();
        Console.WriteLine($"PNG: {outputDirectory}");
        Console.WriteLine(_failures == 0 ? "全部通过" : $"{_failures} 项失败");

        return _failures == 0 ? 0 : 1;
    }

    /// <summary>旋转方案。</summary>
    private enum RotationPlan
    {
        /// <summary>横向基准：不旋转。</summary>
        Horizontal,

        /// <summary>只旋转 Widget（FluentFlyout 今天对竖向任务栏做的）。</summary>
        WidgetOnly,

        /// <summary>Widget +90，panel 用 LayoutTransform(-90) 且尺寸硬编码。</summary>
        PanelLayoutCounterRotate,

        /// <summary>Widget +90，panel 用 RenderTransform(-90)。</summary>
        PanelRenderCounterRotate,

        /// <summary>Widget +90，panel 用 LayoutTransform(-90) 但尺寸 Auto。</summary>
        PanelLayoutCounterRotateAutoSize,

        /// <summary>Widget 用 -90（反向），panel 不旋转。</summary>
        WidgetNeg90,

        /// <summary>
        /// 不旋转，把容器做成「窄而高」的竖条，文字仍横排。
        /// 这是 TaskbarLyrics 在竖向任务栏上的实际做法
        /// （见 EmbeddedTaskbarAnchor.cs：<c>if (metrics.IsVertical) width = taskbarWidth;</c>
        /// 且全仓库没有任何 RotateTransform）。
        /// </summary>
        NoRotationTallBox,

        /// <summary>
        /// 不旋转 + 沿主轴换行（TextWrapping.Wrap）。
        /// 竖向任务栏只有 60px 宽，一行放不下几个字；
        /// 允许换行才能用满主轴方向的长度。
        /// </summary>
        NoRotationWrapped,
    }

    private sealed class RenderResult
    {
        public required BitmapSource Bitmap { get; init; }
        public required Rect InkBounds { get; init; }
        public required int InkPixels { get; init; }
        public required bool FullyInside { get; init; }
        public required Size PanelRenderSize { get; init; }
        public required Point PanelOrigin { get; init; }

        public double AspectRatio => InkBounds.Height > 0
            ? InkBounds.Width / InkBounds.Height
            : 0;
    }

    private static RenderResult Render(RotationPlan plan)
    {
        // 竖向任务栏：视觉宽度 = 60（物理 60，dpiScale=1）
        var isVertical = plan != RotationPlan.Horizontal;

        const double widgetLogicalWidth = 240;
        const double widgetLogicalHeight = 40;

        // 面板内容：两行，明确非正方形
        var stack = new StackPanel { Orientation = Orientation.Vertical };
        stack.Children.Add(new TextBlock
        {
            Text = "字形正立检验 ABC",
            FontSize = 13,
            Foreground = Brushes.Black,
            FontFamily = new FontFamily("Segoe UI"),
        });
        stack.Children.Add(new TextBlock
        {
            Text = "translation line",
            FontSize = 11,
            Foreground = Brushes.DimGray,
            FontFamily = new FontFamily("Segoe UI"),
        });

        var panel = new Grid
        {
            Background = Brushes.Transparent,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        };
        panel.Children.Add(stack);

        var autoSize = plan == RotationPlan.PanelLayoutCounterRotateAutoSize;

        if (!autoSize)
        {
            // 与 TaskbarWidgetControl 一致：容器宽度由宿主写死
            panel.Width = widgetLogicalWidth - 55;
            panel.Height = widgetLogicalHeight;
        }

        switch (plan)
        {
            case RotationPlan.PanelLayoutCounterRotate:
            case RotationPlan.PanelLayoutCounterRotateAutoSize:
                panel.LayoutTransform = new RotateTransform(-90);
                panel.HorizontalAlignment = HorizontalAlignment.Left;
                panel.VerticalAlignment = VerticalAlignment.Top;
                break;

            case RotationPlan.PanelRenderCounterRotate:
                panel.RenderTransform = new RotateTransform(-90);
                panel.RenderTransformOrigin = new Point(0, 0);
                break;
        }

        var widget = new Border
        {
            Background = Brushes.WhiteSmoke,
            Width = widgetLogicalWidth,
            Height = widgetLogicalHeight,
            Child = panel,
        };

        switch (plan)
        {
            case RotationPlan.WidgetOnly:
            case RotationPlan.PanelLayoutCounterRotate:
            case RotationPlan.PanelRenderCounterRotate:
            case RotationPlan.PanelLayoutCounterRotateAutoSize:
                widget.LayoutTransform = new RotateTransform(90);
                break;

            case RotationPlan.WidgetNeg90:
                widget.LayoutTransform = new RotateTransform(-90);
                break;

            case RotationPlan.NoRotationTallBox:
                // 竖向任务栏：宽度收窄到任务栏宽度，高度沿主轴拉长。
                // 文字不旋转，靠主轴方向的长度换取「一行能放多少字」。
                widget.Width = CanvasWidth - 4;
                widget.Height = CanvasHeight - 40;
                panel.Width = CanvasWidth - 8;
                panel.Height = CanvasHeight - 40;
                panel.VerticalAlignment = VerticalAlignment.Center;
                break;

            case RotationPlan.NoRotationWrapped:
                widget.Width = CanvasWidth - 4;
                widget.Height = CanvasHeight - 40;
                panel.Width = CanvasWidth - 8;
                panel.Height = CanvasHeight - 40;
                panel.VerticalAlignment = VerticalAlignment.Top;

                // 关键：允许换行，否则 60px 宽只能显示 3-4 个字。
                foreach (var child in stack.Children.OfType<TextBlock>())
                {
                    child.TextWrapping = TextWrapping.Wrap;
                    child.TextAlignment = TextAlignment.Left;
                }

                break;
        }

        var canvas = new Canvas
        {
            Width = CanvasWidth,
            Height = CanvasHeight,
            Background = Brushes.LightSteelBlue,
        };
        canvas.Children.Add(widget);

        // 与 TaskbarWindow.PositionWidget 的竖向分支同构：
        // left = crossPos（横轴居中），top = primaryPos（主轴起点 20）
        var physicalHeight = plan is RotationPlan.NoRotationTallBox or RotationPlan.NoRotationWrapped
            ? CanvasHeight - 40
            : widgetLogicalHeight;
        var crossPos = (CanvasWidth - physicalHeight) / 2.0;
        Canvas.SetLeft(widget, plan is RotationPlan.NoRotationTallBox or RotationPlan.NoRotationWrapped ? 2 : crossPos);
        Canvas.SetTop(widget, 20);

        canvas.Measure(new Size(CanvasWidth, CanvasHeight));
        canvas.Arrange(new Rect(0, 0, CanvasWidth, CanvasHeight));
        canvas.UpdateLayout();

        // 记录 panel 的实际渲染尺寸与相对画布的原点，
        // 用于判断「内容是不是被排到画布外了」
        var panelOrigin = new Point(0, 0);
        var panelRenderSize = new Size(0, 0);
        try
        {
            panelRenderSize = panel.RenderSize;
            var transform = panel.TransformToAncestor(canvas);
            panelOrigin = transform.Transform(new Point(0, 0));
        }
        catch (InvalidOperationException)
        {
            // 还没连上可视树
        }

        var bitmap = new RenderTargetBitmap(CanvasWidth, CanvasHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(canvas);

        _ = isVertical;

        return Measure(bitmap, panelRenderSize, panelOrigin);
    }

    private static RenderResult Measure(BitmapSource bitmap, Size panelRenderSize, Point panelOrigin)
    {
        var stride = bitmap.PixelWidth * 4;
        var pixels = new byte[stride * bitmap.PixelHeight];
        bitmap.CopyPixels(pixels, stride, 0);

        var minX = int.MaxValue;
        var minY = int.MaxValue;
        var maxX = int.MinValue;
        var maxY = int.MinValue;
        var count = 0;

        for (var y = 0; y < bitmap.PixelHeight; y++)
        {
            for (var x = 0; x < bitmap.PixelWidth; x++)
            {
                var offset = y * stride + x * 4;
                var b = pixels[offset];
                var g = pixels[offset + 1];
                var r = pixels[offset + 2];

                // 墨迹 = 深色。背景 LightSteelBlue(175,196,222) 与
                // WhiteSmoke(245,245,245) 亮度都远高于阈值。
                var luminance = (r * 299 + g * 587 + b * 114) / 1000;
                if (luminance > 140)
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

        var inkBounds = count == 0
            ? Rect.Empty
            : new Rect(minX, minY, maxX - minX + 1, maxY - minY + 1);

        // 贴边即视为被裁剪：内容超出画布后被切掉的特征
        var fullyInside = count > 0 &&
                          minX > 0 && minY > 0 &&
                          maxX < bitmap.PixelWidth - 1 &&
                          maxY < bitmap.PixelHeight - 1;

        return new RenderResult
        {
            Bitmap = bitmap,
            InkBounds = inkBounds,
            InkPixels = count,
            FullyInside = fullyInside,
            PanelRenderSize = panelRenderSize,
            PanelOrigin = panelOrigin,
        };
    }

    private static void SavePng(BitmapSource bitmap, string name)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        var path = Path.Combine(AppContext.BaseDirectory, "out", name + ".png");
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static void Check(string name, bool condition, string detail)
    {
        if (!condition)
        {
            _failures++;
        }

        Console.WriteLine($"[{(condition ? "PASS" : "FAIL")}] {name}");
        Console.WriteLine($"         {detail}");
    }
}
