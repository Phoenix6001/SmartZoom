using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using SmartZoom.Core.Input;
using SmartZoom.Core.Routing;
using SmartZoom.Core.Zoom;

namespace SmartZoom.App.Hosting;

/// <summary>Consumes triggers off the hook thread, resolves the target under the cursor, and zooms it.</summary>
internal sealed partial class TriggerDispatcher(
    ITriggerSource triggerSource,
    IWindowInspector windowInspector,
    ZoomCoordinator coordinator,
    ZoomActivity activity,
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
            return;
        }

        LogTrigger(x, y, target.ProcessName ?? "<inaccessible>", target.ProcessId, target.RootWindow, target.RootClassName, target.HitClassName);

        try
        {
            var outcome = await coordinator.HandleTriggerAsync(target, trigger.Position, cancellationToken).ConfigureAwait(false);
            LogOutcome(outcome.Action);
            activity.Report(outcome);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // One failed zoom must not take the dispatcher (and with it, every future trigger) down.
            LogZoomFailed(ex, target.ProcessName);
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
}
