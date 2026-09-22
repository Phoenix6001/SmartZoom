namespace SmartZoom.Core.Zoom;

/// <summary>Why a trigger produced no zoom. Not every one of these is a defect.</summary>
public enum ZoomReason
{
    /// <summary>The application exposed no content under the cursor.</summary>
    NoContent,

    /// <summary>Content was found, but nothing on the path was a sensible thing to magnify.</summary>
    NoBlock,

    /// <summary>The block already fills the viewport, so there is nothing to zoom to.</summary>
    AlreadyFits,

    /// <summary>The gesture was refused by the injector.</summary>
    GestureRefused,

    /// <summary>The application's own automation interface refused the zoom or was unreachable.</summary>
    AutomationFailed,

    /// <summary>No adapter is configured for this application.</summary>
    NoAdapter,

    /// <summary>
    /// The strategy could not act on the application in the state it was in, and the Ctrl+wheel fallback did
    /// not act either (or was turned off).
    /// </summary>
    /// <remarks>
    /// Only the browser and the two Office adapters ever report a reason of their own. Without this value
    /// every no-op press in the Reader and Ctrl+wheel paths had no reason at all, which is precisely the
    /// "where does a press do nothing?" question the diagnostics record exists to answer.
    /// </remarks>
    AdapterCouldNotAct,
}
