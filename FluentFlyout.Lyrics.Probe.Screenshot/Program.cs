// Copyright (c) 2026 anync
// SPDX-License-Identifier: MIT
//
// 歌词面板截图矩阵：渲染**真实的** LyricsPanel（引用主工程，不是近似视觉树），
// 覆盖 P3 验收要求的组合：浅/深主题 × 横向/竖向 × 100%/150%/200% DPI ×
// 若干歌词状态（单行、双行、翻译行、逐字扫描、长句滚动、纯音乐）。
//
// 为什么需要它：P3 的歌词层目前**还没有接线**——TaskbarWidgetControl.SetLyricsMode
// 与 UpdateLyricsFrame 在生产代码里没有任何调用点，真正的装配在 P4 的
// LyricsService。所以真机播放截图这条路现在跑不通，只能靠 mock 帧驱动面板。
//
// 这个程序就是那个「mock 驱动」：它把面板当成一个普通控件，喂帧、设度量、
// 渲染成 PNG。产物可直接贴进 PR 做视觉验收。
//
// 判据（全部是像素统计，不依赖人眼）：
//   1. 有墨迹（内容没被裁没）
//   2. 墨迹不贴边（没有被 ClipToBounds 切掉）
//   3. 双行模式下行数 >= 2（翻译行真的画出来了）
//   4. 竖向模式下墨迹纵向延伸 > 横向（换行生效，不是被压成一行）

using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentFlyout.Controls;
using FluentFlyout.Lyrics.Layout;
using FluentFlyout.Lyrics.Models;

namespace FluentFlyout.Lyrics.Probe.Screenshot;

internal static class Program
{
    private static int _failures;

    /// <summary>标准任务栏高度（DIP）。</summary>
    private const double WidgetHeight = 40;

    /// <summary>竖向任务栏宽度（DIP）。</summary>
    private const double VerticalWidth = 60;

    /// <summary>
    /// 边界矩阵用的短边取值。
    ///
    /// 基础矩阵只跑标准短边（横 40 / 竖 60），把「小任务栏」与「大字号」
    /// 这两类真实设置排除在外——而它们正是 PR #4 留下的两个存疑项：
    /// 竖向 + 28 DIP 短边能不能读、28 DIP 宽里 20px 字会不会一个字一行。
    /// 存疑项不能靠推理关闭，得先把它们渲染出来看。
    /// </summary>
    private static readonly Variant[] VerticalVariants =
    [
        new("v60", 60, LyricsMetricsPolicy.DefaultPreferredFontSize),
        new("v48", 48, LyricsMetricsPolicy.DefaultPreferredFontSize),
        new("v28-small", LyricsMetricsPolicy.SmallTaskbarCrossSize, LyricsMetricsPolicy.DefaultPreferredFontSize),
        new("v60-font20", 60, 20),
        // 最紧的组合：小任务栏宽度 + 用户把字号拉大。这一组是「竖排没有
        // 宽度方向字号约束」的直接探针——28 DIP 里放 20px 字，一行只站得下
        // 一个汉字。
        new("v28-font20", LyricsMetricsPolicy.SmallTaskbarCrossSize, 20),
    ];

    private static readonly Variant[] HorizontalVariants =
    [
        new("h40", 40, LyricsMetricsPolicy.DefaultPreferredFontSize),
        new("h28-small", LyricsMetricsPolicy.SmallTaskbarCrossSize, LyricsMetricsPolicy.DefaultPreferredFontSize),
        new("h40-font20", 40, 20),
    ];

    /// <summary>一组边界参数：短边长度 + 用户字号（都是 DIP）。</summary>
    private sealed record Variant(string Name, double CrossSize, double FontSize);

    [STAThread]
    private static int Main()
    {
        var outputDirectory = Path.Combine(AppContext.BaseDirectory, "shots");
        if (Directory.Exists(outputDirectory))
        {
            Directory.Delete(outputDirectory, recursive: true);
        }

        Directory.CreateDirectory(outputDirectory);

        Console.WriteLine("=== 歌词面板截图矩阵 ===");
        Console.WriteLine();

        var results = new List<ShotResult>();

        // DPI 只影响「渲染时的物理像素密度」，不影响布局。
        // 用 RenderTargetBitmap 的 dpi 参数模拟，这样同一份布局能在
        // 100%/150%/200% 下渲染出不同像素密度——正是混合 DPI 场景。
        foreach (var dpi in new[] { 96, 144, 192 })
        {
            var dpiScale = dpi / 96.0;
            var dpiLabel = $"{dpi / 96.0 * 100:F0}%";

            foreach (var isDark in new[] { false, true })
            {
                var themeLabel = isDark ? "dark" : "light";

                foreach (var isVertical in new[] { false, true })
                {
                    var orientation = isVertical ? "vertical" : "horizontal";

                    foreach (var state in LyricStates)
                    {
                        var name = $"{themeLabel}-{orientation}-{dpiLabel}-{state.Name}";
                        var result = RenderState(isDark, isVertical, dpiScale, state);
                        results.Add(result with { Name = name });

                        SavePng(result.Bitmap, name);
                    }
                }
            }
        }

        Console.WriteLine($"共 {results.Count} 张");
        Console.WriteLine();

        // ---------- 边界矩阵 ----------
        //
        // 只跑 100% DPI：短边与字号都是 DIP，DPI 只改像素密度，不改变
        // 「一行能放几个字」这个待测性质；基础矩阵已经覆盖了 DPI 维度。
        var edgeResults = new List<ShotResult>();

        foreach (var isDark in new[] { false, true })
        {
            var themeLabel = isDark ? "dark" : "light";

            foreach (var isVertical in new[] { false, true })
            {
                var orientation = isVertical ? "vertical" : "horizontal";
                var variants = isVertical ? VerticalVariants : HorizontalVariants;

                foreach (var variant in variants)
                {
                    foreach (var state in EdgeStates)
                    {
                        var name = $"edge-{themeLabel}-{orientation}-{variant.Name}-{state.Name}";
                        var result = RenderState(isDark, isVertical, 1.0, state, variant);
                        result = result with { Name = name };
                        edgeResults.Add(result);

                        SavePng(result.Bitmap, name);
                    }
                }
            }
        }

        Console.WriteLine($"边界矩阵 {edgeResults.Count} 张（100% DPI）");
        Console.WriteLine();

        // ---------- 断言 ----------
        Console.WriteLine("--- 断言 ---");

        // 1. 除纯音乐态外，所有图都必须有墨迹。
        //
        // 纯音乐态例外是**设计意图**：IsPureMusic 时歌词面板让位给频谱
        //（见 TaskbarWidgetControl.UpdateTextRegionVisibility 的注释），
        // 面板本身不可见，墨迹为 0 才是对的。这条单独反向断言，
        // 免得将来有人「顺手」把纯音乐态也画出文字来。
        foreach (var r in results.Where(x => !x.IsPureMusic))
        {
            Check(
                $"{r.Name}：有墨迹",
                r.InkPixels > 0,
                $"墨迹={r.InkPixels}px");
        }

        foreach (var r in results.Where(x => x.IsPureMusic))
        {
            Check(
                $"{r.Name}：纯音乐态让位给频谱（无墨迹）",
                r.InkPixels == 0,
                $"墨迹={r.InkPixels}px（应为 0：歌词面板在纯音乐态不可见）");
        }

        // 2. 双行状态必须真的画了两行以上（翻译行 / 下一行）
        foreach (var r in results.Where(x => x.IsTwoLine))
        {
            Check(
                $"{r.Name}：双行模式画出了 2 行以上",
                r.InkBounds.Height >= r.LineHeight * 1.4,
                $"墨迹高度={r.InkBounds.Height:F1}px，单行高={r.LineHeight:F1}px");
        }

        // 3. 竖向必须换行（不是被压成一行）
        //
        // **判据不能是「墨迹纵向 > 横向」。** 这句断言在单行短歌词上必然失败：
        // 8 个字在 60 DIP 宽里折成 2 行 × 4 字，包围盒是 52×33——接近正方形，
        // 宽高比失去判别力。这是本探针第二次踩同一个坑（第一轮在 I/J 方案
        // 上用多行文本的宽高比判「是否躺倒」，同样失败）。
        //
        // 正确的判据是**比较内容宽度与面板宽度**：
        //   竖向正确 → 折行把内容压到面板宽度内
        //   竖向错误（没换行）→ 内容远超面板宽度，被 ClipToBounds 裁成一条
        //
        // 再补一条「纵向至少两行高」排除「只剩一个字」的退化情况。
        //
        // 纯音乐态排除：面板不可见、无文字，方向判断没有意义。
        foreach (var r in results.Where(x => x.IsVertical && x.IsWrapped && !x.IsPureMusic))
        {
            Check(
                $"{r.Name}：竖向折行到面板宽度内（没被裁）",
                r.InkBounds.Width <= r.ViewportWidth + 1,
                $"墨迹宽={r.InkBounds.Width:F0}px，面板宽={r.ViewportWidth:F0}px");

            Check(
                $"{r.Name}：竖向至少两行高（换行生效）",
                r.InkBounds.Height >= r.LineHeight * 1.4,
                $"墨迹高={r.InkBounds.Height:F0}px，单行高={r.LineHeight:F1}px");
        }

        // 4. 横向模式必须横向延伸（没被误旋转）
        foreach (var r in results.Where(x => !x.IsVertical && x.IsWrapped && !x.IsPureMusic))
        {
            Check(
                $"{r.Name}：横向未被误旋转（墨迹横向 > 纵向）",
                r.InkBounds.Width > r.InkBounds.Height,
                $"包围盒={r.InkBounds.Width:F0}×{r.InkBounds.Height:F0}");
        }

        // 5. 边界矩阵第一条：内容不许被主轴长度上限裁掉。
        //
        // 判据不能用「墨迹不贴边」：竖排的画布高度按定义等于内容高度，
        // 文字必然贴住上下边，贴边≠裁切。第一版就是这么写的，42 张全红，
        // 红的是判据不是渲染。改用布局数值（实测内容高度 vs 分配高度）——
        // 这也是生产里真正比较的那两个量。
        foreach (var r in edgeResults)
        {
            Check(
                $"{r.Name}：内容没被长度上限裁掉",
                !r.ContentClipped,
                $"短边={r.CrossSize:F0}，字号={r.FontSize:F1}，" +
                $"画布={r.Bitmap.PixelWidth}×{r.Bitmap.PixelHeight}，" +
                $"墨迹高={r.InkBounds.Height:F0}px");
        }

        // 6. 边界矩阵第二条：竖向窄任务栏每行至少两个汉字。
        //
        // 为什么要有这条：28 DIP 的竖向任务栏，13px 字按字宽折行只剩
        // **一个字一行**。此时「没被裁」「没被压成横条」全都通过，
        // 截图矩阵原来的四条判据一条都抓不住——但用户看到的是一条
        // 竖着排的独字长条。判据用行宽比：一个汉字≈一个字号宽，
        // 两个汉字≈两倍，取 1.7 作阈值能区分「1 字」与「2 字」。
        foreach (var r in edgeResults.Where(x => x.IsVertical))
        {
            Check(
                $"{r.Name}：竖向每行至少两个汉字",
                r.InkBounds.Width >= 1.7 * r.FontSize,
                $"行宽={r.InkBounds.Width:F0}px，字号={r.FontSize:F0}px（一个汉字≈{r.FontSize:F0}px）");
        }

        Console.WriteLine();
        Console.WriteLine($"PNG: {outputDirectory}");
        Console.WriteLine(_failures == 0 ? "全部通过" : $"{_failures} 项失败");

        return _failures == 0 ? 0 : 1;
    }

    /// <summary>一个歌词状态的描述。</summary>
    private sealed record LyricState(
        string Name,
        LyricDisplayFrame Frame,
        bool IsTwoLine,
        bool IsPureMusic = false,
        bool IsWrapped = true);

    /// <summary>
    /// 覆盖 P3 全部渲染路径的歌词状态。
    /// </summary>
    private static readonly LyricState[] LyricStates =
    [
        // 单行普通歌词（双行模式下的当前行）
        new("single",
            new LyricDisplayFrame(
                CurrentLine: "夜色渐浓风也变凉",
                NextLine: "",
                Title: "测试曲目"),
            IsTwoLine: false),

        // 双行：当前行 + 下一行预览
        new("two-line",
            new LyricDisplayFrame(
                CurrentLine: "夜色渐浓风也变凉",
                NextLine: "路灯下影子被拉长",
                Title: "测试曲目"),
            IsTwoLine: true),

        // 带翻译行
        new("translation",
            new LyricDisplayFrame(
                CurrentLine: "夜が更けて風も冷たくなる",
                NextLine: "街灯の下で影が伸びる",
                Title: "テスト曲",
                CurrentTranslation: "夜色渐浓风也变凉",
                NextTranslation: "路灯下影子被拉长",
                HasTrackTranslation: true),
            IsTwoLine: true),

        // 逐字扫描（进度 45%）
        new("word-scan",
            new LyricDisplayFrame(
                CurrentLine: "夜色渐浓风也变凉",
                NextLine: "路灯下影子被拉长",
                Title: "测试曲目",
                LineProgress: 0.45,
                WordScanProgress: 0.45),
            IsTwoLine: true),

        // 长句（横向会触发滚动，竖向会折很多行）
        new("long-line",
            new LyricDisplayFrame(
                CurrentLine: "如果这场雨一直下到明天清晨我们是不是就能一直站在这里不分开",
                NextLine: "而你说好要陪我走完这条街",
                Title: "测试曲目",
                LineProgress: 0.3),
            IsTwoLine: true),

        // 纯音乐态
        new("pure-music",
            new LyricDisplayFrame(
                CurrentLine: "纯音乐，请欣赏",
                NextLine: "",
                Title: "测试曲目",
                IsPureMusic: true),
            IsTwoLine: false,
            IsPureMusic: true),
    ];

    /// <summary>
    /// 边界矩阵只跑会长成多行的状态，单行状态没有新信息。
    ///
    /// 必须声明在 <see cref="LyricStates"/> **之后**：静态字段按声明顺序初始化，
    /// 放到前面会在类型初始化时读到 null，`LyricStates[0]` 直接抛
    /// NullReferenceException（这个顺序错误编译器能报 CS8602，别忽略它）。
    /// </summary>
    private static readonly LyricState[] EdgeStates = [LyricStates[0], LyricStates[1], LyricStates[4]];

    private static ShotResult RenderState(bool isDark, bool isVertical, double dpiScale, LyricState state, Variant? variant = null)
    {
        // 面板的真实布局尺寸：
        //   横向 → 宽由内容决定（这里给 300，相当于一句中等长度歌词），高 = 任务栏高度
        //   竖向 → 宽 = 任务栏宽度，高 = 主轴长度（按折行估算）
        //
        // 「小任务栏」由短边本身推导，不额外传标志位：生产里
        // TaskbarWidgetControl 判定的也是短边 < 40 DIP（见 SetSmallTaskbarMode）。
        // 探针若自己另存一个 bool，就会和短边脱钩，测出生产里不存在的组合。
        var crossSize = variant?.CrossSize ?? (isVertical ? VerticalWidth : WidgetHeight);
        var isSmallTaskbar = crossSize < LyricsMetricsPolicy.DefaultTaskbarCrossSize;

        var metrics = LyricsMetricsPolicy.Create(
            isVertical: isVertical,
            isSmallTaskbar: isSmallTaskbar,
            preferredFontSize: variant?.FontSize ?? LyricsMetricsPolicy.DefaultPreferredFontSize);

        var panel = new LyricsPanel();

        // 顺序与生产一致：先设度量（ApplyMetrics 会清宽度缓存），
        // 再应用配色，最后喂帧。
        panel.ApplyMetrics(metrics);
        panel.ApplyWindowsTheme(isDark);
        panel.ApplyFrame(state.Frame);
        panel.SetActive(true);

        // 背景模拟任务栏：浅色主题用近白，深色用深灰。
        // 面板自身没有背景（透明），不给底色的话墨迹判据会失效。
        var background = isDark
            ? Color.FromRgb(0x20, 0x20, 0x20)
            : Color.FromRgb(0xF3, 0xF3, 0xF3);

        double logicalWidth;
        double logicalHeight;

        FrameworkElement renderRoot;

        if (isVertical)
        {
            // ---------- 竖向：必须复刻生产的两层旋转 ----------
            //
            // TaskbarWindow.PositionWidget 给整个 TaskbarWidgetControl 挂
            // LayoutTransform(+90)，LyricsPanel 内部再挂 LayoutTransform(-90)
            // 抵消。少了外层那个 +90，内层的 -90 就没有抵消对象，
            // 渲染出来是躺倒的——这正是本探针第一版犯的建模错误，
            // 也是 FluentFlyout.Lyrics.Probe.Render 第二轮 I 方案揭示的同一件事。
            //
            // 尺寸怎么定，用变换矩阵算清楚（这里绕，但必须算对）：
            //
            //   Widget 逻辑 (Ww × Hw) ── +90 ── 屏幕 (Hw × Ww)
            //   Panel  逻辑 (Wp × Hp) ── −90 ── Widget 内部 (Hp × Wp)
            //                                  ── Widget +90 ── 屏幕 (Wp × Hp)
            //
            //   要让 Panel 填满 Widget：Hp = Ww、Wp = Hw。
            //   竖向 Widget 是 Ww = 主轴长度、Hw = 任务栏宽度，
            //   所以 Panel 逻辑尺寸 = 任务栏宽度 × 主轴长度。
            //
            //   结论：Panel.Width = 任务栏宽度，Panel.Height = 主轴长度。
            //   这样 Panel 内部按「任务栏宽度」排版、文字折行，
            //   净旋转 0 后屏幕上就是一条 60 宽 × 400 高的竖条。
            //
            //   第一版探针把两者写反了（Width=主轴长度），结果 Panel 内部
            //   按 400 宽排版、一行放得下整句，屏幕上只看到两三个字。
            panel.SetVerticalMode(true);

            var estimate = LyricsVerticalLayout.EstimateMainAxisLength(
                content: panel.MeasureVerticalContent(),
                crossSize: crossSize,
                maxLength: TaskbarWidgetControl.MaxLyricsWidgetLength);

            // Panel 在自身坐标系里：宽 = 任务栏宽度，高 = 主轴长度。
            // 对齐（Stretch）由 LyricsPanel.SetVerticalMode 自己设置——
            // 这是生产行为，探针不该代替它。
            panel.Width = crossSize;
            panel.Height = estimate;

            // Widget 在自身坐标系里：宽 = 主轴长度，高 = 任务栏宽度。
            var widget = new Border
            {
                Background = new SolidColorBrush(background),
                Width = estimate,
                Height = crossSize,
                Child = panel,
                LayoutTransform = new RotateTransform(90),
            };

            // 旋转后视觉尺寸是 (任务栏宽度 × estimate)。
            var canvas = new Canvas
            {
                Width = crossSize,
                Height = estimate,
                Background = new SolidColorBrush(background),
            };
            canvas.Children.Add(widget);
            Canvas.SetLeft(widget, 0);
            Canvas.SetTop(widget, 0);

            renderRoot = canvas;
            logicalWidth = crossSize;
            logicalHeight = estimate;
        }
        else
        {
            panel.SetVerticalMode(false);

            logicalWidth = 300;
            logicalHeight = crossSize;

            panel.Width = logicalWidth;
            panel.Height = logicalHeight;

            renderRoot = new Border
            {
                Background = new SolidColorBrush(background),
                Width = logicalWidth,
                Height = logicalHeight,
                Child = panel,
            };
        }

        // DPI 只影响物理像素密度，不影响布局——WPF 按逻辑尺寸布局，
        // RenderTargetBitmap 用目标 dpi 渲染，得到对应密度的图。
        // 这正是混合 DPI 场景的本质。
        renderRoot.Measure(new Size(logicalWidth, logicalHeight));
        renderRoot.Arrange(new Rect(0, 0, logicalWidth, logicalHeight));
        renderRoot.UpdateLayout();

        // 竖向：布局稳定后按**实测内容**兜住估算偏小的一侧，但**不突破上限**。
        //
        // 复刻生产链路（TaskbarWidgetControl.MeasureDesiredWidth）的两步：
        //   1. 估算是按宽度除法推的近似值，实际折行由 TextBlock 断行算法决定，
        //      中文标点禁则会让实际行数多一行 → 取 max(估算, 实测)；
        //   2. 实测值随后被 Math.Min(measured, MaxLyricsWidgetLength) 夹住。
        //
        // 第 2 步就是这次要测的东西：一旦实测超过上限，被夹掉的那部分在屏幕上
        // 表现为「句子没写完」。原来的探针无上限地把 panel 撑到实测高度，
        // 恰好把这一步掩盖成「一切正常」——所以这里必须自己算一遍分配值。
        var contentClipped = false;

        if (isVertical && renderRoot is Canvas verticalCanvas)
        {
            var natural = panel.VerticalContentHeight;
            var allocated = panel.Height;

            if (double.IsFinite(natural) && natural > allocated)
            {
                allocated = Math.Min(natural, TaskbarWidgetControl.MaxLyricsWidgetLength);
            }

            contentClipped = double.IsFinite(natural) && natural > allocated + 0.5;

            if (Math.Abs(allocated - panel.Height) > 0.01)
            {
                panel.Height = allocated;
                verticalCanvas.Height = allocated;
                logicalHeight = allocated;

                if (verticalCanvas.Children[0] is Border widgetBorder)
                {
                    widgetBorder.Width = allocated;
                }

                renderRoot.Measure(new Size(logicalWidth, logicalHeight));
                renderRoot.Arrange(new Rect(0, 0, logicalWidth, logicalHeight));
                renderRoot.UpdateLayout();
            }
        }

        // 诊断：把竖向各层的实际布局尺寸打出来。
        //
        // 竖向这条链有三层变换（Canvas → Widget(+90) → Panel(−90)），
        // 光看代码很难判断哪一层的尺寸没生效——直接量 ActualWidth/Height。
        // 这条输出在本轮定位到两个真实缺陷：
        //   1. primary 高 0（Canvas 的 MeasureOverride 恒返回 0，
        //      放开 Height 约束就塌成 0 高，文字被 ClipToBounds 全裁）
        //   2. root 高 > panel 高（估算只算主行，次要行溢出面板）
        // 保留它作为「下次再坏时能一眼看出坏在哪层」的护栏。
        if (isVertical)
        {
            var root = panel.FindName("RootGrid") as FrameworkElement;
            var primary = panel.FindName("PrimaryContainer") as FrameworkElement;
            var secondary = panel.FindName("SecondaryContainer") as FrameworkElement;

            // 只在这一组 DPI/主题里打，否则诊断行会把断言结果淹掉。
            if (dpiScale == 1.0 && !isDark && variant is null)
            {
                var content = panel.MeasureVerticalContent();
                Console.WriteLine(
                    $"  [diag] {state.Name,-12} " +
                    $"panel={panel.ActualHeight,6:F1} " +
                    $"content={root?.ActualHeight,6:F1} " +
                    $"primary={primary?.ActualHeight,6:F1} " +
                    $"secondary={secondary?.ActualHeight,5:F1} " +
                    $"| est: pw={content.PrimaryWidth:F1} ph={content.PrimaryLineHeight:F2} " +
                    $"sw={content.SecondaryWidth:F1} sh={content.SecondaryLineHeight:F2} " +
                    $"gap={content.Gap:F1} est={panel.Height:F1}");
            }
        }

        var pixelWidth = (int)Math.Ceiling(logicalWidth * dpiScale);
        var pixelHeight = (int)Math.Ceiling(logicalHeight * dpiScale);

        var bitmap = new RenderTargetBitmap(pixelWidth, pixelHeight, 96 * dpiScale, 96 * dpiScale, PixelFormats.Pbgra32);
        bitmap.Render(renderRoot);

        return Measure(
            bitmap,
            state,
            isVertical,
            isDark,
            metrics.LineHeight * dpiScale,
            dpiScale,
            // 竖向时内容折行到「任务栏宽度」这条边；横向时视口是整块宽度。
            viewportWidthPx: (isVertical ? crossSize : logicalWidth) * dpiScale,
            // 记录**生效**字号而不是用户偏好值：这条断言问的是「屏幕上
            // 一行能放几个字」，如果度量层把字号降过，降完的字号才算数。
            crossSize: crossSize,
            fontSize: metrics.FontSize,
            contentClipped: contentClipped);
    }

    private static ShotResult Measure(
        BitmapSource bitmap,
        LyricState state,
        bool isVertical,
        bool isDark,
        double lineHeightPx,
        double dpiScale,
        double viewportWidthPx,
        double crossSize,
        double fontSize,
        bool contentClipped)
    {
        var stride = bitmap.PixelWidth * 4;
        var pixels = new byte[stride * bitmap.PixelHeight];
        bitmap.CopyPixels(pixels, stride, 0);

        // 墨迹判据：与背景的**亮度差**超过阈值即视为文字。
        // 不能用绝对亮度——深色主题下文字是白的、背景是黑的，
        // 浅色主题正好相反。用「与角落背景色的差」两头都适用。
        var bgLuminance = Luminance(pixels, stride, 0, 0);

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
                var luminance = Luminance(pixels, stride, x, y);

                if (Math.Abs(luminance - bgLuminance) < 40)
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

        return new ShotResult
        {
            Name = string.Empty,
            Bitmap = bitmap,
            InkBounds = inkBounds,
            InkPixels = count,
            IsVertical = isVertical,
            IsTwoLine = state.IsTwoLine,
            IsWrapped = state.IsWrapped,
            IsPureMusic = state.IsPureMusic,
            IsDark = isDark,
            LineHeight = lineHeightPx,
            DpiScale = dpiScale,
            ViewportWidth = viewportWidthPx,
            CrossSize = crossSize,
            FontSize = fontSize,
            ContentClipped = contentClipped,
        };
    }

    private static double Luminance(byte[] pixels, int stride, int x, int y)
    {
        var offset = (y * stride) + (x * 4);
        var b = pixels[offset];
        var g = pixels[offset + 1];
        var r = pixels[offset + 2];
        return ((r * 299) + (g * 587) + (b * 114)) / 1000.0;
    }

    private static void SavePng(BitmapSource bitmap, string name)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));

        var path = Path.Combine(AppContext.BaseDirectory, "shots", name + ".png");
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

    private sealed record ShotResult
    {
        public required string Name { get; init; }
        public required BitmapSource Bitmap { get; init; }
        public required Rect InkBounds { get; init; }
        public required int InkPixels { get; init; }
        public required bool IsVertical { get; init; }
        public required bool IsTwoLine { get; init; }
        public required bool IsWrapped { get; init; }
        public required bool IsPureMusic { get; init; }
        public required bool IsDark { get; init; }
        public required double LineHeight { get; init; }
        public required double DpiScale { get; init; }

        /// <summary>
        /// 折行视口宽度（**物理像素**）。
        ///
        /// 竖向时内容被折行到「任务栏宽度」这条边，所以墨迹宽度不应超过它。
        /// 用它做判据比「墨迹纵向 &gt; 横向」可靠：后者在短句折成两行时
        /// 包围盒接近正方形，宽高比失去判别力（本探针踩过两次）。
        /// </summary>
        public required double ViewportWidth { get; init; }

        /// <summary>任务栏短边（DIP）。横向是高度，竖向是宽度。</summary>
        public required double CrossSize { get; init; }

        /// <summary>生效字号（DIP）。</summary>
        public required double FontSize { get; init; }

        /// <summary>
        /// 内容是否被「主轴长度上限」裁掉了一部分。
        ///
        /// 由布局数值判定（实测内容高度 &gt; 分配高度），不看像素：
        /// 竖排时画布高度就是内容高度，墨迹必然贴边，像素判据区分不出
        /// 「刚好填满」和「被切掉一截」。
        /// </summary>
        public required bool ContentClipped { get; init; }
    }
}
