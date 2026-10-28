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

        // 2. 双行状态必须真的画了两行（横向）／两列（竖向）
        //
        // 横向：两行 → 墨迹高 >= 1.4 倍行高。
        //
        // 竖向不能用高度判：一列本来就有 8 个字的长度，无论有没有第二列
        // 都远超两倍行高，这条会恒真。竖向下「第二行」变成了「第二列」，
        // 判别量换成宽度——两列比单列宽出「一列距 + 半个字」。
        //
        // 但短边装不下两列时（两列要 2 × 1.33 × 字号），面板会按设计
        // 丢掉译文列保主列，这时没有第二列才是对的，所以那几种组合跳过。
        foreach (var r in results.Where(x => x.IsTwoLine && !x.IsVertical))
        {
            Check(
                $"{r.Name}：双行模式画出了 2 行以上",
                r.InkBounds.Height >= r.LineHeight * 1.4,
                $"墨迹高度={r.InkBounds.Height:F1}px，单行高={r.LineHeight:F1}px");
        }

        foreach (var r in results.Where(x => x.IsTwoLine && x.IsVertical && !x.IsPureMusic))
        {
            var twoColumnPitch = r.FontSize * LyricsPanel.VerticalColumnPitchFactor * 2;

            if (twoColumnPitch > r.ViewportWidth)
            {
                // 短边放不下两列：设计上就该丢译文列，不参与断言。
                continue;
            }

            Check(
                $"{r.Name}：竖向双行画出了第二列",
                r.InkBounds.Width >= r.FontSize * 1.5,
                $"墨迹宽={r.InkBounds.Width:F0}px，字号={r.FontSize:F0}px");
        }

        // 3. 竖向必须成列（不是躺倒、也不是被压成一条横杠）
        //
        // 逐字竖排下，一句 8 个字的歌词在屏幕上是一条 13 宽 × 138 高的细长条，
        // 宽高比 1:10。所以「纵向远大于横向」这条判据在这个方案里第一次变得
        // 可靠——旧方案（抵消旋转 + 折行）的包围盒是 52×33 的近方形，宽高比
        // 没有判别力，那时只能退而求其次去比「内容宽度 vs 面板宽度」。
        // 方案换了，判据就该换回来。
        //
        // 宽度判据保留：列数再多也不能超出短边，否则第二列被 ClipToBounds 切掉。
        //
        // 纯音乐态排除：面板不可见、无文字，方向判断没有意义。
        foreach (var r in results.Where(x => x.IsVertical && x.IsWrapped && !x.IsPureMusic))
        {
            Check(
                $"{r.Name}：竖排各列都在短边内（没被裁）",
                r.InkBounds.Width <= r.ViewportWidth + 1,
                $"墨迹宽={r.InkBounds.Width:F0}px，短边={r.ViewportWidth:F0}px");

            Check(
                $"{r.Name}：竖向成列（墨迹纵向远大于横向）",
                r.InkBounds.Height >= r.InkBounds.Width * 2,
                $"包围盒={r.InkBounds.Width:F0}×{r.InkBounds.Height:F0}" +
                $"（左上=({r.InkBounds.X:F0},{r.InkBounds.Y:F0})，" +
                $"画布={r.Bitmap.PixelWidth}×{r.Bitmap.PixelHeight}）");
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
        // 判据不能用「墨迹不贴边」：竖排的画布长度按定义等于内容长度，
        // 文字必然贴住两端，贴边≠裁切。第一版就是这么写的，42 张全红，
        // 红的是判据不是渲染。改用布局数值（面板要的长度 vs 分到的长度）——
        // 这也是生产里真正比较的那两个量。
        //
        // 长句豁免：竖排没有滚动，一列到底的句子一旦超过上限就只能被裁。
        // 这是这一版明确的取舍（逐字滚动排在后一轮），不是缺陷。所以长句
        // 不参与断言，只把「裁掉多少」打出来做量化记录——将来接上滚动时，
        // 这行数字就是验收基线。
        // 长句豁免只对**竖向**成立：横向有滚动，长句再多也会一行行推过去，
        // 被裁就是真缺陷。竖向没有滚动，超限只能裁——那是取舍。
        foreach (var r in edgeResults.Where(x => !(x.IsLongLine && x.IsVertical)))
        {
            Check(
                $"{r.Name}：内容没被长度上限裁掉",
                !r.ContentClipped,
                $"短边={r.CrossSize:F0}，字号={r.FontSize:F1}，" +
                $"画布={r.Bitmap.PixelWidth}×{r.Bitmap.PixelHeight}，" +
                $"墨迹高={r.InkBounds.Height:F0}px");
        }

        foreach (var r in edgeResults.Where(x => x.IsLongLine && x.IsVertical))
        {
            Console.WriteLine(
                $"  [INFO] {r.Name}：长句超限属已知取舍 " +
                $"（短边={r.CrossSize:F0}，字号={r.FontSize:F1}，" +
                $"被裁={r.ContentClipped}，墨迹高={r.InkBounds.Height:F0}px）");
        }

        // 6. 边界矩阵第二条：竖排是**逐字排**，每列一个字宽。
        //
        // 这条替换掉了折行时代的「每行至少两个汉字」。旧判据在当时是对的
        // ——28 DIP 窄栏里 13px 字折行只剩一个字，确实是缺陷。但逐字竖排的
        // **定义**就是每列一个字：句子变长时增长的是主轴长度，不是列宽。
        // 沿用旧判据只会得到 19 张「故意失败」的红图（一列 13px < 1.7×13）。
        //
        // 新判据守的是另一条底线——字不能挤成一团、列不能探出短边：
        //   下界 0.75 字号：一列里至少站得下一个完整字
        //   上界 短边     ：第二列一旦溢出，就会被 ClipToBounds 切掉
        //
        // 下界为什么是 0.75 而不是 0.9：
        //   汉字墨迹本来就比 em 方框小一圈——advance 是 1em，黑墨只占中间
        //   约 0.85em（CJK 字体两侧留边）。小字号下 ClearType 又把墨迹量化
        //   到整像素，比例连单调都不是：CI 机器上 13px 量到 12px 墨迹
        //   （0.92），14px 仍量到 12px（0.86）。0.9 的旧下界正好卡在 0.857
        //   这条线上，于是 v28-font20 一档 6 张图全红——而那几张的渲染是
        //   对的（截图里 8 个字竖成一列、笔画完整），错的只是阈值。
        //   0.75 仍抓得住真正的塌缩：被横向压扁或裁掉一半的字约 0.5em。
        foreach (var r in edgeResults.Where(x => x.IsVertical))
        {
            const double MinColumnRatio = 0.75;
            var minColumn = r.FontSize * MinColumnRatio;
            var ratio = r.FontSize > 0 ? r.InkBounds.Width / r.FontSize : 0;

            Check(
                $"{r.Name}：竖向每列一个字宽，且不探出短边",
                r.InkBounds.Width >= minColumn && r.InkBounds.Width <= r.ViewportWidth + 1,
                $"墨迹宽={r.InkBounds.Width:F0}px，字号={r.FontSize:F0}px" +
                $"（{ratio:P0}），短边={r.ViewportWidth:F0}px");
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
        bool IsWrapped = true,
        bool IsLongLine = false);

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

        // 中英混排：拉丁词必须**整词侧躺**——不拆字母、不逐字母反旋。
        //
        // 这条只有逻辑测试（MixedLineKeepsChineseUprightAndLatinWordsWhole）
        // 还不够：反旋漏挂会渲染成「每个字母正立堆叠」，逻辑层看不出来。
        // 竖排里拉丁词的处理方式最接近 CSS 的 text-orientation: mixed，一眼能认。
        //
        // 句子刻意短：60 宽的竖条里塞 30 多个字符，渲染出来就是一条
        // 60×220 的细线，别说验收，连字都数不清。混排要验的是**朝向**，
        // 一个词加几个字足够；也不能短到 4 格——那样包围盒接近正方形，
        // 「成列」判据自己就失去了判别力。
        new("mixed-latin",
            new LyricDisplayFrame(
                CurrentLine: "Rain 落下 夜色渐浓",
                NextLine: "",
                Title: "Mixed"),
            IsTwoLine: false),

        // 长句（横向会触发滚动；竖向一列到底，超过长度上限就被裁掉——
        // 竖排不做逐字滚动是这一版的已知取舍，见 IsLongLine 的注释）
        new("long-line",
            new LyricDisplayFrame(
                CurrentLine: "如果这场雨一直下到明天清晨我们是不是就能一直站在这里不分开",
                NextLine: "而你说好要陪我走完这条街",
                Title: "测试曲目",
                LineProgress: 0.3),
            IsTwoLine: true,
            IsLongLine: true),

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
            // ---------- 竖向：只剩一层旋转，朝向交给字形自己 ----------
            //
            // 现在的竖排是**逐字排**：面板不再挂 −90 反向旋转，方块字各自带
            // RenderTransform(−90) 转正，拉丁词整词保持侧躺沿列向下读
            // （CSS text-orientation: mixed 的做法）。所以这里只需复刻
            // TaskbarWindow.PositionWidget 的那一层 +90。
            //
            // 尺寸怎么定，用变换矩阵算清楚：
            //
            //   Widget 逻辑 (Ww × Hw) ── +90 ── 屏幕 (Hw × Ww)
            //
            //   要让屏幕上是「宽 = 任务栏宽度、高 = 主轴长度」的一条竖带，
            //   就得 Hw = crossSize、Ww = 主轴长度。
            //
            // Panel 与 Widget 同处一个坐标系（没有反向旋转了），于是：
            //   Panel.Width  = 主轴长度
            //   Panel.Height = 短边（任务栏宽度）
            //
            // 这与 TaskbarWidgetControl.CalculateSize 的写法一致。旧版探针
            // 是「Panel.Width = 短边、Height = 主轴」——那套语义随折行方案
            // 一起废除了，照抄旧值现在会把长度当宽度、排出一列被剪断的字。
            //
            // 顺序有讲究：短边必须先写。列数与列距都由它决定（两列要
            // 2.66 × 字号），反过来写的话读到的长度是上一轮的列数。
            panel.SetVerticalMode(true);
            panel.Height = crossSize;

            var contentLength = panel.VerticalContentLength;

            // 内容还没量出来（首帧、空行）时给最小可见长度，
            // 与 MeasureDesiredWidth 的兜底一致——报 0 会渲染出零宽画布。
            var mainAxis = contentLength > 0
                ? Math.Min(contentLength, TaskbarWidgetControl.MaxLyricsWidgetLength)
                : TaskbarFreeSpaceLayout.MinimumDisplayLength;

            panel.Width = mainAxis;

            // Widget 在自身坐标系里：宽 = 主轴长度，高 = 任务栏宽度。
            var widget = new Border
            {
                Background = new SolidColorBrush(background),
                Width = mainAxis,
                Height = crossSize,
                Child = panel,
                LayoutTransform = new RotateTransform(90),
            };

            // 旋转后视觉尺寸是 (任务栏宽度 × 主轴长度)。
            var canvas = new Canvas
            {
                Width = crossSize,
                Height = mainAxis,
                Background = new SolidColorBrush(background),
            };
            canvas.Children.Add(widget);
            Canvas.SetLeft(widget, 0);
            Canvas.SetTop(widget, 0);

            renderRoot = canvas;
            logicalWidth = crossSize;
            logicalHeight = mainAxis;
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

        // 布局稳定后再问一次面板要占多长，用来判定「被上限剪掉」。
        //
        // 竖排的长度是**排版输入**而不是排版结果（每格步进按实测字宽算），
        // 所以正常情况下读到的就是上面写进去的那个值。真变长了只有一种
        // 可能：字体在布局遍里被回退解析换了（缺字 → 别的字体），此时按
        // 新值放大画布，但仍然不突破 MaxLyricsWidgetLength——那一步正是
        // 要测的东西：一旦超限，被夹掉的那一截在屏幕上就是「句子没写完」。
        var contentClipped = false;

        if (isVertical && renderRoot is Canvas verticalCanvas)
        {
            var needed = panel.VerticalContentLength;
            var allocated = Math.Min(needed, TaskbarWidgetControl.MaxLyricsWidgetLength);

            contentClipped = needed > allocated + 0.5;

            if (allocated > panel.Width + 0.5)
            {
                panel.Width = allocated;
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
        // 竖向这条链有两层变换（Canvas → Widget(+90)），光看代码很难判断
        // 哪一层的尺寸没生效——直接量 ActualWidth/Height。这条输出在本轮
        // 定位到两个真实缺陷：
        //   1. primary 高 0（Canvas 的 MeasureOverride 恒返回 0，
        //      放开 Height 约束就塌成 0 高，文字被 ClipToBounds 全裁）
        //   2. root 高 > panel 高（估算只算主行，次要行溢出面板）
        // 保留它作为「下次再坏时能一眼看出坏在哪层」的护栏。
        //
        // 现在竖向渲染的是 VerticalContainer（RootGrid 已隐藏），
        // 所以要量的是它；columns 是字形块个数，正常情况下等于列数。
        if (isVertical)
        {
            var vertical = panel.FindName("VerticalContainer") as FrameworkElement;

            // 只在这一组 DPI/主题里打，否则诊断行会把断言结果淹掉。
            if (dpiScale == 1.0 && !isDark && variant is null)
            {
                var columns = vertical is Canvas verticalCanvas2 ? verticalCanvas2.Children.Count : 0;
                Console.WriteLine(
                    $"  [diag] {state.Name,-12} " +
                    $"panel={panel.ActualWidth,6:F1}x{panel.ActualHeight,5:F1} " +
                    $"needs={panel.VerticalContentLength,6:F1} " +
                    $"canvas={vertical?.ActualWidth,6:F1}x{vertical?.ActualHeight,5:F1} " +
                    $"columns={columns} " +
                    $"clip={contentClipped}");
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
            contentClipped: contentClipped,
            backgroundColor: background);
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
        bool contentClipped,
        Color backgroundColor)
    {
        var stride = bitmap.PixelWidth * 4;
        var pixels = new byte[stride * bitmap.PixelHeight];
        bitmap.CopyPixels(pixels, stride, 0);

        // 墨迹判据：与背景的**亮度差**超过阈值即视为文字。
        // 不能用绝对亮度——深色主题下文字是白的、背景是黑的，
        // 浅色主题正好相反。用「与背景色的差」两头都适用。
        //
        // 背景色用**探针自己写进画布的那个值**，不去采角落像素：
        // 竖排的字形块从 x=0 起排，一旦某个块溢到左上角，采到的基准
        // 就变成文字色，整张图会被判成「满是墨迹」——mixed-latin 就这样
        // 误报过一次（浅色宽 60、深色宽 13，同一份布局两种结论）。
        var bgLuminance = (0.299 * backgroundColor.R)
            + (0.587 * backgroundColor.G)
            + (0.114 * backgroundColor.B);

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

                // 完全透明的像素在画布之外，无所谓墨迹。
                if (pixels[offset + 3] == 0)
                {
                    continue;
                }

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
            IsLongLine = state.IsLongLine,
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

        // **必须反预乘**：目标格式是 Pbgra32，RGB 已经被 alpha 乘过。
        // 直接拿预乘值算亮度，一条 alpha≈0.6 的边界行会被压到 148
        // （0.6 × 0xF3），在浅色底上就成了「墨迹」——可它肉眼完全看不见。
        // mixed-latin 就这样误报过：浅色主题下墨迹横贯整宽 60px，
        // 深色主题同一份布局只有 13px，看着像渲染 bug，其实是判据 bug。
        // PNG 里那一行读出来是 241（编码器做过反预乘），对照像素时才露馅。
        var alpha = pixels[offset + 3];
        if (alpha == 0)
        {
            return 0;
        }

        var scale = 255.0 / alpha;
        var b = pixels[offset] * scale;
        var g = pixels[offset + 1] * scale;
        var r = pixels[offset + 2] * scale;

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

        /// <summary>
        /// 是否是「一列到底装不下」的长句。
        ///
        /// 竖排不做逐字滚动，长句超出主轴上限就只能被裁。这是本版已知取舍，
        /// 所以这类用例不参与「不许裁」的断言，改为一律打印裁切量。
        /// </summary>
        public required bool IsLongLine { get; init; }

        public required bool IsDark { get; init; }
        public required double LineHeight { get; init; }
        public required double DpiScale { get; init; }

        /// <summary>
        /// 跨列方向的可用宽度（**物理像素**），也就是任务栏短边。
        ///
        /// 竖排时字形沿列排、列沿短边铺开（最多两列），所以墨迹宽度不应
        /// 超过它——超了就是第二列被 ClipToBounds 切掉了。
        /// </summary>
        public required double ViewportWidth { get; init; }

        /// <summary>任务栏短边（DIP）。横向是高度，竖向是宽度。</summary>
        public required double CrossSize { get; init; }

        /// <summary>生效字号（DIP）。</summary>
        public required double FontSize { get; init; }

        /// <summary>
        /// 内容是否被「主轴长度上限」裁掉了一部分。
        ///
        /// 由布局数值判定（面板要的长度 &gt; 分到的长度），不看像素：
        /// 竖排时画布长度就是内容长度，墨迹必然贴住两端，像素判据区分不出
        /// 「刚好填满」和「被切掉一截」。
        /// </summary>
        public required bool ContentClipped { get; init; }
    }
}
