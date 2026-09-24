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
WindowInspector    (Interop)   what window is under the cursor, and whose is it
        │
        ▼
ZoomRouter         (Core)      process name → AdapterId
        │
        ▼
ZoomCoordinator    (Core)      already zoomed? then undo. otherwise zoom in
        │
        ▼
IZoomAdapter       (Core)      the adapter that implements the routed strategy
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

## The adapters

A *strategy* is the id a user names in `Routing.Apps` (`Browser`, `Reader`, `WordCom`, `ExcelCom`,
`CtrlWheel`, `None`); an *adapter* is the class that implements one. Each adapter declares its strategy id
and the processes it claims by default in an `AdapterDescriptor`.

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

`%APPDATA%\SmartZoom\settings.json`, read at startup into a `SmartZoomSettings` that lives behind
`SettingsHolder` — one object, one owner, so nothing can hold a stale copy and write it back over the file.

Adapters copy the values they need when they are constructed, which is what makes them cheap and testable.
Changing a setting therefore means **building a new set of them**, not poking the old ones:
`ZoomPipelineFactory` builds a fresh router, adapters and coordinator from a settings snapshot, and
`ZoomEngine` swaps that in behind a gate it shares with the dispatcher, so a zoom and a settings change can
never interleave. Triggers are the exception: `LowLevelInputHook.SetTriggers` takes a new set in place, under
the same lock its callbacks use, because installing a second pair of hooks to change a button would be a much
larger thing to get right.

`SettingsApplier` is the only thing that writes the file while the app runs, and it writes it **after** the
change is in force — a file written first would describe a state the process was never in. Two writers run
before the app starts: `SettingsStore.Load` creates the file with defaults on first run, and the installer's
`--trigger` / `--record-trigger` job (`TriggerCommand`) writes the chosen trigger into it. **Reload settings
file** goes through `SettingsStore.TryLoad` instead, which never writes and never substitutes defaults: a
malformed or unreadable file is reported to the user and the running settings are kept.

A file written by an older version still loads; keys it carries for shapes that no longer exist are ignored,
and the defaults take over.

## The settings window

WPF, under `App/Ui`, owned by `SettingsShell` — which also owns the tray panel and the WPF `Application`
object both of them need. Neither exists until the tray is asked for one, so a user who never opens either
never pays for WPF; both are then created once and kept, so the window comes back on the page it was left on.

```
Ui/Shell/     ShellWindow (frameless chrome, the rail), ShellViewModel, ShellPages (the one place a view
              is constructed), NavigationSection
Ui/Pages/     Overview, Triggers, Applications, Advanced, About — a XAML UserControl and a view model each
Ui/Recorder/  TriggerRecorderWindow: the trigger is performed, not named
Ui/Panel/     The tray panel: the same few facts, for someone who only wants to flip a switch
Ui/Themes/    Light.xaml and Dark.xaml declare the same keys and nothing else names a colour, so
              ThemeManager swaps one for the other and the open window repaints
Ui/Mvvm/      ObservableObject and RelayCommand; no MVVM framework
```

Three rules hold the window together, and they are the reason it cannot drift out of step with the app:

- **No page holds a copy of the settings.** Each one reads `SettingsHolder.Current` in its `Refresh`, and
  `ShellViewModel.Refresh` calls every page's (`IPageModel`) whenever the window is shown. A change made in
  the tray panel, in the tray menu, on another page or by hand in the file is therefore visible at once.
- **Every change goes through `SettingsApplier`**, off the UI thread — its gate can be held by a zoom in
  flight — and the page re-reads afterwards, so what is on screen is what took effect rather than what was
  asked for. There is no Save button, and so nothing can be left unsaved.
- **The validator's findings are shown, not discarded.** `SettingsApplyResult.Problems` becomes a list of
  `ProblemLine` under the page, errors in `Brush.Error` and warnings in `Brush.Warn`: a change that went
  through with a caveat must not look like one that was refused.

What each page offers comes from the running application rather than from a list in the UI. Applications
builds its strategy picker and its "built in" rows from `ZoomRouter.Adapters`, so a strategy added later
offers itself — with its own name and its own sentence — without this code being touched. Triggers names
each entry with `TriggerSettings.ToDefinition(...).DisplayName`, which is what the hook is actually built
from. Advanced's two sliders wait for the dragging to stop before applying, because a slider that applied on
every pixel would rebuild the zoom pipeline a hundred times on the way across.

Removing the last trigger is refused in the page, with a message, rather than left to the validator: an
application with no trigger cannot be started by anything, and a list that silently emptied itself would
read as a bug.

## Adding support for an application

See [adding-an-application.md](adding-an-application.md).
