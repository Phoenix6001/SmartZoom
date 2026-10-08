using SmartZoom.Core.Zoom.Content;
using SmartZoom.Core.Zoom.Gesture;

namespace SmartZoom.Core.Zoom;

/// <summary>
/// How far synthetic touch contacts keep from the edges of a browser's viewport, in physical pixels on the
/// display the press is on.
/// </summary>
/// <remarks>
/// Both distances are things Windows and the browser draw in device-independent pixels, so they are held as
/// DIPs and scaled by the display: a 100% scrollbar is half the width of a 200% one, and fixed pixel values
/// measured at one scale keep the fingers twice as far out as they need to be at the other, or not far
/// enough at 250% and 300%.
/// </remarks>
/// <param name="Scrollbar">
/// The strip a contact may not land in on the right and at the bottom, where a page's scrollbars are.
/// </param>
/// <param name="ResizeBorder">How far inside the viewport contacts stay on the left and at the top.</param>
public readonly record struct EdgeAllowances(int Scrollbar, int ResizeBorder)
{
    /// <summary>
    /// The scrollbar strip, in DIPs. A contact that lands on a scrollbar drags it instead of pinching; a classic
    /// scrollbar is 17 DIPs wide, and 56 px on a 200% display covered its 34 px with room to spare. It applies at
    /// the bottom as well as on the right: a page that scrolls sideways has a horizontal scrollbar there, which
    /// the resize-border allowance alone (12 DIPs) does not cover.
    /// </summary>
    public const double ScrollbarDips = 28;

    /// <summary>
    /// The window's touch resize zone, in DIPs. Windows gives touch a generous grip on a window's resize
    /// borders: on a 200% display a contact 8 px inside the window rect resized the window, one 12 px inside did
    /// not, and a converging zoom-out then dragged the window edge about 150 px inward. 24 px was the allowance
    /// measured there.
    /// </summary>
    public const double ResizeBorderDips = 12;

    /// <summary>
    /// How far inside the viewport the pinch's anchor stays from the left and right: the resize-border allowance
    /// plus the injector's own edge margin. On the right that is inside the scrollbar strip on purpose: an anchor
    /// there is pinched around a focus moved along the row plus a sideways pan, which keeps a block at the right
    /// edge in view.
    /// </summary>
    /// <remarks>
    /// A pinch around an anchor outside the contact area is done around a substitute focus plus a one-finger
    /// pan, and a pan that pushes the content past the layout viewport's edge scrolls the page, which zooming
    /// back out does not undo (an 8 px residual scroll was measured). A slightly inset anchor is free: the
    /// browser clamps the visual viewport at the page edge, so the block lands a few pixels further in.
    /// </remarks>
    public int AnchorInset => ResizeBorder + PinchGeometry.EdgeMargin;

    /// <summary>
    /// How far inside the viewport the pinch's anchor stays from the top and bottom: inside the contact area,
    /// which reaches only to the scrollbar strip at the bottom, plus the injector's edge margin.
    /// </summary>
    /// <remarks>
    /// Unlike on the right, an anchor below the contact area would be pinched around a focus moved up the
    /// column plus a vertical pan, and a vertical pan leaks into the page's scroll. One inset serves both the top
    /// and the bottom, so the top keeps the same distance.
    /// </remarks>
    public int AnchorInsetVertical => Scrollbar + PinchGeometry.EdgeMargin;

    /// <summary>The allowances on a display at this scale.</summary>
    /// <param name="displayScale">
    /// The display's scale factor: 1.0 at 100%, 2.0 at 200%. Anything that is not a positive number is taken as
    /// 1.0, the scale a display reports when it reports nothing.
    /// </param>
    public static EdgeAllowances For(double displayScale)
    {
        var scale = double.IsFinite(displayScale) && displayScale > 0 ? displayScale : 1.0;

        // Rounded up: a fraction of a pixel short of the allowance is a contact on the scrollbar.
        return new EdgeAllowances(Pixels(ScrollbarDips, scale), Pixels(ResizeBorderDips, scale));
    }

    /// <summary>
    /// Where synthetic contacts may land: the viewport minus the scrollbar strip on the right and at the bottom,
    /// and minus the window's touch resize zone on the left and at the top (the scrollbar strip already covers
    /// it on the other two sides).
    /// </summary>
    /// <param name="viewport">The page's viewport, in physical pixels.</param>
    /// <returns>Never narrower or shorter than one pixel, however small the viewport.</returns>
    public PixelRect ContactBounds(PixelRect viewport)
    {
        var left = viewport.Left + ResizeBorder;
        var top = viewport.Top + ResizeBorder;
        return new PixelRect(
            left,
            top,
            Math.Max(left + 1, viewport.Right - Scrollbar),
            Math.Max(top + 1, viewport.Bottom - Scrollbar));
    }

    // The epsilon keeps an exact product (28 x 2.0) from rounding up a pixel on floating-point noise.
    private static int Pixels(double dips, double scale) => (int)Math.Ceiling((dips * scale) - 1e-9);
}
