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
    private readonly Dictionary<AdapterKind, IZoomAdapter> _adapters;
    private readonly WindowZoomStateStore _state;
    private readonly IWindowInspector _windows;
    private readonly bool _fallbackToCtrlWheel;
    private readonly ILogger<ZoomCoordinator> _logger;

    /// <summary>Creates a coordinator over the given adapters.</summary>
    /// <param name="router">Process-to-adapter routing.</param>
    /// <param name="adapters">Available adapters; at most one per <see cref="AdapterKind"/>.</param>
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
        _adapters = adapters.ToDictionary(a => a.Kind);
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _windows = windows ?? throw new ArgumentNullException(nameof(windows));
        _fallbackToCtrlWheel = fallbackToCtrlWheel;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Handles one trigger.</summary>
    /// <param name="target">Window under the cursor.</param>
    /// <param name="point">Cursor position in physical pixels.</param>
    /// <param name="cancellationToken">Cancels the zoom.</param>
    /// <returns>What was done, for logging and diagnostics.</returns>
    public async Task<ZoomAction> HandleTriggerAsync(TargetInfo target, ScreenPoint point, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);

        _state.Prune(_windows.IsWindowAlive);

        if (_state.TryTake(target, out var previousKind, out var restoreState))
        {
            if (_adapters.TryGetValue(previousKind, out var previous))
            {
                await previous.ZoomOutAsync(target, restoreState, cancellationToken).ConfigureAwait(false);
                LogZoomedOut(target.ProcessName, previousKind);
                return ZoomAction.ZoomedOut;
            }
        }

        var kind = _router.Resolve(target.ProcessName);
        if (kind == AdapterKind.None || !_adapters.TryGetValue(kind, out var adapter))
        {
            return ZoomAction.Ignored;
        }

        var result = await adapter.ZoomInAsync(target, point, cancellationToken).ConfigureAwait(false);

        if (result.Status == ZoomInStatus.Unhandled
            && _fallbackToCtrlWheel
            && kind != AdapterKind.CtrlWheel
            && _adapters.TryGetValue(AdapterKind.CtrlWheel, out var fallback))
        {
            LogFallingBack(target.ProcessName, kind);
            kind = AdapterKind.CtrlWheel;
            result = await fallback.ZoomInAsync(target, point, cancellationToken).ConfigureAwait(false);
        }

        switch (result.Status)
        {
            case ZoomInStatus.Applied:
                _state.Save(target, kind, result.RestoreState!);
                LogZoomedIn(target.ProcessName, kind);
                return ZoomAction.ZoomedIn;

            case ZoomInStatus.SelfManaged:
                LogHandled(target.ProcessName, kind);
                return ZoomAction.ZoomedIn;

            default:
                LogUnhandled(target.ProcessName, kind);
                return ZoomAction.Unhandled;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Zoomed in {Process} via {Adapter}.")]
    private partial void LogZoomedIn(string? process, AdapterKind adapter);

    [LoggerMessage(Level = LogLevel.Information, Message = "Zoomed out {Process} via {Adapter}.")]
    private partial void LogZoomedOut(string? process, AdapterKind adapter);

    [LoggerMessage(Level = LogLevel.Information, Message = "{Adapter} handled the trigger in {Process} with nothing to undo (see its own log line for what it did).")]
    private partial void LogHandled(string? process, AdapterKind adapter);

    [LoggerMessage(Level = LogLevel.Debug, Message = "{Adapter} could not handle {Process}; falling back to CtrlWheel.")]
    private partial void LogFallingBack(string? process, AdapterKind adapter);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Adapter} could not zoom {Process}.")]
    private partial void LogUnhandled(string? process, AdapterKind adapter);
}

/// <summary>What <see cref="ZoomCoordinator.HandleTriggerAsync"/> did.</summary>
public enum ZoomAction
{
    /// <summary>No adapter is configured for the target process.</summary>
    Ignored,

    /// <summary>The target was zoomed in.</summary>
    ZoomedIn,

    /// <summary>A previous zoom on the target was undone.</summary>
    ZoomedOut,

    /// <summary>An adapter was selected but could not act.</summary>
    Unhandled,
}
