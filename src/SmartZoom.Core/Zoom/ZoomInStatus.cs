namespace SmartZoom.Core.Zoom;

/// <summary>Status of a zoom-in attempt.</summary>
public enum ZoomInStatus
{
    /// <summary>Nothing happened; the adapter cannot handle this target right now.</summary>
    Unhandled,

    /// <summary>Zoomed; the coordinator stores the restore state for the next trigger.</summary>
    Applied,

    /// <summary>
    /// Dealt with; there is nothing to undo and nothing else should be tried. Either the adapter keeps its
    /// own toggle state and will be invoked again to come back, or it decided that doing nothing was the
    /// right answer and a cruder fallback would be worse.
    /// </summary>
    Handled,
}
