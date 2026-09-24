namespace SmartZoom.Core.Settings;

/// <summary>Tuning shared by every element-aware zoom: browsers, Word and Excel alike.</summary>
/// <remarks>
/// One section for all of them: a change here retunes browsers, Word and Excel alike and says so by its name,
/// and no adapter reads a section named after a different application.
/// </remarks>
public sealed class SmartZoomTuning
{
    /// <summary>Space left between the zoomed block and the edges of the window, in pixels.</summary>
    public int MarginPx { get; set; } = 16;

    /// <summary>Length of the zoom animation when <see cref="ZoomSettings.Animate"/> is on.</summary>
    public int AnimationMs { get; set; } = 280;
}
