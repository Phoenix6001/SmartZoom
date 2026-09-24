# Contributing to SmartZoom

Thanks for looking. SmartZoom is a small, opinionated Windows tray app, and the most useful contributions are
usually support for one more application or one more measurement on hardware that is not the author's.

## Getting it running

You need the .NET 10 SDK; the SDK band is pinned in `global.json` (10.0.x, rolling forward to the latest
feature band).

```powershell
dotnet build
dotnet test
dotnet run --project src/SmartZoom.App
```

It runs as a tray icon. Double-click the icon for the settings window; the right-click menu has the rest (see
the README).

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
  members in Core and Interop need XML documentation (the App and the test projects are exempt).
- `dotnet format` leaves nothing to change. CI checks this, and it is the first thing that will fail.
- `dotnet test` passes. CI also collects coverage and uploads it as the `coverage` artifact: one
  `coverage.cobertura.xml` per test project. To read it, install
  [ReportGenerator](https://github.com/danielpalme/ReportGenerator)
  (`dotnet tool install -g dotnet-reportgenerator-globaltool`) and run
  `reportgenerator -reports:"**/coverage.cobertura.xml" -targetdir:coverage-report`, then open
  `coverage-report/index.html`.
- You have run the manual checks in [docs/testing.md](docs/testing.md) for whatever you touched, **and for one
  browser**, because the gesture code is shared. Automated tests cannot tell you whether a zoom looked right.

Commit messages are a summary line and a bullet list. Keep unrelated changes in separate commits.

## The kinds of change, and what each needs

**Supporting another application.** Start at
[docs/adding-an-application.md](docs/adding-an-application.md). Many applications need no code at all, just a
routing entry — try that first and say so in the issue, because it may be a documentation fix rather than a
feature.

**Changing a measured constant.** Read [docs/measurements.md](docs/measurements.md) first. Most numbers there
were measured on one 3840×2160 display at 200%; the table says which came from browser source instead. Several
are the difference between working and dragging a window's edge across the screen. Change the number, not the
algorithm, and write down what you measured and on what hardware.

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
real touch injection, none of which works on a CI runner; it is verified by hand, on one machine, through the
acceptance run in [docs/testing.md](docs/testing.md). If you find yourself wanting to test something there,
that is a good sign the logic belongs in Core.

`SmartZoom.App` does have one, `tests/SmartZoom.App.Tests`, in the solution and run by CI alongside
`tests/SmartZoom.Core.Tests`. It exists for the things that are genuinely App's own — wiring real components
together end to end, the way `DiagnosticSampleFactoryTests` proves the diagnostics record carries no
coordinates by running a real adapter and a real coordinator rather than asserting against a type that has no
field to leak one in the first place. Prefer Core for anything that can be tested with fakes; reach for App's
test project only when the thing under test is the composition itself.

## Reporting a bug

Please say which application, what you pointed at, and what you expected, and attach the diagnostic report:
Settings → Diagnostics → **Copy**, then paste it into the issue. It carries the counts and the recent examples
that "it does not zoom in X" needs to be acted on — process names, which adapter ran, and why it did nothing —
without you having to go find a log file. Nothing in it is sent anywhere until you paste it; that page is
what reading it and choosing to hand it over looks like.

If you're already debugging something with the log open, tick **Include recent log lines** before copying —
it's opt-in because the log is the least controlled content in the system, so it goes in only when you choose
it.

## Releases

A release is a `vX.Y.Z` tag: the workflow builds the installer and the single executable, checks the tag
against `Directory.Build.props` and `CHANGELOG.md`, and opens a draft GitHub Release for a maintainer to
publish. [docs/releasing.md](docs/releasing.md) has the steps.

## Code of conduct

By participating you agree to the [Code of Conduct](CODE_OF_CONDUCT.md).
