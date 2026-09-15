using SmartZoom.Core.Input;

namespace SmartZoom.Core.Zoom;

/// <summary>Performs a two-finger pinch gesture on the window under a screen point.</summary>
/// <remarks>Browsers turn a touch pinch into visual-viewport zoom: the rendered page is scaled without
/// re-layout, and scaling back below 1.0 clamps to exactly the original view.</remarks>
public interface IPinchInjector
{
    /// <summary>
    /// How far, in pixels, a contact travels from the anchor along the horizontal axis when pinching by
    /// <paramref name="factor"/>. Both contacts must stay inside the target window, so callers keep the
    /// anchor at least this far (plus any scrollbar) from the window's left and right edges.
    /// </summary>
    /// <param name="factor">Zoom factor the gesture will apply; the spread grows with it.</param>
    int MaxContactOffset(double factor);

    /// <summary>Pinches around <paramref name="anchor"/> so the content scales by <paramref name="factor"/>.</summary>
    /// <param name="anchor">Point that stays fixed, physical pixels. Must lie inside the target window with room for the contacts.</param>
    /// <param name="factor">Greater than 1 zooms in, less than 1 zooms out.</param>
    /// <param name="duration">Gesture length; longer looks smoother. Zero performs the minimum two frames.</param>
    /// <param name="cancellationToken">Cancels the gesture; contacts are always lifted.</param>
    /// <returns>False if the OS rejected the injection (e.g. the target window is elevated).</returns>
    Task<bool> PinchAsync(ScreenPoint anchor, double factor, TimeSpan duration, CancellationToken cancellationToken);
}
