# 第三方代码来源说明

本仓库是 [unchihugo/FluentFlyout](https://github.com/unchihugo/FluentFlyout) 的 fork，
用于集成 TaskbarLyrics 的逐字歌词能力。整体仍按 **GPL-3.0-or-later** 分发（见 `LICENSE`）。

## 来源清单

| 目录 | 来源 | 许可证 | 说明 |
|---|---|---|---|
| `FluentFlyoutWPF/`、`FluentFlyoutMSIX/`、`FluentFlyout.SourceGenerators/` | [unchihugo/FluentFlyout](https://github.com/unchihugo/FluentFlyout) | GPL-3.0-or-later | 上游原样保留 |
| `FluentFlyout.Lyrics/` | [ANYNC/TaskbarLyrics](https://github.com/ANYNC/TaskbarLyrics) 的 `TaskbarLyrics.Core` | **MIT** | 版权归 anync，原文见 `LICENSE-TaskbarLyrics` |
| `FluentFlyout.Lyrics.Probe/` | 本项目原创 | GPL-3.0-or-later | 兼容性验证探针 |

### 许可证方向

MIT → GPL-3.0 **允许**（MIT 是 GPL-compatible）。因此把 TaskbarLyrics 的 MIT 代码并入
GPL-3.0 的 FluentFlyout 是合法的，前提是：

1. 保留 TaskbarLyrics 的原始版权声明（`LICENSE-TaskbarLyrics`）
2. 在本文件中记录来源
3. 衍生的整体继续按 GPL-3.0 分发

**反向不成立**：GPL-3.0 代码不得并入 MIT 项目后按 MIT 分发。

## 第三方 NuGet 依赖

| 包 | 版本 | 许可证 | 与 GPL-3.0 兼容 |
|---|---|---|---|
| Lyricify.Lyrics.Helper | 0.2.0 | Apache-2.0 | 是（注意其专利条款） |
| F23.StringSimilarity | 7.0.1 | MIT | 是 |
| Microsoft.EntityFrameworkCore.Sqlite | 10.0.14 | MIT | 是 |
| OpenCC | 0.1.0 | Apache-2.0 | 是 |
| Newtonsoft.Json | 13.0.3 | MIT | 是 |
| SharpZipLib | 1.4.2 | MIT | 是 |

> EF Core 取 `10.0.14` 而非 `10.0.0`：后者传递引入 `SQLitePCLRaw.lib.e_sqlite3 2.1.11`，
> 命中 GHSA-2m69-gcr7-jv3q（高危），在 `-warnaserror` 下会升格为 `error NU1903` 阻断构建。

## 待办

- [ ] 向 FluentFlyout 上游开 issue 说明合并意图与许可安排（**在公开发布前完成**）
- [ ] 在每个从 TaskbarLyrics 拷贝的 `.cs` 文件顶部补 SPDX 标识
