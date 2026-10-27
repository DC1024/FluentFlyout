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

## 4. 竖向任务栏：现有代码没有先例，这是新增行为

`SetVerticalMode` 对文字的处理是**直接藏掉**（`:120`），封面和按钮靠
`RotateTransform(-90)` 抵消 `Widget` 的 `LayoutTransform = RotateTransform(90)`
（`TaskbarWindow.xaml.cs:473`）保持正立。也就是说：**FF 今天在竖向任务栏上根本不显示文字**。

歌词必须显示，所以这是行为变更，不是叠加。

### 4.1 几何推导（已验算）

WPF 的 `RotateTransform(θ)` 用 Y 向下的屏幕坐标系，点变换为
`x' = x·cosθ − y·sinθ`，`y' = x·sinθ + y·cosθ`。

`Widget` 上的 `RotateTransform(90)` 因此把内部坐标映射到屏幕：
内部 `+X` → 屏幕**向下**，内部 `+Y` → 屏幕**向左**。内部盒 `(240, 40)` 的视觉包围盒
变成 `(40, 240)`，且落在 x ∈ (−40, 0) 的负方向上——现有代码已按这个偏移定位
（`TaskbarWindow.xaml.cs:644-647` 的注释就是讲这件事），歌词层**不要**再去旋转整个 `Widget`。

给歌词 `TextBlock` 加 `LayoutTransform = RotateTransform(-90)`：

| 子局部方向 | 映射到内部 | 映射到屏幕 |
|---|---|---|
| `+X` | `−Y` | 向右 |
| `+Y` | `+X` | 向下 |

净旋转 `R(90)·R(−90) = R(0)` → 字形**正立**。于是「子局部里宽 1em、`TextWrapping=Wrap`」
就是屏幕上一列正立字符**自上而下**排列，等价于 TL 的
`writing-mode: vertical-rl; text-orientation: upright`（`style.css:688-689`）。

子局部 `(16, 240)` 经 `LayoutTransform` 后向父级上报 `(240, 16)`：
宽 240 ≤ 240 ✓、高 16 ≤ 40 ✓；两行叠起来 `(240, 32)` 仍装得下。

扫描渐变方向跟着改：沿子局部 `+Y`（屏幕向下），即
`StartPoint=(0,0)`、`EndPoint=(0,height)`。

### 4.2 陷阱：现有代码用 `RenderTransform`，歌词必须用 `LayoutTransform`

`SetVerticalMode` 给封面和按钮的反向旋转用的是 **`RenderTransform`**（`:128`、`:133`）：

```csharp
SongImageBorder.RenderTransform = (Transform?)counterRotate ?? Transform.Identity;
```

`RenderTransform` **不参与布局**——元素在父级看来仍是未旋转的尺寸，只是绘制时转一下。
对 36×36 的封面和 32×32 的按钮完全没问题，因为正方形旋转后包围盒不变。

歌词是非正方形的 `(16, 240)`：用 `RenderTransform` 的话，父级 `StackPanel` 仍按
`16×240` 给它排版，实际画出来却是横躺的 240 宽——直接叠在封面上。**必须用
`LayoutTransform`**，让父级看到变换后的 `(240, 16)`。

代价是 `LayoutTransform` 会触发额外的 measure/arrange 遍。所以：**只在
`Orientation` 切换时设置一次，不要每帧设**。

### 4.3 建议的落地策略

给 `LyricsPanel` 加 `Orientation` 属性（`Horizontal` / `Vertical`），竖向路径先实现
但**默认关闭**，用一个内部开关 `SupportsVerticalTaskbar` 控制：

- 开关关 → 竖向任务栏上歌词 `Collapsed`，行为与今天完全一致（不劣化）；
- 开关开 → 走 4.1 的排版；
- #12 的验收就是把这个开关打开跑一遍四方向任务栏。

TL 在竖向下还用 `flex-direction: row-reverse`（`style.css:677`）把两行反过来排，
WPF 侧对应 `StackPanel` 的子项顺序，需要实测确认读向。

### 4.2 建议的落地策略

给 `LyricsPanel` 加 `Orientation` 属性（`Horizontal` / `Vertical`），竖向路径先实现
但**默认关闭**，用一个内部开关 `SupportsVerticalTaskbar` 控制：

- 开关关 → 竖向任务栏上歌词 `Collapsed`，行为与今天完全一致（不劣化）；
- 开关开 → 走 4.1 的排版；
- #12 的验收就是把这个开关打开跑一遍四方向任务栏。

---

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
