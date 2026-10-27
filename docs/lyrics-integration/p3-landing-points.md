# P3 落点清单：歌词渲染层插在哪、改什么

本文件是 P3 开工前的测绘结果。目的是把「歌词层要接进现有任务栏小组件」这件事
拆成一张**可以照着改的清单**，而不是边写边发现现有代码在踩自己的脚。

- 建立日期：2026-10-27
- 基线：`master` @ `9c594a9`（P2 已合并）
- 范围：`TaskbarWidgetControl`、`TaskbarWindow`、`TaskbarVisualizerControl`

---

## 1. 现状测绘

### 1.1 视觉树

```
TaskbarWindow (AllowsTransparency, Background=Transparent, ClearType/Ideal)
└── Canvas WidgetCanvas
    ├── TaskbarVisualizerControl  固定 84×40（小任务栏 84×28）
    └── TaskbarWidgetControl      宽度每次 UpdateUi 重算，高度 40 / 28
        └── Border MainBorder (CornerRadius=6, ClipToBounds, 圆角 Clip 由 SizeChanged 维护)
            └── Grid
                ├── Image BackgroundImage     封面模糊背景，BlurEffect 80，Opacity 0.4
                └── Border TopBorder (BorderThickness=0,1.25,0,0)
                    └── Grid MainGrid         ScaleTransform 0.9，HorizontalAlignment=Left
                        └── StackPanel MainStackPanel (Horizontal, Margin=4,0,-100,0)
                            ├── Border SongImageBorder   36×36 / 24×24，ZIndex=2
                            ├── StackPanel SongInfoStackPanel (Vertical, Margin=8,0,0,0)
                            │   ├── Canvas SongTitleContainer  Height=19, ClipToBounds
                            │   │   └── TextBlock SongTitle   TranslateTransform
                            │   └── Canvas SongArtistContainer Height=19, Margin=0,-1.5,0,0
                            │       └── TextBlock SongArtist  Opacity=0.5, TranslateTransform
                            └── StackPanel ControlsStackPanel  3 × 32×32 / 24×24 按钮
```

关键点：`MainStackPanel.Margin` 右侧是 **-100**，配合 `MainGrid.HorizontalAlignment=Left`
做「内容多宽就多宽、超出部分交给 `MainBorder.ClipToBounds` 裁掉」。也就是说
**视觉树本身不做宽度约束，宽度完全由 `CalculateSize` 算出来后再赋给 `Widget.Width`**。

### 1.2 度量常量

| 常量 | 值 | 位置 | 备注 |
|---|---|---|---|
| `DefaultTaskbarWidgetHeight` | 40 | `TaskbarWidgetControl.xaml.cs:26` | |
| `SmallTaskbarWidgetHeight` | 28 | `:27` | |
| `DefaultCoverImageSize` / `Small` | 36 / 24 | `:30-31` | |
| `DefaultCoverImageMargin` / `Small` | 55 / 43 | `:32-33` | **封面 + 左边距 + 文字起点偏移的合计** |
| `DefaultControlButtonSize` / `Small` | 32 / 24 | `:36-37` | |
| `_scale` | 0.9 | `:40` | 见下 |
| `_nativeWidgetsPadding` | 216 | `:41` | **硬编码的任务栏预算，P3-8 要替换** |
| `_extraMarginForText` | 6 | `:50` | 防文字被裁 |
| `SmallTaskbarDetectionThreshold` | 40 | `TaskbarWindow.xaml.cs:28` | 任务栏短边 < 40 视为小任务栏 |

**`_scale` 是两处叠加的，别被绕进去**：

1. `MainGrid` 上有 `ScaleTransform ScaleX=ScaleY=0.9`（XAML 第 30 行）；
2. `TaskbarWindow.PositionWidget` 里 `physicalWidth = (int)(logicalWidth * dpiScale * _scale)`
   （`:466`），同时 `Widget.Width = physicalWidth / dpiScale`（`:641`）。

于是 `CalculateSize` 返回的 `logicalWidth` 是**缩放前**的宽度，实际占屏宽度是
`logicalWidth * 0.9`。`maxLogicalWidth = 216 / 0.9 = 240` 正是把这个 0.9 反解回来，
让最终占屏宽度正好落在 216 DIP。**所以 216 是「占屏预算」，240 是「布局预算」，两者不能混用。**

### 1.3 调用链

```
MainWindow.UpdateTaskbar()                       MainWindow.xaml.cs:711
  └─ taskbarWindow.UpdateUi(title, artist, ...)  TaskbarWindow.xaml.cs:702
       ├─ Widget.UpdateUi(...)                   :767  → TaskbarWidgetControl.xaml.cs:504
       └─ Dispatcher.BeginInvoke(UpdatePosition) :770  ← 每次 UI 更新都整窗重排
            └─ PositionWidget                    :456
                 ├─ Widget.SetVerticalMode        :461
                 ├─ Widget.CalculateSize          :464
                 │    └─ UpdateMarquees           TaskbarWidgetControl.xaml.cs:334
                 ├─ SetWindowPos                  :439
                 ├─ UpdateWindowRegion            :446 / :238
                 └─ Canvas.SetLeft/Top + Widget.Width/Height  :639-642
```

`UpdatePosition` 由三处驱动：1500ms 的 `_timer`（`:59-61`）、每次 `UpdateUi`
（`:770`）、`WM_DPICHANGED`（`:79-86`）。每次都会走 `SetWindowPos` + UIA 查询 +
`SetWindowRgn`，**不是廉价操作**。

---

## 2. 落点清单

### 2.1 新增

| 文件 | 职责 |
|---|---|
| `FluentFlyoutWPF/Controls/LyricsPanel.xaml(.cs)` | 歌词渲染组件本体。**独立 UserControl**，不写进 `TaskbarWidgetControl.xaml`——后者已有 747 行，且 #13 要求能独立预览。 |
| `FluentFlyoutWPF/Lyrics/LyricsPanelMetrics.cs` | 度量计算（字号、行高、内边距、可见行数）。纯函数，可单测。参照 TL `LyricsLayoutMetrics.Create` 但砍掉浮动窗口相关字段。 |
| `FluentFlyoutWPF/Lyrics/LyricsTextScanner.cs` | 逐字扫描的画笔维护：持有 `LinearGradientBrush` 与其两个 `GradientStop`，`SetProgress(double)` 只改 `Offset`。**不重建对象**。 |
| `FluentFlyout.Lyrics/Timeline/TaskbarInterval.cs` | 区间结构（`Start`/`End`/`Length`），对应 TL `TaskbarFreeSpaceLayout.cs:3`。放 Core 里以便单测。 |
| `FluentFlyout.Lyrics/Timeline/TaskbarFreeSpaceLayout.cs` | 避让算法移植。纯函数 `TryPlace(...)`，TL 原文 145 行，逻辑与平台无关，**几乎可原样搬**。 |

### 2.2 改动（`TaskbarWidgetControl`）

| 位置 | 现状 | 改成 |
|---|---|---|
| `xaml:42-60` | `SongInfoStackPanel` 内含两行标题/艺术家 | 在 `SongImageBorder` 之后插入 `<controls:LyricsPanel x:Name="LyricsPanel" Visibility="Collapsed"/>`，与 `SongInfoStackPanel` **同级互斥** |
| `:117-135` `SetVerticalMode` | 无条件 `SongInfoStackPanel.Visibility = Collapsed` | 改为 `UpdateTextRegionVisibility()`：歌词开 → 只留 `LyricsPanel`；歌词关 → 走原逻辑 |
| `:137-155` `SetSmallTaskbarMode` | 只调封面/按钮尺寸 | 追加 `LyricsPanel.IsCompact = isSmallTaskbar`，驱动单行降级 |
| `:162-176` `ApplyWindowsTheme` | 只设标题/艺术家/按钮前景色 | 追加 `LyricsPanel.ApplyPalette(primary, secondary, translation, scanOverlay)`。**颜色必须来自 `WindowsThemeDetector`，不能用 App 主题**——任务栏配色跟系统走 |
| `:270-350` `CalculateSize` | 按标题/艺术家文本宽算 | 加歌词分支：宽度取 `max(当前行宽, 翻译行宽)`；签名改为 `CalculateSize(double dpiScale, double maxLogicalWidth)`，预算由调用方给（见 3.2） |
| `:352-361` `UpdateMarquees` | 驱动两条无限/乒乓滚动 | 歌词可见时**直接 return**，不碰标题动画（否则隐藏的 TextBlock 上仍跑着 `RepeatBehavior.Forever` 动画，白烧 CPU） |
| `:504-650` `UpdateUi` | 末尾无条件设置三处 `Visibility`（`:636-640`） | 改为调用 `UpdateTextRegionVisibility()`，不再直接写 `SongInfoStackPanel.Visibility` |
| `:668-710` `AnimateEntrance` | 只动 `SongInfoStackPanel` + `ControlsStackPanel` | 歌词态改为动 `LyricsPanel`（同一个 `Opacity` + `TranslateTransform.X` 从 -10 到 0） |
| 新增 `UpdateLyricsFrame(LyricDisplayFrame)` | — | **轻量入口**：只改文本、扫描进度、颜色。**不触发重排**。见 3.1 |

### 2.3 改动（`TaskbarWindow`）

| 位置 | 现状 | 改成 |
|---|---|---|
| `:31` `_nativeWidgetsPadding = 216` | 兜底偏移 + 宽度上限双重用途 | 宽度上限交给 `TaskbarFreeSpaceLayout`；兜底偏移保留但只作 UIA 失败时的 fallback |
| `:456-648` `PositionWidget` | 先算宽再定位，宽度上限是常量 | 两遍：① 算「理想宽度」→ ② `TryPlace` 求可用区间 → ③ 用区间长度重算 `maxLogicalWidth` 再 clamp。区间来源 = `WidgetsButton` / `SystemTrayIcon` / `TaskbarVisualizer` 三块（前两者已有 `GetTaskbarWidgetRect` / `GetSystemTrayRect`，`:886-899`） |
| `:702-776` `UpdateUi` | 每次末尾 `BeginInvoke(UpdatePosition)` | 增加节流：歌词模式下宽度变化才重排，且最短间隔 250ms（见 3.1） |

### 2.4 不需要改

- `TaskbarVisualizerControl`：纯音乐态直接复用，**但要注意它是独立的 84 宽控件**，
  由 `TaskbarWindow.PositionVisualizer` 单独定位，不能塞进 `LyricsPanel` 里。
- `MainWindow.GetActiveMediaSession()`：P2 已决定复用，不复制。
- `MonitorUtil` / 多屏选择 / DPI 钩子：`TaskbarWindow` 已处理，歌词层继承即可。

---

## 3. 三个必须提前定的架构点

### 3.1 宽度变化必须节流，否则 30fps 会把窗口系统打爆

这是本次测绘**最重要的发现**。

`TaskbarWindow.UpdateUi` 末尾（`:770`）无条件 `BeginInvoke(UpdatePosition)`。如果歌词帧
也走 `UpdateUi`，那就是 **30 次/秒的 `SetWindowPos` + UIA 跨进程查询 + `SetWindowRgn`**。
UIA 查询还带 `TaskLock` 与 1000ms 超时（`GetTaskbarXamlElementRect`，`:783-818`），
实测在开着 Nilesoft Shell / Windhawk 的机器上会直接卡住任务栏——`TaskbarWindow`
自己在 `WindowProc` 里屏蔽 `WM_GETOBJECT`（`:96-103`）就是被这类 mod 坑过。

**定论**：

- 歌词帧**不走** `UpdateUi`，走独立的 `Widget.UpdateLyricsFrame(frame)`；
- 帧内只改 `TextBlock.Text` / 扫描画笔 `Offset` / 少量 `Foreground`，**一律不触发 `Measure`**；
- 只有「换行」且「新行需要的宽度与当前宽度差 > 4 DIP」时才请求重排，且节流 250ms；
- 换行瞬间允许一次重排（用户能感知到的那一下），中间的长句滚动/逐字扫描不重排。

对应地，`LyricFrameDeduplicator` 已把离散信号与连续信号分开（`HasDiscreteChange`），
正好可以用「离散信号变了 → 可能需要重排；只有连续信号变 → 绝不重排」来分流，不需要再写一套判定。

### 3.2 宽度预算要改成参数，不能读常量

`CalculateSize` 现在直接读 `_nativeWidgetsPadding`。P3-8/P3-9 要把它换成实测区间，
但**区间依赖宽度、宽度又依赖区间**。解法是两遍 + 单向 clamp：

```
desired = MeasureDesiredWidth()                   // 不受上限约束
budget  = TaskbarWidthBudget.Resolve(desired, fallback, taskbarLen, preferredStart, occupied)
final   = Math.Min(desired, budget.Width)         // 只缩不放
```

因为 clamp 只会让宽度变小，第二遍不需要重新求 placement。`Resolve` 返回
`NoUsableGap` 时（可用区间 < `MinimumDisplayLength = 100`）退回当前常量行为，保证不劣化。

**单位是这一节最容易错的地方，实测踩到过一次。** 三个坐标系同时存在：

| 量 | 单位 | 换算 |
|---|---|---|
| `_nativeWidgetsPadding = 216` | 视觉 DIP | 恒为 216，不随 DPI 变 |
| `CalculateSize` 的返回值 / `maxLogicalWidth` | 布局 DIP | `视觉DIP / _scale`（0.9） |
| `Widget.Width` 最终占屏 | 物理像素 | `布局DIP × dpiScale × _scale` |

也就是说 **216 从来不是物理像素预算**。UIA 与 `GetWindowRect` 返回的是物理坐标，
若直接把物理像素当成 `availableWidth` 传进 `CalculateSize`，150% DPI 下会凭空多出
1.5 倍宽度。正确做法：`TaskbarWidthBudget` 的输入输出**全部用物理像素**（与 UIA 同域），
只在进出这个纯函数时各换算一次，`TaskbarWindow` 之外一律视觉 DIP。

`Resolve` 的四种结果（`Placed` / `OccupancyUnknown` / `NoUsableGap` / `InvalidInput`）
里，`OccupancyUnknown` 是最关键的一条：**UIA 失败 ≠ 任务栏全空**。任务栏刷新、
Explorer 重启、第三方任务栏工具介入时 UIA 都会返回失败，把它当成「全空」
就会把歌词画到托盘图标上。

### 3.3 逐字扫描的降级判据是数据驱动的，不是能力驱动的

计划里 P3-4 写的是「不支持时退回整行高亮」，容易理解成「检测 WPF 支不支持
`LinearGradientBrush` 遮罩」。查了 Core 实现后可以给出更准的判据：

`LyricSyncService.CalculateWordScanProgress`（`:389` 起）在**没有可用音节数据时直接返回 `null`**：

```csharp
if (!hasUsableSyllable) { return null; }
```

所以规则天然是：

| `WordScanProgress` | 渲染方式 |
|---|---|
| 有值 | 逐字扫描：底层文字用次级色，覆盖层用主色，`OpacityMask` 渐变按进度扫过 |
| `null` | 整行高亮：整行直接用主色（等价于 `LineProgress` 驱动也行，但没必要） |

WPF 侧的 `LinearGradientBrush` 永远可用，**不存在「平台不支持」的分支**。真正要保的
降级是**性能降级**：如果实测单帧 > 8ms，退到硬边 `Clip`（`RectangleGeometry`），
再不行退到整行高亮。这个降级用一个 `ScanMode { Gradient, Clip, WholeLine }` 枚举控制，
默认 `Gradient`，可被设置项或自适应阈值下调。

---

## 4. 竖向任务栏：两轮实测，最终「反向旋转抵消 + 换行」

`SetVerticalMode` 对文字的处理是**直接藏掉**（`:120`），封面和按钮靠
`RenderTransform(-90)` 抵消 `Widget` 的 `LayoutTransform = RotateTransform(90)`
（`TaskbarWindow.xaml.cs:516`）保持正立。也就是说：**FF 今天在竖向任务栏上根本不显示文字**。

歌词必须显示，所以这是行为变更，不是叠加。

这一节记录了**两轮实测、两次推翻**的过程。保留错误推导的原因是：
它们看起来都对，而错的恰恰是「看起来对」的那部分。

### 4.1 第一轮：推翻「panel 加 −90 就能正立」

初版文档给了一个推导：`Widget` 转 +90 让容器贴合竖向任务栏，`LyricsPanel`
再转 −90 抵消，于是 `R(90)·R(−90) = R(0)`，字形正立。还配了方向映射表。

探针（`FluentFlyout.Lyrics.Probe.Render`）把这条变换链搭出来实测，画布 60×320
（竖向任务栏的视觉尺寸）：

| 方案 | 墨迹 | 包围盒 | 宽高比 | 裁剪 | 结论 |
|---|---|---|---|---|---|
| A 基准（横向，不旋转） | 276px | 50×26 | **1.92** | — | 参照：横排文字的宽高比 |
| B 只转 Widget +90 | 487px | 26×104 | 0.25 | 无 | 字形躺倒——这是要修的问题 |
| **C Widget+90，panel `LayoutTransform(-90)` 且尺寸硬编码** | **0px** | — | — | **是** | **内容完全消失** |
| D Widget+90，panel `RenderTransform(-90)` | 64px | 10×26 | 0.38 | 是 | 大部分被裁 |
| E Widget+90，panel `LayoutTransform(-90)` 且尺寸 `Auto` | 211px | 40×26 | 1.54 | 无 | 字形正立但**只显示 3-4 个字** |
| F Widget `-90`，panel 不转 | 487px | 26×104 | 0.25 | 无 | 仍然躺倒 |
| G 不旋转，容器做成竖条 | 268px | 51×26 | **1.96** | 无 | 字形正立，但同样只显示 3-4 个字 |
| **H 不旋转 + `TextWrapping=Wrap`** | **541px** | 51×75 | 0.68 | 无 | **完整显示，正立可读** |

C 方案为什么内容消失：不是旋转方向错了，而是嵌套 `LayoutTransform` 时
父级仍按子元素的**未旋转**尺寸给它排版。panel 硬编码 `Width=185`，旋转 −90 后
向父级上报 `(40, 185)`，但 `Widget` 内部只有 `(240, 40)`，于是 panel 被排到
`x = −135`——整个跑到画布外。探针的 `panel=185,40 @ -135,20` 就是这个证据。

第一轮据此得出的结论是「**不旋转，改用换行**」（H 方案），并引
`TaskbarLyrics` 佐证：全仓库没有任何 `RotateTransform`，竖向时只是
`if (metrics.IsVertical) width = taskbarWidth;`（`EmbeddedTaskbarAnchor.cs:401`）。

### 4.2 第二轮：H 方案的前提在真机上不存在

第一轮的 G/H 方案有一个隐含前提：**`Widget` 本身不旋转**。
但 `TaskbarWindow.PositionWidget` 对竖向是**无条件**挂
`Widget.LayoutTransform = RotateTransform(90)` 的（`:516`，没有任何条件分支）。
也就是说 H 方案在真机上根本跑不到——实际组合是「Widget 转了 +90，panel 不转」。

补测这个真实场景（I/J 两个方案），判据分两项：

- **内容完整性**用墨迹量（多行文本的包围盒天然接近正方形，宽高比在这里没有判别力——
  第一轮已经在 ABCD 上连踩两次）；
- **字形朝向**用单个宽扁字符 `—`（em dash），它的包围盒方向不受换行影响：
  正立是横线（宽高比 ≈ 20），躺倒变竖线（≈ 0.05）。

| 方案 | 墨迹 | 与完整显示(541px)之比 | 单字符 `—` 包围盒 | 结论 |
|---|---|---|---|---|
| **I Widget+90，panel 不旋转 + 换行** | 182px | **34%** | 1×20（宽高比 **0.05**，竖线） | 文字躺倒且被裁掉三分之二 |
| **J Widget+90，panel `LayoutTransform(-90)` + 换行** | **553px** | **102%** | 20×1（宽高比 **20.0**，横线） | **完整显示，正立** |

渲染图（`docs/lyrics-integration/probe/I-*.png` / `J-*.png`）目视确认：
I 的图里文字整体转了 90°、只剩顶部两三个字；J 的图里文字正常横排、
完整可读。

### 4.3 最终结论：反向旋转抵消 + 换行

竖向的正确做法是 **panel 挂 `LayoutTransform(-90)` 抵消宿主旋转，再开
`TextWrapping.Wrap`**。两个条件缺一不可：

- 只抵消不换行 → E 方案，字形正立但一行只放得下 3-4 个字；
- 只换行不抵消 → I 方案，换行发生在「旋转后的坐标系」里，
  文字躺倒且被窄边裁掉。

与第一轮 C 方案（panel 挂 −90 但尺寸硬编码 185）的关键区别在**尺寸**：
C 之所以墨迹 0px，是硬编码宽度超出父级可用空间后被排到画布外。
J 的 panel 宽度由 Grid 布局给出（不硬编码），旋转后父级拿到真实尺寸，不会溢出。

顺带的好处：抵消之后坐标系回到「屏幕坐标」，扫描渐变（沿 `PrimaryContainer`
宽度方向）与长句滚动都天然正确，不需要为竖向单独调方向。

### 4.4 保留的陷阱：`RenderTransform` 不参与布局

封面和按钮的反向旋转用的是 **`RenderTransform`**（`:135`、`:140` 附近）：

```csharp
button.RenderTransform = (Transform?)counterRotate ?? Transform.Identity;
```

`RenderTransform` **不参与布局**——元素在父级看来仍是未旋转的尺寸，只是绘制时转一下。
对 36×36 的封面和 32×32 的按钮没问题，因为正方形旋转后包围盒不变。

**但任何非正方形的元素都不能这么做。** 探针的 D 方案就是反例：
panel 用 `RenderTransform(-90)` 后，父级仍按 `185×40` 给它排版，
实际画出来是躺倒的 40×185，直接叠到画布外（墨迹 64px）。
所以 `LyricsPanel` 用的是 `LayoutTransform`，不是 `RenderTransform`。

### 4.5 关于 `SupportsVerticalTaskbar` 开关

默认已改为 `true`。保留开关的唯一价值是「真机上万一有问题可以一键退回
不显示歌词」（行为与改动前一致）。竖向路径的验收基线是探针的 J 方案。

### 4.6 度量：竖向不受短边约束

小任务栏（28 DIP）在横向必须压成单行——两行 13px 需要约 40 DIP，装不下。
但**竖向不能照搬这个判断**：竖向的物理约束是宽度窄，纵向延伸是整条屏幕高度，
两行永远装得下；压成单行等于把换行这个唯一优势扔掉，屏幕上只剩三四个字。

这条决策抽到了 Core 的 `LyricsMetricsPolicy.Create(isVertical, isSmallTaskbar, ...)`，
用 2×2 矩阵单测锁死（`LyricsMetricsPolicyTests`）：

| 方向 | 小任务栏 | 行数 | 说明 |
|---|---|---|---|
| 横向 | 否 | 2 | 标准 |
| 横向 | 是 | 1 | 28 DIP 装不下两行 |
| 竖向 | 否 | 2 | 标准 |
| **竖向** | **是** | **2** | **不受短边约束** |

实现上竖向传 `availableHeight = 4096`（`VerticalUnconstrainedHeight`）而不是
任务栏短边。不能传 `double.PositiveInfinity`：`LyricsPanelMetrics.Create` 的
合法性检查会把无穷当成脏数据回落成 40，效果与不传一样。

### 4.7 主轴长度估算

竖向下「想要多宽」这个问题的答案是「想要多长」：一句歌词必然折成多行，
需要的主轴长度 = 行数 × 行高。估算在 `LyricsVerticalLayout.EstimateMainAxisLength`：

```
rows = ceil(contentWidth / (crossSize - WrapSafetyMargin))
length = min(rows * lineHeight, maxLength)
```

三个容易写错的点，都在单测里锁了：

1. **向上取整**。差 0.1 像素也要算一行，否则最后几个字被切掉。
2. **不夹 `MinimumLength`**。一句短歌词折成两行约 30 DIP，硬夹到 64 会让小组件
   白占空间、主轴上留一条空白。下限只用于「输入不可用」的兜底路径。
3. **先夹 `rows` 再乘**。用户拖来一句 200 字的歌词时 `rows * lineHeight`
   会溢出成 Infinity。

## 5. 小任务栏（28px）必须单行

两行 13px 需要约 40px（TL 公式：`13 * 2.15 + 12 = 39.95`），28px 装不下。
`SmallTaskbarDetectionThreshold = 40` 已经把小任务栏识别出来了，歌词层照抄这个判断：

- 28px：**只显示当前行**，字号 `clamp(availableHeight * 0.62, 10, 14)` → 28×0.62 = 17.4 → 夹到 14，
  行高 `14 * 1.15 = 16.1 ≤ 28` ✓；
- 翻译行在单行模式下**丢弃**（TL 的 `.single-line` 也是这个语义）；
- 逐字扫描保留（单行更需要它来指示进度）。

---

## 6. 风险登记

| # | 风险 | 严重度 | 落点 | 验证方式 |
|---|---|---|---|---|
| R1 | 歌词帧触发整窗重排，30fps 下卡死任务栏 | **高** | 3.1 | 加计数器统计 `UpdatePosition` 调用次数，播放 60s 应 < 20 次；同时开 Nilesoft Shell 实测 |
| R2 | 逐字扫描每帧重建 Brush 导致掉帧 | **高** | 2.1 `LyricsTextScanner` | `CompositionTarget.Rendering` 打点，目标单帧 < 8ms |
| R3 | 宽度与系统托盘图标重叠（开「显示所有托盘图标」） | **高** | 3.2 / P3-9 | 手动把托盘图标铺满，检查 `TryPlace` 是否把 widget 挤到可用区间内 |
| R4 | 竖向任务栏文字方向错乱 / 误用 `RenderTransform` 导致叠字 | 中 | 4.1 / 4.2 | 四方向任务栏逐一截图，先验符号方向；确认歌词用 `LayoutTransform` |
| R5 | 小任务栏两行溢出被裁 | 中 | 5 | 切到 28px 任务栏，确认只剩一行且不截断 |
| R6 | 歌词与频谱同时显示抢空间 | 中 | 2.4 | 定互斥规则：歌词模式优先，纯音乐态才切频谱 |
| R7 | 隐藏的 `SongTitle` 上仍有 Forever 动画在跑 | 低 | 2.2 `UpdateMarquees` | 歌词态下断点确认 `TranslateTransform.X` 无动画时钟 |
| R8 | `LayoutTransform` 每帧重设导致 measure 抖动 | 中 | 4.2 | 仅在 `Orientation` 变化时设置，播放中改断点确认不进 measure |

---

## 7. 建议的实施顺序

与任务 #8–#13 对应：

1. **#8** `LyricsPanel` + `LyricsPanelMetrics` + 双行/翻译行 → 静态渲染，mock 数据驱动；
2. **#9** `LyricsTextScanner` → 逐字扫描 + 三级降级；
3. **#10** 长句滚动 + 纯音乐态切频谱；
4. **#11** 移植 `TaskbarFreeSpaceLayout` + 改 `CalculateSize` 签名（**先写 Core 单测再改 WPF**）；
5. **#12** 竖向 + 小任务栏 + 混合 DPI；
6. **#13** 独立预览窗口 + 截图矩阵 + PR。

**#11 的 Core 部分（区间结构 + `TryPlace`）可以立刻开写，完全不依赖 WPF**，
适合与 #8/#9 并行——这是 P3 阶段唯一真正能并行的切口。

### 7.1 已完成情况（2026-10-27）

| # | 状态 | 提交 |
|---|---|---|
| #8 | 完成 | `35660ea` |
| #9 | 完成 | `1eaf4ae` |
| #10 | 完成 | `0d408aa` |
| #11 | 完成 | `61ec39f` |
| #12 | 完成 | `b13fa0c`（竖向初版）+ 本轮修正 |
| #13 | 待办 | — |

### 7.2 竖向几何探针

`FluentFlyout.Lyrics.Probe.Render` 是一个离屏渲染探针，用来**实证**竖向任务栏的
排版方案，而不是靠推导。它把与 `TaskbarWindow.PositionWidget` 同构的变换链
搭出来，用 `RenderTargetBitmap` 渲染，再按像素统计墨迹包围盒。

```bash
dotnet run --project FluentFlyout.Lyrics.Probe.Render/Probe.Render.csproj
```

- 退出码 0 = 全部断言通过，可直接作为 CI 门禁；
- PNG 输出到 `bin/.../out/`，已归档的对照图在 `docs/lyrics-integration/probe/`；
- 共 10 个方案、13 条断言。

**探针本身被推翻过两次，这个教训比结论更值得记：**

1. **第一轮推翻了「加 −90 就正立」的推导**（C 方案墨迹 0px）。
   教训：几何推理在布局系统里不可靠，必须渲染出来看。
2. **第二轮推翻了第一轮自己的 H 方案**。H 的前提是「Widget 不旋转」，
   而真机上 `PositionWidget` 对竖向是**无条件**旋转的。
   教训：探针的**变换链必须与生产代码逐行对齐**，少一层就得出反向结论。

现在有两类回归护栏：

- **C 方案必须保持「内容消失」**——若突然有内容了，说明 WPF 布局行为变了，
  4.1 节的结论需要重新验证；
- **I 方案必须保持「躺倒」、J 方案必须保持「正立」**——若哪天 I 变正立了，
  说明有人改了 `PositionWidget` 的竖向分支，4.3 节的结论需要重算。

断言里还有一条值得单说：**判「是否躺倒」不能用多行文本的宽高比**。
窄容器里多行横排的包围盒天然接近正方形，宽高比失去判别力（第一轮在 ABCD 上
连踩两次）。改用单个宽扁字符 `—`：正立是横线（宽高比 ≈ 20），
躺倒变竖线（≈ 0.05），判别力足够且与字体无关。


