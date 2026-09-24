using SmartZoom.App.Diagnostics;
using SmartZoom.Core.Diagnostics;

namespace SmartZoom.App.Tests.Diagnostics;

public sealed class DiagnosticFlushServiceTests
{
    [Fact]
    public async Task Flushes_on_shutdown_even_though_the_30_second_timer_never_ticked()
    {
        using var temp = new TempDirectory();
        var recorder = DiagnosticFixtures.CreateRecorder(temp.Path);
        recorder.Note(new DiagnosticKey(DiagnosticKind.NoWindow, null, null, null));

        var time = new TimerObservingTimeProvider();
        var service = new DiagnosticFlushService(recorder, time);
        await service.StartAsync(CancellationToken.None);

        // StartAsync schedules the loop rather than running it inline, so a stop issued straight away can
        // cancel it before its first line and the shutdown flush inside it never runs. Waiting for the loop
        // to create its timer is what makes "stopped while running" the case under test.
        await time.TimerCreated;

        // The interval is 30 seconds; nothing here waits that long. The write below is the service's
        // "finally { recorder.Flush(); }" running on shutdown, not the periodic tick.
        await service.StopAsync(CancellationToken.None);

        var file = Path.Combine(temp.Path, "diagnostics.json");
        Assert.True(File.Exists(file));

        // Read back through the same production code that loads the record at startup, rather than
        // matching the raw JSON, so this does not depend on incidental serialization details.
        var counter = Assert.Single(DiagnosticFixtures.CreateStore(temp.Path).Load(DiagnosticFixtures.Version).Counters);
        Assert.Equal(new DiagnosticKey(DiagnosticKind.NoWindow, null, null, null), counter.Key);
    }

    /// <summary>The system clock, which also says when the service's periodic timer has been created.</summary>
    private sealed class TimerObservingTimeProvider : TimeProvider
    {
        private readonly TaskCompletionSource _created = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task TimerCreated => _created.Task;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = System.CreateTimer(callback, state, dueTime, period);
            _created.TrySetResult();
            return timer;
        }
    }
}
