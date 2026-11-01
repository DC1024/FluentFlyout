<h1 align="center">FluentFlyout Lyrics</h1>

<p align="center">
  <strong>Synchronized lyrics in the Windows taskbar — on top of everything FluentFlyout already does.</strong>
</p>

<p align="center">
  <a href="LICENSE"><img alt="License" src="https://img.shields.io/badge/license-GPL--3.0-blue"></a>
  <img alt="Platform" src="https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-lightgrey">
  <img alt="Upstream" src="https://img.shields.io/badge/upstream-FluentFlyout%202.16-informational">
  <img alt="Tests" src="https://img.shields.io/badge/tests-900%20cases-success">
</p>

<p align="center">
  <strong>English</strong> | <a href="README.zh.md">简体中文</a>
</p>

---

FluentFlyout Lyrics is a fork of [FluentFlyout](https://github.com/unchihugo/FluentFlyout) — the Fluent 2 media flyout for Windows with an audio flyout, a lock-key flyout and a taskbar widget.

This fork keeps **every feature of the original** and adds a lyrics engine that draws synchronized lyrics inside the taskbar widget: word-by-word highlighting, a translation line, and first-class support for vertical taskbars. It also fixes a set of everyday problems and puts the lyrics pipeline under automated test.

It is an enhancement, not a rewrite — upstream changes are merged monthly.

## What's different 🆚

| | FluentFlyout (upstream) | FluentFlyout Lyrics |
|---|---|---|
| Audio / lock-key / up-next flyouts | ✅ | ✅ |
| Taskbar widget | ✅ | ✅ |
| **Taskbar lyrics** | ❌ | ✅ word-by-word · translation line · vertical taskbars · per-track offset |
| Lyrics sources | — | QQ Music · Kugou · NetEase Cloud Music · LRCLIB |
| Autostart behaviour | opens the settings window | starts silently in the tray |
| Lyrics pipeline tests | — | 900 automated cases |

### 1. Taskbar lyrics 🎤

The largest addition. Lyrics replace the title and artist inside the taskbar widget while a track is playing.

<p align="center">
  <picture>
    <source width="600" media="(prefers-color-scheme: dark)" srcset="docs/images/lyrics-horizontal-dark.png" />
    <source width="600" media="(prefers-color-scheme: light)" srcset="docs/images/lyrics-horizontal-light.png" />
    <img width="600" src="docs/images/lyrics-horizontal-light.png" alt="Current lyric line with its translation underneath">
  </picture>
</p>
<p align="center">
  <picture>
    <source width="120" media="(prefers-color-scheme: dark)" srcset="docs/images/lyrics-vertical-dark.png" />
    <source width="120" media="(prefers-color-scheme: light)" srcset="docs/images/lyrics-vertical-light.png" />
    <img width="120" src="docs/images/lyrics-vertical-light.png" alt="The same lyrics on a vertical taskbar, one glyph per row">
  </picture>
</p>
<p align="center">
  <sub>Rendered at 200% scale — horizontal taskbar above, vertical below. The widget draws the lyrics; the taskbar background is drawn by the screenshot probe.</sub>
</p>

- **Word-by-word highlighting** — karaoke-style fill for sources that carry per-syllable timing, with a three-level scan fallback when the timed source only gives line-level data
- **Translation line** — the second line shows the translation when the source has one, otherwise the next line
- **Vertical taskbars** — lyrics run one glyph per row down the taskbar, and a line too long for the screen scrolls along the main axis instead of being cut off
- **Four taskbar orientations** — top, bottom, left and right are all measured and laid out separately; the left taskbar gets a mirrored layout rather than a rotated one
- **Per-track offset** — nudge lyrics earlier or later in milliseconds; saved per track
- **Custom colour and alignment** — follow the system theme, or set your own `#RRGGBB` / `#AARRGGBB` / `#RGB`
- **Runs alongside the visualizer** — both can be enabled at once and share the available width
- **Credit blocks kept out of the capsule** — Chinese sources ship the opening title card and the full credit roll as *timed lyric lines*, which used to play for thirty seconds and stretch the whole song's width to the taskbar ceiling. Both are now dropped before layout, so the capsule is sized by real lyrics

### 2. Silent autostart

Upstream has two autostart entries registered at the same time — the MSIX `windows.startupTask` and an `HKCU\...\Run` value — and neither passes an argument that says "this launch came from autostart". The second copy fails to grab the single-instance mutex and signals the first one to open the settings window, so **every boot ends with the settings window on screen**.

This fork passes a `--minimized` flag from the autostart entry, treats a second instance carrying that flag as "exit silently", and stops writing the `Run` value at all when running with packaged identity — the manifest's `startupTask` already covers that case. Double-clicking the icon still opens the settings window, as it should.

### 3. Bugs fixed

- **Vertical taskbars showed no lyrics at all.** The panel width was never written by the host; a `Canvas` inside a `Width="Auto"` stack panel measures to zero, so nothing was ever laid out — and nothing errored either.
- **Long vertical lines were cut off mid-word.** The panel now measures the wrapped height with a real `TextBlock` and grows along the main axis to fit.
- **The lyrics database never created its table**, so every track logged `no such table` before falling back.
- **NetEase Cloud Music and Kugou never advanced the lyrics.** Those players leave the Windows media timeline at zero; the fork now falls back to a local monotonic clock, guarded by a track-length sanity check (≥ 30 s) so a bogus estimate can't make the position wrap early.
- **Repeat-one left the local clock stuck at the end of the track.** It now resets on wrap.
- **Opening the lyrics settings page crashed the app.** One icon name in the alignment card was not a real enum value; it only threw at runtime, when the page was constructed.
- **The lyrics panel resized on every line.** Width is now measured once per track from the widest line, so the capsule stops breathing as lines change.
- **Unplugging a monitor moved the widget to another screen.** The target display is now identified by device path instead of index.
- **Instrumental frames re-applied stale content.** Frame application is now idempotent.

### 4. Performance guards

- The frame pump runs at 33 ms and stops entirely when lyrics are off — no idle cross-process COM calls
- Word-by-word scanning drops to cheaper strategies when the timed data doesn't need the expensive one
- A budget counter caps full-window relayouts, so a long line can't trigger a re-layout on every frame
- Lyrics and the visualizer can share the taskbar instead of one silently disabling the other

### 5. Engineering

- **900 test cases**, spread across 550 `[Fact]` and 88 `[Theory]` methods (a theory's data rows each expand into their own case). The lyrics engine is kept free of `System.Windows`, so retrieval, parsing, timing and layout maths are all testable without a UI thread
- A **72-screenshot visual acceptance matrix** covering four taskbar orientations × light/dark × multiple DPI scales
- A CI gate that rejects icon names which aren't real enum values — the class of bug that crashed the settings page
- A portable build workflow, so a testable binary is one download away
- [Architecture notes](docs/lyrics-integration/architecture.md) documenting the layer boundaries and the non-obvious timing rules

## Install 📥

This fork does not ship a Microsoft Store build and has no signed MSIX. Three options:

### 1. Grab a portable build (recommended)

Open [the portable build workflow](https://github.com/DC1024/FluentFlyout/actions/workflows/build-portable.yml), pick a successful run, download `FluentFlyoutLyrics-portable-win-x64.zip`, extract it and run `FluentFlyoutLyrics.exe`.

Requires the [.NET 10 desktop runtime](https://dotnet.microsoft.com/download/dotnet/10.0).

### 2. Build it yourself

```powershell
git clone https://github.com/DC1024/FluentFlyout.git
cd FluentFlyout
dotnet publish FluentFlyoutWPF/FluentFlyout.csproj -c "GitHub Release" -r win-x64 --self-contained false
```

Use the `GitHub Release` configuration — it defines `GITHUB_RELEASE`, which is what makes the licence manager skip the Store entitlement check. A plain `Release` build will look for an entitlement you don't have.

### 3. Coming from upstream

Settings live in `%APPDATA%\FluentFlyout` and the single-instance mutex is unchanged — both deliberate, so your settings and lyric cache carry over untouched.

The executable name did change though, so hand over once:

1. Quit the original from its tray menu
2. Delete the old `FluentFlyout.exe` (or the whole old folder)
3. Run `FluentFlyoutLyrics.exe` once — it rewrites the autostart entry to point at itself

Don't run both at once: they share the mutex, so the second one simply exits.

Upgrading **between releases of this fork** needs none of that. Settings, the lyric cache and the single-instance identity all live outside the install folder, so replacing the folder is the entire procedure.

## Settings

All lyrics options are on the **Lyrics** page. The taskbar widget has to be on first.

| Setting | What it does |
|---|---|
| Show lyrics on the taskbar | The switch itself. Needs the taskbar widget |
| Lyrics font size | In pixels. Shrinks automatically when the taskbar is too short |
| Show translation line | Second line shows the translation when available, otherwise the next line |
| Word-by-word highlight | Light up the current line as it is sung. Off saves CPU |
| Force single line | Current line only — no translation, no next line |
| Lyrics color | Empty follows the system theme. Accepts `#RRGGBB`, `#AARRGGBB` or `#RGB` |
| Text alignment | Horizontal position of short lines. Lines that need scrolling always start from the left |
| Offset for current track | Positive makes lyrics earlier, negative later, in milliseconds. Saved per track |

Lyrics are looked up from QQ Music, Kugou, NetEase Cloud Music and LRCLIB, in that order. The first play of a track needs a network lookup; results are cached locally afterwards.

## Relationship with upstream

- Upstream: [unchihugo/FluentFlyout](https://github.com/unchihugo/FluentFlyout). All credit for the original app goes there.
- Upstream changes are merged **monthly**, as merge commits rather than squashes so the ancestry stays intact and the next merge doesn't replay the same diff.
- This fork's own work sits on top; conflicts are resolved by hand rather than silently dropped.

## Known differences

- Only the **English and Simplified Chinese** dictionaries carry the new product name. Dutch and Turkish use possessive suffixes (`FluentFlyout's`, `FluentFlyout'un`) that a blind rename would turn into broken grammar, and the remaining 27 languages are maintained upstream on Weblate — renaming them here would only create merge conflicts. This is a trade-off, not an oversight.
- The root namespace is still `FluentFlyoutWPF`. It is an internal identifier; renaming it would touch 127 files with zero user-visible benefit.
- The MSIX packaging path is defined but not exercised: there is no Store signing certificate in this repository.

## Credits 🙌

- [Hugo Li](https://unchihugo.github.io) — original developer of FluentFlyout, Microsoft Store publisher, CN & NL translations
- [ANYNC](https://github.com/ANYNC/TaskbarLyrics) — TaskbarLyrics, whose Core layer the lyrics engine was ported from
- [LiAuTraver](https://github.com/LiAuTraver), [AksharDP](https://github.com/AksharDP), [nopeless](https://github.com/nopeless) — upstream code contributors
- [Hykerisme](https://github.com/Hykerisme) — upstream CN translation

## License

GPL-3.0-or-later, inherited from FluentFlyout — see [LICENSE](LICENSE). The TaskbarLyrics-derived core keeps its MIT notice in [LICENSE-TaskbarLyrics](LICENSE-TaskbarLyrics). Third-party components are listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

### Dependencies

- [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet)
- [Dubya.WindowsMediaController](https://github.com/DubyaDude/WindowsMediaController)
- [MicaWPF](https://github.com/Simnico99/MicaWPF)
- [Microsoft.Toolkit.Uwp.Notifications](https://github.com/CommunityToolkit/WindowsCommunityToolkit)
- [NAudio](https://github.com/naudio/NAudio)
- [NLog](https://nlog-project.org/)
- [unchihugo.WPF-UI](https://github.com/unchihugo/wpfui)
- [WPF-UI-Tray](https://github.com/lepoco/wpfui)
