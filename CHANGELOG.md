# Changelog

All notable changes to SmartZoom are recorded here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow
[semantic versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Changed

- **Diagnostics is its own page.** The record of what did not work used to be the fourth section of Advanced,
  below zoom tuning, PDF readers and logging — but two other surfaces send people to it: the status card in the
  window's corner counts recorded issues, and the tray offers **Diagnostic report…**. Both landed at the top of
  Advanced, leaving three sections of tuning to scroll past. Both now open a **Diagnostics** row in the rail
  that holds the report, its Refresh / Copy / Save… / Clear buttons, the "include recent log lines" opt-in and
  the switch that turns recording off. Advanced keeps zoom, PDF readers and logging. Nothing about what is
  recorded, or about it never leaving the machine, has changed.

- **One click to report a problem.** The Diagnostics page has a **Report a problem** button: it ticks
  "include recent log lines", rebuilds the report on screen so you can see what it now contains, copies it, and
  opens the bug form with everything it can answer for you already filled in: the title, the application by its
  real name, the build, the display, the issue itself as a starting description, and — where SmartZoom can tell
  — whether vendor mouse software is running or your displays are scaled differently, which are the two things
  that most often explain a press that never arrived. The report and log boxes are deliberately left empty: one
  Ctrl+V fills the first with the whole report, log included, and a box holding only part of a log would look
  finished when it was not. The log goes in because a report without one usually costs a round
  trip — but the box ticks visibly and the report is rebuilt *before* anything is copied, because showing you the
  report is the whole point and a button that quietly widened what gets shared would defeat it. SmartZoom still
  opens no connection of its own: the address goes to your browser, none of the report travels in it, and
  nothing is shared until you paste and submit.

- **The Diagnostics page says what went wrong before it shows the report.** The status card counts issues and
  sends you here; what greeted you was a markdown table saying `ZoomedNothing/NoBlock`. The same events are now
  listed at the top in sentences — "A press in brave zoomed nothing", "Something was found under the cursor, but
  nothing there was a sensible thing to magnify. Often blank page area. · last seen 47 minutes ago" — with a red
  bar for a failure and an amber one for a press that simply found nothing to do, and a plain "nothing has gone
  wrong" when there is nothing to show. Where there is something to do about it, the line says so.
  **The list stays short however much is recorded**, because events are grouped by what happened rather than by
  where: ten applications that all found nothing to magnify are one line — "75 presses in 10 applications zoomed
  nothing", naming the busiest three — not ten copies of the same sentence. Failures come first whatever their
  count. Past five kinds the rest go behind "show more". The report underneath is unchanged, because that is
  the thing worth pasting into a bug report.

- **The Applications page leads with what is supported, and shows each application's own icon and name.**
  It used to open on an empty "Yours" list and a box asking for a process image name — the one place the
  settings window expected you to know whether an application is called "notepad", "Notepad" or "Notepad.exe"
  — with the list of what actually works pushed below it. Now the supported applications come first, pictured
  and named as they name themselves on this machine; anything not installed still shows its process name.
  Routing something of your own moved below as **Exceptions**, and is done by picking from the applications you
  have open, with typing still there for one that is not running. A new exception defaults to Ctrl+wheel rather
  than to whichever strategy sorted first alphabetically.

- **The tray icon goes grey when SmartZoom is switched off.** Whether it is listening is the one thing its
  place in the notification area exists to say, and it used to say it only in a tooltip. The grey icon is
  derived from the colour one at startup rather than shipped as a second file, so the two cannot drift apart.
  Switching off from the tray panel or the settings window greys it at once, not at the next tooltip refresh.

- **The on/off button is only accent-coloured when it is the thing to press.** While SmartZoom is running the
  button reads "Turn off", which is not the action to encourage, so it is now a neutral grey; it returns to
  accent as "Turn on" once zooming is off. In the tray panel and on the Overview page both.

- **The Overview page's "Live preview" card now previews.** Hovering it plays the zoom on the little mock
  document: the block grows to the width of the window showing it, the way a smart zoom fits a block to a page,
  and settles back when the pointer leaves. It was a still drawing that looked exactly like the cards beside it
  that navigate when clicked — so it read as broken rather than decorative. It stays non-clickable and keeps the
  arrow cursor; the motion is its whole answer to the pointer.

### Fixed

- **A zoom over a plain part of a page is no longer mistaken for a page that blocks zooming.** Whether a pinch
  took effect is decided by comparing the screen around the cursor before and after it. Over a wide margin, an
  empty panel or a flat image there is nothing in that region to move, so a gesture that worked perfectly read
  as one the page had refused — and SmartZoom then sent two more pinches at the same magnification on top of
  the zoom that had already happened, each around a point chosen to sit *outside* the block, which is further
  into the same emptiness, before finally stacking a Ctrl+wheel page zoom on all of it. The page ended up
  magnified several times over and the next press took only the last of those back off. A region is now checked
  for whether it has enough in it to show movement at all, and when it has not, the gesture is trusted rather
  than contradicted. A page that genuinely refuses the gesture is detected exactly as before.

- **The pinch is paced for the monitor it lands on, not for the window under the cursor.** The gesture's
  thresholds are distances in real pixels, so they follow the display's scaling; they were read with
  `GetDpiForWindow`, which answers for the window's own DPI awareness instead — the primary display's scaling
  for an older application, and 100% for one that does not handle scaling at all. On a second monitor set to a
  different scale, a PDF reader could therefore be pinched with thresholds meant for another display.

- **A browser page that was left zoomed no longer zooms twice on the next press, and comes back properly.**
  When a page is still showing a zoom SmartZoom does not remember making — it was restarted, switched off while
  the page was zoomed, or a restore did not take — the next press used to zoom on top of it. Chromium clamps
  the visual viewport at x4, so that second gesture was refused, and a refused gesture looks exactly like a page
  that blocks gestures (`touch-action`): SmartZoom fell back to Ctrl+wheel page zoom, which is a *separate* zoom
  stacked on the first. The page magnified twice, and the next press took only one of the two back off, every
  time. The accessibility tree already gives the page's current zoom away, so a page that is carrying one is now
  cleared first and read again, and the zoom is planned from a page at rest.

## [0.3.0] - 2026-09-24

### Added

- **A new settings window, and a panel in the tray.** Left-click the tray icon and a panel drops out of it
  with the things worth changing: on or off, the trigger you press with a Change button beside it, the zoom
  amount as 1.5×, 2× or 3×, a switch to stop zooming in whatever application you last zoomed, and what
  happened last. Double-click, or **Settings…**, opens the full window: Overview, Triggers, Applications,
  Advanced and About. The right-click menu gained the same quick items, so nothing needs a window at all.
- **Light and dark.** The window, the panel and the recorder follow Windows by default and can be pinned to
  either from the title bar, frame included. Saved as `Appearance` in the settings file.
- **An About page**: the version, links to the project, the issue tracker and the releases, what SmartZoom
  records locally, and the licences. It makes no network call, and neither does anything else in the app.
- The status card in the window's corner opens the record of what did not work, rather than only counting it.

### Changed

- **The settings window is WPF.** The tray, the hook and everything that zooms are unchanged; only the
  windows moved, because rounded cards, shadows and a themed title bar are native there and hand-painted in
  WinForms. It costs nothing to ship: the installer payload is byte-identical, since a self-contained build
  already carried the desktop runtime.
- **The trigger recorder is themed** and keeps every behaviour it had, including ignoring the keyboard's
  auto-repeat, taking a click anywhere on the dialog, and refusing a bare left or right click.
- **A new icon**: a magnifier with a plus, white on a blue tile, drawn separately at each of its eight sizes and stored uncompressed, because Windows' icon loader rejects a compressed frame below 256 px
  so the plus survives at 16 px. The old outline faded into a dark taskbar, and a bare magnifier reads as
  search rather than zoom.
- Settings apply as you change them. There is no Save button anywhere; the sliders are debounced so dragging
  one does not rebuild the zoom pipeline on every pixel.
- Plainer words throughout: "Largest zoom" shown as "3× bigger", "How much PDFs zoom", "Use Ctrl+wheel where
  smart zoom can't work", "Don't pass the press on to the app".
## [0.2.0] - 2026-09-24

### Added

- Mouse triggers can require modifier keys: Ctrl+left click, Alt+right click, Ctrl+middle click. Hold the
  modifiers while you click in the recorder, set `"Modifiers": "Ctrl"` on a `Mouse` trigger in the file, or
  pass `--trigger Ctrl+LeftClick`. The left and right buttons are triggers only with a modifier, so a plain
  click is never taken over, and the settings window warns that Ctrl+click and Shift+click already have jobs
  in browsers, Explorer and Excel.

### Fixed

- **A page that blocks the pinch no longer reports a zoom that never happened.** On an element with
  `touch-action: none` (Jira's task dialogs, most drag-and-drop UIs) Chromium hands the gesture to the page's
  own scripts: the pinch went in cleanly, SmartZoom logged "Zoomed in", remembered a zoom to undo, and
  nothing on screen changed. The screen around the cursor is now compared before and after the gesture, and
  a pinch that changed nothing is tried again — the same zoom, around an anchor outside the blocking
  element (beside it, then at the middle of the window), since the rest of the page usually takes the
  gesture and a pinch scales the whole viewport anyway. The result on such a page is a visual zoom of the
  page centred near the cursor rather than a fit of the element, and the second press still restores it
  exactly. Only when every anchor is refused does SmartZoom fall back to Ctrl+wheel page zoom — the only
  zoom such a page allows — or, with the new `Zoom.Browser.CtrlWheelWhenPinchBlocked` off, report it and
  leave the page alone.

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

[Unreleased]: https://github.com/Phoenix6001/SmartZoom/compare/v0.3.0...HEAD
[0.3.0]: https://github.com/Phoenix6001/SmartZoom/releases/tag/v0.3.0
[0.2.0]: https://github.com/Phoenix6001/SmartZoom/releases/tag/v0.2.0
[0.1.1]: https://github.com/Phoenix6001/SmartZoom/releases/tag/v0.1.1
[0.1.0]: https://github.com/Phoenix6001/SmartZoom/releases/tag/v0.1.0
