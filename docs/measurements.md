# Measured constants

Almost every number in the gesture and scrolling code was measured, not derived. They were taken on one
machine: a 3840×2160 display at 200% scaling, Windows 11, with Chromium (Brave), Firefox, Acrobat Reader, Word
and Excel. If SmartZoom behaves oddly on your hardware, one of these is the likely reason, and re-measuring is
a genuinely useful contribution.

Two rules before you change one:

- **Change the number, not the algorithm.** The algorithms were verified by hand against real applications.
- **Say what you measured.** A value without provenance is how this table became necessary.

Every `& $probe …` below is `tools/SmartZoom.Probe`, the tool these numbers came out of:

```powershell
dotnet build tools\SmartZoom.Probe
$probe = "tools\SmartZoom.Probe\bin\Debug\net10.0-windows10.0.17763.0\smartzoom-probe.exe"
& $probe help
```

## Gesture recognition

Every recognizer ignores small movements before it accepts a gesture. If the injector does not cross that
threshold before it starts counting, the zoom comes out smaller than asked.

All of them live in `Core/Zoom/Gesture/RecognizerProfile.cs`, and
`tests/SmartZoom.Core.Tests/Zoom/Gesture/PinchGeometryTests.cs` asserts these exact values: if a test there
disagrees with this table, the code is wrong, not the table.

| Constant | Value | What was measured | Reproduce with |
|---|---|---|---|
| `ChromiumSpanSlopDips` | 23 | Chromium's documented value is 16 device-independent pixels. At 16, asking for 1.9× produced 1.75×. 23 is what the display actually needed | `& $probe pinch <x> <y> 1.9`, then `& $probe scale before.png after.png` |
| `ChromiumTouchSlopDips` | 8 | How far one contact must move before Chromium calls it a scroll. Taken from Chromium's own default, not measured | — |
| `GeckoSpanSlopPx` | 35 | Gecko's `PINCH_START_THRESHOLD`, in physical pixels and *not* scaled by DPI | as above, over a Firefox window |
| `GeckoTouchSlopDips` | 9.6 | Gecko's `apz.touch_start_tolerance`, 0.1 inch | — |
| `WindowsSpanSlopDips` | 6 | Windows' own recognizer, which every application that does not handle raw touch gets. Measured in Acrobat | as above, over a PDF reader |
| `WindowsTouchSlopDips` | 0 | The same recognizer's one-finger threshold | — |

A measurement that deliberately produced no constant: in Acrobat, a ×2 pinch followed by a ×0.5 pinch leaves
the page about 2% smaller, and the shortfall depends on the zoom it started from. Two calibrations were fitted
and both failed to transfer. That is why a reader's second press ends on the application's own fit-page command
instead of reversing the gesture. See [decisions.md](decisions.md).

## Gesture geometry

| Constant | Value | Where | Why |
|---|---|---|---|
| `HalfGap` | 60 | `Core/Zoom/Gesture/PinchGeometry.cs` | Half the distance between the contacts at scale 1 |
| `MinHalfGap` | 12 | same | Below this, a narrower gesture near a corner is not worth attempting |
| Frame period (`RefreshPeriodMs`) | the monitor's refresh period (17 ms at 59 Hz, 8 ms at 120 Hz) | `Interop/Input/TouchPinchInjector.cs` | Read per gesture for the monitor under the anchor, not the primary display. One contact update per refresh: a fixed 8 ms on a 59 Hz panel sends two updates per refresh, a third of them late (up to 13.7 ms measured), and which of the two a browser consumes varies by browser |
| `MinFrameMs` / `MaxFrameMs` | 8 / 20 | same | The clamp on that period: below 8 ms the thread cannot be woken reliably every frame (a 240 Hz panel would ask for 4 ms); above 20 ms a misreported slow mode would make the zoom stutter |
| `DefaultFrameMs` | 8 | same | Used when the display reports no refresh rate (0 or 1 are Windows' "unknown") |
| `PanDurationMs` / `PanSettleMs` | 140 / 60 | same | The settle stops the browser turning the drag into a fling |
| `ScrollbarAllowance` | 56 | `Core/Zoom/BrowserAdapter.cs` | Sized for the worst case rather than measured: classic scrollbars are 17 px at 100% scaling and 51 px at 300%, so 56 clears them at any scaling. Contacts stay out of that strip |
| `ResizeBorderAllowance` | 24 | same | A touch contact 8 px inside a window grabbed its resize border and dragged the edge 150 px inward during a zoom-out. 12 px did not |
| `EdgeInset` | 28 | same | `ResizeBorderAllowance + 4`. Pushing content past the layout viewport scrolls the page, and zooming back out does not undo it; an 8 px residual was measured |
| `RestoreOvershoot` | 0.9 | same | Pinch slightly past 1.0 so rounding cannot leave the page at 1.02× |

Timing note: `Thread.Sleep` alone quantises to 15.6 ms on Windows, which makes a gesture look steppy. The
frame pacer sleeps in 1 ms steps while more than 2 ms remain and spins the last stretch.

## Reader scrolling

| Constant | Value | Where | What was measured |
|---|---|---|---|
| `NotchPixels` | 48 | `Core/Zoom/Content/ReaderViewGeometry.cs` | One wheel notch in Acrobat, at every zoom level tested. Also Windows' default. Reproduce with `& $probe wheel <x> <y> 480`, which prints requested against measured |
| `StripWidth` / `StripInset` | 96 / 24 | same | The strip sampled to measure movement, kept away from toolbars and page shadows |
| `ColumnStep` | 4 | `Interop/Input/ReaderView.cs` | Every fourth column distinguishes rows of text and is four times faster |
| `SettleTime` | 260 ms | same | Long enough for the reader to finish its scroll animation and repaint |
| `MinCandidates` | 8 | `Core/Zoom/Content/ScrollProfile.cs` | Fewer offsets than this and "how a middling offset scores" means nothing |
| Match margin | 0.8 | same | Evenly spaced lines match tolerably at every line pitch, so the margin cannot be generous |

Acrobat also accepts fine wheel deltas, but the rate changes once it has seen touch input (about 1.2 px per
delta unit, against 48 px per 120 otherwise), which is why corrections use whole notches.

## Content selection

| Constant | Value | Where | Why |
|---|---|---|---|
| `MinWidth` / `MaxWidthFraction` | 200 / 0.9 | `Core/Zoom/Content/BlockSelector.cs` | Narrower is an icon; wider than 90% of the viewport is the page itself |
| `MinHeight` / `MaxHeightFraction` | 16 / 2.0 | same | Taller than two viewports is a container, not a reading unit. Without this, a narrow window zoomed a whole article by a few percent |
| `MinColumnHeight` | 240 | same | A narrow but tall block is a column. Wikipedia's 188×519 table of contents is the case that needed it |
| `MinColumnWidth` | 100 (half of `MinWidth`) | same | Narrower than this is a gutter or an icon strip whatever its height; the 188 px table of contents above clears it |
| `MinScale` | 1.02 | `Core/Zoom/Content/StalePageZoom.cs` | Closer to 1 than this is rounding, scrollbars or borders rather than a stale zoom |

## Windows and timing

| Constant | Value | Where | What was measured |
|---|---|---|---|
| `MaxDecorationSize` | 8 px | `Core/Windows/DecorationPolicy.cs` | Acrobat parks a 5×5 layered window under the pointer once it has seen touch. `& $probe windows <x> <y>` says which window a press would reach |
| `MaxWindowsBehind` | 16 visible | same | Counted in *visible* windows: a real desktop had 64 windows above the reader, only 5 of them visible. Counting every handle gave up long before reaching it |
| `MaxZOrderSteps` | 512 | same | An absolute bound, so a desktop with thousands of hidden windows cannot make a press slow |
| `MaxTriggerAge` | 750 ms | `App/Hosting/TriggerDispatcher.cs` | Older than this and the press was queued behind a slow zoom; acting on it now surprises the user |
| `ModifierReleaseTimeout` | 1500 ms | `Core/Zoom/Reader/ShortcutSender.cs` | How long to wait for a hotkey trigger's modifiers to be released before giving up |
| `KeyDeliverySettle` | 120 ms | same | Time for the target to take the shortcut out of its queue before the foreground moves on |
| `KeyHold` | 30 ms | `Interop/Input/SendInputInjector.cs` | Acrobat ignores a press and release that arrive in the same batch, and ignored the generic Control virtual key entirely |
| `ActivationTimeout` / `FocusSettle` | 300 / 60 ms | `Interop/Windows/WindowActivator.cs` | Waiting for a window to report itself as foreground, then for its own focus handling |
| `MaxHitTestAttempts` × delay | 12 × 50 ms | `Interop/Accessibility/MsaaContentHitTester.cs` | A cold accessibility tree needs roughly 600 ms of retries. `& $probe hittest <x> <y>` shows what it finds |

## Re-measuring

The probe drives the same code the tray app does, so what it reports is what SmartZoom would do. The usual
shape of a measurement is: take a screenshot, do one thing, take another, and compare.

```powershell
# What a press at (2500, 900) would find, and where a x1.9 pinch would put its fingers.
& $probe windows 2500 900
& $probe hittest 2500 900
& $probe plan    2500 900 1.9

# What the recognizer actually delivers for a requested 1.9x.
& $probe shot  before.png 2500 900
& $probe pinch 2500 900 1.9
& $probe shot  after.png 2500 900
& $probe scale before.png after.png 200 1200 1.0 2.5

# Whether a restore was exact. 0.0% is what a browser should give.
& $probe diff before.png restored.png

# The scale each captured frame of a zoom reached, for judging smoothness.
& $probe track frames\zoom-
```

Anything that injects input names the window it is about to act on and counts down first, because it acts on
whatever is under the cursor rather than on a window you chose.

One tool that is deliberately *not* here: the raw-span pinch that fitted a correction curve for the reader's
closing gesture. Two such curves were measured and neither transferred; see [decisions.md](decisions.md).

[testing.md](testing.md) describes the manual acceptance procedure that uses all of this.
