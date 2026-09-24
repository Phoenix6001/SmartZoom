namespace SmartZoom.Core.Settings;

/// <summary>Tuning that only browsers need; the rest is under <see cref="ZoomSettings.Smart"/>.</summary>
public sealed class BrowserZoomSettings
{
    /// <summary>
    /// Minimum distance between the gesture's anchor and the window edges, in pixels. Normally not needed:
    /// the gesture orients its contacts to stay inside the window on its own.
    /// </summary>
    public int AnchorInsetPx { get; set; }

    /// <summary>
    /// What to do on a page that blocks the pinch (<c>touch-action: none</c>, common in dialogs and drag-and-drop
    /// UIs such as Jira's): fall back to Ctrl+wheel page zoom when true, or report the refusal and leave the page
    /// alone when false. Page zoom is coarser and applies per site across every window, but it is the only zoom
    /// such a page allows. Takes effect only when <see cref="ZoomSettings.FallbackToCtrlWheel"/> is also on.
    /// </summary>
    public bool CtrlWheelWhenPinchBlocked { get; set; } = true;
}
