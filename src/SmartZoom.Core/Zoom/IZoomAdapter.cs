using SmartZoom.Core.Input;
using SmartZoom.Core.Routing;

namespace SmartZoom.Core.Zoom;

/// <summary>A strategy for zooming one family of applications.</summary>
/// <remarks>
/// Adapters are stateless with respect to windows: whatever they need to undo a zoom is returned in
/// <see cref="ZoomInResult.RestoreState"/> and handed back by <see cref="ZoomCoordinator"/> on the
/// next trigger for the same window. Adapters that keep their own toggle state (the browser
/// extension) return <see cref="ZoomInResult.SelfManaged"/> and are simply invoked again to undo.
/// </remarks>
public interface IZoomAdapter
{
    /// <summary>Which routing category this adapter serves.</summary>
    AdapterKind Kind { get; }

    /// <summary>Zooms the target in, centered on the cursor.</summary>
    /// <param name="target">Window under the cursor.</param>
    /// <param name="point">Cursor position in physical pixels.</param>
    /// <param name="cancellationToken">Cancels a zoom in progress; adapters must still leave the system in a sane state (no stuck modifier keys).</param>
    Task<ZoomInResult> ZoomInAsync(TargetInfo target, ScreenPoint point, CancellationToken cancellationToken);

    /// <summary>Undoes a zoom previously applied by this adapter.</summary>
    /// <param name="target">Window that was zoomed. It is still alive when this is called.</param>
    /// <param name="restoreState">The state this adapter returned from <see cref="ZoomInAsync"/>.</param>
    /// <param name="cancellationToken">Cancels the restore.</param>
    Task ZoomOutAsync(TargetInfo target, object restoreState, CancellationToken cancellationToken);
}

/// <summary>Outcome of <see cref="IZoomAdapter.ZoomInAsync"/>.</summary>
public sealed class ZoomInResult
{
    private ZoomInResult(ZoomInStatus status, object? restoreState)
    {
        Status = status;
        RestoreState = restoreState;
    }

    /// <summary>The adapter could not act on this target (e.g. its browser extension isn't connected). The coordinator may fall back.</summary>
    public static ZoomInResult Unhandled { get; } = new(ZoomInStatus.Unhandled, null);

    /// <summary>The zoom was applied and the adapter tracks its own toggle state.</summary>
    public static ZoomInResult SelfManaged { get; } = new(ZoomInStatus.SelfManaged, null);

    /// <summary>What happened.</summary>
    public ZoomInStatus Status { get; }

    /// <summary>Opaque data needed to undo the zoom; non-null only when <see cref="Status"/> is <see cref="ZoomInStatus.Applied"/>.</summary>
    public object? RestoreState { get; }

    /// <summary>The zoom was applied; <paramref name="restoreState"/> will be passed to <see cref="IZoomAdapter.ZoomOutAsync"/> to undo it.</summary>
    /// <param name="restoreState">Adapter-specific undo data.</param>
    public static ZoomInResult Applied(object restoreState)
    {
        ArgumentNullException.ThrowIfNull(restoreState);
        return new ZoomInResult(ZoomInStatus.Applied, restoreState);
    }
}

/// <summary>Status of a zoom-in attempt.</summary>
public enum ZoomInStatus
{
    /// <summary>Nothing happened; the adapter cannot handle this target right now.</summary>
    Unhandled,

    /// <summary>Zoomed; the coordinator stores the restore state for the next trigger.</summary>
    Applied,

    /// <summary>Zoomed; the adapter itself toggles back when invoked again.</summary>
    SelfManaged,
}
