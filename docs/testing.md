# Testing

`dotnet test` covers the decisions. It cannot tell you whether a zoom looked right, landed in the right place,
or came back. That part is manual, and this page is the recipe.

## Before you start

**Do not run input tests while someone is using the machine.** Every check below injects real mouse and
keyboard input into whatever window is in front. It will fight you for the cursor.

Two hazards worth knowing, both learned the hard way:

- **A stray Ctrl+0 in Excel hides the selected column.** If a shortcut misses its target, check
  `Columns(1).Hidden` before assuming nothing happened.
- **A cold accessibility tree needs a browser restart to reproduce.** Opening a new window reuses the warm
  tree of the running process, so "the first press after launch" bugs will not appear.

Build and run:

```powershell
Get-Process SmartZoom -ErrorAction SilentlyContinue | Stop-Process -Force   # it locks the build output
dotnet build
Start-Process src\SmartZoom.App\bin\Debug\net10.0-windows10.0.17763.0\SmartZoom.exe
```

The log is `%LOCALAPPDATA%\SmartZoom\logs\smartzoom-<date>.log`. Every press writes one line naming the window
it resolved and one naming the adapter that handled it.

## The acceptance run

Do this before sending anything that touches zoom behaviour. Each row is: point at the thing, press the
trigger, look, press again, look.

| Application | Point at | Expect |
|---|---|---|
| Chromium browser | A paragraph mid-page | Fills the width, crisp; second press returns the page exactly |
| | An image in a side column | Grows in place; the window's own edges do not move |
| | A block near the left edge, and one near the bottom | Same, without the page sliding sideways |
| Firefox | A paragraph | Animates like the Chromium browser, restores exactly |
| Word | A paragraph | 100% to about 300% and back to 100% |
| Excel | A cell low in a large table | The cell you pointed at is on screen afterwards, not the top of the table |
| | A chart | The chart fills the pane; your cell selection is unchanged |
| Acrobat | A paragraph mid-page | Magnifies around the cursor; five toggles return the page to the same place |

"Returns exactly" has a precise meaning for browsers: a screenshot before and after should differ by 0.0% of
pixels. Anything else is a bug, not rounding.

## Judging a zoom

Screenshots are the only reliable judge, because the eye forgives a 40 px drift. Capture the target window
before, after the first press and after the second, then compare the first and third.

Watch for these specifically:

- **The window's own rectangle changing.** That means a touch contact grabbed the resize border.
- **A small vertical shift after a restore.** In a browser it should be zero; in a reader a line of text or so
  is expected.
- **The page scrolling rather than zooming.** Usually a gesture oriented along the wrong axis.
- **The second press doing nothing.** Usually the toggle state was lost, or the press was dropped for age
  while the first zoom was still running.

## Checking what the app decided

The log answers most questions. A healthy press looks like this (one line each from the dispatcher, the
adapter and the coordinator):

```
2026-09-21 14:29:15.587 +03:00 [INF] SmartZoom.App.Hosting.TriggerDispatcher: Trigger at (2500, 900) px -> brave (pid 24484, hwnd 0x401F4, class Chrome_WidgetWin_1, hit Chrome_RenderWidgetHostHWND)
2026-09-21 14:29:15.658 +03:00 [INF] SmartZoom.Core.Zoom.BrowserAdapter: Smart zoom: Group 948x183 px in 1793 px viewport -> x1.83 around (2488, 608).
2026-09-21 14:29:16.024 +03:00 [INF] SmartZoom.Core.Zoom.ZoomCoordinator: Zoomed in brave via Browser.
```

If a press does nothing, look for these in order:

1. **No `Trigger at` line at all** — the hook never saw the press. Vendor mouse software may have eaten it, or
   SmartZoom is not running.
2. **`Trigger at` naming a window you did not expect** — the wrong target was resolved. The class names in the
   line say what was actually under the cursor.
3. **A warning from the adapter** — it names the reason: no content under the cursor, the block already fills
   the window, the application refused.
4. **A warning that the application is routed to a strategy this build does not have** — a typo in
   `Routing.Apps`, or a strategy that was removed. It names the ids that do exist, and falls back to
   Ctrl+wheel rather than doing nothing.

The tray tooltip says the same thing in one line: hover over the icon and it names the last zoom and the
strategy that performed it.

## Re-measuring a constant

Every number in [measurements.md](measurements.md) was taken by injecting a known gesture and measuring the
result from screenshots. The general method:

1. Put the application in a known state — a specific zoom, scrolled to a specific place.
2. Capture the window.
3. Inject one gesture with known parameters.
4. Capture again, and measure what changed: the vertical shift of the content, or the width of a page between
   its left and right edges.
5. Repeat at two or three different gesture sizes. A single data point cannot tell a constant offset from a
   proportional error, which is exactly how one rejected calibration looked correct.

Scale is measured most reliably from a feature whose size is known to scale with it — the width of a PDF page
between its edges, or the spacing between lines of text. Do not measure it through accessibility rectangles:
they do not reflect visual-viewport zoom.

`tools/SmartZoom.Probe` is the tool for all of this. It drives the same code the tray app does, and every
command that injects input names the window it is about to act on and counts down first.

```powershell
$probe = "tools\SmartZoom.Probe\bin\Debug\net10.0-windows10.0.17763.0\smartzoom-probe.exe"

dotnet build tools\SmartZoom.Probe
& $probe help

# What a press at a point would find, and what a gesture there would do.
& $probe windows 2500 900
& $probe hittest 2500 900
& $probe plan    2500 900 1.9

# Measure what the recognizer really delivered for a requested 1.9x.
& $probe shot  before.png 2500 900
& $probe pinch 2500 900 1.9
& $probe shot  after.png 2500 900
& $probe scale before.png after.png 200 1200 1.0 2.5

# Whether a restore was exact. A browser should give 0.0%.
& $probe diff before.png restored.png
```

Note that PowerShell cannot inject these events itself: it copies the nested input structures, so the
gesture arrives malformed. That is why the probe is a compiled program and not a script.
