using SmartZoom.Core.Input;
using SmartZoom.Core.Zoom.Content;

namespace SmartZoom.Core.Zoom;

/// <summary>Performs a two-finger pinch gesture on the window under a screen point.</summary>
/// <remarks>Browsers turn a touch pinch into visual-viewport zoom: the rendered page is scaled without
/// re-layout, and scaling back below 1.0 clamps to exactly the original view.</remarks>
public interface IPinchInjector
{
    /// <summary>Pinches around <paramref name="anchor"/> so the content scales by <paramref name="factor"/>.</summary>
    /// <param name="anchor">Point that stays fixed, physical pixels.</param>
    /// <param name="factor">Greater than 1 zooms in, less than 1 zooms out.</param>
    /// <param name="duration">Gesture length; longer looks smoother. Zero performs the minimum two frames.</param>
    /// <param name="bounds">
    /// Area the contacts must stay inside (the target's content area, excluding scrollbars). The injector
    /// orients the contacts horizontally or vertically around the anchor to satisfy it; if neither fits it
    /// uses the axis with more room.
    /// </param>
    /// <param name="cancellationToken">Cancels the gesture; contacts are always lifted.</param>
    /// <returns>False if the OS rejected the injection (e.g. the target window is elevated).</returns>
    Task<bool> PinchAsync(ScreenPoint anchor, double factor, TimeSpan duration, PixelRect bounds, CancellationToken cancellationToken);
}
