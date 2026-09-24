# Adding an application

There are two ways to make SmartZoom work with an application. Try the first one before writing any code.

## As a user: route it to an existing strategy

Most applications zoom with Ctrl and the mouse wheel. If yours does, it needs no code at all — one line in
`%APPDATA%\SmartZoom\settings.json` under `Routing`:

```jsonc
"Apps": { "notepad": "CtrlWheel" }
```

The key is the process image name, with or without `.exe`, case-insensitive. Then choose **Reload settings
file** from the tray menu; nothing needs a restart.

The strategies you can name are the ids the adapters in your build declare:

| Id | What it does |
|---|---|
| `Browser` | Accessibility hit-test plus a touch pinch: Chromium-based browsers and Firefox |
| `Reader` | Animated pinch around the cursor, landing on the reader's own fit-page zoom: PDF readers |
| `WordCom` | Word's own object model |
| `ExcelCom` | Excel's own object model |
| `CtrlWheel` | Synthesized Ctrl+wheel ticks: anything that zooms with the wheel |
| `None` | Switches SmartZoom off for that application |

`None` is the right answer when the application's own zoom is better than anything we can synthesize.

Two things follow from ids being declared by the adapters rather than listed in the settings file. An id that
no adapter in the build provides is reported at startup and again the first time you press there, and the
application falls back to Ctrl+wheel rather than silently doing nothing. And an application only needs a line
here when the defaults are wrong for you: upgrading SmartZoom brings new applications with it, instead of
leaving them out of a settings file that was written by an older version.

This is the whole story for image viewers, editors and anything else with a conventional zoom. The rest of
this page is for when it is not enough.

## As a contributor: write an adapter

An adapter is worth writing when the application can tell you *what* is under the cursor, so the zoom can
frame a paragraph, a cell or a figure rather than scaling the whole window. Word and Excel expose that
through their object model; browsers expose it through their accessibility tree.

### What you implement

Derive from `ZoomAdapter<TRestore>` in `src/SmartZoom.Core/Zoom/ZoomAdapter.cs`, where `TRestore` is
whatever you need in order to undo a zoom:

```csharp
public sealed class PowerPointComAdapter : ZoomAdapter<SlideViewState>
{
    public PowerPointComAdapter(...) : base(Descriptor) { }

    protected override Task<ZoomInResult> ZoomInAsync(TargetInfo target, ScreenPoint point, CancellationToken ct);
    protected override Task ZoomOutAsync(TargetInfo target, SlideViewState restoreState, CancellationToken ct);
}
```

The coordinator holds every adapter's undo data in one table, so it necessarily stores it as `object`. The
base class puts the type back on both ends: `Applied(state)` only accepts a `TRestore`, and the cast on the
way back happens once, in one place, with an error message that names your adapter.

The descriptor you pass to `base` is the whole registration contract: your id, the processes you claim out of
the box, and enough prose for a settings UI to offer you.

```csharp
public static AdapterDescriptor Descriptor { get; } = new(
    "PowerPointCom",
    ["POWERPNT"],
    "Microsoft PowerPoint",
    "Zooms the slide to the shape under the cursor through PowerPoint's object model.");
```

The static property is what tests, the composition root and your own `base(Descriptor)` call name. Two
adapters claiming the same id, or the same default process, throws at startup rather than quietly letting
one of them win.

`ZoomInAsync` returns one of three things:

| Result | Meaning | What the coordinator does |
|---|---|---|
| `Applied(state)` | Zoomed. `state` is your `TRestore` | Remembers `state`; the next press calls `ZoomOutAsync` with it |
| `ZoomInResult.Handled(reason)` | Handled; nothing to undo. `reason` is a `ZoomReason` saying why nothing was zoomed, and it is what the diagnostics record counts under | Nothing. No fallback |
| `ZoomInResult.Unhandled` | Could not act | Falls back to Ctrl+wheel, if that is enabled |

Return `Unhandled` when the application is in a state you cannot work with and a crude zoom would still be
better than nothing. Return `Handled` when a crude zoom would be *worse* than nothing —
`BrowserAdapter` does this, because the Ctrl+wheel fallback in a browser is page zoom, which is per site,
applies to every window and does not come back.

### The rule about Win32

`SmartZoom.Core` must not reference Win32. If your adapter needs to talk to the application, declare an
interface in Core and implement it in `SmartZoom.Interop`. Word is the worked example:

- `src/SmartZoom.Core/Zoom/Office/IWordAutomation.cs`, `IWordWindow.cs` and `WordViewState.cs` declare the
  automation entry point, the attached window, and the record that carries the zoom and scroll position.
- `src/SmartZoom.Interop/Office/WordAutomation.cs` implements them with late-bound COM on a dedicated STA
  thread, and never leaks a COM type back to Core.

Keep the decisions in Core, where they can be tested with a fake, and the plumbing in Interop, where it
cannot.

### The two edits

1. **Your adapter**, in `src/SmartZoom.Core/Zoom/`, with its `Descriptor`, plus any Core interface and its
   Interop implementation. Add P/Invokes to `src/SmartZoom.Interop/NativeMethods.txt` if you need new ones.
2. **`src/SmartZoom.App/Hosting/ZoomPipelineFactory.cs`** — one line in its `Adapters` list. Anything your
   adapter needs that has no settings in it (an injector, an automation object) is a singleton registered in
   `Program.cs` and injected into the factory.

That is all the routing there is. `ZoomRouter` is built from the descriptors of the adapters that are
actually registered, so forgetting step 2 means your application is simply not handled, which is the same
thing as not having written the adapter — it cannot leave a half-wired route that swallows presses.

One rule the factory implies: **copy what you need from the settings in your constructor**. That is what lets
the whole set of adapters be rebuilt when a setting changes, which is how the settings window applies without
a restart.

Then add your descriptor to the `Registered` list in `tests/SmartZoom.Core.Tests/Routing/ZoomRouterTests.cs`,
which asserts the shipped defaults, write tests for the adapter itself, and add a row to the support table in
`README.md`.

### Tests

Adapters are tested with fakes, never mocks. Look at
`tests/SmartZoom.Core.Tests/Zoom/Office/ExcelComAdapterTests.cs`: `FakeExcelWindow` imitates Excel closely
enough to be useful, including the awkward part where measuring a fit applies it. Test names are sentences
with underscores, and the interesting cases are the ones where the application misbehaves — it scrolled less
than you asked, it refused the shortcut, the window went away mid-zoom.

### Before you send it

Run the manual checks in [testing.md](testing.md) for your application and for one browser, because the
browser path shares the gesture code with everything else. `dotnet test` cannot tell you whether a zoom
looked right.

## PowerPoint, if you are looking for somewhere to start

PowerPoint is not supported, and it is deliberately absent from the default routing: an application routed
to a strategy with no adapter behind it is exactly the silent failure this design exists to prevent. The
sketch:
`AccessibleObjectFromWindow(OBJID_NATIVEOM)` on the slide window gives you a `DocumentWindow`, whose
`View.Zoom` is the zoom and whose `Selection` reaches the shapes; the block is the shape under the cursor.
`WordComAdapter` is the closest model to copy.
