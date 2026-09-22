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
}
