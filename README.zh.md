<p align="center">
	<picture>
		<source width="65%" alt="fluentflyout-title" media="(prefers-color-scheme: dark)" srcset="https://github.com/user-attachments/assets/daa2969f-8ad2-4832-8253-26133a50c921" />
		<source width="65%" alt="fluentflyout-title" media="(prefers-color-scheme: light)" srcset="https://github.com/user-attachments/assets/3e75e514-b9a6-40e3-b170-af9d29a82bb4" />
		<img width="65%" alt="fluentflyout-title" src="https://github.com/user-attachments/assets/daa2969f-8ad2-4832-8253-26133a50c921">
	</picture>
</p>

<p align="center">
	<img alt="GitHub Release" src="https://img.shields.io/github/v/release/unchihugo/FluentFlyout">
	<img alt="Static Badge" src="https://img.shields.io/badge/downloads-500k%2B-blue?color=limegreen">
	<a href="https://hosted.weblate.org/engage/fluentflyout/"><img src="https://hosted.weblate.org/widget/fluentflyout/svg-badge.svg" alt="Translation status"/></a>
	<img alt="GitHub contributors" src="https://img.shields.io/github/contributors-anon/unchihugo/fluentflyout?labelColor=midnightblue&color=goldenrod">
</p>
<p align="center">
  <a href="https://github.com/unchihugo/FluentFlyout/blob/master/README.md">English</a> | <strong>简体中文</strong> | <a href="https://github.com/unchihugo/FluentFlyout/blob/master/README.nl.md">Nederlands</a> | <a href="https://github.com/unchihugo/FluentFlyout/blob/master/README.tr.md">Türkçe</a> | <a href="https://github.com/unchihugo/FluentFlyout/blob/master/README.ru.md">Русский</a> | <a href="https://github.com/unchihugo/FluentFlyout/blob/master/README.kr.md">한국어</a>
</p>

---
FluentFlyout 是一个简单且现代的 Windows 音量控制弹窗，基于 Fluent 2 设计原则构建。它的用户界面与 Windows 10/11 无缝融合，为您提供一个无缝、干净、原生般的媒体控制体验。  

FluentFlyout 能够在一个雅观的现代化的弹窗中显示媒体控制和信息，同时与系统的颜色主题相融合，具有流畅的动画效果，还提供多种布局位置和更多个性化设置。

<a href="https://apps.microsoft.com/detail/9N45NSM4TNBP?mode=direct">
	<img src="https://get.microsoft.com/images/en-us%20dark.svg" width="200"/>
</a>

<img alt="FluentFlyoutHero-Cinemascope" src="https://github.com/user-attachments/assets/a7c7e2db-e21b-4435-9acc-0b2c5f38eb7d" />

## 功能 ✨
- **音乐弹窗：显示封面、标题、艺术家和媒体控制**
- **“即将播放”弹窗：在歌曲结束时显示下一首曲目**
- **锁定键弹窗：一目了然地显示锁定键的状态**
- **任务栏小组件：直接在 Windows 任务栏上显示媒体信息**
- 原生 Windows 风格设计
- 使用 Fluent 2 组件
- 采用 Windows Mica 模糊效果
- 支持深浅色模式的切换
- 与设备主题颜色匹配
- 流畅的动画效果
- 可自定义的弹窗位置
- 包含“列表循环”、“单曲循环”和“随机播放”功能
- 同时监听音量和媒体输入
- 安静地驻留在系统托盘

## 任务栏小组件 ⏯️
<div align="center">
	<img height="440px" width="auto" src="https://github.com/user-attachments/assets/43963c54-e2d8-4b93-9842-482e12b2c592" />
</div>

<details close>
<summary>视频展示</summary>

https://github.com/user-attachments/assets/bfc7666f-1d59-4cbf-8d15-3855671cb147

</details>

## 歌词 🎤
歌词画在任务栏小组件里，播放时替换歌名和歌手。在设置页的 **歌词** 里打开「在任务栏显示歌词」即可；前提是先打开任务栏小组件。

<p align="center">
	<picture>
		<source width="600" alt="横排任务栏上的当前歌词行与它下面的翻译" media="(prefers-color-scheme: dark)" srcset="docs/images/lyrics-horizontal-dark.png" />
		<source width="600" alt="横排任务栏上的当前歌词行与它下面的翻译" media="(prefers-color-scheme: light)" srcset="docs/images/lyrics-horizontal-light.png" />
		<img width="600" alt="横排任务栏上的当前歌词行与它下面的翻译" src="docs/images/lyrics-horizontal-light.png">
	</picture>
</p>
<p align="center">
	<picture>
		<source width="120" alt="同一段歌词在竖排任务栏上折成两列" media="(prefers-color-scheme: dark)" srcset="docs/images/lyrics-vertical-dark.png" />
		<source width="120" alt="同一段歌词在竖排任务栏上折成两列" media="(prefers-color-scheme: light)" srcset="docs/images/lyrics-vertical-light.png" />
		<img width="120" alt="同一段歌词在竖排任务栏上折成两列" src="docs/images/lyrics-vertical-light.png">
	</picture>
</p>
<p align="center">
	<sub>当前句与它的翻译，200% 缩放下的渲染结果——上方是横排任务栏，下方是竖排。<br>歌词与折行由真实控件绘制，任务栏底色由截图探针绘制。</sub>
</p>

- **跟随当前行** — 面板跟着当前句走。长于小组件的句子在打开「启用滚动」后滚动；该选项默认关闭，所以默认情况下超出的部分会被裁掉
- **逐字高亮** — 音源带逐音节时间轴时按卡拉OK方式点亮当前句；关掉可省 CPU
- **翻译行** — 第二行显示翻译，没有翻译时显示下一句；打开「强制单行」就不显示
- **竖排任务栏** — 歌词折成多列铺满任务栏高度，而不是压成一行
- **单曲偏移** — 按毫秒把歌词提前或延后，按歌曲单独保存
- **自定义颜色与对齐** — 跟随系统主题，或自己填 `#RRGGBB` / `#AARRGGBB` / `#RGB`
- **可与频谱共存** — 两个可以同时开，共用可用宽度

### 歌词设置
都在设置页的 **歌词** 里。

| 设置项 | 作用 |
|---|---|
| 在任务栏显示歌词 | 总开关。依赖任务栏小组件，需要先打开它 |
| 歌词字号 | 单位为像素。任务栏高度不够时自动缩小 |
| 显示翻译行 | 有翻译时第二行显示翻译，没有时显示下一句预告 |
| 逐字高亮 | 当前句按演唱进度逐字点亮。关掉可降低 CPU 占用 |
| 强制单行 | 只显示当前句，不显示翻译与下一句 |
| 歌词颜色 | 留空跟随系统主题。支持 `#RRGGBB`、`#AARRGGBB` 或 `#RGB` |
| 文本对齐 | 短句在可用宽度内的水平位置。长句需要滚动时一律从左侧开始 |
| 当前歌曲偏移 | 正值让歌词提前，负值延后，单位毫秒。按歌曲单独保存 |

歌词来自 QQ 音乐、酷狗、网易云与 LRCLIB，按这个顺序查找。首次播放某首歌时需要联网检索，之后会缓存到本地。

> **不向系统报告播放进度的播放器。** 网易云、酷狗等播放器把 Windows 媒体时间轴留在零点——进度一直是 `00:00`，从不前进。FluentFlyout 会识别这种情况，改为从本次播放的起点计时，歌词照样滚动。从中间续播或拖动进度条后，计时会差出你跳过的那段，用上面的「当前歌曲偏移」校正。

## 音乐弹窗 🎵
<div align="center">
	<img height="205px" width="auto" src="https://github.com/user-attachments/assets/4dab1c12-594a-4785-bddc-0da1783bf1c8"> <img height="205px" src="https://github.com/user-attachments/assets/b4306026-b274-418b-a39e-78877e7610a7"> 	<img height="190px" src="https://github.com/user-attachments/assets/39de69fe-54c8-4b22-880c-7f0370b8dd9c"> <img height="190px" src="https://github.com/user-attachments/assets/a25adb0e-963a-49a5-8abb-d9a288c2ad9a"> <img height="190px" src="https://github.com/user-attachments/assets/2de44e7b-7e6c-4575-bf3b-0be2f741c994">
</div>
<details open>
<summary>v2.0 截图</summary>
<div align="center">
	<img height="220px" width="auto" src="https://github.com/user-attachments/assets/e45592d5-8576-4d6a-8679-56baacccd585"> <img height="220px" width="auto" src="https://github.com/user-attachments/assets/ff2fcfab-8e24-48cf-9bdf-d35252eb3e67">
</div>
</details>

## 怎么下载？
### 安装选项
**Microsoft Store 版本**提供自动更新，但需一次性付费解锁部分额外功能。  
**GitHub 版本**完全免费且开源，但需手动更新。

### 通过 Microsoft Store 安装
<a href="https://apps.microsoft.com/detail/9N45NSM4TNBP?mode=direct">
	<img src="https://get.microsoft.com/images/en-us%20dark.svg" width="300"/>
</a>

> 在寻找 FluentFlyout 设置吗？点击系统托盘图标来访问设置。
### 用 .msixbundle 安装程序（如果 Microsoft Store 不可用）
1. 前往 [最新发布](https://github.com/unchihugo/FluentFlyout/releases/latest) 页面
2. 下载 **"*.cer"** 文件
3. 打开证书，并使用管理员权限进行安装
4. 将证书放入 **"受信任的根证书颁发机构"** 中
5. 下载 **"*.msixbundle"**
6. 应用安装程序将弹出，点击 **"安装"**，如果之前已安装 FluentFlyout，点击 **"更新"**
7. 完成！播放音乐，试试按下您的媒体或音量键

## 即将推出的功能 📝
- [x] 设置
- [x] 可编辑的弹窗超时
- [x] 实现紧凑布局
- [x] 移除 Windows Forms 依赖
- [x] 增加更多媒体控制（循环✅，随机播放✅，进度滑块）
- [ ] 更多动画效果
- [x] 从 `alt+tab` 中移除FluentFlyout的窗口
### 问题
- Windows 10 界面可能无法如预期显示

## 贡献 💖
欢迎以任何方式贡献！来看看 [CONTRIBUTING.md](https://github.com/unchihugo/FluentFlyout/blob/master/.github/CONTRIBUTING.md) 。如果您想帮助翻译，请访问我们的 [Weblate 页面](https://hosted.weblate.org/engage/fluentflyout/)。

### 翻译状态
<a href="https://hosted.weblate.org/engage/fluentflyout/">
<img src="https://hosted.weblate.org/widget/fluentflyout/multi-auto.svg" alt="Translation status" />
</a>

### 感谢我们的贡献者团队！
<a href="https://github.com/unchihugo/fluentflyout/graphs/contributors">
  <img src="https://contrib.rocks/image?repo=unchihugo/fluentflyout&anon=1" />
</a>

## 致谢 🙌
- [Hugo Li](https://unchihugo.github.io) - 原始开发者、Microsoft Store 发布者、中文和荷兰翻译
- [LiAuTraver](https://github.com/LiAuTraver) - 贡献者
- [AksharDP](https://github.com/AksharDP) - 贡献者
- [Hykerisme](https://github.com/Hykerisme) - 中文翻译
- [nopeless](https://github.com/nopeless) - 贡献者
### 依赖
- [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet)
- [Dubya.WindowsMediaController](https://github.com/DubyaDude/WindowsMediaController)
- [MicaWPF](https://github.com/Simnico99/MicaWPF)
- [Microsoft.Toolkit.Uwp.Notifications](https://github.com/CommunityToolkit/WindowsCommunityToolkit)
- [NAudio](https://github.com/naudio/NAudio)
- [NLog](https://nlog-project.org/)
- [System.Drawing.Common](https://dot.net/)
- [unchihugo.WPF-UI](https://github.com/unchihugo/wpfui)
- [WPF-UI-Tray](https://github.com/lepoco/wpfui)