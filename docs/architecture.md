# Architecture

SmartZoom watches for a trigger, works out what is under the cursor, and asks the best available strategy to
magnify it. Pressing again undoes exactly what was done.

## The three projects

| Project | Target | Rule |
|---|---|---|
| `SmartZoom.Core` | `net10.0` | No Win32, no UI. Every decision worth testing lives here |
| `SmartZoom.Interop` | `net10.0-windows` | All P/Invoke, COM and screen access, behind interfaces declared in Core |
| `SmartZoom.App` | `net10.0-windows` | The tray icon, the generic host, logging, and the composition root |

References point one way: `App → Core, Interop` and `Interop → Core`. If you find yourself wanting a Win32
type in Core, declare an interface instead — that is the seam that makes the tests possible.

## One press, end to end

```
LowLevelInputHook  (Interop)   WH_MOUSE_LL + WH_KEYBOARD_LL on a dedicated thread
        │                      the callback allocates nothing and never logs
        ▼
TapDetector / HotkeyMatcher (Core)   is this a trigger? do we swallow the press?
        │
        ▼  Channel<TriggerEvent>
TriggerDispatcher  (App)       drops events older than 750 ms, contains exceptions
        │
        ▼
IWindowInspector   (Interop)   what window is under the cursor, and whose is it
        │
        ▼
ZoomRouter         (Core)      process name → AdapterId
        │
        ▼
ZoomCoordinator    (Core)      already zoomed? then undo. otherwise zoom in
        │
        ▼
IZoomAdapter       (Core)      the strategy for this application
        │
        ▼
IPinchInjector / IInputInjector / IContentHitTester / IWordAutomation … (Interop)
```

The hook callback is the one piece of this with hard rules: it must be allocation-free, take no locks that
anything else holds, and never log, because Windows silently removes a hook that is slow. Work is handed to a
channel and done elsewhere.

## Toggling

`ZoomCoordinator` asks `WindowZoomStateStore` whether this window is already zoomed. The store is keyed by the
root window handle and the owning process id, because handles are recycled, and it prunes dead windows on every
trigger. If there is an entry, the adapter that created it is asked to undo it with the state it saved. If
there is not, the routed adapter is asked to zoom in.

Adapters are stateless. Everything needed to undo a zoom travels in the restore state the adapter returns.

## The strategies

| Adapter | For | How |
|---|---|---|
| `BrowserAdapter` | Chromium and Gecko browsers | Finds the block under the cursor in the accessibility tree, then pinches with synthetic touch. No extension needed |
| `ReaderPinchAdapter` | PDF readers | An animated pinch around the cursor, landing on the reader's own fit-page command |
| `ReaderShortcutAdapter` | PDF readers that ignore touch | The reader's own fit-width and fit-page shortcuts, with the cursor's content scrolled to the top first. `Zoom.Reader.Mode` chooses between the two; both answer to the id `Reader` |
| `WordComAdapter` | Word | The object model: paragraph, table or picture under the cursor, zoomed in steps |
| `ExcelComAdapter` | Excel | The object model: the surrounding island of filled cells, or the cells a chart covers |
| `CtrlWheelAdapter` | Anything else configured, and the fallback | Synthesized Ctrl and wheel notches, counted so they can be reversed exactly |

Two things are worth knowing before changing any of them. Zoom is *visual* wherever possible: browsers scale
the rendered page without re-laying it out, which is why text stays crisp and why scaling back below 1.0 lands
exactly where it started. And every gesture constant was measured, not derived — see
[measurements.md](measurements.md) before touching one.

## Where the interesting decisions live

- **What counts as a block**: `Core/Zoom/Content/BlockSelector.cs`, and `SmartZoomPlanner.cs` for the scale and
  anchor that follow from it.
- **Where the fingers go**: `Core/Zoom/Gesture/PinchGeometry.cs` decides, `Interop/Input/TouchPinchInjector.cs`
  performs. Contacts must stay clear of scrollbars and of the window's resize border, and the gesture is
  oriented along the row where it can be, because a vertical drag leaks into the page's scroll position. Every
  gesture regression in this project has been in that one file, which is why it is in the testable project.
- **What the accessibility tree says**: `Interop/Accessibility/MsaaContentHitTester.cs`. Chromium builds its
  tree lazily, so the first question has to wake it up, and a tree that was serialized during a pinch answers
  in the wrong coordinates until the page next scrolls (`Core/Zoom/Content/StalePageZoom.cs`).
- **How far a reader actually scrolled**: `Core/Zoom/Content/ReaderViewGeometry.cs` and `ScrollProfile.cs`
  decide, `Interop/Input/ReaderView.cs` reads the screen. Readers do not expose a scroll position, so the
  movement is measured by comparing a strip of the window before and after.
- **Whether the window under the cursor is the one the user meant**: `Core/Windows/DecorationPolicy.cs`, applied
  by `Interop/Windows/WindowInspector.cs`. Acrobat parks a five-pixel window under the pointer, and without
  looking past it every press lands on that.

## Settings

`%APPDATA%\SmartZoom\settings.json`, read once at startup into a single `SmartZoomSettings` object that is
registered as a singleton. Adapters receive their own section by constructor injection. Editing the file
requires a restart; the tray menu says so.

A file written by an older version keeps its lists exactly as written — new defaults are not merged in.

## Adding support for an application

See [adding-an-application.md](adding-an-application.md).
