using SmartZoom.App.Diagnostics;
using SmartZoom.Core.Diagnostics;

namespace SmartZoom.App.Tests.Diagnostics;

public sealed class CrashTests
{
    [Fact]
    public void Writes_the_crash_to_disk_synchronously_with_no_separate_flush()
    {
        // The point of Crash.Record: a crash handler gets no second chance to flush on a timer, because
        // the process is about to end. This proves the write already happened by the time Record returns.
        using var temp = new TempDirectory();
        var recorder = DiagnosticFixtures.CreateRecorder(temp.Path);

        Crash.Record(recorder, new InvalidOperationException("the process is going down"));

        var file = Path.Combine(temp.Path, "diagnostics.json");
        Assert.True(File.Exists(file));

        // Read back through the same production code that loads the record at startup, rather than
        // matching the raw JSON, so this does not depend on incidental serialization details.
        var record = DiagnosticFixtures.CreateStore(temp.Path).Load(DiagnosticFixtures.Version);

        var counter = Assert.Single(record.Counters);
        Assert.Equal(new DiagnosticKey(DiagnosticKind.Crashed, null, null, nameof(InvalidOperationException)), counter.Key);

        var sample = Assert.Single(record.Samples);
        Assert.Null(sample.Detail);
        Assert.NotNull(sample.Exception);
        Assert.Contains(nameof(InvalidOperationException), sample.Exception, StringComparison.Ordinal);
        Assert.Contains("the process is going down", sample.Exception, StringComparison.Ordinal);
    }

    [Fact]
    public void An_exception_the_ui_thread_survived_is_kept_apart_from_one_that_took_the_process_down()
    {
        // Both are Crashed, but a report has to tell "SmartZoom died" from "a dialog threw and the app carried
        // on"; the adapter slot is where that difference lives, so it must reach the file.
        using var temp = new TempDirectory();
        var recorder = DiagnosticFixtures.CreateRecorder(temp.Path);

        Crash.Record(recorder, new InvalidOperationException("dialog"), Crash.UiThread);
        Crash.Record(recorder, new InvalidOperationException("fatal"));

        var record = DiagnosticFixtures.CreateStore(temp.Path).Load(DiagnosticFixtures.Version);

        Assert.Equal(2, record.Counters.Count);
        Assert.Contains(record.Counters, c => c.Key == new DiagnosticKey(DiagnosticKind.Crashed, null, "UiThread", nameof(InvalidOperationException)));
        Assert.Contains(record.Counters, c => c.Key == new DiagnosticKey(DiagnosticKind.Crashed, null, null, nameof(InvalidOperationException)));
    }

    [Fact]
    public void Never_throws_even_when_the_diagnostics_directory_cannot_be_created()
    {
        // A file (not a directory) sitting where the settings directory should be makes every write inside
        // it fail. A crash handler that lets this escape replaces a diagnosable crash with an undiagnosable
        // one, so nothing here may throw.
        using var temp = new TempDirectory();
        var blocker = Path.Combine(temp.Path, "blocked");
        File.WriteAllText(blocker, "not a directory");
        var recorder = DiagnosticFixtures.CreateRecorder(blocker);

        var exception = Record.Exception(() => Crash.Record(recorder, new InvalidOperationException("boom")));

        Assert.Null(exception);
    }
}
