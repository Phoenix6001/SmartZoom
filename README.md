# SmartZoom for Windows

macOS-style **smart zoom** for Windows: press a mouse button and the application under the
cursor zooms its *content* to fit the element you're pointing at. Press again to return to exactly
where you were.

Windows has no system gesture for this, so SmartZoom is a small tray app that captures a mouse trigger
and routes it to a per-application zoom strategy:

| Target | Strategy | Precision |
|---|---|---|
| Chrome, Edge, Firefox | Browser extension over native messaging: hit-tests the DOM element under the cursor and fits it to the viewport | Exact, element-aware |
| Word, PowerPoint | Office object model (COM) | Exact zoom, exact restore |
| PDF viewers, Excel, image viewers | Synthesized Ctrl+wheel centered on the cursor | Approximate |
| Everything else | Ignored | — |

> **Status:** early development. Ctrl+wheel zoom with toggle-back works for the configured PDF,
> image and spreadsheet apps; browser and Office adapters are next. See [Roadmap](#roadmap).

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

SmartZoom runs in the notification area. Right-click the icon for **Enabled**, **Open settings file**,
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
    "BrowserProcesses": ["chrome", "msedge", "firefox"],
    "CtrlWheelProcesses": ["Acrobat", "AcroRd32", "SumatraPDF", "i_view64", "i_view32", "EXCEL"],
    "WordProcesses": ["WINWORD"],
    "PowerPointProcesses": ["POWERPNT"],
    "Overrides": { "EXCEL": "None" } // per-process override; wins over the lists
  },
  "Zoom": {
    "MinScale": 1.1,
    "MaxScale": 3.0,
    "Animate": true,
    "FallbackToCtrlWheel": true,   // use Ctrl+wheel when a richer adapter can't act
    "CtrlWheel": { "Ticks": 6, "IntervalMs": 20 }
  }
}
```

To try SmartZoom on an app that isn't listed but zooms with Ctrl+wheel (Windows 11 Notepad, for
example), add it to `Overrides`: `"Overrides": { "notepad": "CtrlWheel" }`.

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

```
src/
  SmartZoom.Core/      Platform-independent logic: trigger state machine, routing, settings model.
                       No Win32; fully unit-tested.
  SmartZoom.Interop/   Win32 via CsWin32, behind interfaces declared in Core.
  SmartZoom.App/       WinForms tray app and composition root (Generic Host + Serilog).
tests/
  SmartZoom.Core.Tests/
```

Design notes:

- **Low-level mouse hook, not Raw Input.** Only a hook can swallow events. The hook owns a dedicated
  thread that just pumps messages. Its callback runs an allocation-free state machine under a short lock
  and hands work to channels, because Windows silently removes hooks that exceed `LowLevelHooksTimeout`.
- **Per-Monitor V2 DPI awareness.** Hook coordinates are physical pixels and are kept physical end to
  end, so `WindowFromPoint` is correct on mixed-DPI multi-monitor setups.
- **Replayed input is tagged** in `dwExtraInfo` so the hook ignores its own injections. Input
  injected by other software, such as button remappers, is still processed.

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

## Roadmap

1. ✅ **M1** Tray app, mouse hook, double-tap detection, target routing diagnostics
2. ✅ **M2** Ctrl+wheel adapter with per-window toggle state
3. **M3** Native messaging host and Chrome extension with element-aware zoom
4. **M4** Edge and Firefox support, per-user installer for native messaging manifests
5. **M5** Word and PowerPoint COM adapters with exact restore
6. **M6** Settings UI, live reload, multi-monitor and mixed-DPI polish

## License

[MIT](LICENSE)
