namespace SmartZoom.Core.Settings;

/// <summary>Tuning that only browsers need; the rest is under <see cref="ZoomSettings.Smart"/>.</summary>
public sealed class BrowserZoomSettings
{
    /// <summary>
    /// Minimum distance between the gesture's anchor and the window edges, in pixels. Normally not needed:
    /// the gesture orients its contacts to stay inside the window on its own.
    /// </summary>
    public int AnchorInsetPx { get; set; }
}
