# SmartZoom for Windows

macOS-style **smart zoom** for Windows: double-press a mouse button and the application under the
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

> **Status:** early development. Milestone 1 (trigger capture and routing diagnostics) is done;
> nothing zooms yet. See [Roadmap](#roadmap).

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
  "Trigger": {
    "Button": "XButton2",          // Middle | XButton1 | XButton2
    "DoubleTapWindowMs": null,     // null = system double-click time
    "SwallowClicks": false         // see below
  },
  "Routing": {
    "BrowserProcesses": ["chrome", "msedge", "firefox"],
    "CtrlWheelProcesses": ["Acrobat", "AcroRd32", "SumatraPDF", "i_view64", "i_view32", "EXCEL"],
    "WordProcesses": ["WINWORD"],
    "PowerPointProcesses": ["POWERPNT"],
    "Overrides": { "EXCEL": "None" } // per-process override; wins over the lists
  },
  "Zoom": { "MinScale": 1.1, "MaxScale": 3.0, "Animate": true }
}
```

**`SwallowClicks`** hides trigger presses from the target app. Because SmartZoom can't know at the
first press whether a second is coming, it holds that press back and replays it once the double-tap
window expires. With `XButton1`/`XButton2` this delays Back/Forward by the window length. With the
default (`false`), presses reach the app immediately and it also sees them.

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

## Known limitations

- **Elevated windows can't be zoomed.** Windows' User Interface Privilege Isolation (UIPI) blocks a
  non-elevated process from sending input to windows running as administrator. This is by design.
  SmartZoom does not require admin rights.
- Ctrl+wheel zoom is approximate: if an app hits its zoom limit, toggling back may not restore
  the exact previous level.

## Roadmap

1. ✅ **M1** Tray app, mouse hook, double-tap detection, target routing diagnostics
2. **M2** Ctrl+wheel adapter with per-window toggle state
3. **M3** Native messaging host and Chrome extension with element-aware zoom
4. **M4** Edge and Firefox support, per-user installer for native messaging manifests
5. **M5** Word and PowerPoint COM adapters with exact restore
6. **M6** Settings UI, live reload, multi-monitor and mixed-DPI polish

## License

[MIT](LICENSE)
