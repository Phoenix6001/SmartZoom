namespace SmartZoom.Core.Zoom;

/// <summary>What <see cref="ZoomCoordinator.HandleTriggerAsync"/> did.</summary>
public enum ZoomAction
{
    /// <summary>No adapter is configured for the target process.</summary>
    Ignored,

    /// <summary>The target was zoomed in.</summary>
    ZoomedIn,

    /// <summary>A previous zoom on the target was undone.</summary>
    ZoomedOut,

    /// <summary>An adapter dealt with the trigger and deliberately changed nothing.</summary>
    Handled,

    /// <summary>An adapter was selected but could not act.</summary>
    Unhandled,
}
