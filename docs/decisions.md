# Design decisions

Why SmartZoom is built the way it is, including the approaches that were tried and abandoned. Each entry is
here because someone will otherwise reasonably suggest the alternative.

## Input

**A low-level hook, not Raw Input.** Only a hook can swallow a press. SmartZoom's triggers are buttons and
keys the user has assigned to it, so the press must not also reach the application underneath. The hook runs on
its own thread with a bare message loop; its callback allocates nothing, logs nothing and blocks on nothing,
because Windows silently unhooks a callback that is slow.

**Input injected by other software is processed.** Only SmartZoom's own events are ignored, marked by a tag in
`dwExtraInfo`. Vendor mouse utilities re-emit remapped buttons as injected events, and a user who assigned a
side button in that software still expects it to work.

**Swallow mode holds the first press** and replays it if no second press arrives in time. With a single-tap
trigger nothing is held, so swallowing costs nothing.

## Targeting

**Toggle state is keyed by window handle and process id.** Handles are recycled, so a live handle may belong
to a different window in a different process by the time the second press arrives. Both are checked, and dead
entries are pruned on every trigger.

**Tiny windows above the cursor are looked past, not through.** Acrobat parks a five-pixel layered window under
the pointer once it has seen touch input, which would otherwise become the target of the next press. SmartZoom
walks the z-order to the window behind it. An earlier version hid the window and asked again; that mutated
another application's window and was replaced.

## Browsers

**The native path, not an extension.** No installer can add a browser extension silently, and SmartZoom aims
at zero setup. Instead it wakes Chromium's accessibility tree, hit-tests the block under the cursor, and
injects a two-finger touch pinch. Chromium turns that into visual-viewport zoom: no re-layout, crisp text, and
an exact return because the scale clamps at 1.0. The extension design is shelved, not forgotten; it would give
better block detection for anyone willing to install one.

**Waking the tree needs UI Automation, not just MSAA.** A fresh browser process only switches its renderer into
full accessibility mode after something asks. Asking through MSAA alone was not enough; the handshake also
touches the tree through UI Automation.

**Accessibility rectangles cannot be trusted during a pinch.** Chromium bakes the current pinch scale and
offset into every rectangle when it serializes the tree, and keeps them until the page next scrolls. SmartZoom
detects a document rectangle that is a uniformly enlarged copy of the window and translates back.

**Firefox needs a different injection device.** Gecko converts two-finger input from the ordinary touch
injection device into a touchpad pan gesture, so a symmetric pinch does nothing at all. Injecting through a
synthetic pointer device registers under a different name and reaches the zoom engine normally.

## Documents

**Office through the object model, obtained from the window.** `AccessibleObjectFromWindow` with
`OBJID_NATIVEOM` on the document pane returns that window's own automation object. The running object table
would find only one instance and is missing on modern .NET, and it cannot tell you which of several open
documents the cursor is over.

**Excel computes its own fit.** Asking Excel to zoom to a selection and reading the percentage back is more
reliable than computing it: its point-to-pixel conversion ignores the zoom level, and its reported pane width
includes the row headers, so a hand-rolled fit clipped the last column. The measurement applies the zoom as a
side effect, which the method name and return type now admit.

**Word is zoomed in steps, not by gesture.** Driving Word's zoom with a touch pinch looks smoother, because
Word renders a live preview, but Word commits the result asynchronously and overwrites the exact value set
afterwards. Zoom drifted 100 → 130 → 160 across cycles. The pinch path was removed.

**A reader's second press lands on the reader's own zoom.** Windows' gesture recognizer keeps back a share of
a closing pinch, and how much depends on the zoom it starts from: a ×2 and a ×0.5 leave the page about 2%
smaller each time, and it compounds. Two calibrations were measured and rejected. One fitted isolated gestures
exactly — closing to a 60 px half-span from 90, 110, 130 and 150 gave 0.763, 0.639, 0.554 and 0.497, all
matching `end/start + 0.095` — and still did not transfer to a gesture that followed another. Naming a state
the application defines is the only way to make the press exact however many times it is pressed. The cost is
that a user who was not at fit page loses their zoom once.

## Everything else

**Ctrl+wheel counts what it delivered.** The fallback adapter counts the notches that actually went out and
reverses exactly those, leaves Control alone if the user is physically holding it, and releases it in a
`finally`. Centring is whatever the application does, which is why it is a fallback.

**Browsers never fall back to Ctrl+wheel.** In a browser that is page zoom: it applies per site across every
window and does not come back. An adapter that cannot act in a browser reports that it handled the trigger
rather than let the fallback run.

**A shortcut is re-aimed immediately before it is sent.** The target window is brought to the front, then
SmartZoom waits up to a second and a half for the user to let go of the trigger's modifiers. If the foreground
moves during that wait the press is abandoned, because these shortcuts are destructive elsewhere — Ctrl+0
hides the selected column in Excel.

**Quality gates are part of the work, not a cleanup pass.** Warnings are errors, analyzers run at
`latest-recommended`, public members carry XML documentation, package versions are central, and CI builds,
formats, tests and publishes.
