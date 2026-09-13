using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SmartZoom.Core.Input;
using SmartZoom.Core.Routing;

namespace SmartZoom.App.Hosting;

/// <summary>Consumes triggers off the hook thread, resolves the target under the cursor, and routes it.</summary>
internal sealed partial class TriggerDispatcher(
    ITriggerSource triggerSource,
    IWindowInspector windowInspector,
    ZoomRouter router,
    ILogger<TriggerDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var trigger in triggerSource.Triggers.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            {
                Dispatch(trigger);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }

    private void Dispatch(TriggerEvent trigger)
    {
        var (x, y) = trigger.Position;
        var target = windowInspector.GetTargetAt(trigger.Position);
        if (target is null)
        {
            LogNoWindow(x, y);
            return;
        }

        var adapter = router.Resolve(target.ProcessName);
        LogTrigger(x, y, target.ProcessName ?? "<inaccessible>", target.ProcessId, target.RootWindow, target.RootClassName, target.HitClassName, adapter);

        // M2+: hand off to the IZoomAdapter selected by 'adapter'.
    }

    [LoggerMessage(Level = LogLevel.Information,
        Message = "Trigger at ({X}, {Y}) px -> {Process} (pid {ProcessId}, hwnd 0x{RootWindow:X}, class {RootClass}, hit {HitClass}) route={Adapter}")]
    private partial void LogTrigger(int x, int y, string process, uint processId, nint rootWindow, string rootClass, string hitClass, AdapterKind adapter);

    [LoggerMessage(Level = LogLevel.Information, Message = "Trigger at ({X}, {Y}) px hit no window.")]
    private partial void LogNoWindow(int x, int y);
}
