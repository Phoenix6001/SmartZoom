using Microsoft.Extensions.Logging.Abstractions;

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
        var recorder = CreateRecorder(temp.Path);

        Crash.Record(recorder, new InvalidOperationException("the process is going down"));

        var file = Path.Combine(temp.Path, "diagnostics.json");
        Assert.True(File.Exists(file));

        // Read back through the same production code that loads the record at startup, rather than
        // matching the raw JSON, so this does not depend on incidental serialization details.
        var store = new DiagnosticStore(
            new AppPaths(SettingsDirectory: temp.Path, LogDirectory: Path.Combine(temp.Path, "logs")),
            NullLogger<DiagnosticStore>.Instance);
        var record = store.Load("0.1.0-test");

        var counter = Assert.Single(record.Counters);
        Assert.Equal(new DiagnosticKey(DiagnosticKind.Crashed, null, null, nameof(InvalidOperationException)), counter.Key);

        var sample = Assert.Single(record.Samples);
        Assert.Null(sample.Detail);
        Assert.NotNull(sample.Exception);
        Assert.Contains(nameof(InvalidOperationException), sample.Exception, StringComparison.Ordinal);
        Assert.Contains("the process is going down", sample.Exception, StringComparison.Ordinal);
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
        var recorder = CreateRecorder(blocker);

        var exception = Record.Exception(() => Crash.Record(recorder, new InvalidOperationException("boom")));

        Assert.Null(exception);
    }

    private static DiagnosticRecorder CreateRecorder(string directory) =>
        new(
            new DiagnosticStore(
                new AppPaths(SettingsDirectory: directory, LogDirectory: Path.Combine(directory, "logs")),
                NullLogger<DiagnosticStore>.Instance),
            TimeProvider.System,
            version: "0.1.0-test");

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("smartzoom-crash-test-").FullName;

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
                // Best effort; the OS temp folder gets cleaned up eventually regardless.
            }
        }
    }
}
