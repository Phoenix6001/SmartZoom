using System.Threading.Channels;

using Microsoft.Extensions.Logging.Abstractions;

using SmartZoom.App.Diagnostics;
using SmartZoom.App.Hosting;
using SmartZoom.App.Tests.Diagnostics;
using SmartZoom.Core.Diagnostics;
using SmartZoom.Core.Input;
using SmartZoom.Core.Routing;
using SmartZoom.Core.Settings;
using SmartZoom.Core.Zoom;

namespace SmartZoom.App.Tests.Hosting;

/// <summary>
/// Exercises the live dispatch loop end to end (a real <see cref="Channel{T}"/>, a real
/// <see cref="ZoomEngine"/> and <see cref="ZoomCoordinator"/>), so what is asserted is what
/// <see cref="TriggerDispatcher"/> actually does with a trigger, not a restatement of it.
/// </summary>
public sealed class TriggerDispatcherTests
{
    private static readonly ScreenPoint Point = new(10, 20);

    private readonly FakeTriggerSource _source = new();
    private readonly ZoomActivity _activity = new();

    [Fact]
    public async Task A_trigger_that_hits_no_window_is_counted_and_never_reaches_the_engine()
    {
        var windows = new FakeWindowInspector { Target = null };
        using var temp = new TempDirectory();
        var recorder = DiagnosticFixtures.CreateRecorder(temp.Path);

        await RunOneTriggerAsync(windows, ScriptedEngine(), recorder);

        var counter = Assert.Single(recorder.Snapshot().Counters);
        Assert.Equal(new DiagnosticKey(DiagnosticKind.NoWindow, null, null, null), counter.Key);
    }

    [Fact]
    public async Task A_trigger_older_than_the_stale_limit_is_dropped_before_the_window_is_even_resolved()
    {
        // A press that sat in the queue behind a slow zoom is not what the user wants acted on now: it
        // is dropped without touching the window under the cursor, the engine or the record.
        var windows = new FakeWindowInspector { Target = new TargetInfo(0x2, 0x2, 2, "notepad", "R", "H") };
        using var temp = new TempDirectory();
        var recorder = DiagnosticFixtures.CreateRecorder(temp.Path);

        var dispatcher = new TriggerDispatcher(
            _source, windows, ScriptedEngine(), _activity, recorder, TimeProvider.System, NullLogger<TriggerDispatcher>.Instance);

        await dispatcher.StartAsync(CancellationToken.None);
        var stale = unchecked((uint)Environment.TickCount - 5_000);
        _source.Writer.TryWrite(new TriggerEvent(Point, stale));
        _source.Writer.Complete();
        await dispatcher.ExecuteTask!;

        Assert.Equal(0, windows.Calls);
        Assert.Null(_activity.LastTrigger);
        Assert.Empty(recorder.Snapshot().Counters);
    }

    [Fact]
    public async Task An_adapter_that_throws_is_recorded_with_a_redacted_exception_and_no_detail()
    {
        var target = new TargetInfo(0x1, 0x1, 1, "boom", "R", "H");
        var windows = new FakeWindowInspector { Target = target };
        using var temp = new TempDirectory();
        var recorder = DiagnosticFixtures.CreateRecorder(temp.Path);

        var thrown = new InvalidOperationException("something the adapter choked on");
        await RunOneTriggerAsync(windows, ThrowingEngine(thrown), recorder);

        var counter = Assert.Single(recorder.Snapshot().Counters);
        Assert.Equal(new DiagnosticKey(DiagnosticKind.AdapterThrew, "boom", null, nameof(InvalidOperationException)), counter.Key);

        var sample = Assert.Single(recorder.Snapshot().Samples);
        Assert.Null(sample.Detail);
        Assert.NotNull(sample.Exception);
        Assert.Contains("InvalidOperationException", sample.Exception, StringComparison.Ordinal);
        Assert.Contains("something the adapter choked on", sample.Exception, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_trigger_that_hits_no_window_leaves_the_dispatcher_running_even_if_recording_throws()
    {
        // A real throw from DiagnosticRecorder.Note, not a fake standing in for one: TimeProvider.GetUtcNow()
        // is the only thing Note calls that could plausibly fail, so making IT throw exercises the guard
        // through the actual production code path rather than asserting against a stand-in.
        var windows = new FakeWindowInspector { Target = null };
        using var temp = new TempDirectory();
        var recorder = DiagnosticFixtures.CreateRecorder(temp.Path, new ThrowingTimeProvider());

        var dispatcher = new TriggerDispatcher(
            _source, windows, ScriptedEngine(), _activity, recorder, TimeProvider.System, NullLogger<TriggerDispatcher>.Instance);

        await dispatcher.StartAsync(CancellationToken.None);
        _source.Writer.TryWrite(new TriggerEvent(Point, unchecked((uint)Environment.TickCount)));
        _source.Writer.TryWrite(new TriggerEvent(Point, unchecked((uint)Environment.TickCount)));
        _source.Writer.Complete();

        // If the guard were missing, recorder.Note's throw would escape DispatchAsync and ExecuteAsync's
        // await foreach, faulting this task instead of completing it — awaiting it would rethrow.
        await dispatcher.ExecuteTask!;

        // Not just "the task didn't fault": the loop kept reading. A second trigger reached GetTargetAt,
        // which only happens on the next iteration of the still-running await foreach.
        Assert.Equal(2, windows.Calls);
    }

    [Fact]
    public async Task A_trigger_whose_adapter_threw_leaves_the_dispatcher_running_even_if_recording_that_also_throws()
    {
        // The worst-case ordering: the recording this exercises is the one inside DispatchAsync's own catch
        // block, with nothing further wrapping it — an adapter has already failed, and now recording that
        // failure fails too. A ThrowingTimeProvider makes DiagnosticRecorder.Note genuinely throw, same as
        // the NoWindow guard test above, but reached from the AdapterThrew path instead.
        var target = new TargetInfo(0x1, 0x1, 1, "boom", "R", "H");
        var windows = new FakeWindowInspector { Target = target };
        using var temp = new TempDirectory();
        var recorder = DiagnosticFixtures.CreateRecorder(temp.Path, new ThrowingTimeProvider());

        var dispatcher = new TriggerDispatcher(
            _source, windows, ThrowingEngine(new InvalidOperationException("adapter boom")), _activity, recorder,
            TimeProvider.System, NullLogger<TriggerDispatcher>.Instance);

        await dispatcher.StartAsync(CancellationToken.None);
        _source.Writer.TryWrite(new TriggerEvent(Point, unchecked((uint)Environment.TickCount)));
        _source.Writer.TryWrite(new TriggerEvent(Point, unchecked((uint)Environment.TickCount)));
        _source.Writer.Complete();

        // If RecordSafely did not wrap this site, recorder.Note's throw inside the catch block would escape
        // DispatchAsync and ExecuteAsync's await foreach, faulting this task instead of completing it.
        await dispatcher.ExecuteTask!;

        // The loop kept running after the first (adapter-failed, then recording-failed) trigger: the second
        // still reached GetTargetAt.
        Assert.Equal(2, windows.Calls);
    }

    [Fact]
    public async Task A_press_that_zoomed_nothing_because_no_adapter_claims_the_process_is_counted()
    {
        var target = new TargetInfo(0x2, 0x2, 2, "notepad", "R", "H");
        var windows = new FakeWindowInspector { Target = target };
        using var temp = new TempDirectory();
        var recorder = DiagnosticFixtures.CreateRecorder(temp.Path);

        await RunOneTriggerAsync(windows, ScriptedEngine(), recorder);

        var counter = Assert.Single(recorder.Snapshot().Counters);
        Assert.Equal(new DiagnosticKey(DiagnosticKind.ZoomedNothing, "notepad", null, "NoAdapter"), counter.Key);

        // No adapter ran at all, so there is no path shape to keep: a counter, no worked example.
        Assert.Empty(recorder.Snapshot().Samples);
    }

    private async Task RunOneTriggerAsync(FakeWindowInspector windows, ZoomEngine engine, DiagnosticRecorder recorder)
    {
        var dispatcher = new TriggerDispatcher(
            _source, windows, engine, _activity, recorder, TimeProvider.System, NullLogger<TriggerDispatcher>.Instance);

        await dispatcher.StartAsync(CancellationToken.None);
        _source.Writer.TryWrite(new TriggerEvent(Point, unchecked((uint)Environment.TickCount)));
        _source.Writer.Complete();

        // The channel completing (rather than the stopping token) is what ends the loop, so awaiting the
        // background task directly is race-free: it only completes once the one trigger above is handled.
        await dispatcher.ExecuteTask!;
    }

    private static ZoomEngine ScriptedEngine()
    {
        var adapters = Array.Empty<IZoomAdapter>();
        var router = new ZoomRouter(
            [],
            new RoutingSettings { Apps = new Dictionary<string, AdapterId>(StringComparer.OrdinalIgnoreCase) },
            NullLogger<ZoomRouter>.Instance);
        var coordinator = new ZoomCoordinator(
            router, adapters, new WindowZoomStateStore(), new UnusedWindowInspector(), fallbackToCtrlWheel: false, NullLogger<ZoomCoordinator>.Instance);
        var pipeline = new ZoomPipeline(coordinator, router, new Dictionary<AdapterId, Type>());
        return new ZoomEngine(pipeline, new WindowZoomStateStore(), NullLogger<ZoomEngine>.Instance);
    }

    private static ZoomEngine ThrowingEngine(Exception exception)
    {
        var adapter = new ThrowingAdapter(exception);
        var router = new ZoomRouter(
            [adapter.Descriptor],
            new RoutingSettings { Apps = new Dictionary<string, AdapterId>(StringComparer.OrdinalIgnoreCase) },
            NullLogger<ZoomRouter>.Instance);
        var coordinator = new ZoomCoordinator(
            router, [adapter], new WindowZoomStateStore(), new UnusedWindowInspector(), fallbackToCtrlWheel: false, NullLogger<ZoomCoordinator>.Instance);
        var pipeline = new ZoomPipeline(coordinator, router, new Dictionary<AdapterId, Type> { [adapter.Descriptor.Id] = typeof(object) });
        return new ZoomEngine(pipeline, new WindowZoomStateStore(), NullLogger<ZoomEngine>.Instance);
    }

    private sealed class FakeWindowInspector : IWindowInspector
    {
        public TargetInfo? Target { get; set; }

        public int Calls { get; private set; }

        public TargetInfo? GetTargetAt(ScreenPoint point)
        {
            Calls++;
            return Target;
        }

        public bool IsWindowAlive(nint window, uint processId) => true;
    }

    // Throws from GetUtcNow(), the one thing DiagnosticRecorder.Note calls that could plausibly fail, so a
    // DiagnosticRecorder built with this genuinely throws from Note/Sample rather than being a fake that only
    // pretends to.
    private sealed class ThrowingTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => throw new InvalidOperationException("diagnostics boom");
    }

    // A coordinator built from an adapter with no default-claimed process never calls GetTargetAt itself
    // (the dispatcher already resolved the window before the engine is touched); this exists only to fail
    // loudly if that ever stops being true.
    private sealed class UnusedWindowInspector : IWindowInspector
    {
        public TargetInfo? GetTargetAt(ScreenPoint point) => throw new NotSupportedException();

        public bool IsWindowAlive(nint window, uint processId) => true;
    }

    private sealed class ThrowingAdapter(Exception exception) : IZoomAdapter
    {
        public AdapterDescriptor Descriptor { get; } = new("Boom", ["boom"], "Boom", "Always throws.");

        public Type RestoreType => typeof(object);

        public Task<ZoomInResult> ZoomInAsync(TargetInfo target, ScreenPoint point, CancellationToken cancellationToken) =>
            throw exception;

        public Task ZoomOutAsync(TargetInfo target, object restoreState, CancellationToken cancellationToken) =>
            throw exception;
    }
}
