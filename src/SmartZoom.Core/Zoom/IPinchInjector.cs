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

    /// <summary>
    /// Undoes a zoom made with <see cref="PinchAsync"/> around the same anchor: pinches by
    /// <paramref name="factor"/> (below 1), knowing the page is zoomed by <paramref name="zoomedScale"/>.
    /// </summary>
    /// <remarks>
    /// The scale lets an injector put the page back where it was when the contacts cannot be centred on the
    /// anchor (see <see cref="Gesture.PinchGeometry.UndoPan"/>). Without it, this is <see cref="PinchAsync"/>.
    /// </remarks>
    /// <param name="anchor">The anchor the zoom-in used.</param>
    /// <param name="factor">Less than 1.</param>
    /// <param name="zoomedScale">The scale the zoom-in reached, as planned.</param>
    /// <param name="duration">Gesture length.</param>
    /// <param name="bounds">Area the contacts must stay inside; the one the zoom-in used.</param>
    /// <param name="cancellationToken">Cancels the gesture; contacts are always lifted.</param>
    /// <returns>False if the OS rejected the injection.</returns>
    Task<bool> PinchOutAsync(ScreenPoint anchor, double factor, double zoomedScale, TimeSpan duration, PixelRect bounds, CancellationToken cancellationToken) =>
        PinchAsync(anchor, factor, duration, bounds, cancellationToken);
}
