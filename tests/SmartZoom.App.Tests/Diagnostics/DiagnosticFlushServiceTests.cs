using Microsoft.Extensions.Logging.Abstractions;

using SmartZoom.App.Diagnostics;
using SmartZoom.Core.Diagnostics;

namespace SmartZoom.App.Tests.Diagnostics;

public sealed class DiagnosticFlushServiceTests
{
    [Fact]
    public async Task Flushes_on_shutdown_even_though_the_30_second_timer_never_ticked()
    {
        using var temp = new TempDirectory();
        var recorder = CreateRecorder(temp.Path);
        recorder.Note(new DiagnosticKey(DiagnosticKind.NoWindow, null, null, null));

        var service = new DiagnosticFlushService(recorder, TimeProvider.System);
        await service.StartAsync(CancellationToken.None);

        // The interval is 30 seconds; nothing here waits that long. The write below is the service's
        // "finally { recorder.Flush(); }" running on shutdown, not the periodic tick.
        await service.StopAsync(CancellationToken.None);

        var file = Path.Combine(temp.Path, "diagnostics.json");
        Assert.True(File.Exists(file));

        // Read back through the same production code that loads the record at startup, rather than
        // matching the raw JSON, so this does not depend on incidental serialization details.
        var store = new DiagnosticStore(
            new AppPaths(SettingsDirectory: temp.Path, LogDirectory: Path.Combine(temp.Path, "logs")),
            NullLogger<DiagnosticStore>.Instance);
        var counter = Assert.Single(store.Load("0.1.0-test").Counters);
        Assert.Equal(new DiagnosticKey(DiagnosticKind.NoWindow, null, null, null), counter.Key);
    }

    private static DiagnosticRecorder CreateRecorder(string directory) =>
        new(
            new DiagnosticStore(
                new AppPaths(SettingsDirectory: directory, LogDirectory: Path.Combine(directory, "logs")),
                NullLogger<DiagnosticStore>.Instance),
            TimeProvider.System,
            version: "0.1.0-test");
}
