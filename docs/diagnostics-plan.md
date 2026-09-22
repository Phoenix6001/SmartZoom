# Diagnostics Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Record, locally, what SmartZoom failed to do, and render it as a markdown report a user can read and paste into a bug report.

**Architecture:** A pure tally in `SmartZoom.Core/Diagnostics` (counters keyed by process/adapter/reason, plus a short ring of detailed samples), persisted as capped JSON by `SmartZoom.App`, rendered to markdown against an `IMachineFacts` interface that `SmartZoom.Interop` implements. Recording hangs off two seams that already exist — `ZoomActivity` (outcomes) and `TriggerDispatcher` (caught adapter exceptions) — so nothing new is threaded through the zoom pipeline.

**Tech Stack:** .NET 10, C# 13, WinForms, xUnit, `System.Text.Json`, CsWin32.

**Spec:** [docs/diagnostics-design.md](diagnostics-design.md)

## Global Constraints

- **No network code.** No HTTP client, no socket, no endpoint, no upload — not even disabled. `SECURITY.md`'s "makes no network connections and sends nothing anywhere" must remain literally true.
- **No identifier.** No install ID, session ID, GUID, hardware or user-derived value.
- **Never recorded:** window titles, page text, URLs, absolute screen coordinates, keystrokes, username, machine name, captured pixels.
- **Absolute paths are rewritten** to `%APPDATA%` / `%LOCALAPPDATA%` everywhere they are rendered, including inside exception messages.
- `dotnet build` must be warning-free (warnings are errors, analyzers at `latest-recommended`, XML docs required on public members).
- `dotnet format --verify-no-changes` must pass.
- `SmartZoom.Core` may not reference Win32. Win32 goes in `SmartZoom.Interop` behind an interface declared in Core.
- Nothing added to the low-level hook callback: it stays allocation-free, non-logging, non-blocking.
- Tests: xUnit, fakes not mocks, one nested class per scenario, `Snake_case_sentence` names.
- **Stop a running SmartZoom before building** — it locks its own DLLs: `Get-Process SmartZoom -ErrorAction SilentlyContinue | Stop-Process -Force`.
- Caps, fixed: **200** counter keys, **20** detail samples, **12** path nodes, **4000** stack characters, **256 KB** file.

---

### Task 1: A zoom that did nothing says why

The spec records `ZoomedNothing` with a reason, but no reason survives to where recording happens: `ZoomInResult.Handled` is a parameterless singleton and `ZoomCoordinator` maps `Handled` to `ZoomAction.ZoomedIn`. That also means the tray tooltip currently says "Zoomed in brave via Browser" when nothing happened. This task fixes both.

**Files:**
- Create: `src/SmartZoom.Core/Zoom/ZoomReason.cs`
- Modify: `src/SmartZoom.Core/Zoom/ZoomAction.cs`, `src/SmartZoom.Core/Zoom/ZoomInResult.cs`, `src/SmartZoom.Core/Zoom/ZoomOutcome.cs`, `src/SmartZoom.Core/Zoom/ZoomCoordinator.cs`, and every adapter that returns `ZoomInResult.Handled` (the compiler lists them)
- Test: `tests/SmartZoom.Core.Tests/Zoom/ZoomCoordinatorTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `ZoomReason` enum; `ZoomInResult.Handled(ZoomReason reason)`; `ZoomAction.Handled`; `ZoomOutcome.Reason` of type `ZoomReason?`.

- [ ] **Step 1: Write the failing test**

In `tests/SmartZoom.Core.Tests/Zoom/ZoomCoordinatorTests.cs`, inside the existing test class:

```csharp
public sealed class An_adapter_that_did_nothing
{
    [Fact]
    public async Task Is_not_reported_as_a_zoom()
    {
        var adapter = new FakeAdapter(ZoomInResult.Handled(ZoomReason.NoBlock));
        var coordinator = Build(adapter);

        var outcome = await coordinator.HandleTriggerAsync(Brave, Cursor, CancellationToken.None);

        Assert.Equal(ZoomAction.Handled, outcome.Action);
        Assert.Equal(ZoomReason.NoBlock, outcome.Reason);
    }
}
```

- [ ] **Step 2: Run the test and watch it fail**

```powershell
dotnet test --filter "Is_not_reported_as_a_zoom"
```

Expected: compile error — `ZoomReason` does not exist and `Handled` is a property, not a method.

- [ ] **Step 3: Add the reason type**

Create `src/SmartZoom.Core/Zoom/ZoomReason.cs`:

```csharp
namespace SmartZoom.Core.Zoom;

/// <summary>Why a trigger produced no zoom. Not every one of these is a defect.</summary>
public enum ZoomReason
{
    /// <summary>The application exposed no content under the cursor.</summary>
    NoContent,

    /// <summary>Content was found, but nothing on the path was a sensible thing to magnify.</summary>
    NoBlock,

    /// <summary>The block already fills the viewport, so there is nothing to zoom to.</summary>
    AlreadyFits,

    /// <summary>The gesture was refused by the injector.</summary>
    GestureRefused,

    /// <summary>No adapter is configured for this application.</summary>
    NoAdapter,
}
```

- [ ] **Step 4: Add the action and carry the reason**

In `ZoomAction.cs` add, after `Unhandled`:

```csharp
    /// <summary>An adapter dealt with the trigger and deliberately changed nothing.</summary>
    Handled,
```

In `ZoomInResult.cs` replace the `Handled` property with:

```csharp
    /// <summary>Dealt with, with nothing for the coordinator to undo and no fallback to try.</summary>
    /// <param name="reason">Why nothing was zoomed; recorded by diagnostics and shown in the tray.</param>
    public static ZoomInResult Handled(ZoomReason reason) => new(ZoomInStatus.Handled, null) { Reason = reason };

    /// <summary>Why nothing happened, when <see cref="Status"/> is <see cref="ZoomInStatus.Handled"/>.</summary>
    public ZoomReason? Reason { get; private init; }
```

In `ZoomOutcome.cs` add `ZoomReason? Reason = null` as the last record parameter, and extend `ToString()`:

```csharp
        ZoomAction.Handled => $"Nothing to zoom in {Process} ({Reason})",
```

- [ ] **Step 5: Map it in the coordinator**

In `ZoomCoordinator.cs`, the `ZoomInStatus.Handled` branch currently returns `ZoomAction.ZoomedIn`. Change it to:

```csharp
            case ZoomInStatus.Handled:
                LogHandled(target.ProcessName, id);
                return new ZoomOutcome(ZoomAction.Handled, target.ProcessName, id, result.Reason);
```

And in the "no adapter" path, return `new ZoomOutcome(ZoomAction.Ignored, process, null, ZoomReason.NoAdapter)`.

- [ ] **Step 6: Fix every call site the compiler names**

```powershell
Get-Process SmartZoom -ErrorAction SilentlyContinue | Stop-Process -Force
dotnet build
```

Replace each `return ZoomInResult.Handled;` with the reason that call site already logs — e.g. in `BrowserAdapter`, the `LogNoContent` path becomes `ZoomInResult.Handled(ZoomReason.NoContent)`, the `LogNoBlock` path `ZoomReason.NoBlock`, the `LogAlreadyFits` path `ZoomReason.AlreadyFits`, and the `LogPinchRejected` path `ZoomReason.GestureRefused`.

- [ ] **Step 7: Run the tests**

```powershell
dotnet test
```

Expected: PASS. Existing tests asserting `ZoomAction.ZoomedIn` for a handled-with-nothing case must be updated to `ZoomAction.Handled` — that assertion was encoding the bug.

- [ ] **Step 8: Commit**

```powershell
git add src/SmartZoom.Core/Zoom tests/SmartZoom.Core.Tests/Zoom
git commit -m "A zoom that changed nothing no longer reports itself as a zoom"
```

---

### Task 2: The tally

**Files:**
- Create: `src/SmartZoom.Core/Diagnostics/DiagnosticKind.cs`, `DiagnosticKey.cs`, `DiagnosticCounter.cs`, `DiagnosticSample.cs`, `DiagnosticRecord.cs`
- Test: `tests/SmartZoom.Core.Tests/Diagnostics/DiagnosticRecordTests.cs`

**Interfaces:**
- Consumes: `ZoomReason` (Task 1).
- Produces: `DiagnosticRecord` with `Note(DiagnosticKey key, DateTimeOffset when)`, `Sample(DiagnosticSample sample)`, `IReadOnlyList<DiagnosticCounter> Counters`, `IReadOnlyList<DiagnosticSample> Samples`, `int OmittedKeys`, `string Version`.

- [ ] **Step 1: Write the failing tests**

Create `tests/SmartZoom.Core.Tests/Diagnostics/DiagnosticRecordTests.cs`:

```csharp
using SmartZoom.Core.Diagnostics;

namespace SmartZoom.Core.Tests.Diagnostics;

public class DiagnosticRecordTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);

    private static DiagnosticKey Key(string process = "msedge") =>
        new(DiagnosticKind.ZoomedNothing, process, "Browser", "NoBlock");

    public sealed class The_same_thing_happening_twice
    {
        [Fact]
        public void Is_one_counter_with_a_count_of_two()
        {
            var record = new DiagnosticRecord("0.1.0");

            record.Note(Key(), Noon);
            record.Note(Key(), Noon.AddMinutes(5));

            var counter = Assert.Single(record.Counters);
            Assert.Equal(2, counter.Count);
            Assert.Equal(Noon, counter.FirstSeen);
            Assert.Equal(Noon.AddMinutes(5), counter.LastSeen);
        }
    }

    public sealed class More_keys_than_the_cap
    {
        [Fact]
        public void Keeps_the_first_two_hundred_and_counts_the_rest()
        {
            var record = new DiagnosticRecord("0.1.0");

            for (var i = 0; i < 205; i++)
                record.Note(Key($"app{i}"), Noon);

            Assert.Equal(200, record.Counters.Count);
            Assert.Equal(5, record.OmittedKeys);
        }

        [Fact]
        public void Still_counts_a_key_it_already_knows()
        {
            var record = new DiagnosticRecord("0.1.0");
            for (var i = 0; i < 205; i++)
                record.Note(Key($"app{i}"), Noon);

            record.Note(Key("app0"), Noon);

            Assert.Equal(2, record.Counters.Single(c => c.Key.Process == "app0").Count);
        }
    }

    public sealed class More_samples_than_the_ring
    {
        [Fact]
        public void Keeps_only_the_most_recent_twenty()
        {
            var record = new DiagnosticRecord("0.1.0");

            for (var i = 0; i < 25; i++)
                record.Sample(new DiagnosticSample(Key(), Noon, $"shape{i}", null));

            Assert.Equal(20, record.Samples.Count);
            Assert.Equal("shape24", record.Samples[^1].Detail);
            Assert.DoesNotContain(record.Samples, s => s.Detail == "shape4");
        }
    }
}
```

- [ ] **Step 2: Run them and watch them fail**

```powershell
dotnet test --filter "DiagnosticRecordTests"
```

Expected: compile error — the `SmartZoom.Core.Diagnostics` namespace does not exist.

- [ ] **Step 3: Write the types**

`DiagnosticKind.cs`:

```csharp
namespace SmartZoom.Core.Diagnostics;

/// <summary>The class of thing that went wrong, or deliberately did nothing.</summary>
public enum DiagnosticKind
{
    /// <summary>A press resolved to a window and produced no zoom.</summary>
    ZoomedNothing,

    /// <summary>An adapter raised an exception the dispatcher caught.</summary>
    AdapterThrew,

    /// <summary>A trigger resolved to no window at all.</summary>
    NoWindow,

    /// <summary>An unhandled exception reached the top of the process.</summary>
    Crashed,
}
```

`DiagnosticKey.cs`:

```csharp
namespace SmartZoom.Core.Diagnostics;

/// <summary>What a counter counts. Deliberately holds no title, no text and no coordinates.</summary>
/// <param name="Kind">The class of event.</param>
/// <param name="Process">Image name of the application, or null when it could not be identified.</param>
/// <param name="Adapter">The strategy involved, or null when none was chosen.</param>
/// <param name="Reason">Why, as a stable label.</param>
public sealed record DiagnosticKey(DiagnosticKind Kind, string? Process, string? Adapter, string? Reason);
```

`DiagnosticCounter.cs`:

```csharp
namespace SmartZoom.Core.Diagnostics;

/// <summary>How often one thing has happened, and when it first and last did.</summary>
public sealed class DiagnosticCounter
{
    /// <summary>Creates a counter at its first occurrence.</summary>
    /// <param name="key">What is being counted.</param>
    /// <param name="when">When it first happened.</param>
    public DiagnosticCounter(DiagnosticKey key, DateTimeOffset when)
    {
        Key = key;
        FirstSeen = when;
        LastSeen = when;
        Count = 1;
    }

    /// <summary>What is being counted.</summary>
    public DiagnosticKey Key { get; }

    /// <summary>How many times it has happened.</summary>
    public int Count { get; private set; }

    /// <summary>When it first happened.</summary>
    public DateTimeOffset FirstSeen { get; }

    /// <summary>When it last happened.</summary>
    public DateTimeOffset LastSeen { get; private set; }

    /// <summary>Records one more occurrence.</summary>
    /// <param name="when">When it happened.</param>
    public void Add(DateTimeOffset when)
    {
        Count++;
        LastSeen = when;
    }
}
```

`DiagnosticSample.cs`:

```csharp
namespace SmartZoom.Core.Diagnostics;

/// <summary>One concrete example, kept so a count can actually be diagnosed.</summary>
/// <param name="Key">What happened.</param>
/// <param name="When">When it happened.</param>
/// <param name="Detail">The accessibility path shape, roles and sizes only, or null.</param>
/// <param name="Exception">Type, redacted message and truncated stack, or null.</param>
public sealed record DiagnosticSample(DiagnosticKey Key, DateTimeOffset When, string? Detail, string? Exception);
```

`DiagnosticRecord.cs`:

```csharp
namespace SmartZoom.Core.Diagnostics;

/// <summary>
/// A tally of what SmartZoom failed to do, bounded by construction.
/// </summary>
/// <remarks>
/// A tally rather than a journal: a journal of every event grows without limit and says less than a count.
/// When the key cap is reached new keys are refused rather than evicted, because the long tail is the
/// interesting part and silently dropping it would make the counts lie.
/// </remarks>
public sealed class DiagnosticRecord
{
    /// <summary>The most distinct things worth counting before the record stops taking new ones.</summary>
    public const int MaxKeys = 200;

    /// <summary>How many worked examples are kept.</summary>
    public const int MaxSamples = 20;

    private readonly Dictionary<DiagnosticKey, DiagnosticCounter> _counters = [];
    private readonly List<DiagnosticSample> _samples = [];

    /// <summary>Creates an empty record for one build of SmartZoom.</summary>
    /// <param name="version">The version that produced it; a record from another version is discarded on load.</param>
    public DiagnosticRecord(string version) => Version = version;

    /// <summary>The SmartZoom version this record describes.</summary>
    public string Version { get; }

    /// <summary>The counters, most frequent first.</summary>
    public IReadOnlyList<DiagnosticCounter> Counters =>
        [.. _counters.Values.OrderByDescending(c => c.Count)];

    /// <summary>The worked examples, oldest first.</summary>
    public IReadOnlyList<DiagnosticSample> Samples => _samples;

    /// <summary>How many distinct things were not counted because the cap was reached.</summary>
    public int OmittedKeys { get; private set; }

    /// <summary>Counts one occurrence.</summary>
    /// <param name="key">What happened.</param>
    /// <param name="when">When it happened.</param>
    public void Note(DiagnosticKey key, DateTimeOffset when)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (_counters.TryGetValue(key, out var counter))
        {
            counter.Add(when);
            return;
        }

        if (_counters.Count >= MaxKeys)
        {
            OmittedKeys++;
            return;
        }

        _counters[key] = new DiagnosticCounter(key, when);
    }

    /// <summary>Keeps one worked example, dropping the oldest when the ring is full.</summary>
    /// <param name="sample">The example.</param>
    public void Sample(DiagnosticSample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);

        _samples.Add(sample);
        if (_samples.Count > MaxSamples)
            _samples.RemoveAt(0);
    }
}
```

- [ ] **Step 4: Run the tests**

```powershell
dotnet test --filter "DiagnosticRecordTests"
```

Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add src/SmartZoom.Core/Diagnostics tests/SmartZoom.Core.Tests/Diagnostics
git commit -m "Add the diagnostics tally"
```

---

### Task 3: Redaction

**Files:**
- Create: `src/SmartZoom.Core/Diagnostics/Redaction.cs`
- Test: `tests/SmartZoom.Core.Tests/Diagnostics/RedactionTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `static string Redaction.Paths(string text, string home, string localAppData, string appData)` and `static string Redaction.Truncate(string text, int max)`.

Passing the folders in rather than reading `Environment` keeps this pure and testable on any machine.

- [ ] **Step 1: Write the failing tests**

```csharp
using SmartZoom.Core.Diagnostics;

namespace SmartZoom.Core.Tests.Diagnostics;

public class RedactionTests
{
    private const string Home = @"C:\Users\ada";
    private const string Local = @"C:\Users\ada\AppData\Local";
    private const string Roaming = @"C:\Users\ada\AppData\Roaming";

    private static string Redact(string text) => Redaction.Paths(text, Home, Local, Roaming);

    public sealed class A_path_under_the_profile
    {
        [Fact]
        public void Becomes_an_environment_variable()
        {
            Assert.Equal(@"%LOCALAPPDATA%\SmartZoom\logs", Redact(@"C:\Users\ada\AppData\Local\SmartZoom\logs"));
            Assert.Equal(@"%APPDATA%\SmartZoom\settings.json", Redact(@"C:\Users\ada\AppData\Roaming\SmartZoom\settings.json"));
            Assert.Equal(@"%USERPROFILE%\Documents\tax.pdf", Redact(@"C:\Users\ada\Documents\tax.pdf"));
        }

        [Fact]
        public void Is_replaced_inside_a_longer_sentence()
        {
            var message = @"Could not access 'C:\Users\ada\Documents\tax.pdf' because it is in use.";

            Assert.DoesNotContain("ada", Redact(message), StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Is_matched_whatever_its_casing()
        {
            Assert.Equal(@"%USERPROFILE%\x", Redact(@"c:\users\ADA\x"));
        }
    }

    public sealed class Text_longer_than_the_limit
    {
        [Fact]
        public void Is_cut_and_marked()
        {
            var cut = Redaction.Truncate(new string('x', 50), 10);

            Assert.StartsWith("xxxxxxxxxx", cut, StringComparison.Ordinal);
            Assert.Contains("truncated", cut, StringComparison.Ordinal);
        }

        [Fact]
        public void Shorter_text_is_untouched()
        {
            Assert.Equal("short", Redaction.Truncate("short", 10));
        }
    }
}
```

- [ ] **Step 2: Run them and watch them fail**

```powershell
dotnet test --filter "RedactionTests"
```

Expected: compile error — `Redaction` does not exist.

- [ ] **Step 3: Write it**

```csharp
using System.Globalization;

namespace SmartZoom.Core.Diagnostics;

/// <summary>
/// Removes the parts of a string that identify the person running SmartZoom.
/// </summary>
/// <remarks>
/// Applied to everything rendered into a report, exception messages included: an <see cref="IOException"/>
/// carries the path that failed, and that path usually contains a username and often a document name.
/// The longest folder is replaced first so that the profile root does not shadow the folders beneath it.
/// </remarks>
public static class Redaction
{
    /// <summary>Replaces well-known user folders with the variables that name them.</summary>
    /// <param name="text">The text to clean.</param>
    /// <param name="home">The user profile folder.</param>
    /// <param name="localAppData">The local application data folder.</param>
    /// <param name="appData">The roaming application data folder.</param>
    /// <returns>The text with those folders replaced.</returns>
    public static string Paths(string text, string home, string localAppData, string appData)
    {
        ArgumentNullException.ThrowIfNull(text);

        foreach (var (folder, name) in Longest(home, localAppData, appData))
        {
            if (!string.IsNullOrEmpty(folder))
                text = text.Replace(folder, name, StringComparison.OrdinalIgnoreCase);
        }

        return text;
    }

    /// <summary>Cuts text to a maximum length, saying so where it was cut.</summary>
    /// <param name="text">The text.</param>
    /// <param name="max">The most characters to keep.</param>
    /// <returns>The text, cut if it was longer.</returns>
    public static string Truncate(string text, int max)
    {
        ArgumentNullException.ThrowIfNull(text);

        return text.Length <= max
            ? text
            : string.Create(CultureInfo.InvariantCulture, $"{text[..max]}… [truncated, {text.Length} characters]");
    }

    private static IEnumerable<(string Folder, string Name)> Longest(string home, string local, string roaming) =>
        new[] { (local, "%LOCALAPPDATA%"), (roaming, "%APPDATA%"), (home, "%USERPROFILE%") }
            .OrderByDescending(p => p.Item1?.Length ?? 0);
}
```

- [ ] **Step 4: Run the tests**

```powershell
dotnet test --filter "RedactionTests"
```

Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add src/SmartZoom.Core/Diagnostics/Redaction.cs tests/SmartZoom.Core.Tests/Diagnostics/RedactionTests.cs
git commit -m "Add path redaction for diagnostic output"
```

---

### Task 4: Machine facts, and the report

This is the task the privacy contract is tested in.

**Files:**
- Create: `src/SmartZoom.Core/Diagnostics/IMachineFacts.cs`, `DisplayFacts.cs`, `DiagnosticReport.cs`
- Test: `tests/SmartZoom.Core.Tests/Diagnostics/DiagnosticReportTests.cs`, `tests/SmartZoom.Core.Tests/Diagnostics/FakeMachineFacts.cs`

**Interfaces:**
- Consumes: `DiagnosticRecord`, `Redaction` (Tasks 2–3).
- Produces: `IMachineFacts` with `string AppVersion { get; }`, `string OperatingSystem { get; }`, `IReadOnlyList<DisplayFacts> Displays { get; }`; `DisplayFacts(int Width, int Height, int RefreshHz, double Scale, bool Primary)`; `static string DiagnosticReport.Render(DiagnosticRecord record, IMachineFacts facts, string settingsJson, string? logTail, Redactor redact)` where `Redactor` is `delegate string Redactor(string text)`.

- [ ] **Step 1: Write the fake**

`tests/SmartZoom.Core.Tests/Diagnostics/FakeMachineFacts.cs`:

```csharp
using SmartZoom.Core.Diagnostics;

namespace SmartZoom.Core.Tests.Diagnostics;

internal sealed class FakeMachineFacts : IMachineFacts
{
    public string AppVersion { get; set; } = "0.1.0";

    public string OperatingSystem { get; set; } = "Windows 11 (10.0.26200)";

    public IReadOnlyList<DisplayFacts> Displays { get; set; } =
        [new DisplayFacts(3840, 2160, 59, 2.0, Primary: true)];
}
```

- [ ] **Step 2: Write the failing tests, privacy contract first**

`tests/SmartZoom.Core.Tests/Diagnostics/DiagnosticReportTests.cs`:

```csharp
using SmartZoom.Core.Diagnostics;

namespace SmartZoom.Core.Tests.Diagnostics;

public class DiagnosticReportTests
{
    private static readonly DateTimeOffset Noon = new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);

    private static string Render(DiagnosticRecord record, string settings = "{}", string? logTail = null) =>
        DiagnosticReport.Render(record, new FakeMachineFacts(), settings, logTail, t => t);

    public sealed class Whatever_it_is_given
    {
        // The contract in docs/diagnostics-design.md, as a test. A comment promising this is worth little
        // to somebody reviewing from outside; a failing build when a field is added in a year is worth much.
        [Fact]
        public void A_report_never_contains_a_window_title()
        {
            var record = new DiagnosticRecord("0.1.0");
            record.Sample(new DiagnosticSample(
                new DiagnosticKey(DiagnosticKind.ZoomedNothing, "msedge", "Browser", "NoBlock"),
                Noon,
                Detail: "Group 949x79 < Document 3832x2074",
                Exception: null));

            var report = Render(record);

            Assert.DoesNotContain("Quarterly results - Microsoft Edge", report, StringComparison.Ordinal);
            Assert.Contains("Group 949x79", report, StringComparison.Ordinal);
        }

        [Fact]
        public void A_report_contains_no_absolute_user_path()
        {
            var record = new DiagnosticRecord("0.1.0");
            var settings = """{"LogDirectory":"C:\\Users\\ada\\AppData\\Local\\SmartZoom\\logs"}""";

            var report = DiagnosticReport.Render(
                record,
                new FakeMachineFacts(),
                settings,
                logTail: null,
                redact: t => Redaction.Paths(t, @"C:\Users\ada", @"C:\Users\ada\AppData\Local", @"C:\Users\ada\AppData\Roaming"));

            Assert.DoesNotContain(@"C:\Users\ada", report, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("%LOCALAPPDATA%", report, StringComparison.Ordinal);
        }
    }

    public sealed class The_machine_section
    {
        [Fact]
        public void Names_the_refresh_rate_because_that_is_what_gesture_pacing_depends_on()
        {
            var report = Render(new DiagnosticRecord("0.1.0"));

            Assert.Contains("3840x2160", report, StringComparison.Ordinal);
            Assert.Contains("59", report, StringComparison.Ordinal);
            Assert.Contains("200%", report, StringComparison.Ordinal);
        }
    }

    public sealed class The_counter_table
    {
        [Fact]
        public void Puts_the_most_frequent_first()
        {
            var record = new DiagnosticRecord("0.1.0");
            record.Note(new DiagnosticKey(DiagnosticKind.ZoomedNothing, "rare", "Browser", "NoBlock"), Noon);
            for (var i = 0; i < 9; i++)
                record.Note(new DiagnosticKey(DiagnosticKind.ZoomedNothing, "common", "Browser", "NoBlock"), Noon);

            var report = Render(record);

            Assert.True(
                report.IndexOf("common", StringComparison.Ordinal) < report.IndexOf("rare", StringComparison.Ordinal),
                "the nine-times entry should be listed above the once entry");
        }
    }

    public sealed class The_log_tail
    {
        [Fact]
        public void Is_absent_unless_it_was_asked_for()
        {
            Assert.DoesNotContain("Recent log", Render(new DiagnosticRecord("0.1.0")), StringComparison.Ordinal);
            Assert.Contains("Recent log", Render(new DiagnosticRecord("0.1.0"), logTail: "a line"), StringComparison.Ordinal);
        }
    }
}
```

- [ ] **Step 3: Run them and watch them fail**

```powershell
dotnet test --filter "DiagnosticReportTests"
```

Expected: compile error — `IMachineFacts`, `DisplayFacts` and `DiagnosticReport` do not exist.

- [ ] **Step 4: Write the facts types**

`IMachineFacts.cs`:

```csharp
namespace SmartZoom.Core.Diagnostics;

/// <summary>What the machine is, for the questions that depend on hardware rather than behaviour.</summary>
public interface IMachineFacts
{
    /// <summary>The SmartZoom version.</summary>
    string AppVersion { get; }

    /// <summary>A human-readable Windows version.</summary>
    string OperatingSystem { get; }

    /// <summary>Every display attached, primary first.</summary>
    IReadOnlyList<DisplayFacts> Displays { get; }
}
```

`DisplayFacts.cs`:

```csharp
namespace SmartZoom.Core.Diagnostics;

/// <summary>One display. Refresh rate is here because gesture pacing follows it.</summary>
/// <param name="Width">Width in physical pixels.</param>
/// <param name="Height">Height in physical pixels.</param>
/// <param name="RefreshHz">Refresh rate in hertz, or 0 when the display did not report one.</param>
/// <param name="Scale">Scale factor, where 2.0 is 200%.</param>
/// <param name="Primary">Whether this is the primary display.</param>
public sealed record DisplayFacts(int Width, int Height, int RefreshHz, double Scale, bool Primary);
```

- [ ] **Step 5: Write the renderer**

`DiagnosticReport.cs`:

```csharp
using System.Globalization;
using System.Text;

namespace SmartZoom.Core.Diagnostics;

/// <summary>Cleans one piece of text before it is rendered.</summary>
/// <param name="text">The text.</param>
/// <returns>The text with identifying parts removed.</returns>
public delegate string Redactor(string text);

/// <summary>
/// Renders a record as markdown meant to be read by the person who produced it, then pasted into an issue.
/// </summary>
/// <remarks>
/// Human-readable rather than encoded, because showing the report is the whole consent mechanism: consent
/// to something unreadable is not consent. A section that throws degrades to "unavailable" rather than
/// costing the reader the rest of the report.
/// </remarks>
public static class DiagnosticReport
{
    /// <summary>Builds the report.</summary>
    /// <param name="record">What went wrong.</param>
    /// <param name="facts">What the machine is.</param>
    /// <param name="settingsJson">The settings file's contents.</param>
    /// <param name="logTail">Recent log lines, or null when the user did not ask for them.</param>
    /// <param name="redact">Applied to every piece of free text.</param>
    /// <returns>Markdown.</returns>
    public static string Render(
        DiagnosticRecord record,
        IMachineFacts facts,
        string settingsJson,
        string? logTail,
        Redactor redact)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(redact);

        var text = new StringBuilder();
        text.AppendLine("## SmartZoom diagnostic report").AppendLine();

        Section(text, "Version", () =>
            text.AppendLine(CultureInfo.InvariantCulture, $"- SmartZoom {facts.AppVersion}")
                .AppendLine(CultureInfo.InvariantCulture, $"- {facts.OperatingSystem}"));

        Section(text, "Displays", () =>
        {
            foreach (var d in facts.Displays)
            {
                var role = d.Primary ? " (primary)" : string.Empty;
                text.AppendLine(CultureInfo.InvariantCulture,
                    $"- {d.Width}x{d.Height} at {d.RefreshHz} Hz, {d.Scale * 100:0}%{role}");
            }
        });

        Section(text, "Settings", () =>
            text.AppendLine("```json").AppendLine(redact(settingsJson)).AppendLine("```"));

        Section(text, "What didn't work", () =>
        {
            if (record.Counters.Count == 0)
            {
                text.AppendLine("Nothing recorded.");
                return;
            }

            text.AppendLine("| Count | Application | Strategy | What happened | Last seen |");
            text.AppendLine("|---|---|---|---|---|");
            foreach (var c in record.Counters)
            {
                text.AppendLine(CultureInfo.InvariantCulture,
                    $"| {c.Count} | {c.Key.Process ?? "?"} | {c.Key.Adapter ?? "-"} | {c.Key.Kind}/{c.Key.Reason ?? "-"} | {c.LastSeen:u} |");
            }

            if (record.OmittedKeys > 0)
            {
                text.AppendLine(CultureInfo.InvariantCulture,
                    $"{Environment.NewLine}{record.OmittedKeys} further distinct events were not counted (cap reached).");
            }
        });

        Section(text, "Recent details", () =>
        {
            foreach (var s in record.Samples)
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"- **{s.Key.Process ?? "?"}** {s.Key.Kind}/{s.Key.Reason ?? "-"} at {s.When:u}");
                if (s.Detail is { } detail)
                    text.AppendLine(CultureInfo.InvariantCulture, $"  - path: `{redact(detail)}`");
                if (s.Exception is { } exception)
                    text.AppendLine("  - ```").AppendLine(redact(exception)).AppendLine("    ```");
            }
        });

        if (logTail is { Length: > 0 })
            Section(text, "Recent log", () => text.AppendLine("```").AppendLine(redact(logTail)).AppendLine("```"));

        return text.ToString();
    }

    private static void Section(StringBuilder text, string title, Action body)
    {
        text.AppendLine(CultureInfo.InvariantCulture, $"### {title}").AppendLine();
        try
        {
            body();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"unavailable ({ex.GetType().Name})");
        }

        text.AppendLine();
    }
}
```

- [ ] **Step 6: Run the tests**

```powershell
dotnet test --filter "DiagnosticReportTests"
```

Expected: PASS.

- [ ] **Step 7: Commit**

```powershell
git add src/SmartZoom.Core/Diagnostics tests/SmartZoom.Core.Tests/Diagnostics
git commit -m "Render a diagnostic report, with the privacy contract under test"
```

---

### Task 5: Reading the machine

**Files:**
- Create: `src/SmartZoom.Interop/Windows/MachineFacts.cs`
- Modify: `src/SmartZoom.Interop/NativeMethods.txt`

**Interfaces:**
- Consumes: `IMachineFacts`, `DisplayFacts` (Task 4).
- Produces: `MachineFacts : IMachineFacts`, constructed with no arguments.

`SmartZoom.Interop` has no test project by design — this needs real monitors.

- [ ] **Step 1: Declare the Win32 surface**

Append to `src/SmartZoom.Interop/NativeMethods.txt` (skip any already present — `EnumDisplaySettings`, `DEVMODEW` and `ENUM_DISPLAY_SETTINGS_MODE` were added earlier):

```
EnumDisplayDevices
DISPLAY_DEVICEW
GetDpiForMonitor
MonitorFromPoint
```

- [ ] **Step 2: Write it**

```csharp
using System.Globalization;
using System.Runtime.InteropServices;

using SmartZoom.Core.Diagnostics;

using Windows.Win32;
using Windows.Win32.Graphics.Gdi;

namespace SmartZoom.Interop.Windows;

/// <summary>Reads the machine's displays and Windows version.</summary>
/// <remarks>
/// Refresh rate is the reason this exists: the gesture frame interval follows the display, and a constant
/// measured on one panel was wrong on another for months with nothing in a bug report to show it.
/// </remarks>
public sealed class MachineFacts : IMachineFacts
{
    /// <inheritdoc />
    public string AppVersion { get; } =
        typeof(MachineFacts).Assembly.GetName().Version?.ToString() ?? "unknown";

    /// <inheritdoc />
    public string OperatingSystem { get; } = RuntimeInformation.OSDescription;

    /// <inheritdoc />
    public IReadOnlyList<DisplayFacts> Displays => Enumerate();

    private static List<DisplayFacts> Enumerate()
    {
        var displays = new List<DisplayFacts>();

        for (uint i = 0; ; i++)
        {
            var device = new DISPLAY_DEVICEW { cb = (uint)Marshal.SizeOf<DISPLAY_DEVICEW>() };
            if (!PInvoke.EnumDisplayDevices(null, i, ref device, 0))
                break;

            // Mirroring drivers and detached adapters describe no screen anybody is looking at.
            const uint AttachedToDesktop = 0x0000_0001;
            const uint PrimaryDevice = 0x0000_0004;
            if ((device.StateFlags & AttachedToDesktop) == 0)
                continue;

            var mode = new DEVMODEW { dmSize = (ushort)Marshal.SizeOf<DEVMODEW>() };
            if (!PInvoke.EnumDisplaySettings(device.DeviceName.ToString(), ENUM_DISPLAY_SETTINGS_MODE.ENUM_CURRENT_SETTINGS, ref mode))
                continue;

            displays.Add(new DisplayFacts(
                (int)mode.dmPelsWidth,
                (int)mode.dmPelsHeight,
                (int)mode.dmDisplayFrequency,
                Scale(mode),
                Primary: (device.StateFlags & PrimaryDevice) != 0));
        }

        return displays;
    }

    // dmLogPixels is not populated by EnumDisplaySettings on every driver, so fall back to 1.0 rather than
    // reporting a scale of zero, which would read as a fault rather than as "not reported".
    private static double Scale(DEVMODEW mode) =>
        mode.dmLogPixels > 0 ? Math.Round(mode.dmLogPixels / 96.0, 2) : 1.0;
}
```

- [ ] **Step 3: Build**

```powershell
Get-Process SmartZoom -ErrorAction SilentlyContinue | Stop-Process -Force
dotnet build
```

Expected: warning-free. If CsWin32 reports that a constant belongs to an enum, use the enum name it suggests — that is how `ENUM_DISPLAY_SETTINGS_MODE` was arrived at.

- [ ] **Step 4: Check it against the real machine**

```powershell
dotnet run --project tools/SmartZoom.Probe -- windows 1920 1080
Get-CimInstance Win32_VideoController | Select-Object CurrentRefreshRate, CurrentHorizontalResolution
```

Expected: the refresh rate and resolution reported by `MachineFacts` match `Win32_VideoController`. If `dmLogPixels` reports 1.0 on a scaled display, note it — the report says "not reported" rather than lying.

- [ ] **Step 5: Commit**

```powershell
git add src/SmartZoom.Interop
git commit -m "Read display and OS facts for the diagnostic report"
```

---

### Task 6: Persistence

**Files:**
- Create: `src/SmartZoom.App/Diagnostics/DiagnosticStore.cs`, `src/SmartZoom.App/Diagnostics/DiagnosticRecorder.cs`
- Modify: `src/SmartZoom.App/AppPaths.cs`
- Test: `tests/SmartZoom.Core.Tests/Diagnostics/DiagnosticRecordTests.cs` (version scoping)

**Interfaces:**
- Consumes: `DiagnosticRecord` (Task 2).
- Produces: `DiagnosticRecorder` with `void Note(DiagnosticKey key)`, `void Sample(DiagnosticSample sample)`, `DiagnosticRecord Current { get; }`, `void Clear()`, `bool Enabled { get; set; }`, `void Flush()`; `DiagnosticStore` with `DiagnosticRecord Load(string version)` and `void Save(DiagnosticRecord record)`.

- [ ] **Step 1: Add the path**

In `src/SmartZoom.App/AppPaths.cs`, beside `SettingsFile`:

```csharp
    /// <summary>The diagnostics record, machine-local like the logs because it describes this machine.</summary>
    public string DiagnosticsFile => Path.Combine(Path.GetDirectoryName(LogDirectory)!, "diagnostics.json");
```

- [ ] **Step 2: Write the store**

```csharp
using System.Text.Json;

using Microsoft.Extensions.Logging;

using SmartZoom.Core.Diagnostics;

namespace SmartZoom.App.Diagnostics;

/// <summary>Reads and writes the diagnostics record, and never lets a failure reach the caller.</summary>
/// <remarks>
/// Every failure here is swallowed: diagnostics exists to explain a problem, and a diagnostics subsystem that
/// creates one has failed at its only job. A record from another version is discarded rather than merged,
/// because a count that spans a fix cannot answer "is this build broken for you".
/// </remarks>
internal sealed partial class DiagnosticStore(AppPaths paths, ILogger<DiagnosticStore> logger)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>The maximum size of the file on disk.</summary>
    public const int MaxBytes = 256 * 1024;

    /// <summary>Loads the record for this version, or an empty one.</summary>
    /// <param name="version">The running SmartZoom version.</param>
    /// <returns>A record; never null.</returns>
    public DiagnosticRecord Load(string version)
    {
        try
        {
            if (!File.Exists(paths.DiagnosticsFile))
                return new DiagnosticRecord(version);

            var stored = JsonSerializer.Deserialize<DiagnosticFile>(File.ReadAllText(paths.DiagnosticsFile), Json);
            if (stored is null || stored.Version != version)
                return new DiagnosticRecord(version);

            var record = new DiagnosticRecord(version);
            stored.Restore(record);
            return record;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            LogUnreadable(ex);
            return new DiagnosticRecord(version);
        }
    }

    /// <summary>Writes the record, dropping detail before counters if it is too large.</summary>
    /// <param name="record">The record.</param>
    public void Save(DiagnosticRecord record)
    {
        try
        {
            var json = JsonSerializer.Serialize(DiagnosticFile.From(record), Json);
            if (json.Length > MaxBytes)
                json = JsonSerializer.Serialize(DiagnosticFile.From(record, withSamples: false), Json);

            Directory.CreateDirectory(Path.GetDirectoryName(paths.DiagnosticsFile)!);
            File.WriteAllText(paths.DiagnosticsFile, json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogUnwritable(ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Diagnostics record could not be read; starting a new one.")]
    private partial void LogUnreadable(Exception ex);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Diagnostics record could not be written.")]
    private partial void LogUnwritable(Exception ex);
}
```

In the same file, below the store, the on-disk shape. It is deliberately separate from `DiagnosticRecord`
and deliberately not public: the file format is not a contract anybody outside this class may depend on.

```csharp
/// <summary>The on-disk shape. Separate from the record so the file format can change freely.</summary>
internal sealed class DiagnosticFile
{
    public string Version { get; set; } = string.Empty;

    public int OmittedKeys { get; set; }

    public List<StoredCounter> Counters { get; set; } = [];

    public List<DiagnosticSample> Samples { get; set; } = [];

    public static DiagnosticFile From(DiagnosticRecord record, bool withSamples = true) => new()
    {
        Version = record.Version,
        OmittedKeys = record.OmittedKeys,
        Counters = [.. record.Counters.Select(c => new StoredCounter
        {
            Key = c.Key,
            Count = c.Count,
            FirstSeen = c.FirstSeen,
            LastSeen = c.LastSeen,
        })],
        Samples = withSamples ? [.. record.Samples] : [],
    };

    public void Restore(DiagnosticRecord record)
    {
        foreach (var stored in Counters)
        {
            // Replayed rather than assigned, so the record's own caps apply to a file that was edited by hand.
            record.Note(stored.Key, stored.FirstSeen);
            for (var i = 1; i < stored.Count; i++)
                record.Note(stored.Key, stored.LastSeen);
        }

        foreach (var sample in Samples)
            record.Sample(sample);
    }

    internal sealed class StoredCounter
    {
        public DiagnosticKey Key { get; set; } = new(DiagnosticKind.ZoomedNothing, null, null, null);

        public int Count { get; set; }

        public DateTimeOffset FirstSeen { get; set; }

        public DateTimeOffset LastSeen { get; set; }
    }
}
```

- [ ] **Step 3: Write the recorder**

```csharp
using SmartZoom.Core.Diagnostics;

namespace SmartZoom.App.Diagnostics;

/// <summary>
/// The one place anything is recorded. Thread-safe, allocation-light, and never touches disk on the zoom path.
/// </summary>
internal sealed class DiagnosticRecorder(DiagnosticStore store, TimeProvider time, string version)
    : IGesturePacingSink
{
    private readonly Lock _gate = new();
    private DiagnosticRecord _record = store.Load(version);
    private bool _dirty;

    /// <summary>Whether anything is recorded at all.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>The record, for rendering. Never mutated by the caller.</summary>
    public DiagnosticRecord Current
    {
        get { lock (_gate) { return _record; } }
    }

    /// <summary>Counts one occurrence.</summary>
    /// <param name="key">What happened.</param>
    public void Note(DiagnosticKey key)
    {
        if (!Enabled)
            return;

        lock (_gate)
        {
            _record.Note(key, time.GetUtcNow());
            _dirty = true;
        }
    }

    /// <summary>Keeps one worked example.</summary>
    /// <param name="sample">The example.</param>
    public void Sample(DiagnosticSample sample)
    {
        if (!Enabled)
            return;

        lock (_gate)
        {
            _record.Sample(sample);
            _dirty = true;
        }
    }

    /// <summary>Forgets everything recorded so far.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _record = new DiagnosticRecord(version);
            _dirty = true;
        }
    }

    /// <inheritdoc />
    public void Paced(int frames, int intervalMs, int lateFrames, double worstLateMs)
    {
        if (!Enabled)
            return;

        lock (_gate)
        {
            _record.Gestures.Add(frames, intervalMs, lateFrames, worstLateMs);
            _dirty = true;
        }
    }

    /// <summary>Writes the record if anything has changed.</summary>
    public void Flush()
    {
        DiagnosticRecord snapshot;
        lock (_gate)
        {
            if (!_dirty)
                return;

            snapshot = _record;
            _dirty = false;
        }

        store.Save(snapshot);
    }
}
```

- [ ] **Step 4: Add the version-scoping test**

In `DiagnosticRecordTests.cs`:

```csharp
    public sealed class A_record_from_another_version
    {
        [Fact]
        public void Carries_its_version_so_a_loader_can_reject_it()
        {
            Assert.Equal("0.2.0", new DiagnosticRecord("0.2.0").Version);
        }
    }
```

- [ ] **Step 5: Run the tests and build**

```powershell
dotnet test
dotnet build
```

Expected: PASS, warning-free.

- [ ] **Step 6: Commit**

```powershell
git add src/SmartZoom.App/Diagnostics src/SmartZoom.App/AppPaths.cs tests/SmartZoom.Core.Tests/Diagnostics
git commit -m "Persist the diagnostics record, capped and version-scoped"
```

---

### Task 7: Gesture health

The spec's first question is whether the measured constants are wrong on other hardware, and the gesture
pacing numbers are the evidence for it. They are already computed and logged; this keeps a running total so a
report can carry them.

**Files:**
- Create: `src/SmartZoom.Core/Diagnostics/GestureHealth.cs`, `src/SmartZoom.Core/Diagnostics/IGesturePacingSink.cs`
- Modify: `src/SmartZoom.Core/Diagnostics/DiagnosticRecord.cs`, `src/SmartZoom.Core/Diagnostics/DiagnosticReport.cs`, `src/SmartZoom.Interop/Input/TouchPinchInjector.cs`
- Test: `tests/SmartZoom.Core.Tests/Diagnostics/GestureHealthTests.cs`

**Interfaces:**
- Consumes: `DiagnosticRecord` (Task 2), `DiagnosticReport` (Task 4).
- Produces: `IGesturePacingSink` with `void Paced(int frames, int intervalMs, int lateFrames, double worstLateMs)`;
  `GestureHealth` with `Add(...)`, `int Gestures`, `int Frames`, `int LateFrames`, `double WorstLateMs`,
  `int IntervalMs`; `DiagnosticRecord.Gestures` of type `GestureHealth`.

- [ ] **Step 1: Write the failing test**

`tests/SmartZoom.Core.Tests/Diagnostics/GestureHealthTests.cs`:

```csharp
using SmartZoom.Core.Diagnostics;

namespace SmartZoom.Core.Tests.Diagnostics;

public class GestureHealthTests
{
    public sealed class Several_gestures
    {
        [Fact]
        public void Add_up_and_keep_the_worst_lateness()
        {
            var health = new GestureHealth();

            health.Add(frames: 18, intervalMs: 17, lateFrames: 0, worstLateMs: 12.4);
            health.Add(frames: 18, intervalMs: 17, lateFrames: 3, worstLateMs: 21.9);
            health.Add(frames: 18, intervalMs: 17, lateFrames: 1, worstLateMs: 9.0);

            Assert.Equal(3, health.Gestures);
            Assert.Equal(54, health.Frames);
            Assert.Equal(4, health.LateFrames);
            Assert.Equal(21.9, health.WorstLateMs);
            Assert.Equal(17, health.IntervalMs);
        }

        [Fact]
        public void Are_absent_from_the_report_until_one_has_happened()
        {
            var record = new DiagnosticRecord("0.1.0");

            var report = DiagnosticReport.Render(record, new FakeMachineFacts(), "{}", null, t => t);

            Assert.DoesNotContain("Gesture health", report, StringComparison.Ordinal);
        }

        [Fact]
        public void Appear_in_the_report_once_one_has()
        {
            var record = new DiagnosticRecord("0.1.0");
            record.Gestures.Add(frames: 18, intervalMs: 17, lateFrames: 3, worstLateMs: 21.9);

            var report = DiagnosticReport.Render(record, new FakeMachineFacts(), "{}", null, t => t);

            Assert.Contains("Gesture health", report, StringComparison.Ordinal);
            Assert.Contains("17 ms", report, StringComparison.Ordinal);
        }
    }
}
```

- [ ] **Step 2: Run it and watch it fail**

```powershell
dotnet test --filter "GestureHealthTests"
```

Expected: compile error — `GestureHealth` does not exist and `DiagnosticRecord` has no `Gestures`.

- [ ] **Step 3: Write the aggregate**

`src/SmartZoom.Core/Diagnostics/GestureHealth.cs`:

```csharp
namespace SmartZoom.Core.Diagnostics;

/// <summary>
/// Running totals for how well injected gestures were delivered.
/// </summary>
/// <remarks>
/// Totals rather than one row per gesture: the question is whether this machine can deliver frames on time
/// at all, which a hundred rows answer no better than four numbers. The frame interval follows the display's
/// refresh rate, so it belongs beside the display section rather than as a constant in the source.
/// </remarks>
public sealed class GestureHealth
{
    /// <summary>How many gestures have been delivered.</summary>
    public int Gestures { get; private set; }

    /// <summary>How many frames those gestures asked for in total.</summary>
    public int Frames { get; private set; }

    /// <summary>How many of those frames missed their slot.</summary>
    public int LateFrames { get; private set; }

    /// <summary>The worst lateness seen, in milliseconds.</summary>
    public double WorstLateMs { get; private set; }

    /// <summary>The frame interval most recently used, in milliseconds.</summary>
    public int IntervalMs { get; private set; }

    /// <summary>Records one delivered gesture.</summary>
    /// <param name="frames">Frames the gesture asked for.</param>
    /// <param name="intervalMs">The interval between them.</param>
    /// <param name="lateFrames">How many missed their slot.</param>
    /// <param name="worstLateMs">The worst lateness in this gesture.</param>
    public void Add(int frames, int intervalMs, int lateFrames, double worstLateMs)
    {
        Gestures++;
        Frames += frames;
        LateFrames += lateFrames;
        IntervalMs = intervalMs;
        WorstLateMs = Math.Max(WorstLateMs, worstLateMs);
    }
}
```

`src/SmartZoom.Core/Diagnostics/IGesturePacingSink.cs`:

```csharp
namespace SmartZoom.Core.Diagnostics;

/// <summary>Told how each injected gesture was actually delivered.</summary>
/// <remarks>
/// Declared in Core so the injector, which lives in Interop, can report without knowing what records it.
/// </remarks>
public interface IGesturePacingSink
{
    /// <summary>Reports one delivered gesture.</summary>
    /// <param name="frames">Frames the gesture asked for.</param>
    /// <param name="intervalMs">The interval between them.</param>
    /// <param name="lateFrames">How many missed their slot.</param>
    /// <param name="worstLateMs">The worst lateness in this gesture.</param>
    void Paced(int frames, int intervalMs, int lateFrames, double worstLateMs);
}
```

- [ ] **Step 4: Hang it off the record and render it**

In `DiagnosticRecord.cs`:

```csharp
    /// <summary>How well injected gestures have been delivered on this machine.</summary>
    public GestureHealth Gestures { get; } = new();
```

In `DiagnosticReport.Render`, after the "Recent details" section:

```csharp
        if (record.Gestures.Gestures > 0)
        {
            Section(text, "Gesture health", () =>
            {
                var g = record.Gestures;
                text.AppendLine(CultureInfo.InvariantCulture,
                    $"- {g.Gestures} gestures, {g.Frames} frames at {g.IntervalMs} ms");
                text.AppendLine(CultureInfo.InvariantCulture,
                    $"- {g.LateFrames} frames missed their slot, worst {g.WorstLateMs:F1} ms late");
            });
        }
```

- [ ] **Step 5: Report from the injector**

In `TouchPinchInjector`, add an optional sink to the primary constructor (`IGesturePacingSink? pacing = null`) and call it immediately after the existing `LogPacing(...)` line:

```csharp
            pacing?.Paced(frames, interval, lateFrames, worstLate);
```

Optional because the probe tool constructs the injector directly and has nothing to record into.

- [ ] **Step 6: Run the tests**

```powershell
dotnet test --filter "GestureHealthTests"
dotnet build
```

Expected: PASS, warning-free.

- [ ] **Step 7: Commit**

```powershell
git add src/SmartZoom.Core/Diagnostics src/SmartZoom.Interop/Input/TouchPinchInjector.cs tests/SmartZoom.Core.Tests/Diagnostics
git commit -m "Record how well injected gestures are delivered"
```

---

### Task 8: Recording what happens

**Files:**
- Modify: `src/SmartZoom.App/Hosting/ZoomActivity.cs`, `src/SmartZoom.App/Hosting/TriggerDispatcher.cs`, `src/SmartZoom.App/Program.cs`
- Create: `src/SmartZoom.App/Diagnostics/DiagnosticFlushService.cs`

**Interfaces:**
- Consumes: `DiagnosticRecorder` (Task 6), `ZoomOutcome.Reason` (Task 1).
- Produces: nothing new; wiring only.

- [ ] **Step 1: Record outcomes that changed nothing**

In `TriggerDispatcher`, after `activity.Report(outcome)`:

```csharp
            if (outcome.Action is ZoomAction.Handled or ZoomAction.Unhandled or ZoomAction.Ignored)
            {
                recorder.Note(new DiagnosticKey(
                    DiagnosticKind.ZoomedNothing,
                    outcome.Process,
                    outcome.Adapter?.ToString(),
                    outcome.Reason?.ToString() ?? outcome.Action.ToString()));
            }
```

Add `DiagnosticRecorder recorder` to the primary constructor.

- [ ] **Step 2: Record adapter exceptions**

In the existing `catch` that calls `LogZoomFailed`:

```csharp
            var key = new DiagnosticKey(DiagnosticKind.AdapterThrew, target.ProcessName, null, ex.GetType().Name);
            recorder.Note(key);
            recorder.Sample(new DiagnosticSample(
                key,
                time.GetUtcNow(),
                Detail: null,
                Exception: Redaction.Truncate($"{ex.GetType().FullName}: {ex.Message}{Environment.NewLine}{ex.StackTrace}", 4000)));
```

- [ ] **Step 3: Record a trigger that hit no window**

In the `target is null` branch, beside `LogNoWindow`:

```csharp
            recorder.Note(new DiagnosticKey(DiagnosticKind.NoWindow, null, null, null));
```

- [ ] **Step 4: Write the flush service**

```csharp
using Microsoft.Extensions.Hosting;

namespace SmartZoom.App.Diagnostics;

/// <summary>Writes the diagnostics record periodically, and once on the way out.</summary>
internal sealed class DiagnosticFlushService(DiagnosticRecorder recorder, TimeProvider time) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, time);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
                recorder.Flush();
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
        finally
        {
            recorder.Flush();
        }
    }
}
```

- [ ] **Step 5: Record a crash, synchronously**

In `Program.Main`, before the host is built:

```csharp
        // Written in the handler rather than on the flush timer: a crash record that dies with the crash is
        // exactly the failure this exists to prevent.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
                Crash.Record(recorder, ex);
        };

        Application.ThreadException += (_, e) => Crash.Record(recorder, e.Exception);
```

Create `src/SmartZoom.App/Diagnostics/Crash.cs`:

```csharp
using SmartZoom.Core.Diagnostics;

namespace SmartZoom.App.Diagnostics;

/// <summary>Writes a crash to the record before the process goes.</summary>
/// <remarks>
/// Flushed here and now rather than left to the timer: the timer will not run again. Every failure is
/// swallowed, because throwing out of a crash handler replaces a diagnosable crash with an undiagnosable one.
/// </remarks>
internal static class Crash
{
    public static void Record(DiagnosticRecorder recorder, Exception ex)
    {
        try
        {
            var key = new DiagnosticKey(DiagnosticKind.Crashed, null, null, ex.GetType().Name);
            recorder.Note(key);
            recorder.Sample(new DiagnosticSample(
                key,
                DateTimeOffset.UtcNow,
                Detail: null,
                Exception: Redaction.Truncate(
                    $"{ex.GetType().FullName}: {ex.Message}{Environment.NewLine}{ex.StackTrace}", 4000)));
            recorder.Flush();
        }
        catch
        {
            // There is nowhere left to report a failure to report a failure.
        }
    }
}
```

- [ ] **Step 6: Register everything**

In `Program.cs`, beside the other singletons:

```csharp
        builder.Services.AddSingleton<IMachineFacts, MachineFacts>();
        builder.Services.AddSingleton<DiagnosticStore>();
        builder.Services.AddSingleton(sp => new DiagnosticRecorder(
            sp.GetRequiredService<DiagnosticStore>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<IMachineFacts>().AppVersion));
        builder.Services.AddSingleton<IGesturePacingSink>(sp => sp.GetRequiredService<DiagnosticRecorder>());
        builder.Services.AddHostedService<DiagnosticFlushService>();
```

- [ ] **Step 7: Build, test and check it by hand**

```powershell
Get-Process SmartZoom -ErrorAction SilentlyContinue | Stop-Process -Force
dotnet build
dotnet test
dotnet run --project src/SmartZoom.App
```

Point at an empty page margin in a browser and press the trigger three times — those presses find no block. Wait 30 seconds, then:

```powershell
Get-Content "$env:LOCALAPPDATA\SmartZoom\diagnostics.json"
```

Expected: one counter with `Count: 3`, the browser's process name, `Browser`, and `NoBlock`. No window title, no URL, no coordinates anywhere in the file.

- [ ] **Step 8: Commit**

```powershell
git add src/SmartZoom.App
git commit -m "Record presses that zoomed nothing, adapter exceptions and crashes"
```

---

### Task 9: The Diagnostics page

**Files:**
- Create: `src/SmartZoom.App/Settings/DiagnosticsPage.cs`
- Modify: `src/SmartZoom.App/Settings/SettingsForm.cs:60-63`, `src/SmartZoom.App/Tray/TrayApplicationContext.cs`
- Test: manual

**Interfaces:**
- Consumes: `DiagnosticRecorder`, `IMachineFacts`, `DiagnosticReport`, `AppPaths`.
- Produces: `DiagnosticsPage : UserControl`, constructed as `new DiagnosticsPage(recorder, facts, paths)`.

- [ ] **Step 1: Write the page**

Create `src/SmartZoom.App/Settings/DiagnosticsPage.cs`, following the existing pages: built in code, no
designer file, `AutoScaleMode.Dpi`, a `TableLayoutPanel` for layout.

```csharp
internal sealed class DiagnosticsPage : UserControl
{
    private readonly DiagnosticRecorder _recorder;
    private readonly IMachineFacts _facts;
    private readonly AppPaths _paths;

    private readonly TextBox _report = new()
    {
        Multiline = true,
        ReadOnly = true,
        Dock = DockStyle.Fill,
        ScrollBars = ScrollBars.Both,
        WordWrap = false,
        Font = new Font("Consolas", 9f),
    };

    private readonly CheckBox _enabled = new() { Text = "&Record what doesn't work", AutoSize = true };
    private readonly CheckBox _includeLog = new() { Text = "Include recent &log lines", AutoSize = true };
    private readonly Button _copy = new() { Text = "&Copy", AutoSize = true };
    private readonly Button _save = new() { Text = "&Save…", AutoSize = true };
    private readonly Button _clear = new() { Text = "Clear recorded &data", AutoSize = true };

    public DiagnosticsPage(DiagnosticRecorder recorder, IMachineFacts facts, AppPaths paths)
    {
        _recorder = recorder;
        _facts = facts;
        _paths = paths;

        AutoScaleMode = AutoScaleMode.Dpi;
        Dock = DockStyle.Fill;
        Padding = new Padding(12);

        _enabled.Checked = recorder.Enabled;
        _enabled.CheckedChanged += (_, _) => recorder.Enabled = _enabled.Checked;
        _includeLog.CheckedChanged += (_, _) => Render();
        _copy.Click += (_, _) => Clipboard.SetText(_report.Text);
        _save.Click += (_, _) => Save();
        _clear.Click += (_, _) => { recorder.Clear(); Render(); };

        Controls.Add(BuildLayout());
        Render();
    }

    private TableLayoutPanel BuildLayout()
    {
        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
        buttons.Controls.AddRange([_copy, _save, _clear, _enabled, _includeLog]);
        foreach (Control control in buttons.Controls)
            control.Margin = new Padding(0, 6, 8, 0);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label
        {
            Text = "This is everything SmartZoom would tell you about itself. Read it, then copy it into a "
                + "bug report. Nothing here is sent anywhere.",
            Dock = DockStyle.Fill,
            AutoSize = false,
            Height = 36,
        });
        layout.Controls.Add(_report);
        layout.Controls.Add(buttons);
        return layout;
    }

    private void Save()
    {
        using var dialog = new SaveFileDialog { FileName = "smartzoom-diagnostics.md", Filter = "Markdown|*.md" };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            File.WriteAllText(dialog.FileName, _report.Text);
    }
}
```

The report itself is built in `Render`:

```csharp
    private void Render()
    {
        var settings = File.Exists(_paths.SettingsFile) ? File.ReadAllText(_paths.SettingsFile) : "{}";
        var tail = _includeLog.Checked ? LogTail() : null;
        _report.Text = DiagnosticReport.Render(_recorder.Current, _facts, settings, tail, Redact);
    }

    private static string Redact(string text) => Redaction.Paths(
        text,
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
```

`LogTail()` reads the last 200 lines of the newest file in `_paths.LogDirectory`, inside a `try` that returns null on failure.

`Copy` calls `Clipboard.SetText(_report.Text)`. `Save…` uses a `SaveFileDialog` defaulting to `smartzoom-diagnostics.md`. `Clear` calls `_recorder.Clear()` then `Render()`.

- [ ] **Step 2: Add the tab**

In `SettingsForm.cs` after the Zoom tab:

```csharp
        tabs.TabPages.Add(Page("Diagnostics", new DiagnosticsPage(recorder, facts, paths)));
```

Take `DiagnosticRecorder` and `IMachineFacts` as constructor parameters of `SettingsForm` and pass them through from wherever it is constructed.

- [ ] **Step 3: Add the tray item**

In `TrayApplicationContext`, beside the existing items:

```csharp
        // Opens the page rather than copying silently: a tray item that filled the clipboard unread would
        // defeat the point of showing the report at all.
        _menu.Items.Add(new ToolStripMenuItem("&Diagnostic report…", null, (_, _) => ShowSettings(SettingsTab.Diagnostics)));
```

Give `ShowSettings` an optional tab argument and select that tab when the form opens.

- [ ] **Step 4: Check it by hand**

```powershell
Get-Process SmartZoom -ErrorAction SilentlyContinue | Stop-Process -Force
dotnet build
dotnet run --project src/SmartZoom.App
```

- Tray → **Diagnostic report…** opens the settings window on the Diagnostics tab.
- The report shows the display's real resolution, refresh rate and scale.
- Read it end to end: no window titles, no URLs, no `C:\Users\<you>`.
- **Copy**, paste into a text editor: identical to what was on screen.
- Tick **Include recent log lines**: the log section appears, with paths redacted.
- **Clear recorded data**: the table empties and `diagnostics.json` empties within 30 seconds.
- Untick **Record what doesn't work**, press the trigger on an empty margin, refresh: no new rows.

- [ ] **Step 5: Commit**

```powershell
git add src/SmartZoom.App/Settings src/SmartZoom.App/Tray
git commit -m "Add the Diagnostics page and the tray entry to it"
```

---

### Task 10: Documentation

**Files:**
- Modify: `SECURITY.md`, `CONTRIBUTING.md`, `README.md`, `CHANGELOG.md`

- [ ] **Step 1: Extend the SECURITY.md privacy section**

Keep the existing sentence and add, immediately after it:

```markdown
SmartZoom also keeps a local record of what it failed to do — presses that zoomed nothing, adapters that
threw, and crashes — at `%LOCALAPPDATA%\SmartZoom\diagnostics.json`. It holds process names, adapter names,
the *shape* of the accessibility path under the cursor (roles and sizes, never text), display characteristics
and exception stacks. It never holds window titles, page text, URLs, screen coordinates, keystrokes, your
username or any identifier. It is bounded, reset whenever SmartZoom is updated, and can be turned off and
cleared in Settings → Diagnostics.

Nothing in it is sent anywhere. The Diagnostics page renders it as a report you read on screen and copy
yourself; that is the only way any of it leaves the machine.
```

- [ ] **Step 2: Point bug reports at the report**

In `CONTRIBUTING.md`, under "Reporting a bug", replace the request for log lines with a request for the diagnostic report (Settings → Diagnostics → Copy), noting the log-lines checkbox for anyone already debugging.

- [ ] **Step 3: README and CHANGELOG**

README: one line under the tray menu list for **Diagnostic report…**, and one under configuration for the switch. CHANGELOG: an entry under **Added** describing the record and the report, and stating that no network connection is made.

- [ ] **Step 4: Final verification**

```powershell
Get-Process SmartZoom -ErrorAction SilentlyContinue | Stop-Process -Force
dotnet build
dotnet test
dotnet format --verify-no-changes
```

Expected: warning-free build, all tests pass, no formatting changes.

Confirm the global constraint directly:

```powershell
Select-String -Path "src\**\*.cs" -Pattern "HttpClient|WebClient|Socket|WebRequest|UploadString" |
  Where-Object { $_.Path -notlike "*\obj\*" }
```

Expected: no matches. If there are any, the design has been violated.

- [ ] **Step 5: Commit**

```powershell
git add SECURITY.md CONTRIBUTING.md README.md CHANGELOG.md
git commit -m "Document the local diagnostics record and the report"
```
