# Contributing to SmartZoom

Thanks for looking. SmartZoom is a small, opinionated Windows tray app, and the most useful contributions are
usually support for one more application or one more measurement on hardware that is not the author's.

## Getting it running

You need the .NET 10 SDK; the exact version is pinned in `global.json`.

```powershell
dotnet build
dotnet test
dotnet run --project src/SmartZoom.App
```

It runs as a tray icon with no window. Right-click it for the settings file, the log folder and an on/off
switch.

**One gotcha that will bite you on your second build:** a running SmartZoom locks its own DLLs, so stop it
first.

```powershell
Get-Process SmartZoom -ErrorAction SilentlyContinue | Stop-Process -Force
```

## Where things live

`SmartZoom.Core` holds every decision and may not reference Win32. `SmartZoom.Interop` holds all the P/Invoke,
COM and screen access, behind interfaces declared in Core. `SmartZoom.App` is the tray icon and the
composition root. [docs/architecture.md](docs/architecture.md) walks one press from the hook to the zoom.

That split is the whole reason the project is testable. If you need a Win32 type in Core, declare an interface
instead.

## Before you open a pull request

- `dotnet build` is warning-free. Warnings are errors here, analyzers run at `latest-recommended`, and public
  members need XML documentation.
- `dotnet format` leaves nothing to change. CI checks this, and it is the first thing that will fail.
- `dotnet test` passes.
- You have run the manual checks in [docs/testing.md](docs/testing.md) for whatever you touched, **and for one
  browser**, because the gesture code is shared. Automated tests cannot tell you whether a zoom looked right.

Commit messages are a summary line and a bullet list. Keep unrelated changes in separate commits.

## The kinds of change, and what each needs

**Supporting another application.** Start at
[docs/adding-an-application.md](docs/adding-an-application.md). Many applications need no code at all, just a
routing entry — try that first and say so in the issue, because it may be a documentation fix rather than a
feature.

**Changing a measured constant.** Read [docs/measurements.md](docs/measurements.md) first. Every number there
was measured on a 200% display, and several are the difference between working and dragging a window's edge
across the screen. Change the number, not the algorithm, and write down what you measured and on what
hardware.

**Fixing a zoom that misbehaves.** Include the log lines. `%LOCALAPPDATA%\SmartZoom\logs` records the window
each press resolved and the adapter that handled it, which usually identifies the layer at fault in one line.

**Touching the input hook.** `LowLevelInputHook`'s callback must stay allocation-free, must not log, and must
not block. Windows removes a hook that is slow, and the symptom is that the app stops responding to the
trigger with nothing in the log.

## Tests

xUnit, fakes rather than mocks, and test names that read as sentences:

```csharp
[Fact]
public async Task The_view_is_scrolled_to_the_row_under_the_cursor_not_the_top_of_the_block()
```

The interesting tests are the ones where the application misbehaves: it scrolled less than you asked, it
refused the shortcut, the window closed mid-zoom. Those are the cases that were found by hand and should never
have to be found again.

`SmartZoom.Interop` has no test project. What is left in it needs a real window, a real accessibility tree or
real touch injection, none of which works on a CI runner. If you find yourself wanting to test something there,
that is a good sign the logic belongs in Core.

## Reporting a bug

Please say which application, what you pointed at, what you expected, and what the log said. "It does not zoom
in X" is hard to act on; the `Trigger at …` line plus the warning that follows it usually explains itself.

## Code of conduct

By participating you agree to the [Code of Conduct](CODE_OF_CONDUCT.md).
