# Changelog

All notable changes to **FluentFlyout Lyrics** are recorded here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project uses
[Semantic Versioning](https://semver.org/). The fork points at FluentFlyout **2.16.1**; everything
below is this fork's own history, not upstream's.

## [Unreleased]

### Fixed

- Taskbar XAML lookups no longer block the UI thread. The calls already ran on the thread pool, but
  the dispatcher thread then sat in `Wait(1000)` / `Wait(500)` for up to three elements per pass — 4.5
  seconds of stall against a 1.5-second timer, measured in the wild as four query timeouts followed by
  4.4 seconds without audio callbacks. Because the widget window is a cross-process child of the
  taskbar, a blocked UI thread also stalls Explorer's synchronous messages and its own UIA walk into
  that subtree, which is how a slow lookup turned into an unclickable taskbar. Probes now start
  without waiting, are harvested when they finish, and a probe that finds a *changed* rectangle wakes
  the layout up instead of the caller waiting for it
- The widget no longer stays invisible for the rest of the session when Explorer takes longer than a
  minute to come back. The "Explorer is restarting" gate used to be a boolean that only the success
  path cleared, so a single timeout — or a single exception — froze taskbar integration until the app
  was restarted; it is now a deadline that expires on its own, and recovery releases it on every path

### Changed

- Install instructions point at the release page now. The old advice — dig a zip out of the Actions
  artifacts — still works for unreleased `master` builds, but those need a GitHub login and expire
  after thirty days, which is a poor first experience for a release
- Startup is diagnosable from the log now: every widget gets one line when it attaches to a taskbar
  (which handle, whether it fell back to the main bar) and one when it first manages to position
  itself, plus a throttled warning when positioning is skipped because the taskbar handle is not
  ready yet — that branch used to return silently, which is why "sometimes it doesn't come up after
  boot" left nothing behind to look at. Ten log files are kept instead of three

## [1.0.0] — 2026-11-01

First release. 27 pull requests, 2026-10-27 → 2026-11-01.

### Added

**Taskbar lyrics** — lyrics replace the title and artist inside the taskbar widget while a track plays.

- Word-by-word highlighting, with a three-level scan fallback for sources that only carry line timing
- Translation line under the current lyric
- QQ Music, Kugou, NetEase Cloud Music and LRCLIB as sources
- Per-track offset in milliseconds, saved per song
- Custom colour (`#RRGGBB` / `#AARRGGBB` / `#RGB`) and alignment
- Eight lyrics settings on a dedicated page
- Four taskbar orientations measured and laid out separately; the left taskbar is mirrored rather
  than rotated, and the vertical layout runs one glyph per row down the main axis
- A line too long for a vertical taskbar scrolls along the main axis instead of being cut off
- Instrumental tracks lay out a proper placeholder instead of repeating the last frame

**Junk metadata is kept out of the capsule** — Chinese sources ship the opening title card
(`song - artist`, up to thirty seconds of screen time) and the full credit roll as *timed lyric
lines*. Both are now recognised and dropped before layout, so the capsule is sized by real lyrics
rather than by whichever recording-credits line happens to be the longest.

### Changed

- Renamed to **FluentFlyout Lyrics** (`FluentFlyoutLyrics.exe`, `FluentFlyoutLyrics` assembly).
  Settings folder, single-instance mutex, window events and autostart value names were deliberately
  left alone, so an upgrade from upstream keeps settings and lyric cache intact
- Autostart now starts silently in the tray instead of opening the settings window on every boot
- Synced upstream commits up to 2.16.1, including the taskbar widget's first-frame flash fix
- README rewritten for the fork, in English and Chinese, with screenshots and a settings table

### Fixed

- **Vertical taskbars showed no lyrics at all** — the panel width was never written, so a `Canvas`
  inside a `Width="Auto"` stack panel measured to zero and nothing was laid out. No error either
- **Long vertical lines were cut off at the sixth character** — the panel now measures its wrapped
  length and grows along the main axis
- **The left taskbar rendered unmirrored** — the block was rotated where it should have been mirrored
- **Vertical lines triggered a full-window relayout on every wrap** — the panel now reports its main
  axis length instead
- **NetEase Cloud Music and Kugou never advanced the lyrics** — those players leave the Windows media
  timeline at zero; the pipeline falls back to a local monotonic clock, guarded by a track-length
  sanity check so a bogus estimate cannot make the position wrap early
- **Repeat-one left the clock stuck at the end of the track** — it now resets on wrap
- **The capsule resized on every line** — width is measured once per track instead of per line
- **Opening the lyrics settings page crashed the app** — one icon name in the alignment card was not a
  real enum value, which only throws when the page is constructed
- **The lyrics database never created its table**, so every track logged `no such table` before
  falling back
- **Unplugging a monitor moved the widget to another screen** — the target display is identified by
  device path now, not by index
- **Instrumental frames re-applied stale content** — frame application is idempotent again

### Engineering

- Test project built from scratch: **900 cases** across 550 `[Fact]` and 88 `[Theory]` methods. The
  lyrics engine avoids `System.Windows`, so retrieval, parsing, timing and layout maths are testable
  without a UI thread
- A 72-screenshot visual acceptance matrix: four orientations × light/dark × several DPI scales
- A CI gate that rejects icon names which are not real enum values — the class of bug that crashed
  the settings page
- A portable build workflow, so a testable binary is one download away
- Architecture notes and a performance baseline under `docs/lyrics-integration/`
