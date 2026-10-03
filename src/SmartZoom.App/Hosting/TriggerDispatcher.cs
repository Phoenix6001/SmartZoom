using System.Globalization;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using SmartZoom.App.Diagnostics;
using SmartZoom.Core.Diagnostics;
using SmartZoom.Core.Input;
using SmartZoom.Core.Routing;
using SmartZoom.Core.Zoom;

namespace SmartZoom.App.Hosting;

/// <summary>Consumes triggers off the hook thread, resolves the target under the cursor, and zooms it.</summary>
internal sealed partial class TriggerDispatcher(
    ITriggerSource triggerSource,
    IWindowInspector windowInspector,
    ZoomEngine engine,
    ZoomActivity activity,
    DiagnosticRecorder recorder,
    TimeProvider time,
    ILogger<TriggerDispatcher> logger,
    TimeSpan? zoomDeadline = null,
    TimeSpan? maxTriggerAge = null) : BackgroundService
{
    /// <summary>
    /// How long one press gets. Everything legitimate is well inside it — a gesture is about 330 ms and a
    /// reader shortcut waits at most 1.5 s for the user to let go of a hotkey's modifiers — so a zoom still
    /// running at this point is one that is not coming back. Abandoning it costs that press; not abandoning
    /// it costs every press after it, because the zoom is still holding the engine's gate.
    /// </summary>
    internal static readonly TimeSpan ZoomDeadline = TimeSpan.FromSeconds(6);

    /// <summary>A trigger older than this was queued behind a slow zoom; acting on it now would surprise the user.</summary>
    internal static readonly TimeSpan MaxTriggerAge = TimeSpan.FromMilliseconds(750);

    // Only tests pass these; the container has no TimeSpan to hand and takes the defaults.
    private readonly TimeSpan _zoomDeadline = zoomDeadline ?? ZoomDeadline;
    private readonly TimeSpan _maxTriggerAge = maxTriggerAge ?? MaxTriggerAge;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var trigger in triggerSource.Triggers.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            {
                await DispatchAsync(trigger, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }

    private async Task DispatchAsync(TriggerEvent trigger, CancellationToken cancellationToken)
    {
        var (x, y) = trigger.Position;

        // Both clocks are GetTickCount-based, so the difference is valid across wraparound.
        var age = unchecked((uint)Environment.TickCount - trigger.TimestampMs);
        if (age > _maxTriggerAge.TotalMilliseconds)
        {
            LogStale(x, y, age);
            return;
        }

        var target = windowInspector.GetTargetAt(trigger.Position);
        if (target is null)
        {
            LogNoWindow(x, y);
            RecordSafely(() => recorder.Note(new DiagnosticKey(DiagnosticKind.NoWindow, null, null, null)));
            return;
        }

        LogTrigger(x, y, target.ProcessName ?? "<inaccessible>", target.ProcessId, target.RootWindow, target.RootClassName, target.HitClassName);
        activity.Seen(time.GetUtcNow());

        // The deadline covers the wait for the engine's gate as well as the zoom itself, which is the point:
        // a zoom that never returns would otherwise hold that gate for the life of the process and every
        // later press would be dropped in silence. Strategies put the user's foreground window and modifier
        // keys back from a finally with no token, so being cancelled here does not strand any of that.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_zoomDeadline);

        try
        {
            var outcome = await engine.HandleTriggerAsync(target, trigger.Position, deadline.Token).ConfigureAwait(false);
            LogOutcome(outcome.Action);
            activity.Report(outcome);

            if (outcome.Action is ZoomAction.Handled or ZoomAction.Unhandled or ZoomAction.Ignored)
            {
                RecordSafely(() =>
                {
                    var (key, sample) = DiagnosticSampleFactory.ForZoomedNothing(outcome, time.GetUtcNow());
                    recorder.Note(key);
                    if (sample is not null)
                        recorder.Sample(sample);
                });
            }
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            // The deadline, not shutdown: this one is a finding, so it is recorded like any other failure
            // rather than being swallowed as a cancellation.
            LogZoomAbandoned(target.ProcessName, _zoomDeadline.TotalSeconds);

            RecordSafely(() =>
            {
                var key = new DiagnosticKey(DiagnosticKind.AdapterThrew, target.ProcessName, null, nameof(TimeoutException));
                recorder.Note(key);
                recorder.Sample(new DiagnosticSample(
                    key,
                    time.GetUtcNow(),
                    Detail: string.Create(
                        CultureInfo.InvariantCulture,
                        $"The zoom was still running after {_zoomDeadline.TotalSeconds:0.#} s and was abandoned."),
                    Exception: null));
            });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // One failed zoom must not take the dispatcher (and with it, every future trigger) down. Logged
            // before anything is recorded: the zoom failure itself must reach the log even if recording it
            // then fails too.
            LogZoomFailed(ex, target.ProcessName);

            RecordSafely(() =>
            {
                var key = new DiagnosticKey(DiagnosticKind.AdapterThrew, target.ProcessName, null, ex.GetType().Name);
                recorder.Note(key);
                recorder.Sample(new DiagnosticSample(
                    key,
                    time.GetUtcNow(),
                    Detail: null,
                    Exception: DiagnosticText.ForException(ex)));
            });
        }
    }

    /// <summary>
    /// Runs a diagnostics recording action and swallows anything it throws, so that a failure to record can
    /// never end the dispatcher.
    /// </summary>
    /// <param name="record">The recording to attempt.</param>
    private void RecordSafely(Action record)
    {
        try
        {
            record();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogDiagnosticsFailed(ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Trigger at ({X}, {Y}) px -> {Process} (pid {ProcessId}, hwnd 0x{RootWindow:X}, class {RootClass}, hit {HitClass})")]
    private partial void LogTrigger(int x, int y, string process, uint processId, nint rootWindow, string rootClass, string hitClass);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Outcome: {Action}.")]
    private partial void LogOutcome(ZoomAction action);

    [LoggerMessage(Level = LogLevel.Information, Message = "Trigger at ({X}, {Y}) px hit no window.")]
    private partial void LogNoWindow(int x, int y);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Dropped trigger at ({X}, {Y}) px: {AgeMs} ms old.")]
    private partial void LogStale(int x, int y, uint ageMs);

    [LoggerMessage(Level = LogLevel.Error,
        Message = "The zoom in {Process} was still running after {Seconds} s and was abandoned, so the next press is not held up by it.")]
    private partial void LogZoomAbandoned(string? process, double seconds);

    [LoggerMessage(Level = LogLevel.Error, Message = "Zoom failed for {Process}.")]
    private partial void LogZoomFailed(Exception exception, string? process);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Failed to record a diagnostic; continuing.")]
    private partial void LogDiagnosticsFailed(Exception exception);
}
