# Diagnostics: a report worth pasting into an issue

SmartZoom fails quietly. A press that finds nothing to zoom does nothing at all, which is indistinguishable
from a press that never arrived, which is indistinguishable from an application SmartZoom does not support.
Three separate defects with one symptom, and none of them produce anything a user can hand over.

This design adds a local record of what did not work, and a report that turns it into something a bug
report can carry.

## What it is for

Three questions, each of which has cost real debugging time:

1. **Are the measured constants wrong on hardware that is not the author's?** Every number in
   [measurements.md](measurements.md) was taken on one machine at 200 % scaling. The gesture frame interval
   was 8 ms against a 59 Hz display for months, so a third of the injected frames went out late, and nothing
   in a bug report would have revealed it.
2. **Where does a press do nothing?** Pages built out of `div`s could not be zoomed anywhere, silently, for
   weeks, because Chromium reports a generic container as `ROLE_SYSTEM_PANE` and that role was unmapped. A
   count of no-op presses by application would have put it at the top of a list the first time anyone looked.
3. **What is throwing?** Adapter exceptions are caught so that one failed zoom cannot take the dispatcher
   down with it. That is correct, and it means they are invisible.

## What it is not

**No network code exists anywhere in SmartZoom, and none is added here.** No telemetry, no endpoint, no
upload, no background reporting. `SECURITY.md`'s statement — *"SmartZoom makes no network connections and
sends nothing anywhere"* — remains literally true after this change, and that is a requirement of the design
rather than a happy accident.

**No identifier of any kind.** No install ID, no session ID, no hardware or user-derived value. Nothing
written here can be correlated across machines or across restarts of the same machine.

The report leaves the machine only when a person reads it and pastes it somewhere. That is the entire
consent mechanism, and it is why the report is human-readable markdown rather than an encoded blob: consent
to something unreadable is not consent.

## The privacy contract

SmartZoom sees everything on the screen it is pointing at. The value of this record depends entirely on the
gap between what it *can* see and what it *keeps* being obvious and enforced.

**Recorded:**

| Field | Why it is needed |
|---|---|
| Process name (`msedge`, `WINWORD`) | The answer to "which application should I support next" |
| Adapter id (`Browser`, `Reader`) | Which strategy was chosen, and whether routing is wrong |
| Reason (`NoBlock`, `NoContent`, …) | Which of the three failure classes this is |
| Accessibility path *shape* | Roles and rectangle **sizes** — `Group 949x79 < Other(16) 1874x500` |
| Exception type, message, stack | What threw |
| Display scale, resolution, refresh rate | Question 1 above |
| SmartZoom version, Windows build | Which build, which OS |

**Never recorded, under any circumstance:**

- **Window titles.** They carry document names, email subjects and URLs. The zoom pipeline reads them; this
  record must not.
- **Page text or URLs.** The path shape says `Group 949x79`, never the words inside it. This is the rule that
  makes the `PANE` class of defect diagnosable without recording what was being read.
- **Screen coordinates.** The existing debug log includes them; the record keeps sizes only. Absolute
  positions describe a monitor layout and add nothing to any of the three questions.
- **Keystrokes.** Unchanged from the existing hook rule: modifiers and configured trigger keys only, and
  none of that reaches diagnostics.
- **Username, machine name, absolute paths.** Anything rendered into the report has the user profile path
  rewritten back to `%APPDATA%` / `%LOCALAPPDATA%`. This applies to exception messages too — an `IOException`
  carries the path that failed.
- **Captured pixels.** Nothing the screen-capture path reads is retained.

Process names are the one genuinely identifying field: "this person runs an internal tool called X" is a real
disclosure. It is kept because it is the single most useful field in the record, and it is defensible only
because nothing is sent automatically. If that ever changes, this decision has to be revisited first.

## The data model

A **tally, not a journal**. A journal of every event grows without limit and says less.

**Counters**, keyed by `(Kind, Process, Adapter, Reason)`:

- `Count`, `FirstSeen`, `LastSeen`.
- Cap: **200 distinct keys.** Beyond the cap, new keys are not added and an `OmittedKeys` counter is
  incremented. Eviction is deliberately not used — the long tail is the interesting part, and silently
  dropping it would make the counts lie.

**Kinds**, each corresponding to a point where SmartZoom already knows it has failed:

| Kind | Raised when |
|---|---|
| `ZoomedNothing` | A press resolved to a window and produced no zoom. `Reason` distinguishes `NoContent`, `NoBlock`, `AlreadyFits`, `GestureRefused`, `AutomationFailed`, `NoAdapter` and `AdapterCouldNotAct` (the strategy could not act and the Ctrl+wheel fallback did not either). It is always set for a press that zoomed nothing. `NoAdapter` and `AlreadyFits` are correct behaviour rather than defects, and are counted because "people keep pressing in an application that is routed to nothing" is exactly question 2 |
| `AdapterThrew` | An adapter raised an exception the dispatcher caught |
| `NoWindow` | A trigger resolved to no window at all |
| `Crashed` | An unhandled exception reached the top of the process |

**Detail ring**: the most recent **20** samples, each carrying the path shape (truncated to 12 nodes) and, for
exceptions, type, redacted message and a stack trace truncated to 4 000 characters. Counts establish
frequency; one concrete example is what actually gets diagnosed.

**Gesture health**: a small aggregate of the pacing already logged — frames requested, frames that missed
their slot, worst lateness — kept as running totals rather than per-gesture rows.

## Storage and lifecycle

**Location:** `%LOCALAPPDATA%\SmartZoom\diagnostics.json`, beside `logs\`. `AppPaths` already owns that root.

**Version scoped.** The file records the SmartZoom version that produced it. On load, a different version
means the counters are discarded and started fresh. "43 no-op presses in `msedge`" is misleading if 40 of
them predate the fix; the question is always *is this build broken for you*.

**Written:** held in memory, flushed on a 30-second timer when dirty and once on clean shutdown. A crash is
the exception — an unhandled exception is written **synchronously in the handler**, because a crash record
that dies with the crash is precisely the failure this is meant to eliminate.

**Bounded:** 200 keys, 20 samples, and a hard 256 KB ceiling on the file. A record that exceeds the ceiling
is truncated from the detail ring first, counters last.

**On by default**, with a switch to turn it off and a button to clear it. Justified because it holds strictly
less than the debug log that is already written, and never leaves the machine.

## Staying out of the way

Diagnostics must never change whether or how a zoom happens.

- Recording is an in-memory append under a light lock. No I/O on the zoom path.
- Nothing inside the hook callback. The existing rule — allocation-free, no logging, no blocking — stands
  unchanged, and diagnostics has no presence there.
- Every I/O failure (disk full, file locked, unwritable directory) is swallowed and disables diagnostics for
  the remainder of the session rather than propagating. A corrupt or unparseable file on load is discarded and
  started fresh. This is the posture `SettingsStore` already takes.
- If a report section throws while rendering — display enumeration is the likely candidate — that section
  renders as `unavailable` and the rest of the report is still produced.

## The report

Built on demand, when the page is opened or the report is refreshed — never held in memory between
viewings, and never written to disk unless the user chooses **Save…**. Markdown, built to be pasted into a
GitHub issue:

1. **Header** — SmartZoom version, Windows build, architecture.
2. **Displays** — per monitor: resolution, scale factor, refresh rate. The section that exists because of
   question 1.
3. **Settings** — triggers, routing, zoom tuning, with paths rewritten.
4. **What didn't work** — the counter table, most frequent first.
5. **Recent details** — the ring: path shapes and redacted exception stacks.
6. **Gesture health** — the pacing aggregate.

One opt-in extra: an **"include recent log lines"** checkbox, off by default. The log is the least controlled
content in the system, so it goes in only when somebody is actively debugging and chooses it.

## Where the code lives

Following the existing split, and adding no new plumbing through the zoom pipeline:

- **`SmartZoom.Core/Diagnostics/`** — the tally, the caps, the kinds and reasons, redaction, version scoping,
  and the markdown renderer. Pure, no Win32, fully unit-testable.
- **`SmartZoom.Core`** — a `IMachineFacts` interface for display and OS information, so the renderer can be
  tested against a fake.
- **`SmartZoom.Interop`** — the `IMachineFacts` implementation: monitor enumeration, `EnumDisplaySettings`
  (already declared in `NativeMethods.txt`) and per-monitor DPI.
- **`SmartZoom.App`** — the JSON store, the flush service, the crash handler, and the UI.

**Hook points**, both of which already exist and already see what is needed:

- `ZoomActivity` is where outcomes are reported, and already receives `ZoomOutcome` (action, process,
  adapter). `ZoomedNothing` is derived there.
- `TriggerDispatcher` already catches adapter exceptions so a failed zoom cannot end the dispatcher.
  `AdapterThrew` is raised from that catch.

## The user interface

A fourth page in the settings window beside Triggers, Applications and Zoom, containing:

- The report itself in a read-only box. **Showing it is the consent mechanism** — "the user reads it before
  pasting" is only true if it is put in front of them.
- **Copy**, **Save…**, **Clear recorded data**, and the on/off switch.
- The "include recent log lines" checkbox.

The tray menu gains one **Diagnostic report…** item, because the tray is where people go when something is
wrong. It *opens this page* rather than copying to the clipboard directly: a tray item that silently filled
the clipboard would defeat the principle in the paragraph above it, which is that nothing is handed over
before it has been read.

## Testing

Everything that matters is pure and deterministic, tested with a fake `IMachineFacts` and a fixed
`TimeProvider`:

- Counter keying, the 200-key cap and its `OmittedKeys` behaviour, ring eviction at 20, truncation of path
  shapes and stack traces, the 256 KB ceiling.
- Version scoping: a file from another version is discarded; a file from this one is resumed.
- Redaction: profile paths in settings **and in exception messages** are rewritten.
- Markdown rendering, including that a failing section degrades to `unavailable` without failing the report.

**The privacy contract gets its own tests, at the two places it can actually break.**

An earlier version of this section showed a renderer test asserting a report never contained a literal window
title — `A_report_never_contains_a_window_title`, fed a tally seeded with one. It was removed during
implementation: `DiagnosticKey` and `DiagnosticSample` have no title field, no URL field and no coordinate
field by construction, so that assertion could not fail against any implementation that compiled. A comment
promising "we never record titles" is worth little to somebody reviewing this from outside, but neither is a
test that passes for the same reason the comment would be believed.

What replaced it, in `tests/SmartZoom.Core.Tests/Diagnostics/DiagnosticReportTests.cs`:

- `Every_free_text_field_is_redacted_before_it_is_rendered` seeds a distinct sentinel into the settings JSON,
  a sample's `Detail`, its `Exception`, and the log tail, and asserts none of the four survives rendering.
  This is the renderer's actual job — applying the caller-supplied `Redactor` at every call site — and it
  fails the moment any one of those four is bypassed.
- `A_report_contains_no_absolute_user_path` asserts against a username the input genuinely contains, so it
  fails if redaction is ever skipped or the pattern narrowed.
- `A_section_that_throws.Renders_unavailable_without_losing_the_rest_of_the_report` exercises the type's own
  documented promise — one failing section degrades to `unavailable` rather than costing the reader the rest
  of the report — via a `FakeMachineFacts` that can be told to throw.

The guarantee that no *producer* ever hands the renderer a title, a URL, or a coordinate in the first place
cannot be tested against `DiagnosticKey`/`DiagnosticSample` directly, for the same reason the original test
could not fail: there is nowhere on those types to put one. It is tested instead in
`tests/SmartZoom.App.Tests/Diagnostics/DiagnosticSampleFactoryTests.cs`, where a real `BrowserAdapter` reads a
real accessibility path carrying absolute screen coordinates, through a real `ZoomCoordinator`, into
`DiagnosticSampleFactory` — the code that actually builds what gets recorded — and asserts the resulting
sample's path-shape string is `"Group 40x30 < Document 1920x1080"`, not a string containing the coordinates
the input genuinely had. That test lives in `SmartZoom.App.Tests` rather than `SmartZoom.Core.Tests` because
the guarantee it proves belongs to the App-level wiring between a real adapter and the diagnostics recorder,
not to a Core type in isolation.

Display enumeration itself stays untested, consistent with `SmartZoom.Interop` having no test project: it
needs real monitors.

## Documentation that changes with it

- **`SECURITY.md`** — the privacy section gains the record: where it lives, what it holds, what it refuses to
  hold, and that it is local. The "no network connections" sentence is unchanged, and the new paragraph should
  make clear that it is still true.
- **`CONTRIBUTING.md`** — "Reporting a bug" currently asks for log lines; it should ask for the report.
- **`README.md`** — one line under configuration for the switch.
- **`CHANGELOG.md`** — under Added.

## Deliberately not in scope

- Any network transmission, now or behind a flag.
- Any identifier, including a resettable one.
- Usage analytics: how often people zoom, which triggers they chose, whether they came back. Answering those
  requires an identifier, and the identifier is the part that cannot be justified on an application that
  installs a global keyboard hook.
- Automatic issue creation or pre-filled GitHub URLs. The report goes to the clipboard; where it goes next is
  the user's business.
