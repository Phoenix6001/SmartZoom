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
    /// <param name="reason">Why nothing was zoomed; recorded by diagnostics and shown in the tray.</param>
    /// <param name="detail">
    /// A privacy-safe description of what was under the cursor, or null. Never a title, a URL or a
    /// coordinate — see <see cref="Content.ContentPath.Shape"/>, the only intended source of this value.
    /// </param>
    public static ZoomInResult Handled(ZoomReason reason, string? detail = null) =>
        new(ZoomInStatus.Handled, null) { Reason = reason, Detail = detail };

    /// <summary>What happened.</summary>
    public ZoomInStatus Status { get; }

    /// <summary>Opaque data needed to undo the zoom; non-null only when <see cref="Status"/> is <see cref="ZoomInStatus.Applied"/>.</summary>
    public object? RestoreState { get; }

    /// <summary>Why nothing happened, when <see cref="Status"/> is <see cref="ZoomInStatus.Handled"/>.</summary>
    public ZoomReason? Reason { get; private init; }

    /// <summary>A privacy-safe description of what was under the cursor, when one is available.</summary>
    public string? Detail { get; private init; }

    /// <summary>The zoom was applied; <paramref name="restoreState"/> will be passed to <see cref="IZoomAdapter.ZoomOutAsync"/> to undo it.</summary>
    /// <param name="restoreState">Adapter-specific undo data.</param>
    public static ZoomInResult Applied(object restoreState)
    {
        ArgumentNullException.ThrowIfNull(restoreState);
        return new ZoomInResult(ZoomInStatus.Applied, restoreState);
    }
}
