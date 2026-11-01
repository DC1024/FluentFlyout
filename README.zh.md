<h1 align="center">FluentFlyout Lyrics</h1>

<p align="center">
  <strong>在 Windows 任务栏上唱卡拉 OK —— 原版 FluentFlyout 的全部功能，加上一套歌词引擎。</strong>
</p>

<p align="center">
  <a href="LICENSE"><img alt="License" src="https://img.shields.io/badge/license-GPL--3.0-blue"></a>
  <img alt="Platform" src="https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-lightgrey">
  <img alt="Upstream" src="https://img.shields.io/badge/upstream-FluentFlyout%202.16-informational">
  <img alt="Tests" src="https://img.shields.io/badge/tests-881%20cases-success">
</p>

<p align="center">
  <a href="README.md">English</a> | <strong>简体中文</strong>
</p>

---

FluentFlyout Lyrics 是 [FluentFlyout](https://github.com/unchihugo/FluentFlyout) 的分支。原版是一个 Fluent 2 风格的 Windows 媒体弹窗工具：音量弹窗、锁定键弹窗、任务栏小组件。

这个分支**保留了原版的全部功能**，在此之上加了一套画在任务栏小组件里的同步歌词引擎——逐字点亮、翻译行、竖排任务栏完整支持；顺带修掉了一批影响日常使用的问题，并把歌词链路纳入了自动化测试。

这是增强，不是重写。上游的改动每月合并一次。

## 相对原版做了什么 🆚

| | 原版 FluentFlyout | FluentFlyout Lyrics |
|---|---|---|
| 音频 / 锁定键 / 即将播放 弹窗 | ✅ | ✅ |
| 任务栏小组件 | ✅ | ✅ |
| **任务栏歌词** | ❌ | ✅ 逐字点亮 · 翻译行 · 竖排 · 每曲偏移 |
| 歌词音源 | — | QQ 音乐 · 酷狗 · 网易云 · LRCLIB |
| 开机自启的表现 | 弹出设置窗口 | 静默进托盘 |
| 歌词链路测试 | — | 881 个自动化用例 |

### 1. 任务栏歌词 🎤

这是最大的一块增量。播放时歌词替换掉小组件里的歌名和歌手。

<p align="center">
  <picture>
    <source width="600" media="(prefers-color-scheme: dark)" srcset="docs/images/lyrics-horizontal-dark.png" />
    <source width="600" media="(prefers-color-scheme: light)" srcset="docs/images/lyrics-horizontal-light.png" />
    <img width="600" src="docs/images/lyrics-horizontal-light.png" alt="横排任务栏上的当前歌词行与它下面的翻译">
  </picture>
</p>
<p align="center">
  <picture>
    <source width="120" media="(prefers-color-scheme: dark)" srcset="docs/images/lyrics-vertical-dark.png" />
    <source width="120" media="(prefers-color-scheme: light)" srcset="docs/images/lyrics-vertical-light.png" />
    <img width="120" src="docs/images/lyrics-vertical-light.png" alt="同一段歌词在竖排任务栏上，一行一个字">
  </picture>
</p>
<p align="center">
  <sub>200% 缩放下的渲染结果——上方横排任务栏，下方竖排。歌词由真实控件绘制，任务栏底色由截图探针绘制。</sub>
</p>

- **逐字点亮** — 音源带逐音节时间轴时按卡拉 OK 方式填色；只有行级时间轴时走三级降级的扫描方案
- **翻译行** — 第二行有翻译就显示翻译，没有就显示下一句
- **竖排任务栏** — 歌词一行一个字往下排；一句长过屏幕就沿主轴滚动，而不是在后半个词处截断
- **四个方向各自适配** — 上、下、左、右任务栏分别测绘与排版；左侧任务栏用左右镜像，而不是把整块转 90°
- **每曲偏移** — 按毫秒把歌词提前或延后，按歌曲单独保存
- **自定义颜色与对齐** — 跟随系统主题，或自己填 `#RRGGBB` / `#AARRGGBB` / `#RGB`
- **可与频谱共存** — 两个可以同时开，共用可用宽度

### 2. 开机不再弹设置窗口

原版同时注册了两个自启入口——MSIX 的 `windows.startupTask` 和一条 `HKCU\...\Run` 值——而这两条都不带任何「本次是自启」的标记。第二份实例抢不到单例互斥量，就给第一份发信号把设置窗口叫出来，于是**每次开机都以设置窗口收场**。

这个分支让自启入口带上 `--minimized`，带这个标记的第二实例一律静默退出；并且在以打包身份运行时干脆不写 `Run` 值——清单里的 `startupTask` 已经覆盖了这件事。双击图标仍然照常打开设置窗口。

### 3. 修掉的缺陷

- **竖排任务栏一个歌词字都不显示。** 面板宽度从来没被宿主写入。`Canvas` 放在 `Width="Auto"` 的横向 `StackPanel` 里，量出来的排列宽度是 0，于是什么都没排上——而且不会报任何错。
- **竖排长句在后半个词处被截断。** 现在用真实 `TextBlock` 量出折行高度，面板沿主轴撑开。
- **歌词映射库从来没建过表**，每首歌都要先撞一次 `no such table` 才降级。
- **网易云、酷狗的歌词停在第一行不动。** 这些播放器把 Windows 媒体时间轴留在零点；本分支改用本地单调时钟兜底，并用「曲长 ≥ 30 秒」的判据挡掉伪造估计值，避免位置提前回绕。
- **单曲循环时本地时钟卡在曲尾。** 现在会跟着回绕归位。
- **点开歌词设置页直接崩溃。** 对齐卡片里有一个图标名不是真实枚举值，这个问题只在页面构造时抛，编译期完全看不见。
- **歌词面板每换一句就变宽变窄。** 宽度改为按整首最宽的一句算一次，胶囊不再随句子呼吸。
- **拔掉显示器，小组件跑到别的屏幕上。** 目标显示器改用设备路径做身份，不再依赖序号。
- **纯音乐帧会把上一句的内容再画一遍。** 帧应用改成了幂等。

### 4. 性能护栏

- 取帧泵跑在 33 ms，关掉歌词就整个停掉——不留空转的跨进程 COM 调用
- 逐字扫描在时间轴数据不需要昂贵方案时自动降级
- 整窗重排有预算计数器，长句不会每帧触发一次重排
- 歌词和频谱可以共用任务栏，而不是其中一个把另一个静默关掉

### 5. 工程

- **881 个测试用例**，分布在 531 个 `[Fact]` 与 88 个 `[Theory]` 方法里（一个 theory 的每条数据行会展开成独立用例）。歌词引擎不引用 `System.Windows`，所以检索、解析、时间轴与排版计算全都不需要 UI 线程就能断言
- **72 张截图的视觉验收矩阵**，覆盖四个任务栏方向 × 深浅色 × 多种缩放比例
- 一道 CI 门禁：图标名必须是真实存在的枚举值——正是上面那个崩溃的同类问题
- 便携版出包工作流，下载解压即可测试
- [架构文档](docs/lyrics-integration/architecture.md)，写清分层边界与那些「不写下来就一定会踩」的时序规则

## 安装 📥

这个分支不发布 Microsoft Store 版本，也没有签名的 MSIX。三条路：

### 1. 下载构建好的便携包（推荐）

打开[便携版构建工作流](https://github.com/DC1024/FluentFlyout/actions/workflows/build-portable.yml)，选一次成功的运行，下载 `FluentFlyoutLyrics-portable-win-x64.zip`，解压后双击 `FluentFlyoutLyrics.exe`。

需要 [.NET 10 桌面运行时](https://dotnet.microsoft.com/download/dotnet/10.0)。

### 2. 自己编译

```powershell
git clone https://github.com/DC1024/FluentFlyout.git
cd FluentFlyout
dotnet publish FluentFlyoutWPF/FluentFlyout.csproj -c "GitHub Release" -r win-x64 --self-contained false
```

必须用 `GitHub Release` 配置——它定义了 `GITHUB_RELEASE` 编译符号，许可证管理靠它跳过 Store 授权检查。用普通的 `Release` 编译出来会去连 Store 找一个你根本没有的授权。

### 3. 从原版迁过来

设置放在 `%APPDATA%\FluentFlyout`，单例互斥量也没改——这两点都是故意的，设置和歌词缓存原样沿用，不用重新配。

但可执行文件名变了，所以要做一次交接：

1. 在托盘菜单里退出原版
2. 删掉旧的 `FluentFlyout.exe`，或者整个旧目录
3. 运行一次 `FluentFlyoutLyrics.exe`——它会把自启项改写成指向自己

不要让两个版本同时跑：它们共用同一个互斥量，后来的那个会直接退出。

## 设置项

歌词相关的都在设置页的 **歌词** 里。前提是先打开任务栏小组件。

| 设置项 | 作用 |
|---|---|
| 在任务栏显示歌词 | 总开关。依赖任务栏小组件 |
| 歌词字号 | 单位为像素。任务栏高度不够时自动缩小 |
| 显示翻译行 | 有翻译时第二行显示翻译，没有时显示下一句 |
| 逐字高亮 | 当前句按演唱进度逐字点亮。关掉可降低 CPU 占用 |
| 强制单行 | 只显示当前句，不显示翻译与下一句 |
| 歌词颜色 | 留空跟随系统主题。支持 `#RRGGBB`、`#AARRGGBB` 或 `#RGB` |
| 文本对齐 | 短句在可用宽度内的水平位置。长句需要滚动时一律从左侧开始 |
| 当前歌曲偏移 | 正值让歌词提前，负值延后，单位毫秒。按歌曲单独保存 |

歌词来自 QQ 音乐、酷狗、网易云与 LRCLIB，按这个顺序查找。首次播放某首歌需要联网检索，之后缓存到本地。

> **不向系统报告播放进度的播放器。** 网易云、酷狗等把 Windows 媒体时间轴留在零点——进度一直是 `00:00`，从不前进。本分支会识别这种情况，改为从本次播放的起点计时，歌词照样滚动。从中间续播或拖动进度条后，计时会差出你跳过的那段，用「当前歌曲偏移」校正。

## 与上游的关系

- 上游是 [unchihugo/FluentFlyout](https://github.com/unchihugo/FluentFlyout)，原版应用的功劳全在那儿。
- 上游改动**每月合并一次**，用 merge commit 而不是 squash——squash 会让上游提交在祖先关系里消失，下次合并的基点就错了，同一段 diff 会被反复重放。
- 本分支自己的改动叠在上面。同步时若冲突，宁可中断交人工处理，也不静默丢弃。

## 已知差异

- 界面只有**英文与简体中文**两个词典更新了产品名。荷兰语的 `FluentFlyout's`、土耳其语的 `FluentFlyout'un` / `FluentFlyout'tan` 带属格与后缀元音和谐，机器替换会生成语法错误的假文案；其余 27 种语言由上游在 Weblate 上维护，在这里改只会在每月同步时制造冲突。这是取舍，不是遗漏。
- 根命名空间仍是 `FluentFlyoutWPF`。那是纯内部标识，改它要动 127 个文件，用户一个字符也看不见。
- MSIX 打包路径有定义但从未跑通过：本仓库没有 Store 签名证书。

## 致谢 🙌

- [Hugo Li](https://unchihugo.github.io) — FluentFlyout 原作者、Microsoft Store 发布者、中文与荷兰语翻译
- [ANYNC](https://github.com/ANYNC/TaskbarLyrics) — TaskbarLyrics 作者，歌词引擎的 Core 层移植自该项目
- [LiAuTraver](https://github.com/LiAuTraver)、[AksharDP](https://github.com/AksharDP)、[nopeless](https://github.com/nopeless) — 上游代码贡献者
- [Hykerisme](https://github.com/Hykerisme) — 上游中文翻译

## 许可

GPL-3.0-or-later，继承自 FluentFlyout，见 [LICENSE](LICENSE)。移植自 TaskbarLyrics 的 Core 层保留其 MIT 声明，见 [LICENSE-TaskbarLyrics](LICENSE-TaskbarLyrics)。第三方组件列在 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。

### 依赖

- [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet)
- [Dubya.WindowsMediaController](https://github.com/DubyaDude/WindowsMediaController)
- [MicaWPF](https://github.com/Simnico99/MicaWPF)
- [Microsoft.Toolkit.Uwp.Notifications](https://github.com/CommunityToolkit/WindowsCommunityToolkit)
- [NAudio](https://github.com/naudio/NAudio)
- [NLog](https://nlog-project.org/)
- [unchihugo.WPF-UI](https://github.com/unchihugo/wpfui)
- [WPF-UI-Tray](https://github.com/lepoco/wpfui)
