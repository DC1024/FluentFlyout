# Lyrics 模块移植基线

本文件冻结移植工作的起点状态。后续任何"是不是移植坏了"的争论，都以这里的数字为准。

- 建立日期：2026-10-27
- 状态：P1 完成（Core 层移植 + 测试迁移 + 命名空间归位）

---

## 1. 上游基线

两个仓库都是 fork，各自配置了 `upstream` 远端但**禁止 push**（`set-url --push DISABLED`），
防止误把提交推到别人的仓库。

| 仓库 | 基线 commit | 日期 | 说明 |
|---|---|---|---|
| `unchihugo/FluentFlyout` | `2d742fa4fc90a228803d90db2902740463113e77` | — | 提交信息 `Update version number to 2.16.1.0` |
| `ANYNC/TaskbarLyrics` | `c713adc8971b123f7719cacabbdf3b8f2830af0b` | 2026-10-27 15:26 +0800 | 提交信息 `调整任务栏避让事件调度` |

**为什么冻结 SHA 而不是版本号**：TaskbarLyrics 没有打 tag，版本号只存在于 csproj 里且不随提交更新。
只有 SHA 能唯一确定"我们抄的是哪一版"。

### 同步上游的做法

```bash
# FluentFlyout
git fetch upstream
git log --oneline HEAD..upstream/master   # 先看有没有新东西
git merge upstream/master                 # 只在 feat/* 分支上做，master 走 PR

# TaskbarLyrics 只在需要对比 Core 实现时 fetch，不合并
git fetch upstream
git diff upstream/master -- TaskbarLyrics.Core/
```

**不要**把 `upstream/master` 直接合进 `master`。`master` 受 ruleset 保护，
必须走 PR 且 J1/J2 通过。

---

## 2. 编译基线

目标框架 `net10.0-windows10.0.22000.0`（与 FluentFlyout 主工程一致）。

```bash
dotnet build FluentFlyout.Lyrics/FluentFlyout.Lyrics.csproj -c Release -warnaserror
```

| 指标 | 基线值 |
|---|---|
| 警告 | **0** |
| 错误 | **0** |
| 程序集名 | `FluentFlyout.Lyrics.dll` |
| 源文件数 | 58 个 `.cs` |
| 源码行数 | 9,137 行 |

`-warnaserror` 是硬门槛，不是建议。它在本次移植中实际拦下过两类问题：

1. **NU1903**：EF Core `10.0.0` 传递引入 `SQLitePCLRaw.lib.e_sqlite3 2.1.11`，
   命中 GHSA-2m69-gcr7-jv3q（高危）。处理方式是**升到 `10.0.14`**（传递依赖变成 `2.1.13`），
   不是加 `NoWarn` 掩盖。
2. **CS8602**：`LyricsData.Lines` 在 Lyricify 库里是可空的，解析失败返回 `null` 而不抛异常。

两个 RID 都要能编译（`win-x64` / `win-arm64`），但 `windows-latest` 是 x64 runner，
**`-r win-arm64` 只证明能编译，不证明能运行**。

---

## 3. 测试基线

```bash
dotnet test FluentFlyout.Lyrics.Tests/FluentFlyout.Lyrics.Tests.csproj -c Release
```

| 指标 | 基线值 |
|---|---|
| 测试总数 | **326** |
| 通过 | 326 |
| 失败 | 0 |
| 跳过 | 0 |
| 测试类 | 24 个 |
| `[Fact]` / `[Theory]` | 209 处声明（Theory 展开后共 326 个用例） |

### 构成

| 来源 | 数量 | 说明 |
|---|---|---|
| 上游 `TaskbarLyrics.Core.Tests` 移植 | 318 | 全部迁移，未删减 |
| 新增解密回归测试 | 8 | `LyricsDecryptionTests.cs` |

**为什么补这 8 个**：上游的测试样本全是**明文**，KRC/QRC 的解密路径在原始仓库里
**零覆盖**。移植时补上了真实的加解密往返测试，包括：

- `KrcDecryptLyrics_RecoversExactPlaintext` / `QrcDecryptLyrics_RecoversExactPlaintext`
- `KrcEncryption_IsDeterministic` / `QrcEncryption_IsDeterministic`（同明文必得同密文）

---

## 4. 功能基线检查表

移植"没坏"的判定标准。每项都对应一个已通过的自动化测试或探针检查。

### 4.1 解密与解析

| # | 检查项 | 判定 | 证据 |
|---|---|---|---|
| 1 | KRC 解密（XOR + zlib inflate） | 明文逐字节一致 | `KrcDecryptLyrics_RecoversExactPlaintext` |
| 2 | QRC 解密（TripleDES + zlib inflate） | 明文逐字节一致 | `QrcDecryptLyrics_RecoversExactPlaintext` |
| 3 | YRC 解析 | 行/字时间轴保留 | `ParseYrcFixtureRetainsAbsoluteLineAndSegmentTiming` |
| 4 | LRC 解析 + 偏移 + 翻译对齐 | 三项同时正确 | `ParseLrcFixtureAppliesOffsetExpandsTimestampsAndAlignsTranslation` |
| 5 | QQ 音乐加密载荷 | 解码后按字计时 | `EncryptedQrcPayloadDecodesAndParsesAsWordTimedLyrics` |
| 6 | 损坏载荷容错 | 返回 null 不抛异常 | `LegacyMappingReturnsNullForUnusablePayload`（7 个变体） |

### 4.2 解析与匹配

| # | 检查项 | 判定 |
|---|---|---|
| 7 | 歌词行匹配 / 相似度评分 | `LyricMatcherTests` 全绿 |
| 8 | 搜索计划生成 | `LyricSearchPlannerTests` 全绿 |
| 9 | 源路由策略 | `LyricSourceRoutingPolicyTests` 全绿 |
| 10 | 瞬态失败重试策略 | `LyricTransientFailurePolicyTests` 全绿 |

### 4.3 缓存与持久化

| # | 检查项 | 判定 |
|---|---|---|
| 11 | 原始/解析后缓存读写 | `LyricPipelineCacheTests`、`LyricCacheStoreTests` 全绿 |
| 12 | 解析结果缓存与协调 | `ResolvedLyricCacheTests`、`ResolvedLyricCacheCoordinatorTests` 全绿 |
| 13 | SQLite 用户数据（偏移量） | `TrackLyricOffsetStore` 相关测试全绿 |
| 14 | 歌曲搜索映射表 | `SongSearchMapResolver` 相关测试全绿 |

### 4.4 探针验证（9 项，云端与本地结果一致）

来自已归档的验证仓库 `DC1024/ff-lyrics-verify`，`docs/verification-report.md`：

```
[PASS] V1  ver=0.2.0.0, TFM=.NETStandard,Version=v2.1, runtime=10.0.14
[PASS] V2  lines=2, syllables[0]=2, text[0]='qrczero'
[PASS] V3  lines=2, syllables[0]=3, text[0]='krczeroword'
[PASS] V4  lines=4, first='alpha fixture line'
[PASS] V5  lines=2, text[0]='yrczeroword'
[PASS] V6  往返一致, len=63, roundtrip=OK (12ms)
[PASS] V7  往返一致, len=71, roundtrip=OK (6ms)
[PASS] V8  Newtonsoft.Json=13.0.0.0, ICSharpCode.SharpZipLib=1.4.2.13
[PASS] V9  loaded=[False, True, True, True]  (3/4)
总计: 9/9 通过
```

V9 的 3/4 是**预期结果**：四个源里有一个在当前网络环境下不可达，不属于移植缺陷。

---

## 5. 依赖基线

| 包 | 版本 | 备注 |
|---|---|---|
| `Lyricify.Lyrics.Helper` | 0.2.0 | `netstandard2.1`，从 `net10.0-windows` 消费 |
| `Microsoft.EntityFrameworkCore.Sqlite` | 10.0.14 | **不可降到 10.0.0**，见 §2 |
| `F23.StringSimilarity` | 7.0.1 | |
| `Newtonsoft.Json` | 13.0.0.0（运行时） | 由 Lyricify 传递引入 |
| `ICSharpCode.SharpZipLib` | 1.4.2.13（运行时） | 由 Lyricify 传递引入 |

`dotnet list package --vulnerable --include-transitive` 基线：**没有易受攻击的包**。

---

## 6. 已修复的两个"只在 CI 上炸"的问题

留档，因为这两个都属于本地怎么试都复现不了的类型。

### 6.1 测试夹具的 CRLF 污染

**现象**：PR #1 的 `J3 单元测试` 挂掉，4 个解密测试 `Assert.Equal() Failure: Strings differ`，
本地 326 全绿。

**根因**：仓库根 `.gitattributes` 有 `* text=auto`，Windows runner 检出时把夹具转成 CRLF，
而解密/解压得到的内容是 LF。本地 `core.autocrlf=input` 恰好把文件保持为 LF，完全掩盖了问题。

**修复**（两层，缺一不可）：
1. `FluentFlyout.Lyrics.Tests/.gitattributes` 把 `TestData/Lyrics/*` 全部标记 `-text`
2. `ReadFixture` 做 LF 归一化，作为第二道防线

**验证方式**：脚本把全部夹具转成 CRLF 模拟 CI → 326 全绿；还原为 LF → 326 全绿；
最后在真实 runner 上确认 `Passed: 326`。

### 6.2 仓库布局耦合

**现象**：迁移后 16 个测试报 `Assert.NotNull() Failure: Value is null`。

**根因**：`ReadFixture` 靠向上查找 `TaskbarLyrics.sln` 并硬编码 `"TaskbarLyrics.Core.Tests"`
目录名来定位 `TestData`。移植后解决方案叫 `FluentFlyout.sln`、项目目录也改了名，两个前提同时失效。

**修复**：改读 `AppContext.BaseDirectory/TestData/Lyrics`，由 csproj 的
`<None Update="TestData\**\*"><CopyToOutputDirectory>PreserveNewest` 保证文件就位。
这样测试不再依赖仓库目录结构。

---

## 7. 刻意保留的"不完美"

记录在案，避免后来者当成 bug 去"修"。

| 项 | 现状 | 原因 |
|---|---|---|
| SPDX 出处说明 | 文件头仍写「源自 TaskbarLyrics ... 的 `TaskbarLyrics.Core`」 | 许可归属信息，不是命名空间，改了会破坏 MIT 归属声明 |
| 运行时数据目录 | 仍是 `%APPDATA%\TaskbarLyrics\database` | 改它会让已有用户的缓存与偏移数据"消失"，需要独立的迁移方案 |
| `CLAAssistant` 检查 | fork 上永远失败 | 依赖上游的 secrets 与 CLA 服务配置，fork 里没有 |
| `-r win-arm64` | 只验证编译 | `windows-latest` 是 x64 runner，跑不了 ARM64 产物 |

---

## 8. 复现整套基线

```bash
# 环境：.NET SDK 10.0.403（WorkBuddy 自带）
export DOTNET_ROOT="$HOME/.workbuddy/binaries/dotnet10"
export PATH="$DOTNET_ROOT:$PATH"

git clone https://github.com/DC1024/FluentFlyout.git
cd FluentFlyout
git checkout <baseline-sha>

dotnet build FluentFlyout.Lyrics/FluentFlyout.Lyrics.csproj -c Release -warnaserror
dotnet test  FluentFlyout.Lyrics.Tests/FluentFlyout.Lyrics.Tests.csproj -c Release
```

预期输出：

```
已成功生成。
    0 个警告
    0 个错误

已通过! - 失败: 0，通过: 326，已跳过: 0，总计: 326
```

> **本机踩坑**：`dotnet` 在 `PATH` 上指向 `C:\Program Files\dotnet\dotnet.exe`，
> 但那台机器只装了**运行时**没有 SDK，直接跑会报 `No .NET SDKs were found`。
> SDK 实际在 `~/.workbuddy/binaries/dotnet10`，必须显式设 `DOTNET_ROOT` 并前置到 `PATH`。

---

## 9. 下一步

P1 到此结束。P2 开始做 WPF 原生渲染层，届时会新增：

- `IMusicSessionProvider` 适配器（接 FluentFlyout 现有的媒体会话）
- `LyricFramePump`（把 `LyricDisplayFrame` 推给渲染层）
- 渲染层本体

基线的意义在于：P2 每加一块，都能拿这里的 326 做回归，确认 Core 层没被带坏。
