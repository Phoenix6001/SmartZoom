namespace SmartZoom.Core.Zoom;

/// <summary>Outcome of <see cref="IZoomAdapter.ZoomInAsync"/>.</summary>
public sealed class ZoomInResult
{
    private ZoomInResult(ZoomInStatus status, object? restoreState)
    {
        Status = status;
        RestoreState = restoreState;
    }

    /// <summary>The adapter could not act on this target. The coordinator may fall back to Ctrl+wheel.</summary>
    public static ZoomInResult Unhandled { get; } = new(ZoomInStatus.Unhandled, null);

    /// <summary>Dealt with, with nothing for the coordinator to undo and no fallback to try.</summary>
    public static ZoomInResult Handled { get; } = new(ZoomInStatus.Handled, null);

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
