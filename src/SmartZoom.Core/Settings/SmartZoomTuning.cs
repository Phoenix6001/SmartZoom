namespace SmartZoom.Core.Settings;

/// <summary>Tuning shared by every element-aware zoom: browsers, Word and Excel alike.</summary>
/// <remarks>
/// These used to live under "Browser", which meant that tuning a browser silently retuned Word and Excel
/// too, and that the Office adapters read a section named after something else.
/// </remarks>
public sealed class SmartZoomTuning
{
    /// <summary>Space left between the zoomed block and the edges of the window, in pixels.</summary>
    public int MarginPx { get; set; } = 16;

    /// <summary>Length of the zoom animation when <see cref="ZoomSettings.Animate"/> is on.</summary>
    public int AnimationMs { get; set; } = 280;
}
