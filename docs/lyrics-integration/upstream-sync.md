# 上游同步手册

两个仓库都是 fork，会持续收到上游更新。本文规定**什么时候同步、怎么同步、冲突怎么处理**。

配套文件：`baseline.md`（当前冻结的基线 SHA 与回归数字）。

---

## 1. 三仓库拓扑

```
unchihugo/FluentFlyout  ──fork──>  DC1024/FluentFlyout  ──>  本地 forks/FluentFlyout
        (上游)                          (我们的主战场)              master + feat/*

ANYNC/TaskbarLyrics     ──fork──>  DC1024/TaskbarLyrics ──>  本地 forks/TaskbarLyrics
        (上游)                     (只读参考，不提交)           （只在对比实现时 fetch）
```

本地两个 clone 都配了 `upstream` 远端，但 **push URL 被设为 `DISABLED`**：

```bash
git remote -v
# upstream  https://github.com/unchihugo/FluentFlyout.git (fetch)
# upstream  DISABLED (push)
```

这是故意的。`git push upstream` 会直接报错，而不是尝试推送到别人的仓库。
**不要**为了"方便"把它改回来。

---

## 2. 分支模型

| 分支 | 用途 | 保护 |
|---|---|---|
| `master` | 与上游对齐的主线，只接受 PR 合并 | ruleset `protect-master`，要求 J1 + J2(win-x64) 通过 |
| `feat/*` | 单个功能或阶段 | 无 |
| `refactor/*` | 纯重构，不改行为 | 无 |

`master` 的 ruleset 还会阻止删除和 force-push。**不要绕过它**——如果某个改动必须直推，
说明分支模型用错了，应该开 PR。

---

## 3. 同步上游的时机

不是越勤越好。三个明确的触发点：

| 触发点 | 动作 |
|---|---|
| 准备开一个新的功能分支前 | `git fetch upstream` 看一眼，有更新就合到 `master` |
| 上游发了新版本 tag | 必同步，并跑一遍 326 回归 |
| 上游改动了媒体会话 / 任务栏相关代码 | 必同步，因为 P2 渲染层要接这些接口 |

日常开发中途**不要**随手 merge upstream。半合不合的状态会让 CI 失败原因难以归因。

---

## 4. 同步 FluentFlyout

```bash
cd forks/FluentFlyout

# 1. 先看清楚有什么
git fetch upstream
git log --oneline HEAD..upstream/master
git diff --stat HEAD..upstream/master

# 2. 在 master 上合并（本地 master 可以直推，推远端要走 PR）
git checkout master
git merge upstream/master

# 3. 跑回归
dotnet build FluentFlyout.Lyrics/FluentFlyout.Lyrics.csproj -c Release -warnaserror
dotnet test  FluentFlyout.Lyrics.Tests/FluentFlyout.Lyrics.Tests.csproj -c Release

# 4. 推送（master 有保护，需要走 PR 或临时调整 ruleset）
```

### 第 4 步的现实问题

`master` 受 ruleset 保护，本地 merge 完**推不上去**：

```
remote: error: GH006: Protected branch update failed
remote: 2 of 2 required status checks are expected
```

两种处理方式，按情况选：

**A. 走 PR（推荐，日常）**
```bash
git checkout -b sync/upstream-$(date +%Y%m%d)
git push origin sync/upstream-20261027
# 开 PR，等 J1/J2 通过，合并
```

**B. 临时放宽 ruleset（仅当上游改动巨大、需要一次性对齐时）**
在 GitHub 仓库 Settings → Rules → `protect-master` 里临时把 enforcement 设为 Disabled，
推送后**立刻改回 Active**。这个操作要留记录，否则下次有人发现保护是关的就麻烦了。

---

## 5. 同步 TaskbarLyrics

这个仓库**只读**。我们不再向它提交，也不需要合并它的更新。

只在需要对比 Core 实现时 fetch：

```bash
cd forks/TaskbarLyrics
git fetch upstream
git diff upstream/master -- TaskbarLyrics.Core/    # 看上游改了什么
git log --oneline HEAD..upstream/master            # 看提交列表
```

如果上游修了 Core 层的 bug，需要**手工**把那个修复搬到 FluentFlyout 侧
（`FluentFlyout.Lyrics/` 对应文件），并补一个测试。不要尝试 merge——两边的目录结构、
命名空间、TFM 都已经不同，merge 只会产生一堆无意义冲突。

### 搬运上游修复的检查清单

- [ ] 找到对应的 FluentFlyout 侧文件（命名空间已从 `TaskbarLyrics.Core.*` 改为 `FluentFlyout.Lyrics.*`）
- [ ] 保留文件头部的 SPDX 出处说明
- [ ] 补一个能复现该 bug 的测试（如果上游没带测试）
- [ ] `-warnaserror` 下 0 警告
- [ ] 326 基线不退化

---

## 6. 冲突处理

### 6.1 预期会冲突的地方

| 位置 | 为什么会冲突 |
|---|---|
| `FluentFlyout.sln` | 上游增删项目时会重排配置块，我们这边也有改动 |
| `.github/workflows/*.yml` | 上游会调 action 版本，我们加了 `verify.yml` |
| `FluentFlyoutWPF/` 下的文件 | P2 之后我们会往主工程里加代码 |
| `README.md` | 上游常改 |

### 6.2 处理原则

1. **`FluentFlyout.sln` 冲突不要手工解**。正确做法是：
   ```bash
   git checkout --theirs FluentFlyout.sln    # 先取上游版本
   dotnet sln FluentFlyout.sln add FluentFlyout.Lyrics/FluentFlyout.Lyrics.csproj
   dotnet sln FluentFlyout.sln add FluentFlyout.Lyrics.Tests/FluentFlyout.Lyrics.Tests.csproj
   dotnet sln FluentFlyout.sln add FluentFlyout.Lyrics.Probe/verify.csproj
   ```
   让工具重建，比手工对齐 GUID 可靠得多。

2. **`verify.yml` 冲突时保留我们的版本**，但检查上游是否升级了 action 版本
   （`checkout@vN`、`setup-dotnet@vN`），有就跟着升。

3. **`FluentFlyoutWPF/` 的冲突**：这部分是 P2 之后的主要风险区。原则是
   **上游优先**——我们的歌词代码应该尽量集中在独立文件里，减少对上游文件的侵入式修改。
   如果发现某个改动必须动上游的核心文件，先想想能不能用 partial class 或事件订阅绕开。

---

## 7. 权限与凭据

| 项 | 要求 |
|---|---|
| PAT scope | `repo` + **`workflow`**（改 `.github/workflows/` 必须有 workflow scope，否则推送被拒） |
| 仓库可见性 | **public**。私有仓库的 Windows runner 按 2× 分钟计费 |
| Actions | fork 上需要**手动**在 Actions 页面点一次启用 |

### 本机的一个坑

`~/.git-credentials` 里有 PAT，但 **WorkBuddy 自带的 PortableGit 没有 `git-credential-store.exe`**：

```bash
ls /mingw64/bin/git-credential*
# git-credential-helper-selector.exe
# git-credential-manager.exe
# git-credential-wincred.exe
# —— 没有 store
```

所以 `git config credential.helper store` 是个空指令，`git push` 会报
`could not read Username for 'https://github.com'`。

**绕过方式**（不要改全局配置）：

```bash
TOKEN=$(sed -n '1p' ~/.git-credentials | sed 's|https://[^:]*:\([^@]*\)@.*|\1|')
git -c credential.helper= push "https://x-access-token:${TOKEN}@github.com/DC1024/FluentFlyout.git" <branch>
```

### 代理的坑

环境变量里的 `https_proxy=http://127.0.0.1:3321` 指向的是 **WorkBuddy 自己的
`sandbox-cli.exe`，不是代理**。真正的代理在 **7897**（`verge-mihomo.exe`）。

```bash
# 确认哪个端口是真的代理
netstat -ano | grep LISTENING | grep -E ':(3321|7897)'
# 逐个试
curl -s -o /dev/null -w '%{http_code}\n' -m 8 -x http://127.0.0.1:7897 https://github.com
```

推送时显式指定：

```bash
https_proxy=http://127.0.0.1:7897 git -c http.version=HTTP/1.1 push ...
```

---

## 8. CI 分层

| 层级 | 触发 | 内容 | 耗时 |
|---|---|---|---|
| L1 | 每次 push / PR | J1 探针、J2 双 RID 编译、J3 测试、dotnet-format | ~3 min |
| L2 | 合并到 master | L1 全部 + 上游自带的构建/发布流程 | ~10 min |
| L3 | 发版前手动 | 打包 MSIX、实机验证 | 手工 |

L1 的 `CLAAssistant` 在 fork 上**永远失败**——它依赖上游的 secrets 和 CLA 服务配置。
这是预期行为，不影响合并（ruleset 只要求 J1 和 J2 win-x64）。

---

## 9. 常见故障速查

| 症状 | 原因 | 处理 |
|---|---|---|
| `CONNECT tunnel failed, response 502` | 代理端口选错或代理抖动 | 按 §7 确认 7897，重试 |
| `could not read Username` | `credential.helper store` 无效 | 按 §7 用内联 token |
| `GH006: Protected branch update failed` | 直推 master | 开 PR |
| `No .NET SDKs were found` | `dotnet` 指向只有运行时的目录 | `export DOTNET_ROOT=~/.workbuddy/binaries/dotnet10` |
| 测试只在 CI 挂，本地全绿 | 行尾 / 路径 / 环境差异 | 见 `baseline.md` §6 |
| `NETSDK1004: 找不到资产文件` | 删过 `obj/` | `dotnet restore` |
