# Changelog

All notable changes to SmartZoom are recorded here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow
[semantic versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

Nothing yet.

## [0.1.1] - 2026-09-24

### Fixed

- **Chromium builds whose accessibility tree never reaches a document node can be zoomed.** Some Chrome and
  Edge installations (seen with Chromium's native UI Automation provider switched on) answer the hit-test
  with a chain of elements that has no document at its top, and every press ended in "no accessible
  content". The render window's own rectangle is the page in every build, so it now stands in for the
  missing document.

### Changed

- The debug log line for a press that found no accessible content now includes the path SmartZoom did get
  back (roles, sizes and raw role numbers), so a diagnostic report from a machine where nothing zooms says
  what the browser's tree looks like there.

## [0.1.0] - 2026-09-24

### Added

- A release workflow: pushing a `vX.Y.Z` tag builds the installer and the single executable, checks the tag
  against the version in `Directory.Build.props` and the CHANGELOG, and opens a draft GitHub Release with
  both files and their SHA-256 checksums attached (`docs/releasing.md`).
- Smart zoom in Chromium browsers and Firefox: the block under the cursor is found in the accessibility tree
  and magnified with a synthetic touch pinch, with no browser extension.
- Smart zoom in Word and Excel through their object models.
- Smart zoom in PDF readers: an animated pinch around the cursor that lands on the reader's own fit-page
  command, so repeated presses cannot drift. `Zoom.Reader.Mode: "Shortcuts"` uses the reader's fit-width and
  fit-page shortcuts instead, for a reader that ignores touch.
- Ctrl+wheel zoom with an exact toggle back for applications with no better strategy.
- Triggers on mouse buttons or keyboard shortcuts, single or double tap, with the press optionally swallowed.
- A settings window: triggers are recorded by pressing them, applications are routed with a picker built from
  what the strategies declare about themselves, and the settings people actually turn have controls.
  Double-click the tray icon, or start SmartZoom while it is already running, to open it.
- Settings apply without a restart, from the window and from **Reload settings file** in the tray menu.
- A per-user installer, `SmartZoom-<version>-setup.exe`, built by `install/build.ps1` and by CI. It never
  asks for administrator rights, carries its own .NET runtime, offers to start SmartZoom at sign-in, upgrades
  in place over a running copy, and keeps your settings on uninstall unless you ask otherwise. It asks how you
  want to start a zoom through SmartZoom's own recorder, pre-filled with the current trigger: Cancel keeps
  what was there. Silent installs never ask.
- `SmartZoom.exe --record-trigger` asks for a trigger and writes it; `--trigger <button-or-keys> [--taps 1|2]
  [--swallow]` writes one without asking, for scripted deployment; `--quit` asks a running copy to close
  cleanly.
- A local diagnostics record and a Diagnostics page in Settings. SmartZoom keeps a bounded tally of presses
  that zoomed nothing, adapters that threw, crashes and how well injected gestures were delivered, at
  `%LOCALAPPDATA%\SmartZoom\diagnostics.json`, and renders it on demand as a markdown report with **Refresh**,
  **Copy**, **Save…**, **Clear recorded data**, an on/off switch (`Diagnostics.Enabled`, on by default) and an
  opt-in **Include recent log lines**. **Diagnostic report…** in the tray menu opens that page. Nothing is
  sent anywhere; [SECURITY.md](SECURITY.md) says what it holds and what it never holds, and
  [docs/diagnostics-design.md](docs/diagnostics-design.md) says why.
- The tray tooltip names the last zoom, the strategy that performed it and how long ago a press last arrived
  ("Zoomed in brave via Browser (2 min ago)", or "no press seen yet"), and the log notes every quarter of an
  hour that it is listening and nothing has arrived.
- `Logging.Level` in the settings file.
- The debug log reports each gesture's pacing (frames sent, worst lateness, frames that missed their slot),
  and names the raw accessibility role of any element it could not classify (`Other(16)`).
- `tools/SmartZoom.Probe`, the tool every measured constant came from, is part of the repository and built by
  CI. It drives the same code the tray app does and can reproduce each number in `docs/measurements.md` on
  other hardware; `smartzoom-probe track` reports the scale each captured frame of a zoom reached.
- An application icon.
- CI checks formatting, collects coverage, builds the framework-dependent single-file executable and the
  installer.
- Contributor documentation: architecture, adding an application, design decisions, measured constants, the
  diagnostics design and a manual testing guide.

### Changed

- `Routing` is a single `Apps` map of process name to strategy id, e.g. `"Apps": { "notepad": "CtrlWheel" }`.
  The ids are `Browser`, `Reader`, `WordCom`, `ExcelCom`, `CtrlWheel` and `None`. Defaults live in the code
  rather than in the settings file, so upgrading brings support for new applications with it, and an
  application only needs an entry when the default is wrong.
- `Zoom.Smart` holds the `MarginPx` and `AnimationMs` that browsers, Word and Excel share; `Zoom.Browser`
  keeps only `AnchorInsetPx`. `Zoom.Reader` names its fixed factor `Magnification` (which `MinScale` and
  `MaxScale` do not clamp) and its scroll gap `TopGapPx`.
- A settings file from an older build still loads: keys it carries for shapes that no longer exist are
  ignored and those settings fall back to their defaults. There is no migration.
- Naming a strategy the build does not have is reported in the log and falls back to Ctrl+wheel rather than
  doing nothing.
- Gesture frames are paced to the refresh rate of the monitor under the cursor, one contact update per
  refresh, rather than a fixed 8 ms.
- The log file is opened shared, so the Diagnostics report can include today's log while SmartZoom is still
  writing it.
- Adding support for an application is one class and one registration; the router is built from the
  registered adapters, so a strategy the build does not contain cannot be routed to.
- The gesture geometry, recognizer thresholds, decoration policy and reader scroll arithmetic live in
  `SmartZoom.Core`, unit-tested against the values in `docs/measurements.md`.

### Removed

- PowerPoint is no longer in the default routing. It had no adapter, so a press there did nothing at all; it
  falls through like any other unsupported application.

### Fixed

- Pages built out of iframes can be zoomed: only the outermost document is the page, and the frames below it
  are containers like any other.
- Pages built out of plain `div`s can be zoomed: Chromium's generic container role is treated as a block.
- A browser zoom no longer begins with a visible shrink and rebound in Edge: the preparatory pinch-out that
  Chromium clamped invisibly is gone.
- The Close button and Escape on the settings window close it.
- **Reload settings file** on a malformed JSON file reports the parse error instead of replacing the file
  with defaults; a locked or unreadable file is reported too.
- The tray's **Enabled** check mark follows the live setting after Save or Reload.
- Word: a COM failure part-way through a zoom restores the previous zoom, as Excel already did.
- The reader shortcut path no longer pulls focus back to the previous window when the user switched windows
  during the wait.
- Settings window: the log-level list shows every level, and an out-of-range value in the file is no longer
  silently clamped and saved.
- Diagnostics: the 256 KB ceiling is measured in bytes; unhandled UI-thread exceptions the app survives are
  recorded with an `UiThread` slot; concurrent flushes cannot lose recorded data; the report is built off the
  UI thread.
- `--record-trigger` followed by Cancel no longer creates a settings file.
- `Routing.Apps` keys stay case-insensitive after loading from the file.
- Double-tap swallow: a press arriving while a replayed release is still owed replays that release first.
- `smartzoom-probe`: a missing argument prints which one is missing; capture analysis is faster.

[Unreleased]: https://github.com/Phoenix6001/SmartZoom/compare/v0.1.1...HEAD
[0.1.1]: https://github.com/Phoenix6001/SmartZoom/releases/tag/v0.1.1
[0.1.0]: https://github.com/Phoenix6001/SmartZoom/releases/tag/v0.1.0
