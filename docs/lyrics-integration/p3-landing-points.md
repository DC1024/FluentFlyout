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
- ~~只有「换行」且「新行需要的宽度与当前宽度差 > 4 DIP」时才请求重排，且节流 250ms~~；
- 换行瞬间允许一次重排（用户能感知到的那一下），中间的长句滚动/逐字扫描不重排。

> **2026-10-28 修订：宽度改为「整首歌内固定」**
>
> 上面那条「换行时按新行宽度重排」在真机上被否掉了，报了两条现象：
> ① 胶囊宽度随每一句胀缩；② 长句刚出现、正在滚动时，约 1 秒后胶囊才变宽，
> 那次滚动白跑一趟。
>
> 根因是宽度跟着**当前行**走，而重排又挂在 `TaskbarWindow` 的 1.5s 定时器上
> （`_timer.Interval = 1500`）。两条叠加：既抖，又慢一拍。
>
> 现在的做法：
>
> - 换歌时 `LyricsService.DocumentChanged` 把整份文档送到渲染层，
>   `LyricsPanel.ApplyDocument` 用 `LyricsDocumentWidth` 一次算出**全曲最宽一句**；
> - `DesiredContentWidth` 直接返回它，于是同一首歌里宿主拿到的宽度恒定，
>   换行不再触发任何重排；
> - `DesiredWidthChanged` 接到 `WidgetWidthStrategyChanged` 后**立刻**重排，
>   不再等 1.5s 定时器——宽度真的变了（换歌 / 改字号 / 改对齐 / 开关翻译）就马上生效；
> - 超过上限（横向 480）的长句照旧滚动，但不会再有「滚到一半变宽」。
>
> 竖向没有改：竖向不滚动，宽度被任务栏钉死，能协商的是主轴长度，
> 而「整首最宽」在竖向下要按每句折行分别估算，成本与收益不成比例。


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

注意这条只管**行数与字号**。逐字竖排下「两行」落到屏幕上是**两列**，
而列数另有硬约束：两列要 `2 × 1.33 × 字号`（字号 13 时 34.6 DIP），短边装不下
就丢掉译文列、保住主列（见 `LyricsPanel.BuildVerticalTree`）。所以 28 DIP 竖向
确实只剩一列——但字号不会被压到 10，这跟横向「压成单行」是两回事。

### 4.7 主轴长度：由面板实测上报，不做除法估算

> **这一节在逐字竖排方案里被整体推翻过一次。** 原文（「折行后行数 × 行高」的估算）
> 留在 git 历史里，这里只记结论与理由。

结论先说：**竖向长度不是估出来的，是排出来的。**

旧方案里竖排是「抵消旋转 + 折行」：面板宽 = 短边，一句歌词折成若干行，
主轴长度 = 行数 × 行高，而行数能用 `ceil(文本宽 / 可用宽)` 推出来，所以估算可行。
但它绕不开一个容量问题：37.5 DIP 的可用宽度里，13px 汉字一行只站得下 **2 个**——
28 个字的句子要 14 行、560 DIP，比半个任务栏还长。

现在的竖排是**逐字排**：一列沿主轴排下去，每格一个字，拉丁词整词侧躺占一格。于是：

- 主轴长度 = Σ(每格实测步进)，是**排版的直接结果**，中间没有「行数」这个量；
- 每格步进由字体决定（汉字 1em、空格 0.33em、标点各有宽度、拉丁词是整词宽度），
  宽度除法推不出来——一律按字号算会让空格变成大豁口、让拉丁词尾部被裁；
- 一列能装 44 个汉字（578 ÷ 13），整句几乎总能一列到底，不再需要折行。

所以 `LyricsPanel.VerticalContentLength` 直接返回**建树时累加的那个数**，
`TaskbarWidgetControl` 拿它当主轴长度用：

```csharp
// TaskbarWidgetControl.MeasureDesiredWidth，竖向分支
ApplyVerticalLyricsPanelCross(ResolveVerticalCrossSize(crossSize));  // 先写短边
var contentLength = LyricsPanel.VerticalContentLength;               // 再读长度
if (contentLength <= 0)
{
    contentLength = TaskbarFreeSpaceLayout.MinimumDisplayLength;     // 首帧兜底
}
return Math.Min((contentLength + MeasureVerticalLeadIn()) * _scale, maxMainAxisLength);
```

顺序不能反：**列数与列距都由短边决定**（两列要 2.66 × 字号）。短边还没写进去就去读长度，
读到的是上一轮的列数——症状是「字号改大之后，歌词还是按旧字号排了一次」。

`LyricsVerticalLayout.EstimateMainAxisLength` 因此退出了竖向生产路径，目前只剩单测在用。
保留它的理由有两条：`ResolveMaxMainAxisLength` 仍在按任务栏实际长度推上限；
将来接「逐字滚动」时还需要一个「至少多长」的下界。

#### 4.7.1 禁则与折行偏差（已随折行方案一起退场）

旧方案为了避免「末行被裁」，专门讨论过中文禁则让实际折行多出约一行的偏差，
并定下「估算给下限、实测兜上限」的分工。逐字竖排不再折行——一列到底，
行首禁则无从谈起——这一整段随之失效。

唯一继承下来的是**上限**那一步：主轴长度超过任务栏能给的预算时，
超出的字形会被 `ClipToBounds` 吃掉，屏幕上表现为「句子没写完」。竖排不做
逐字滚动是这一版的已知取舍，截图矩阵把这类长句标成 `IsLongLine`、只记录
裁切量而不判失败，等接上滚动再收紧。

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
| R1 | 歌词帧触发整窗重排，30fps 下卡死任务栏 | **高** | 3.1 | **已建护栏**（见 7.2.9）：`--reposition` 探针模拟 60s 播放数请求数 + 常驻计数器 `RepositionBudget`；竖向换行重排已消除（20 → 0）。**事件驱动这一半已收口**；剩余的恒定基线（1.5s 心跳 40 次/分）见 8.1，真机确认见 8.2 |
| R2 | 逐字扫描每帧重建 Brush 导致掉帧 | **高** | 2.1 `LyricsTextScanner` | `CompositionTarget.Rendering` 打点，目标单帧 < 8ms |
| R3 | 宽度与系统托盘图标重叠（开「显示所有托盘图标」） | **高** | 3.2 / P3-9 | 手动把托盘图标铺满，检查 `TryPlace` 是否把 widget 挤到可用区间内 |
| R4 | 竖向任务栏文字方向错乱 / 误用 `RenderTransform` 导致叠字 | 中 | 4.1 / 4.2 | 四方向任务栏逐一截图，先验符号方向；确认歌词用 `LayoutTransform` |
| R5 | 小任务栏两行溢出被裁 | 中 | 5 | 切到 28px 任务栏，确认只剩一行且不截断 |
| R6 | 歌词与频谱同时显示抢空间 | 中 | 2.4 | **已改为共存**：频谱固定 84 DIP、歌词块按内容收缩，2560 任务栏上合计约 22%；`GetOccupiedIntervals` 把频谱区间列为占用区，两块互相避让，越界再由 `LyricsMainAxisLimit.CapToPreferredHalf` 夹住 |
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
| #12 | 完成 | `b13fa0c`（竖向初版）+ `086faef`（二次实测修正） |
| #13 | 进行中 | 截图矩阵已建好（72 张全绿），待归档与 PR |

### 7.2 探针：三种用途，不要混用

P3 阶段有三个探针入口，**判据完全不同，混在一个退出码里会互相掩盖**：

| 入口 | 用途 | 输入 | 断言数 |
|---|---|---|---|
| `Probe.Render` | **几何方案选型**：验证「竖向到底该不该旋转」 | 手工搭的近似视觉树 | 13 |
| `Probe.Screenshot` | **视觉验收**：渲染真实 `LyricsPanel`，出可归档的图 | mock 帧 + 真实控件 | 72 张 + 8 张整机（含长度恒定、左右镜像） |
| `Probe.Screenshot --reposition` | **调用次数验收**（R1）：60s 播放请求了多少次整窗重排 | mock 帧 1800 张，不渲染 | 4 |

```bash
dotnet run --project FluentFlyout.Lyrics.Probe.Render/Probe.Render.csproj
dotnet run --project FluentFlyout.Lyrics.Probe.Screenshot/Probe.Screenshot.csproj
dotnet run --project FluentFlyout.Lyrics.Probe.Screenshot/Probe.Screenshot.csproj -- --assembly
dotnet run --project FluentFlyout.Lyrics.Probe.Screenshot/Probe.Screenshot.csproj -- --reposition
```

退出码 0 = 全部断言通过，可直接作为 CI 门禁（`verify` 里是三独立步骤）。

#### 7.2.1 `Probe.Render`：几何选型的两次推翻

- 第一轮推翻「加 −90 就正立」的推导（C 方案墨迹 0px）。
  教训：几何推理在布局系统里不可靠，必须渲染出来看。
- 第二轮推翻第一轮自己的 H 方案。H 的前提是「Widget 不旋转」，
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

#### 7.2.2 `Probe.Screenshot`：为什么必须用 mock 帧

**P3 结束时歌词层还没有接线**：`TaskbarWidgetControl.SetLyricsMode` 与
`UpdateLyricsFrame` 在生产代码里没有任何调用点（`grep` 只命中定义与注释），
`LyricFramePump` 同样无人引用。真正的装配在 P4 的 `LyricsService`。

所以「真机播放截图验收」这条路现在跑不通。截图矩阵必须用 mock 帧驱动——
这正是 `Probe.Screenshot` 的存在理由：它把面板当成普通控件，喂帧、设度量、
渲染成 PNG，产物可直接贴进 PR。

矩阵规模：3 DPI（100/150/200%）× 2 主题（浅/深）× 2 方向 × 6 状态 = **72 张**。
状态覆盖单行、双行、翻译行、逐字扫描、长句、纯音乐。

竖向渲染必须**复刻生产的两层变换**，否则测的不是生产行为：

```csharp
// Panel 逻辑 (Wp × Hp) ── −90 ── Widget 内部 (Hp × Wp) ── +90 ── 屏幕 (Wp × Hp)
// 要让 Panel 填满 Widget：Hp = Ww、Wp = Hw
panel.Width = VerticalWidth;        // 60 = 任务栏宽度
panel.Height = mainAxisLength;      // ≈ 400 = 主轴长度
var widget = new Border {
    Width = mainAxisLength, Height = VerticalWidth,
    Child = panel, LayoutTransform = new RotateTransform(90),
};
```

第一版把 `Width`/`Height` 写反（`Width = 主轴长度`），结果面板内部按 400 宽排版、
一行放得下整句，屏幕上只看到两三个字——与 C 方案同源的坑。

#### 7.2.3 截图矩阵抓到的两个真实缺陷

这两个都是**生产代码的缺陷**，不是探针的问题，且都不会在肉眼扫一眼时被发现：

1. **`Canvas` 在 `Auto` 行里塌成 0 高。** 竖向把 `PrimaryContainer.Height`
   设成 `NaN` 后，`PrimaryRow="Auto"` + `Canvas` → 主行高度 0 → 主行文字
   被 `ClipToBounds` **全部裁掉**；次要行因 `SecondaryRow` 仍是固定
   `LineHeight` 反而可见。表现是「单行状态无墨迹、双行状态只画 1 行」。
   `Canvas.MeasureOverride` 对任何内容都返回 `(0,0)`，这是它的设计。
   修法：竖向必须显式把实测折行高度写回 `Canvas.Height`。

2. **估算只算主行，次要行溢出面板。** 见 4.7 第 3 点。

诊断手段值得记下来：**分层打印 `ActualHeight`**（panel / RootGrid /
PrimaryContainer / SecondaryContainer）一次就能定位到是哪一层没生效。
只看最终 PNG 只会看到「字没了」，看不出是哪一层的问题。

#### 7.2.4 竖向断言的判据也不能用宽高比

这是同一个陷阱在截图矩阵上的**第二次复发**。原本写的是
「竖向换行生效 ⇔ 墨迹纵向 > 横向」，但 8 个字在 60 DIP 宽里折成 2 行 × 4 字，
包围盒 52×33——接近正方形，宽高比失去判别力，单行状态必然失败。

改用**内容宽度 vs 面板宽度**：竖向正确时折行会把内容压到面板宽度内；
没换行时内容远超面板宽度、被裁成一条。再补一条「纵向至少两行高」
排除「只剩一个字」的退化情况。这个判据与文本长度无关，短句长句都成立。

#### 7.2.5 逐字竖排的阈值锚在「墨迹」，不在「字号」

判据换成「每列一个字宽」之后，下界写成 `墨迹宽 >= 0.9 × 字号`。本地 72 张
全绿，CI 上 `v28-font20` 一档 6 张全红——而两种环境的差别只有 1 像素：

| 环境 | 生效字号 | 墨迹宽 | 比值 | 判定 |
|---|---|---|---|---|
| 本地 | 14 px | 13 px | 96% | PASS |
| CI | 14 px | 12 px | 86% | FAIL（12 < 12.6）|

差 1 像素就翻脸，说明阈值锚错了对象。汉字墨迹本来就比 em 方框小一圈：
advance 是 1 em，黑墨只占中间约 0.85—0.95 em；小字号下 ClearType 再把边缘
量化到整像素，比例连单调都不是——同一套代码，13 px 量到 12 px（0.92），
14 px 仍量到 12 px（0.86）。0.9 恰好骑在这条线上，于是「短边 28 DIP、字号被
夹到 14」这一档必然误伤。

下界改为 0.75 字号：仍抓得住真正的塌缩（被横向压扁或裁掉一半约 0.5 em），
但不再对渲染器的 1 像素抖动敏感。断言消息里连比值一起打出来
（`墨迹宽=12px，字号=14px（86%）`），下次再争阈值不必重新量图。

**只在 CI 红、本地绿时怎么定责**：先把 `lyrics-screenshots` artifact 下下来
看原图，别急着改判据。这次两张图（`v28-small` 与 `v28-font20`）并排看是同一个
渲染结果——8 个字竖成一列、笔画完整——才敢确认问题在阈值、不在排版。

**同源的坑**：本地 `dotnet format` 会报几百条 `ENDOFLINE`（`FluentFlyoutWPF/`
下的文件）和几十条 `WHITESPACE`（其它工程），CI 却全绿。原因是工作区是 LF
（本仓 `core.autocrlf=input`），而 `FluentFlyoutWPF/.editorconfig` 要求 CRLF，
CI 检出时 `autocrlf=true` 正好合规。格式这项以 CI 为准；本地只用 `--include`
单查某个文件，抓与行尾无关的规则（如 `FINALNEWLINE`）。

#### 7.2.6 真机摆不出竖向任务栏，就退一步验装配链

竖向一直缺一个「真机截图」，但**这台机器上做不到**：Win11（build 26300）的任务栏
只能贴在屏幕底边，`StuckRects3` 的 edge 字节恒为 3（0/1/2 分别对应左/上/右）。
没有竖排任务栏，就没有竖着的小组件可以截。

替代方案是把「除任务栏定位之外」的每一层都换成生产代码：新建
`VerticalWidgetProbe`（`Probe.Screenshot --assembly`），构造真实的
`TaskbarWidgetControl`，走
`SetLyricsMode → SetVerticalMode → MeasureDesiredWidth → CalculateSize`，
再按 `TaskbarWindow.PositionWidget` 的写法（尺寸写在控件自身、
`LayoutTransform = +90`、外面包一层 Canvas 当任务栏）排版渲染。

主矩阵与它的分工值得写清楚：

| | 尺寸来源 | 能抓到什么 |
|---|---|---|
| 主矩阵（72 张） | 夹具直接写 `Panel.Width/Height` | 面板内部的排版、朝向、裁切 |
| 装配链（8 张） | 生产 `CalculateSize` | 宿主把尺寸写错（历史缺陷都在这里） |

实测（29 字长句，短边 40 / 28）：

| 短边 | `panel.Height` | `VerticalContentLength` | `panel.Width` | 墨迹包围盒 | 列数 |
|---|---|---|---|---|---|
| 40 | 37.5 | 377.0 | 377.0 | 26 × 340 | 2（主行 + 下一行）|
| 28 | 25.5 | 377.0 | 377.0 | 13 × 339 | 1（译文列被丢弃）|

`37.5 = 40 − 2.5`（`WidgetCrossInset`），`377 = 29 字 × 13 px`——两个数都对得上，
说明尺寸链两端都对：短边从宿主减内缩后写进面板，主轴长度由面板上报、宿主只做
clamp。28 DIP 那一行同时验证了「两列装不下就丢译文列」这条硬约束在宿主层也成立。

**它管不到的**：`TaskbarWindow` 的吸附/避让/多显示器、任务栏自身的透明与主题色、
以及真实 SMTC 帧的到达节奏。这些仍然只有真机能验。

**单位陷阱**（踩过一次）：`MeasureDesiredWidth` 返回的是**视觉 DIP**（内部乘过
`_scale`），`CalculateSize` 的 `availableWidth` 收的也是视觉 DIP。早先的探针
在这个值上又乘了一次 0.9，预算凭空少 11%，长句被判成「被裁」。

#### 7.2.7 竖排长度要按「整首歌最长一句」定尺

真机反馈的第一条：**「长度会随着歌词长度变化」**——同一首歌里胶囊一会儿长一会儿短。

横向从来不会有这个毛病，因为它有一层文档级的尺子：

```
LyricsDocumentWidth.MeasureMaxLineWidth(lines) → _documentMaxWidth
DesiredContentWidth 优先返回 _documentMaxWidth，没有文档才回退当前行
```

竖向缺的正是这层对称结构：`VerticalContentLength` 直接返回当前行的排字长度，
换一句就变一截。宿主拿这个值写 `panel.Width`，胶囊于是跟着句子伸缩。

修法与横向严格对称：

| | 横向 | 竖向 |
|---|---|---|
| Core 计算 | `LyricsDocumentWidth.MeasureMaxLineWidth` | `VerticalDocumentLength.MeasureMaxColumnLength` |
| 面板缓存 | `_documentMaxWidth` | `_verticalDocumentLength` |
| 对外出口 | `DesiredContentWidth` 优先文档宽度 | `VerticalContentLength` 优先文档长度 |
| 无文档时 | 回退当前行 | 回退当前行 `_verticalContentLength` |

**唯一容易写错的地方：量整首与排字必须共用同一套 run 兜底规则。**
`VerticalRunSplitter` 切出的 run 与 `BuildVerticalColumn` 排的是同一串，
但两处量宽若各写一份兜底（量不出时怎么办），长句会差出几个字。现在统一走
`MeasureVerticalRun(run, fontSize, fontWeight)`：量不出来按 `0.2 × 字号` 走一步。

`MeasureMaxColumnLength` 的签名里**故意不含当前行**——一旦把当前行当参数传进去，
「换句不变」这个性质就没法靠类型守住，测试也只能靠肉眼。抽样上限沿用横向的
200 句，等距抽样且必含末句（长句常在副歌，均匀抽样容易漏掉）。

验证（`--assembly` 装配链）：有文档时短句 464.0 / 长句 464.0，恒定；
把文档去掉做对照则是 74.0 / 464.0——两个数不等，证明这套夹具确实能区分长度，
「恒定」不是夹具写死造成的假绿。Core 侧 `VerticalDocumentLengthTests` 10 个用例
锁住换句不变、译文参与、NaN 忽略、200 字抽样必含末句。

#### 7.2.8 左侧任务栏：整块排版左右对调，拉丁词翻 180°

真机反馈的第二条：**「任务栏固定左侧时，歌词要从右往左出，字母要符合从屏幕
中间往左看；现在固定右侧时是正常的」**。

先把坐标系摆清楚，否则「镜像」这个词会指错对象：宿主恒挂 `LayoutTransform(+90)`，
**屏幕外侧**在右侧任务栏时对应面板的小 `y`，在左侧任务栏时对应大 `y`。
所以镜像不是去改坐标映射，而是改「哪一侧算外侧 + 字往哪边躺」。

改动只有两处，多一处都不该动：

1. **两列左右对调**——主列从「贴屏幕中间」改成「贴屏幕边缘」，译文/下一行退到内侧；
2. **拉丁词翻 180°**——从「字顶朝屏幕右、从上往下读」变成「字顶朝左、从下往上读」。

**方块字不镜像。** 镜像会把汉字左右翻转成另一个字（「这」翻过来不是「这」），
那不是排版。这一点与 `text-orientation: mixed` 的直觉一致：竖排里方块字本来就
正立堆叠，只有拉丁词需要处理躺向。

**为什么是转 180°，不是 `ScaleX = -1`。** 水平镜像会把字母本身反写：
`s` 变成照镜子一样的反向记号、`c` 的开口朝左，整词读不出来。转 180° 只改
「往哪边躺、往哪边读」两个自由度，字母形状仍然正确。

净角度（含宿主那层 +90）：

| 元素 | 右侧任务栏 | 左侧任务栏 |
|---|---|---|
| 方块字 | −90（抵掉宿主）→ 屏幕上 0，正立 | 同左，**不镜像** |
| 拉丁词 | 不转 → +90，字顶朝右、从上往下 | +180 → 270 ≡ −90，字顶朝左、从下往上 |

**脏检查要跟着加一维。** `VerticalBuildKey` 增加 `Mirrored` 字段；
`SetVerticalMirror` 变值时清 `_verticalKey` 与 `_verticalDocumentKey`，
**不当场重建**——首次调用时短边还没写进面板，当场建树会按旧短边定列数
（就是 7.2.2 那个「尺寸与排版不同步」的坑）。改按需重建：下一次
`EnsureVerticalLayout` 用新短边一次建对。

**贴边判定不能比坐标 0。** 副屏可以是负坐标，左侧任务栏的窗口左边界同样不是 0。
改用 `MonitorUtil.GetMonitor(taskbarHandle).monitorArea` 与 `taskbarRect` 的左右间距
比大小（`leftGap <= rightGap`），异常时回退 false，保持改动之前的行为。

断言（装配链，30 字主行 + 13 字次要行）：

```
[PASS] 右侧任务栏：主列在屏幕右半（右半=2794px，左半=994px）
[PASS] 左侧任务栏：主列移到屏幕左半（左半=2827px，右半=972px）
[PASS] 镜像只换列序，不改长度（左侧=425.0，右侧=425.0）
```

第三条是防「镜像顺手把尺寸也改了」——真机上那条错位会表现为「切任务栏边时胶囊跳一下」。

备选方案曾有三个：A 只换列序（拉丁词不动）、B 列序 + 拉丁词镜像、C 整块反向旋转
（文字从下往上但字躺着）。用户看完对照图选了 **B**，并明确要求英文也镜像。

#### 7.2.9 R1 护栏：重排次数要先能数出来

R1 的验收口径是「播放 60s，整窗重排 < 20 次」。这句话在变成可执行的东西之前
只是一句话，所以顺序是：先建探针 → 修 → 再上常驻计数器。

**探针（`--reposition`）**：真控件 + mock 帧，30fps × 60s = 1800 帧、20 句轮换，
数 `WidgetWidthStrategyChanged` 的触发次数（生产里它的处理器就是
`BeginInvoke(UpdatePosition)`，所以两者次数相等）。不渲染像素——这条判据与画面无关。

修之前的第一组数（红）：

| 方向 | 有文档 | 无文档（对照） |
|---|---|---|
| 横向 | 0 | 20 |
| 竖向 | **20** | 20 |

竖向每换一行就请求一次整窗重排，而这 20 次一次都不必要。

**根因**：`ReportDesiredWidth` 用的是横向判据「整首最宽句 → 当前行实测」，
而竖向的 `_documentMaxWidth` 恒为 0（`RecalculateDocumentWidth` 对竖向直接清零），
于是每一帧都退回去量当前行；每句长度不同，差值越过 4 DIP 阈值就发一次事件。

**修法**：竖向改报主轴长度（`VerticalContentLength`）。竖排的短边被任务栏钉死、
没有协商余地，能协商的只有沿主轴的长度，而它在整首歌内恒定。修后竖向有文档 = 0，
无文档仍是 20——那条路径确实需要换行重排，没有被误伤。

一个坑：探针第一版没给面板写尺寸，竖向的字形树根本没建起来，
`VerticalContentLength` 恒为 0，「无文档」那组从 20 变成 0，看着像修好了。
**夹具必须按生产写入短边与主轴长度**，否则这条分组是空断言。

**常驻计数器**：`FluentFlyout.Lyrics/Diagnostics/RepositionBudget`
（滚动窗口、基线豁免、告警节流，12 个单测）。`UpdatePosition` 现在带来源标签
（`Timer` / `WidthStrategy` / `Dpi` / `Target` / `MediaUi` / `Startup`），
超预算时打一条 Warn 并带上分来源次数——「超了」本身不足以定位，得看出是哪一路。

**验收口径要说清**：1.5s 定时器心跳本身就是 40 次/分，把它算进 20 的预算里，
告警会永远亮着，也就等于没有告警。所以预算只数**非基线**：定时器计数但不计入。
口径因此是「歌词等事件驱动路径 < 20 次/分」，而不是「总量」。
要把总量也压下去得动心跳策略（廉价环境签名 + 占用区降频刷新），那是行为变更，
需要真机验证，单独排在 **8.1**——那里把「两半缺一不可」和「签名必须含自动隐藏状态」
讲清楚了。

**顺带修的一处**：`CalculateAndSetPosition` 撞上「正在排」时原来是**丢弃**，
现在记下来源、这一轮结束后补跑一次。丢弃在轮询时代没问题（1.5s 后还会来），
但歌词路径是事件驱动的：换歌那次请求恰好落在一次 UIA 慢查询里就会被吞掉，
表现为「新宽度要等 1.5s 才生效」，而日志里一条错误都没有。

#### 7.2.10 一个从未被创建过的表

`app_error` 里每隔十几秒一条 `SQLite Error 1: 'no such table: SongSearchMaps'`，
一场 90 秒的播放能刷出 11 条。症状看着像「查询写错了」，实际是**这张表从来没被建过**。

现场有三样东西可以直接对上：

| 观察 | 含义 |
|---|---|
| `%APPDATA%\TaskbarLyrics\database\song_maps.db` 是 **0 字节** | 文件在，结构不在 |
| 文件时间戳 = 程序第一次运行的时间，之后再没变过 | 建了文件就再没人碰它 |
| 同目录的 `user_data.db` 有 24 KB、表齐全 | 另一路持久化是好的 |

**根因**：`SongSearchMapDbContext.InitializeDatabase()` 在全仓**没有任何调用点**
（`grep InitializeDatabase` 只有定义那一行）。所以 `EnsureCreated()` 一次都没跑过，
EF 只是在 `UseSqlite` 打开数据源时顺手把 0 字节文件建了出来。对照组
`TrackLyricOffsetStore.Load()` 里是调了 `EnsureCreated()` 的，这就是两个库命运不同的原因。

**修法：谁读谁负责建表。** `SongSearchMapResolver.Resolve` 首次调用时做一次幂等的
`EnsureCreated`（`_schemaReady` 标记 + 双检锁），不再依赖任何外部启动钩子。
不放到 App 启动里补一行调用的理由是**入口有两个**——`LyricResolutionCoordinator`
构造时 `new` 一个、`LyricDiagnosticRunner` 自己也 `new` 一个——靠外部初始化迟早再漏一次。

**降级口径**：映射库只是「别名纠正」这层可选增强，本仓库当前甚至还没有写入它的入口。
所以建表失败不能拖住歌词解析：`Resolve` 直接返回 `LyricMapping.Unchanged(track)`，
并且**首次失败写一条 ERROR，之后降到 Debug，同时静默 10 分钟再重试**。
解析在每次换歌、每次搜索尝试时都会被调用——同一故障每次都写 ERROR，
错误日志会把这个真问题埋掉（那 11 条就是证据）。

**一个坑**：0 字节文件不等于无效数据库。EF 打开数据源时就把文件建出来了，所以
「文件存在」不能当作「表已建」的证据；反过来，`EnsureCreated` 面对 0 字节文件
是能正常补齐结构的，不需要先删文件。这一点用 `ResolveCreatesSchemaWhenDatabaseFileIsEmpty`
钉住了——测试里先写一个 0 字节文件，再断言建表成功，复现的就是线上那份现场。

**测试（4 个）**：全新库 / 0 字节库 / 命中已有映射 / 库彻底不可用时降级。
最后一个把目录本身当数据源，验证异常不会漏到解析链路上。

### 7.3 归档截图

`Probe.Screenshot` 每次生成 72 张（另有 `--assembly` 的 8 张整机图），代表性
样本归档在 `docs/lyrics-integration/screenshots/`（全量产物在 `bin/.../shots/`
与 `bin/.../assembly/`）：

| 文件 | 内容 | 验证点 |
|---|---|---|
| `K-horizontal-two-line.png` | 横向 · 双行 | 基准行为未变 |
| `L-horizontal-long-line.png` | 横向 · 长句 | 长句不折行、走滚动 |
| `M-vertical-single.png` | 竖向 · 单行 | 8 个字排成一列，字正立 |
| `N-vertical-two-line.png` | 竖向 · 双行 | **两列**：右列主行、左列下一行 |
| `O-vertical-translation.png` | 竖向 · 翻译行 | 译文列字号更小、颜色更淡 |
| `P-vertical-long-line.png` | 竖向 · 长句 | 29 个字一列到底（377 DIP） |
| `Q-vertical-dark-two-line.png` | 竖向 · 深色 | 深色主题对比度 |
| `R-horizontal-200-word-scan.png` | 横向 · 200% · 逐字扫描 | 扫描遮罩位置与 DPI 无关 |
| `S-vertical-mixed-latin.png` | 竖向 · 200% · 中英混排 | **拉丁词整词侧躺、汉字正立** |
| `T-assembly-two-column.png` | 整机 · 短边 40 | 封面 + **两列**：右列主行、左列下一行；尺寸来自 `CalculateSize` |
| `U-assembly-single-column.png` | 整机 · 短边 28 · 深色 | 只一列：28 DIP 放不下两列，丢译文列保主列 |
| `V-side-by-side.png` | 整机 · 左贴边 vs 右贴边对照 | 主列随贴边换到外侧；拉丁词翻 180°；两侧长度同值 |
| `V-latin-closeup.png` | 上图的 4× 特写 | 字母形状正确（不是镜面反写），字顶朝外侧 |

竖排那六张（M/N/O/P/Q/S）是逐字竖排这一版的回归样本：将来若有人动了
`LyricsPanel.Vertical.cs` 里的朝向判定或列布局，它们会先变。横向那三张
（K/L/R）没有变——横向走的仍是原来的折行 + 滚动路径。T/U 两张是**宿主层**
的样本：它们连封面缩略图一起截进来，因为要证明「面板确实被排在了歌词该在
的位置上」，而不只是面板内部画对了。

V 两张是**边位**的样本，也是 7.2.8 的人工验收依据：左右两条并排放在同一张图上，
「主列是否换到外侧」「英文词是否翻过来」一眼可判。它们由
`Probe.Screenshot --assembly` 的 `RenderSideBySide()` 生成，改完贴边判定后要
重新跑一次并覆盖归档，否则文档会比代码旧。

---

## 8. 下一步

R1 还剩两半，都不适合与这一轮一起改：R1b 是**行为变更**（要真机确认代价可接受），
R1c 是**验证**（探针推的是 mock 帧，真实节奏只有真机有）。

| 项 | 一句话 | 详见 |
|---|---|---|
| **R1b 心跳降频** | 压掉「什么都没发生时也在跑」的 40 次/分 | 8.1 |
| **R1c 真机复测** | 确认 R1 的修法在真实 SMTC 节奏下也成立 | 8.2 |

### 8.1 R1b：心跳降频降的是哪一段

**它不是修 bug。** 7.2.9 那轮消掉的是**事件驱动**的超支（竖向每换一句一次，
60s 20 次）；R1b 动的是另一条与 R1 完全不重叠的路径：`TaskbarWindow` 里那个
`_timer.Interval = 1500ms` 的 `DispatcherTimer`（在 `SetupWindow` 附近），
**每分钟 40 轮，与「有没有变化」无关**。

#### 一轮在干什么

`UpdatePosition` → `CalculateAndSetPosition`，全部无条件执行：

| 步骤 | 代价 |
|---|---|
| `GetSelectedTaskbarHandle` + `GetParent` 比较 | 廉价 Win32 |
| `GetDpiForWindow` | 廉价 |
| `GetTaskbarFrameRect` → 读 `TaskbarFrame` 的 `BoundingRectangle` | **跨进程 UIA** |
| `Widget.SetSmallTaskbarMode` → `MeasureDesiredWidth` → `GetOccupiedIntervals` | **跨进程 UIA ×2**（Widgets 按钮 + 托盘） |
| `SetWindowPos` | 无条件，哪怕坐标一个像素都没变 |
| `UpdateWindowRegion`（`CreateRectRgn` + `SetWindowRgn`） | 无条件重建 region，让任务栏那棵 XAML 树局部重排 |

两个容易看漏的细节：

- `OccupiedCacheLifetime` 是 500ms，而心跳是 1.5s，**这个缓存对心跳永远不命中**。
  它的真实用途是挡「换歌 + 换行」那种叠加突发，不是给心跳兜底。
- `_taskbarFrameElement` 缓存的是 UIA **元素引用**，省掉的是 `FindFirst`
  （最坏 1000ms）那一步；`BoundingRectangle` 仍然每轮跨进程读一次。

也就是说，这 40 次/分里绝大多数轮次的输入完全相同、输出完全相同，纯属重复劳动。

#### 为什么值得降

1. **结果几乎恒定**：任务栏没动、托盘没变、DPI 没变，算出来还是同一个矩形。
2. **UIA 是跨进程的，对面是 explorer.exe 的 UI 线程**。代码注释已经写明
   「单次最多等 1000ms（找元素）+ 500ms（取矩形）」。装上 Nilesoft Shell /
   Windhawk 这类**注入式** mod 之后，UIA provider 的响应会被拉长，
   1.5s 的周期可能直接撞上上一轮的尾巴——这也是把「撞车丢弃」改成
   「补跑一次」的原因（见 7.2.9 末段）。
3. **`SetWindowPos` + `SetWindowRgn` 在跟 mod 抢同一棵树**：mod 改一次任务栏，
   我们紧接着重排一次，两边互相触发，用户看到的就是「任务栏一顿一顿」。

#### 降频由两半组成，缺一不可

| 成分 | 抓什么 | 怎么做 |
|---|---|---|
| **A. 环境签名闸门** | 「任务栏本身变了」：移动、改尺寸、隐藏、换屏、换 DPI、Explorer 重启 | 用 `GetWindowRect` + `GetSystemMetrics` + `GetDpiForWindow` 算签名（微秒级，不碰 UIA），签名没变就整轮跳过 |
| **B. 占用区查询降到 5s** | 「托盘图标变了」 | 托盘增减**不改变任务栏矩形**，签名抓不到，只能单独限频 |

漏了 A：任务栏移动/隐藏仍然每分钟空跑 40 次。
漏了 B：托盘图标增减永远看不见，小组件会压到图标上。
**两半漏的正好互补。**

#### 代价与坑

- 托盘图标增减后，避让最多慢 5s。平时无感，但「显示所有托盘图标」刚打开那一下
  会看到小组件先压住图标、几秒后让开。这是 B 的固有代价，不是 bug。
- **签名里必须含任务栏自动隐藏状态**（`ABM_GETSTATE` 的 `ABS_AUTOHIDE`）。
  这是最容易漏的一项：漏掉它，抽屉弹出/收起就不算「环境变了」，
  表现为小组件浮在错误位置不动。**优先级高于「显示器数」**——后者在插拔时
  通常伴随矩形变化，已经能被抓到。
- 事件驱动的路径（换歌、改设置、歌词宽度）完全不受影响，它们本来就不看心跳，
  所以 A/B 都不会让歌词变糊。

#### 分阶段

1. 先做 **R1c**（8.2）——成本低、几分钟出结论，而且它验的是已经合进去的版本。
2. **只做 A**（签名闸门）。风险最低、收益最大，且不改任何可见行为：
   签名没变 = 这一轮本来就什么都不会变。
3. **B 延后**，看 A 落地后 `RepositionBudget` 里 `Timer` 的绝对次数是否还需要再压。
   若真机显示已经平稳，B 可以永远不做——它换来的只有 UIA 次数，代价却是可见的。

### 8.2 其余待办

**R1c 真机复测**：探针推的是 mock 帧，真实 SMTC 帧的到达节奏取决于播放器
（有的 1s 才推一次），只有真机能验。开 Nilesoft Shell + Windhawk 播一小时，
`%APPDATA%\FluentFlyout\` 的日志里不该出现 `repositioned` 的 Warn。

其余按 2026-10-28 定下的顺序：

- **karaoke 逐字点亮**（视觉效果，用户指定放最后）；
- **竖排超长句**的截断/滚动策略（现在不滚，超出预算即被裁）；
- **手工矩阵**：四方向任务栏 × 三种 DPI × 深浅主题；
- **README** 里的歌词说明（截图 + 设置项）。
