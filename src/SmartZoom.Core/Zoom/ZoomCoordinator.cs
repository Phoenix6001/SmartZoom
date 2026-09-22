using Microsoft.Extensions.Logging;

using SmartZoom.Core.Input;
using SmartZoom.Core.Routing;

namespace SmartZoom.Core.Zoom;

/// <summary>
/// Turns a trigger on a target into a zoom-in or a zoom-out: picks the adapter for the target's
/// process, consults per-window state to decide the direction, and falls back to Ctrl+wheel when
/// a richer adapter can't act.
/// </summary>
public sealed partial class ZoomCoordinator
{
    private readonly ZoomRouter _router;
    private readonly Dictionary<AdapterId, IZoomAdapter> _adapters;
    private readonly WindowZoomStateStore _state;
    private readonly IWindowInspector _windows;
    private readonly bool _fallbackToCtrlWheel;
    private readonly ILogger<ZoomCoordinator> _logger;
    private readonly HashSet<string> _reportedMissing = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Creates a coordinator over the given adapters.</summary>
    /// <param name="router">Process-to-adapter routing.</param>
    /// <param name="adapters">Available adapters; at most one per <see cref="AdapterId"/>.</param>
    /// <param name="state">Per-window toggle memory.</param>
    /// <param name="windows">Used to detect windows that have closed.</param>
    /// <param name="fallbackToCtrlWheel">When an adapter reports <see cref="ZoomInStatus.Unhandled"/>, try the Ctrl+wheel adapter instead.</param>
    /// <param name="logger">Logger.</param>
    public ZoomCoordinator(
        ZoomRouter router,
        IEnumerable<IZoomAdapter> adapters,
        WindowZoomStateStore state,
        IWindowInspector windows,
        bool fallbackToCtrlWheel,
        ILogger<ZoomCoordinator> logger)
    {
        ArgumentNullException.ThrowIfNull(adapters);

        _router = router ?? throw new ArgumentNullException(nameof(router));
        _adapters = adapters.ToDictionary(a => a.Descriptor.Id);
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _windows = windows ?? throw new ArgumentNullException(nameof(windows));
        _fallbackToCtrlWheel = fallbackToCtrlWheel;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Handles one trigger.</summary>
    /// <param name="target">Window under the cursor.</param>
    /// <param name="point">Cursor position in physical pixels.</param>
    /// <param name="cancellationToken">Cancels the zoom.</param>
    /// <returns>What was done, for logging, the tray tooltip and diagnostics.</returns>
    public async Task<ZoomOutcome> HandleTriggerAsync(TargetInfo target, ScreenPoint point, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);

        _state.Prune(_windows.IsWindowAlive);

        if (_state.TryTake(target, out var previousId, out var restoreState))
        {
            // TryTake has already removed the entry, so everything below has to cope without it.
            if (!_adapters.TryGetValue(previousId, out var previous))
            {
                // The window was zoomed by an adapter this process no longer has. Nothing can undo it from
                // here, so say so instead of silently zooming the window a second time.
                LogRestoreImpossible(target.ProcessName, previousId);
            }
            else if (!previous.RestoreType.IsInstanceOfType(restoreState))
            {
                // Same id, different adapter behind it: the reader's two modes, after a settings change that
                // the applier failed to invalidate. Zooming in again is a worse answer than it sounds, but it
                // is far better than the alternative, which is an exception and a window stuck zoomed.
                LogRestoreObsolete(target.ProcessName, previousId, restoreState.GetType().Name, previous.RestoreType.Name);
            }
            else
            {
                await previous.ZoomOutAsync(target, restoreState, cancellationToken).ConfigureAwait(false);
                LogZoomedOut(target.ProcessName, previousId);
                return new ZoomOutcome(ZoomAction.ZoomedOut, target.ProcessName, previousId);
            }
        }

        // No entry at all is the normal case for most applications on a machine, and so is an explicit
        // "None": both mean the trigger is not ours and must stay silent.
        if (_router.Resolve(target.ProcessName) is not { } id || id == AdapterId.None)
        {
            return new ZoomOutcome(ZoomAction.Ignored, target.ProcessName, null, ZoomReason.NoAdapter);
        }

        ZoomInResult result;
        if (_adapters.TryGetValue(id, out var adapter))
        {
            result = await adapter.ZoomInAsync(target, point, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            // Settings name a strategy this build does not have. Treated as "could not act" so the user
            // still gets the fallback, and reported once per process so a typo is visible in the log.
            WarnMissingAdapterOnce(target.ProcessName, id);
            result = ZoomInResult.Unhandled;
        }

        var fallbackId = CtrlWheelAdapter.Descriptor.Id;
        if (result.Status == ZoomInStatus.Unhandled
            && _fallbackToCtrlWheel
            && id != fallbackId
            && _adapters.TryGetValue(fallbackId, out var fallback))
        {
            LogFallingBack(target.ProcessName, id);
            id = fallbackId;
            result = await fallback.ZoomInAsync(target, point, cancellationToken).ConfigureAwait(false);
        }

        switch (result.Status)
        {
            case ZoomInStatus.Applied:
                _state.Save(target, id, result.RestoreState!);
                LogZoomedIn(target.ProcessName, id);
                return new ZoomOutcome(ZoomAction.ZoomedIn, target.ProcessName, id);

            case ZoomInStatus.Handled:
                LogHandled(target.ProcessName, id);
                return new ZoomOutcome(ZoomAction.Handled, target.ProcessName, id, result.Reason);

            default:
                LogUnhandled(target.ProcessName, id);
                return new ZoomOutcome(ZoomAction.Unhandled, target.ProcessName, id);
        }
    }

    private void WarnMissingAdapterOnce(string? process, AdapterId id)
    {
        lock (_reportedMissing)
        {
            if (!_reportedMissing.Add($"{process}/{id}"))
                return;
        }

        LogMissingAdapter(process, id, string.Join(", ", _adapters.Keys.Select(k => k.Value).Order(StringComparer.OrdinalIgnoreCase)));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Zoomed in {Process} via {Adapter}.")]
    private partial void LogZoomedIn(string? process, AdapterId adapter);

    [LoggerMessage(Level = LogLevel.Information, Message = "Zoomed out {Process} via {Adapter}.")]
    private partial void LogZoomedOut(string? process, AdapterId adapter);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Adapter} handled the trigger in {Process} with nothing to undo (see its own log line for what it did).")]
    private partial void LogHandled(string? process, AdapterId adapter);

    [LoggerMessage(Level = LogLevel.Debug, Message = "{Adapter} could not handle {Process}; falling back to CtrlWheel.")]
    private partial void LogFallingBack(string? process, AdapterId adapter);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Adapter} could not zoom {Process}.")]
    private partial void LogUnhandled(string? process, AdapterId adapter);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Process} is routed to \"{Adapter}\", which this build does not have; falling back. Check \"Routing.Apps\" in the settings file. Available: {Available}.")]
    private partial void LogMissingAdapter(string? process, AdapterId adapter, string available);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Process} was zoomed by \"{Adapter}\", which is no longer registered, so the zoom cannot be undone. Undo it in the application itself.")]
    private partial void LogRestoreImpossible(string? process, AdapterId adapter);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Process} was zoomed by a different \"{Adapter}\" than the one running now ({Stored} rather than {Expected}); that zoom cannot be undone, so this press zooms in again.")]
    private partial void LogRestoreObsolete(string? process, AdapterId adapter, string stored, string expected);
}
