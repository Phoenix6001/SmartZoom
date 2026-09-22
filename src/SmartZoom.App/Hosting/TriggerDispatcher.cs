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
    ILogger<TriggerDispatcher> logger) : BackgroundService
{
    // A trigger older than this was queued behind a slow zoom; acting on it now would surprise the user.
    private static readonly TimeSpan MaxTriggerAge = TimeSpan.FromMilliseconds(750);

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
        if (age > MaxTriggerAge.TotalMilliseconds)
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

        try
        {
            var outcome = await engine.HandleTriggerAsync(target, trigger.Position, cancellationToken).ConfigureAwait(false);
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
    /// Runs a diagnostics recording action, swallowing anything it throws. Diagnostics never participates in
    /// control flow: every call site above that records something routes through here rather than its own
    /// try/catch, because one of those sites already sits inside DispatchAsync's exception handler — a throw
    /// there has nothing further wrapping it and would escape ExecuteAsync's await foreach outright, ending
    /// the dispatcher (and every future trigger) along with it.
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

    [LoggerMessage(Level = LogLevel.Error, Message = "Zoom failed for {Process}.")]
    private partial void LogZoomFailed(Exception exception, string? process);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Failed to record a diagnostic; continuing.")]
    private partial void LogDiagnosticsFailed(Exception exception);
}
