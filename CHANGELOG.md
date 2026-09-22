# Changelog

All notable changes to SmartZoom are recorded here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow
[semantic versioning](https://semver.org/spec/v2.0.0.html).

Nothing has been released yet, so everything so far lives under Unreleased.

## [Unreleased]

### Added

- Smart zoom in Chromium browsers and Firefox: the block under the cursor is found in the accessibility tree
  and magnified with a synthetic touch pinch, with no browser extension.
- Smart zoom in Word and Excel through their object models.
- Smart zoom in PDF readers: an animated pinch around the cursor that lands on the reader's own fit-page
  command, so repeated presses cannot drift.
- Ctrl+wheel zoom with an exact toggle back for applications with no better strategy.
- Triggers on mouse buttons or keyboard shortcuts, single or double tap, with the press optionally swallowed.
- Contributor documentation: architecture, adding an application, design decisions, measured constants and a
  manual testing guide.
- CI now checks formatting, collects coverage, and builds and publishes the single-file executable.
- `tools/SmartZoom.Probe`, the tool every measured constant came from, is now part of the repository and is
  built by CI. It drives the same code the tray app does and can reproduce each number in
  `docs/measurements.md` on other hardware.
- An application icon, instead of the generic Windows one.
- The tray tooltip names the last zoom and the strategy that performed it, so what happened is visible
  without opening a log file.
- `Logging.Level` in the settings file. Logging was hard-wired to Debug with no way to turn it down.
- **A settings window.** Triggers are recorded by pressing them rather than typed as `XButton2` into JSON;
  applications are routed with a picker built from what the adapters say about themselves; the settings
  people actually turn have controls. Double-click the tray icon, or start SmartZoom while it is already
  running, to open it.
- **Settings apply without a restart**, from the window and from **Reload settings file** in the tray menu
  for people who edit the JSON. Adapters still copy their settings at construction — the composition root
  builds a new set instead, and the input hook takes a new trigger set in place.
- `SettingsValidator` checks a settings file by running the real constructors, so a bad value is a message
  next to the control rather than an exception at startup.
- **An installer**: `SmartZoom-<version>-setup.exe`, built by `install/build.ps1` and by CI. Per-user, so it
  never asks for administrator rights; offers to start SmartZoom at sign-in; upgrades in place over a running
  copy; and leaves your settings alone when you uninstall unless you ask it not to. It carries its own .NET
  runtime, so there is no prerequisite to install and nothing to explain.
- **The tray tooltip says when a press last reached SmartZoom** ("Zoomed in brave via Browser (2 min ago)",
  or "no press seen yet"), and the log notes every quarter of an hour that it is listening and nothing has
  arrived. A trigger that never gets here leaves no trace anywhere, so a working idle SmartZoom and one whose
  button is being eaten by vendor mouse software looked exactly alike — including in the log.
- **The wobble at the start of every browser zoom is gone.** Each zoom-in used to begin with an instant
  pinch-out, on the theory that a browser clamps it at 1.0 and nobody sees it. Chromium does; Edge draws the
  shrink first and rebounds, so every zoom began with the page visibly lurching for about a tenth of a second
  before the zoom the user asked for started. Frame-by-frame capture showed Edge tracking Chromium exactly
  once it was removed. The stuck-zoom case it guarded still recovers, through the reset that already runs
  when a visually zoomed page stops offering a sensible block.
- **Zoom gestures are paced to the display's refresh rate** instead of a fixed 8 ms. On a 59 Hz screen that
  was two injected touch moves per refresh, and a third of them went out late — by up to 13.7 ms — because a
  thread cannot reliably be woken that often. Uneven samples arriving twice a refresh leave it to the
  browser's input sampling which one it sees, and browsers differ. One sample per refresh: every frame now
  lands in its slot (0 late, measured), with the same total duration and pixel-exact restores.
- The debug log reports gesture pacing — how many frames went out, how late the worst one was, and how many
  missed their slot — so a stutter can be told apart from the browser rendering it badly.
- `smartzoom-probe track <prefix>` reports the scale each frame of a captured zoom reached. Percentage-of-
  differing-pixels saturates on text and template matching loses its lock once content triples in size, so
  both made smooth zooms look ragged and ragged ones look smooth; ink spread does neither.
- **Pages built out of plain `div`s can be zoomed.** Chromium reports a generic container as
  `ROLE_SYSTEM_PANE`, which SmartZoom did not recognise, so it treated it as an unknown element and skipped
  it. On a page whose layout is mostly `div`s — most news sites — every candidate under the cursor was
  skipped and the press did nothing at all, with only a line in the debug log to say why. Panes are now
  containers like any other block.
- The debug log's "path under the cursor" line now names the accessibility role number behind any element
  SmartZoom could not classify (`Other(16)`), which is what a "nothing happened" report needs.
- The installer asks how you want to start a zoom, by opening SmartZoom's own recorder: press the button or
  shortcut you want, or click one of the four common choices shown underneath it. It opens filled in with
  your current trigger, so reinstalling is "confirm or change" — Cancel keeps exactly what you had, and OK
  leaves your other triggers untouched. SmartZoom then works the moment it starts, rather than after a trip
  into settings to find out what "XButton2" means. A setup script cannot see a mouse's side buttons — Inno
  Setup only knows left, right and middle — so a list of presets on a wizard page could never have offered
  "the button behind the wheel"; the recorder can. Silent installs never ask.
- `SmartZoom.exe --record-trigger` asks for a trigger and writes what was pressed;
  `SmartZoom.exe --trigger <button-or-keys> [--taps 1|2] [--swallow]` writes one without asking, for scripted
  deployment. Both go through the same code that reads the file, so everything else in it is kept.
- `SmartZoom.exe --quit` asks a running copy to close cleanly. The installer uses it before replacing the
  executable, so an upgrade takes the tray icon down rather than leaving a ghost behind.

### Changed, breaking

- **`Routing` is now a single `Apps` map** of process name to strategy id, e.g.
  `"Apps": { "notepad": "CtrlWheel" }`. The six per-strategy process lists and `Overrides` are gone, and so
  is the `AdapterKind` enum behind them: strategies are named by string ids that the adapters declare
  themselves (`Browser`, `Reader`, `WordCom`, `ExcelCom`, `CtrlWheel`, `None`). `Keys` is now `Reader`.

  Defaults moved out of the settings file and into the adapters, which fixes an upgrade problem: a file
  written by an older version never gained the new version's applications. **There is no migration.** An
  existing file keeps working — the old `Routing` keys are ignored and the defaults take over — but any
  per-application choice recorded in them has to be written again under `Apps`.

- **`Zoom.Browser.MarginPx` and `AnimationMs` moved to `Zoom.Smart`.** Word and Excel read them too, so
  tuning "Browser" silently retuned Office. `Zoom.Browser` now holds only `AnchorInsetPx`.
- **`Zoom.Reader.Scale` is now `Magnification`** (it is a fixed factor, which `MinScale`/`MaxScale` do not
  clamp) and **`Zoom.Reader.MarginPx` is now `TopGapPx`** (it is a scroll gap, not the fit margin that the
  identically named setting next to it means).
- **The settings file is no longer migrated.** `Migrate()` and the legacy `Trigger`, `Button` and
  `Zoom.Keys` shapes are gone. A file from an older build still loads — unknown keys are ignored — but the
  settings it recorded under an old name fall back to the default, so rename them by hand.
- **`Zoom.Reader.Gesture` (a boolean) is now `Zoom.Reader.Mode`**, `"Pinch"` (the default, unchanged
  behaviour) or `"Shortcuts"`. The two are separate adapters now rather than one class with a flag, and the
  application registers whichever the mode names.
- `ZoomInResult.SelfManaged` is now `ZoomInResult.Handled`, which is what it always meant: dealt with, with
  nothing to undo and no fallback to try. Adapters derive from `ZoomAdapter<TRestore>`, which names the type
  of their undo data instead of every adapter unboxing an `object` by hand.

### Removed

- PowerPoint is no longer in the default routing. It never had an adapter, so a press there did nothing at
  all: no zoom, no fallback and no warning. It now falls through like any other unsupported application, and
  routing anything to a strategy the build does not have is reported in the log and falls back to Ctrl+wheel.

### Changed

- Adding support for an application is now one class and one registration instead of five coordinated edits,
  none of which the compiler checked. `ZoomRouter` is built from the adapters that are registered, so an
  application can no longer be routed to a strategy the build does not contain.
- The gesture geometry, the recognizer thresholds, the decoration policy and the reader's scroll arithmetic
  moved into `SmartZoom.Core`, where they are unit-tested against the values in `docs/measurements.md`. Every
  gesture regression in this project has been in that code, and none of it could be tested before.
- The reader adapter was two adapters behind a boolean; it is now `ReaderPinchAdapter` and
  `ReaderShortcutAdapter`, sharing a `ShortcutSender` for the foreground etiquette.
- Word's unused touch-pinch path is gone. It was disabled by passing null and the reason was only in a
  comment; `docs/decisions.md` records what was measured.

### Fixed

- **Pages built out of iframes could not be zoomed at all.** The accessibility walk stopped at the first
  document it met, which on a news site is an embedded card or an advertisement — so a 656 px frame became
  "the page", every real container above it was invisible, and a paragraph filling its little frame was
  rejected for being wider than 90% of "the viewport". A press did nothing, silently. Only the outermost
  document is the page now; the frames below it are containers like any other. Found on an MSN article, in
  both Edge and Brave.
- **The Close button on the settings window did nothing.** It carried a `DialogResult`, which only closes a
  form opened with `ShowDialog`; this one is modeless. Escape was dead for the same reason.

### Known gaps

- `SmartZoom.Interop` has no automated tests; the measured constants in `docs/measurements.md` were verified by
  hand on one machine at 200% display scaling.
