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

            // 真实场景复刻：TaskbarWindow.PositionWidget 对竖向仍然无条件加
            // Widget.LayoutTransform = RotateTransform(90)，而 P3-12 的歌词实现
            // 是「panel 不旋转 + 换行」。两者叠加后到底正不正立，
            // 必须渲染出来看——这正是第一轮探针漏掉的那一步。
            ("I-real-widget90-lyrics-wrapped", RotationPlan.RealWidget90LyricsWrapped),
            ("J-real-widget90-lyrics-wrap-counterrotated", RotationPlan.RealWidget90LyricsWrappedCounterRotated),
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

        // ---------- 真实场景：Widget 无条件 +90 与「不旋转 + 换行」叠加 ----------
        //
        // 这是第二轮探针新增的判据。第一轮的 G/H 只测了「Widget 不旋转」的理想情况，
        // 而 TaskbarWindow.PositionWidget 对竖向是**无条件**挂 RotateTransform(90) 的。
        // 两者叠加后到底行不行，必须单独验证。
        var real = results["I-real-widget90-lyrics-wrapped"];
        var realCounter = results["J-real-widget90-lyrics-wrap-counterrotated"];

        // 判据一：内容是否完整。用「墨迹量」而不是宽高比——
        // 多行文本的包围盒天然接近正方形，宽高比在这里没有判别力
        // （第一轮就是在这里踩过一次）。H 是「Widget 不旋转 + 换行」的
        // 完整显示基准，I / J 的墨迹应当与之相当。
        var completeRatio = (double)realCounter.InkPixels / Math.Max(1, wrapped.InkPixels);
        Check(
            "真实场景 J（panel 反向 -90 抵消）：内容完整显示",
            realCounter.FullyInside && completeRatio > 0.85,
            $"J={realCounter.InkPixels}px vs H 基准={wrapped.InkPixels}px（比值 {completeRatio:F2}），" +
            $"未裁剪={realCounter.FullyInside}");

        // 判据二：I 方案（panel 不旋转）在 Widget 已旋转 90° 的前提下
        // 会被父级的高度（40）裁掉大半——这正是「只去掉 panel 的旋转不够」的证据。
        var truncationRatio = (double)real.InkPixels / Math.Max(1, wrapped.InkPixels);
        Check(
            "真实场景 I（panel 不旋转）：内容被 Widget 旋转后的窄边裁掉",
            !real.FullyInside || truncationRatio < 0.6,
            $"I={real.InkPixels}px（仅为 H 基准的 {truncationRatio:P0}），未裁剪={real.FullyInside}");

        // 判据三：字形朝向。用一行**不会被换行**的短西文单独渲染——
        // 字母天然宽扁，正立时包围盒宽 > 高，躺倒时宽 < 高。
        // 这条不能用上面那张多行图判断：窄容器里多行横排同样是「高 > 宽」。
        var uprightI = MeasureOrientation(counterRotate: false);
        var uprightJ = MeasureOrientation(counterRotate: true);
        Check(
            "字形朝向：J（panel -90 抵消）后文字正立（横线仍横着）",
            uprightJ.AspectRatio > 3.0,
            $"单字符 — 包围盒={uprightJ.InkBounds.Width:F0}×{uprightJ.InkBounds.Height:F0}，" +
            $"宽高比={uprightJ.AspectRatio:F2}（>3 为横线，即正立）");
        Check(
            "字形朝向：I（panel 不旋转）后文字躺倒（横线变竖线）",
            uprightI.AspectRatio < 0.35,
            $"单字符 — 包围盒={uprightI.InkBounds.Width:F0}×{uprightI.InkBounds.Height:F0}，" +
            $"宽高比={uprightI.AspectRatio:F2}（<0.35 为竖线，即躺倒）");

        Console.WriteLine();
        Console.WriteLine($"PNG: {outputDirectory}");
        Console.WriteLine(_failures == 0 ? "全部通过" : $"{_failures} 项失败");

        return _failures == 0 ? 0 : 1;
    }

    /// <summary>
    /// 量「字形本身」的朝向——用一个**单个宽扁字符**，它的包围盒方向
    /// 不受换行影响。
    ///
    /// 为什么不能用多字符：竖向任务栏屏幕宽度只有 40 DIP，任何多字符文本
    /// 都必然折成多行，折行后的包围盒接近正方形，宽高比失去判别力
    /// （这一轮在 ABCD 上连踩两次）。
    ///
    /// 选 <c>—</c>（em dash）：正立时是一条宽扁的横线（宽高比 ≈ 10），
    /// 躺倒后变成一条竖线（≈ 0.1）。判别力足够，且与字体无关——
    /// Segoe UI / 微软雅黑都有这个字符。
    /// </summary>
    private static RenderResult MeasureOrientation(bool counterRotate)
    {
        const double widgetLogicalHeight = 40;
        const double panelWidth = widgetLogicalHeight - 8; // 32：任务栏宽度内

        var text = new TextBlock
        {
            Text = "—",
            FontSize = 20,
            Foreground = Brushes.Black,
            FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI"),
            TextWrapping = TextWrapping.NoWrap,
            Width = panelWidth,
            TextAlignment = TextAlignment.Center,
        };

        var panel = new Grid
        {
            Background = Brushes.Transparent,
            Width = panelWidth,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        panel.Children.Add(text);

        if (counterRotate)
        {
            panel.LayoutTransform = new RotateTransform(-90);
        }

        var widget = new Border
        {
            Background = Brushes.WhiteSmoke,
            Width = CanvasHeight - 40,   // 240：主轴长度
            Height = widgetLogicalHeight, // 40：任务栏宽度
            Child = panel,
        };

        // PositionWidget 对竖向无条件加的这个旋转——I / J 的共同前提。
        widget.LayoutTransform = new RotateTransform(90);

        var canvas = new Canvas
        {
            Width = CanvasWidth,
            Height = CanvasHeight,
            Background = Brushes.LightSteelBlue,
        };
        canvas.Children.Add(widget);

        Canvas.SetLeft(widget, (CanvasWidth - widgetLogicalHeight) / 2.0);
        Canvas.SetTop(widget, 20);

        canvas.Measure(new Size(CanvasWidth, CanvasHeight));
        canvas.Arrange(new Rect(0, 0, CanvasWidth, CanvasHeight));
        canvas.UpdateLayout();

        var bitmap = new RenderTargetBitmap(CanvasWidth, CanvasHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(canvas);

        return Measure(bitmap, new Size(0, 0), new Point(0, 0));
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

        /// <summary>
        /// <b>真实场景复刻</b>：Widget 挂 <c>RotateTransform(90)</c>
        /// （<c>TaskbarWindow.PositionWidget</c> 对竖向的无条件行为），
        /// 歌词 panel 不旋转、只开换行——即 P3-12 当前实现的样子。
        ///
        /// 这一项用来回答「不旋转 + 换行」到底能不能抵消 Widget 的旋转。
        /// </summary>
        RealWidget90LyricsWrapped,

        /// <summary>
        /// 同 <see cref="RealWidget90LyricsWrapped"/>，但 panel 再挂一个
        /// <c>LayoutTransform(-90)</c> 把 Widget 的旋转抵消掉。
        /// 与 C 的区别是这里 panel 尺寸是 Auto（不是硬编码 185），
        /// 所以不会像 C 那样被排到画布外。
        /// </summary>
        RealWidget90LyricsWrappedCounterRotated,
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

            case RotationPlan.RealWidget90LyricsWrapped:
                // 复刻 TaskbarWidgetControl 的竖向几何：
                //   Widget 的「逻辑宽度」= 旋转后屏幕上的高度 = 主轴长度（240）
                //   Widget 的「逻辑高度」= 旋转后屏幕上的宽度 = 任务栏宽度（40）
                // 这正是 CalculateSize 的 `_isVertical && _isLyricsMode` 分支
                // 取 `logicalWidth = maxLogicalWidth` 的含义。
                widget.Width = CanvasHeight - 40;   // 240，主轴长度
                widget.Height = widgetLogicalHeight; // 40，任务栏宽度
                widget.LayoutTransform = new RotateTransform(90);

                // 歌词 panel 不旋转，只开换行；宽度钉到任务栏宽度。
                // 注意旋转前坐标系里 panel 的「宽度」对应屏幕上的纵向延伸，
                // 所以这里设的是 Widget.Height 而不是 Width。
                panel.Width = widgetLogicalHeight - 4;
                panel.Height = double.NaN;
                panel.VerticalAlignment = VerticalAlignment.Center;

                foreach (var child in stack.Children.OfType<TextBlock>())
                {
                    child.TextWrapping = TextWrapping.Wrap;
                    child.TextAlignment = TextAlignment.Left;
                    child.Width = widgetLogicalHeight - 8;
                }

                break;

            case RotationPlan.RealWidget90LyricsWrappedCounterRotated:
                // 同上，但用 LayoutTransform(-90) 抵消 Widget 的 +90。
                // 与 C 方案的区别：这里尺寸是 Auto（不硬编码），
                // 所以父级拿到的是旋转后的真实尺寸，不会被排到画布外。
                widget.Width = CanvasHeight - 40;
                widget.Height = widgetLogicalHeight;
                widget.LayoutTransform = new RotateTransform(90);

                panel.Width = widgetLogicalHeight - 4;
                panel.Height = double.NaN;
                panel.VerticalAlignment = VerticalAlignment.Center;
                panel.LayoutTransform = new RotateTransform(-90);

                foreach (var child in stack.Children.OfType<TextBlock>())
                {
                    child.TextWrapping = TextWrapping.Wrap;
                    child.TextAlignment = TextAlignment.Left;
                    child.Width = widgetLogicalHeight - 8;
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
        var fillsCanvas = plan is RotationPlan.NoRotationTallBox or RotationPlan.NoRotationWrapped;
        var physicalHeight = fillsCanvas ? CanvasHeight - 40 : widgetLogicalHeight;
        var crossPos = (CanvasWidth - physicalHeight) / 2.0;

        // 真实场景复刻的两项：Widget 旋转后视觉尺寸是 (40 宽 × 240 高)，
        // 横向居中、主轴从 20 起——与 PositionWidget 的算法一致。
        if (plan is RotationPlan.RealWidget90LyricsWrapped or RotationPlan.RealWidget90LyricsWrappedCounterRotated)
        {
            crossPos = (CanvasWidth - widgetLogicalHeight) / 2.0;
        }

        Canvas.SetLeft(widget, fillsCanvas ? 2 : crossPos);
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
