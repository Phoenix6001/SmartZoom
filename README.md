# SmartZoom for Windows

macOS-style **smart zoom** for Windows: press a mouse button and the application under the
cursor zooms its *content* to fit the element you're pointing at. Press again to return to exactly
where you were.

Windows has no system gesture for this, so SmartZoom is a small tray app that captures a mouse trigger
and routes it to a per-application zoom strategy:

| Target | Strategy | Precision |
|---|---|---|
| Chrome, Edge, Brave, Opera, Vivaldi (any Chromium browser) | Native smart zoom: finds the paragraph/image under the cursor through the browser's accessibility tree and pinch-zooms it to fill the window — no extension needed | Element-aware, visual zoom (no reflow), exact restore |
| Firefox | Native smart zoom, the same way: the block under the cursor comes from Firefox's accessibility tree and is pinch-zoomed through a synthetic touch device that Firefox accepts as a touch screen | Element-aware, visual zoom (no reflow), exact restore |
| Word | Smart zoom through Word's object model: the paragraph, table or picture under the cursor is zoomed to fill the document pane; the previous zoom and scroll position are restored exactly | Element-aware, exact restore |
| Excel | Smart zoom through Excel's object model: the block of data under the cursor — the surrounding island of filled cells, or the cells a chart or picture covers — is zoomed to fill the worksheet pane, and the view is scrolled to the row you pointed at | Element-aware, exact restore |
| Acrobat, Acrobat Reader, SumatraPDF | An animated pinch around the cursor, the same gesture the browsers get: what you pointed at stays where it is and grows. The second press animates the magnification away and lands on the reader's own "fit page" | Animated, follows the cursor, never drifts |
| Image viewers | Synthesized Ctrl+wheel centered on the cursor | Approximate |
| Everything else | Ignored | — |

> **Status:** early development. Smart zoom works in Chromium browsers, Firefox, Word, Excel and PDF readers;
> Ctrl+wheel zoom with toggle-back works for the configured image apps. PowerPoint is not supported yet.
> See [Roadmap](#roadmap).

## Requirements

- Windows 10 1809+ or Windows 11
- [.NET 10 SDK](https://dotnet.microsoft.com/download) to build; .NET 10 Desktop Runtime to run
- A mouse with a middle button or side (X) buttons. No vendor software is needed.

## Build and run

```powershell
dotnet build
dotnet test
dotnet run --project src/SmartZoom.App
```

SmartZoom runs in the notification area. Right-click the icon for **Enabled**, **Open settings file (restart to apply)**,
**Open log folder**, and **Exit**.

Publish a single-file executable:

```powershell
dotnet publish src/SmartZoom.App -c Release -r win-x64
```

## Configuration

Settings are created on first run at `%APPDATA%\SmartZoom\settings.json`. In this release, restart
SmartZoom after editing.

```jsonc
{
  "Enabled": true,
  "Triggers": [                    // any of these fires SmartZoom
    {
      "Mouse": "XButton2",         // Middle | XButton1 | XButton2
      "TapCount": 2,               // 2 = double-tap (safe for a shared button), 1 = every press
      "DoubleTapWindowMs": null,   // null = system double-click time
      "SwallowClicks": false       // see below
    },
    { "Keys": "Ctrl+Alt+Z", "TapCount": 1, "SwallowClicks": true },
    { "Keys": "Ctrl", "TapCount": 2 }   // double-tap a bare modifier
  ],
  "Routing": {
    // Which application gets which strategy. Only the ones you want to change:
    // every adapter already claims the applications it was written for.
    "Apps": {
      "notepad": "CtrlWheel",    // an app SmartZoom doesn't know about
      "EXCEL": "None"            // ... and one you'd rather it left alone
    }
  },
  "Zoom": {
    "MinScale": 1.1,
    "MaxScale": 3.0,
    "Animate": true,
    "FallbackToCtrlWheel": true,   // use Ctrl+wheel when a richer adapter can't act
    "CtrlWheel": { "Ticks": 6, "IntervalMs": 20 },
    "Smart": {                     // shared by browsers, Word and Excel
      "MarginPx": 16,              // space between the zoomed block and the window edge
      "AnimationMs": 280           // zoom animation length when Animate is on
    },
    "Reader": {                            // PDF readers
      "Mode": "Pinch",                     // or "Shortcuts" for a reader that ignores touch
      "Magnification": 2.0,                // fixed factor; MinScale/MaxScale do not clamp it
      "AnimationMs": 300,
      "ZoomInKeys": "Ctrl+2",              // "Shortcuts" mode: fit width
      "ZoomOutKeys": "Ctrl+0",             // the zoom the second press lands on
      "FollowCursor": true,                // ... scrolling what you pointed at to the top first
      "TopGapPx": 16                       // gap left above it
    },
    "Browser": {
      "AnchorInsetPx": 0           // extra keep-out from window edges; normally not needed
    }
  },
  "Logging": {
    "Level": "Debug"               // Debug, Information, Warning, Error: how much reaches the log file
  }
}
```

The defaults live in the code, not in your file, so upgrading SmartZoom brings support for new
applications with it. `Routing.Apps` is only for overriding them.

The strategies you can name are `Browser`, `Reader`, `WordCom`, `ExcelCom`, `CtrlWheel`, and `None` to
switch SmartZoom off for an application. To try SmartZoom on an app that isn't listed but zooms with
Ctrl+wheel (Windows 11 Notepad, for example), give it `"CtrlWheel"`. Naming a strategy that doesn't
exist is reported in the log and falls back to Ctrl+wheel rather than doing nothing.

Each entry in `Triggers` is either a **mouse button** (`"Mouse"`) or a **key combination** (`"Keys"`).
All of them are active at once, so a desktop mouse button and a laptop hotkey can live in one file.

**`Keys`** accepts modifiers `Ctrl`, `Alt`, `Shift`, `Win` joined with `+` and a final key: a letter or
digit, `F1`–`F24`, `Space`, `Enter`, `Tab`, `Esc`, arrows, `Home`/`End`/`PageUp`/`PageDown`,
`Insert`/`Delete`, `Numpad0`–`Numpad9`, and a bare modifier (`"Keys": "Ctrl"`) for a double-tap
trigger. Modifiers must match exactly: `Ctrl+Z` does not fire on `Ctrl+Shift+Z`.

**`TapCount`**: the default double-tap lets an input keep its normal job (a single Back/Forward press
still works; a single Ctrl still copies). If you can dedicate an input to SmartZoom — for example the
mode-shift button behind the wheel on an MX Master, reassigned in Logi Options+ — set `1` and every
press zooms.

**`SwallowClicks`** hides the trigger's presses from the target app (for hotkeys, only the final key;
the modifiers pass through). With single tap this is free and recommended for a dedicated input. With
double-tap, SmartZoom can't know at the first press whether a second is coming, so it holds that press
back and replays it once the double-tap window expires; with `XButton1`/`XButton2` that delays
Back/Forward by the window length. With the default (`false`), presses reach the app immediately and it
also sees them.

**Laptop touchpad.** Windows has no API for custom touchpad gestures, and the Mac gesture (two-finger
double-tap) collides with Windows' own two-finger-tap-is-right-click. The reliable route is Windows
Settings → Bluetooth & devices → Touchpad → *Taps* → **Three-finger tap: Middle mouse button**, then add
`{ "Mouse": "Middle", "TapCount": 1 }` (or keep `2` if you also use middle-click). On Windows 11 the
four-finger tap can instead be a *Custom shortcut*, which you point at one of your `Keys` triggers.

Older files with a single `"Trigger"` object are upgraded to `"Triggers"` automatically on startup.

Logs are written to `%LOCALAPPDATA%\SmartZoom\logs` (rolling daily, 14 days kept).

## Architecture

Three projects. `SmartZoom.Core` holds every decision and never touches Win32, which is what makes it
testable. `SmartZoom.Interop` holds all the P/Invoke, COM and screen access, behind interfaces declared in
Core. `SmartZoom.App` is the tray icon, the generic host and the composition root.

A press travels from a low-level hook, through tap detection, to a dispatcher that resolves the window under
the cursor, a router that picks a strategy for that application, and an adapter that performs the zoom and
returns whatever it needs to undo it.

- [docs/architecture.md](docs/architecture.md) follows one press end to end.
- [docs/adding-an-application.md](docs/adding-an-application.md) is how to support another application, with
  or without writing code.
- [docs/decisions.md](docs/decisions.md) explains why it works this way, including the approaches that were
  tried and abandoned.
- [docs/measurements.md](docs/measurements.md) records what every gesture constant was measured against.
- [docs/testing.md](docs/testing.md) is the manual acceptance run, because automated tests cannot tell you
  whether a zoom looked right.

## Troubleshooting

**Nothing happens when I press the side button.** Vendor mouse software (Logi Options+,
Razer Synapse, …) often intercepts side buttons and re-emits them as keyboard shortcuts or other
actions, so Windows never sees the physical button. Check the log: every trigger-button transition
the hook receives is recorded at Debug level (`Input XButton2 x1 down …`). If your presses aren't
there, either assign the button to plain *Back*/*Forward* in the vendor software, or pick a button
that does arrive — the wheel click (`"Mouse": "Middle"`) is usually untouched. Note that vendor
software can also have per-application profiles, so the same button may arrive differently
depending on which window is focused.

**Presses arrive but no trigger fires.** With `TapCount: 2` the two presses must both be *down* events within
`DoubleTapWindowMs` (default: the system double-click time). If you double-click slowly, raise it —
`700` is a comfortable value.

## Known limitations

- **Elevated windows can't be zoomed.** Windows' User Interface Privilege Isolation (UIPI) blocks a
  non-elevated process from sending input to windows running as administrator. This is by design.
  SmartZoom does not require admin rights.
- Ctrl+wheel zoom is approximate: if an app hits its zoom limit, toggling back may not restore
  the exact previous level. Zooming with the app's own controls in between also throws the
  toggle off; SmartZoom only remembers how far *it* zoomed.
- Toggle state is per top-level window, not per document or tab.
- Browser smart zoom: the achieved scale can differ from the planned one by a few percent (the
  gesture recognizer's slop is compensated with a measured constant). Blocks very close to the
  window's left or right edge can't be placed exactly, because the gesture's contacts must stay
  inside the window. Pages that disable pinch zoom (`user-scalable=no`) can't be smart-zoomed and
  fall back to Ctrl+wheel.
- Word smart zoom changes the document zoom level in steps, so the motion is not as fluid as the
  browsers' pinch: Word re-lays out the page at every level. Word's own status-bar zoom slider shows
  the change and the value returns to the original on the second trigger.
- Excel smart zoom jumps straight to the fitting zoom rather than animating, because Excel reports a
  fitting zoom only by performing one. An empty cell with no data around it is not a block, so a
  trigger there does nothing.
- The pinch in a PDF reader magnifies by a fixed amount rather than fitting the page to the window:
  readers do not say how big the page is, and the gesture is aimed, not computed. Raise or lower
  `Zoom.Reader.Scale` to taste.
- The second press in a PDF reader ends on the reader's own "fit page", not on whatever zoom you had
  before. Windows' gesture recognizer keeps back a share of a closing pinch, and how much depends on
  the zoom it starts from, so an inverse gesture alone left the reader about 2 % smaller every time
  and compounded (measured in Acrobat at the default x2: in x1.92, out x0.512). Naming a state
  instead of reversing a change is what makes the second press exact however many times it is
  pressed. The view comes back within about a line of text, and the first gesture also switches
  Acrobat into its touch mode, which widens its toolbars once and shifts the page slightly.
- A reader that ignores touch should have `Zoom.Reader.Mode` set to `"Shortcuts"`. SmartZoom then uses the
  reader's own fit-width and fit-page shortcuts, which are exact but jump rather than animate, and
  scrolls the block under the cursor to the top first. That scroll is measured from the screen,
  because readers expose no scroll position; on a page it cannot read — a blank area, or one whose
  lines are too even to tell apart — it assumes the scroll went as asked, and the return may then be
  out by the difference.

## Roadmap

1. ✅ **M1** Tray app, mouse hook, double-tap detection, target routing diagnostics
2. ✅ **M2** Ctrl+wheel adapter with per-window toggle state
2½. ✅ **M2.5** Keyboard hotkeys and multiple simultaneous triggers
3. ✅ **M3** Native smart zoom in Chromium browsers (accessibility hit-test + touch pinch)
4. 🔧 **M4** Firefox ✅, per-user installer
5. 🔧 **M5** Office and PDF readers: Word ✅, Acrobat ✅, Excel ✅, PowerPoint (unclaimed — see
   [docs/adding-an-application.md](docs/adding-an-application.md))
6. **M6** Settings UI, live reload, multi-monitor and mixed-DPI polish

## Contributing

Bug reports, measurements on hardware that is not the author's, and support for one more application are all
welcome. [CONTRIBUTING.md](CONTRIBUTING.md) covers getting it building and what a pull request needs;
[docs/adding-an-application.md](docs/adding-an-application.md) covers adding an application, which often needs
no code at all.

Everyone taking part is expected to follow the [Code of Conduct](CODE_OF_CONDUCT.md). Security issues go
through [SECURITY.md](SECURITY.md) rather than a public issue.

## License

[MIT](LICENSE)
