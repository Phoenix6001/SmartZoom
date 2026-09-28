using SmartZoom.Core.Input;

namespace SmartZoom.Core.Zoom.Content;

/// <summary>
/// Computes the scale and anchor that make a block fill the viewport width, the way a pinch
/// gesture would: every point P maps to <c>A + (P - A) * scale</c> for anchor A.
/// </summary>
/// <remarks>
/// The zoomed view is always a window onto the area that is visible <em>now</em>: after scaling, the
/// screen shows a <c>width/scale × height/scale</c> region of the current viewport. The plan places
/// that region so the block sits at the left margin and is vertically centred, then clamps it inside
/// the viewport. Asking for anything outside would make the browser scroll the page itself, which is
/// not undone by zooming back out.
/// </remarks>
/// <param name="MinScale">Smallest fit worth doing; below it there is nothing to fit to and <see cref="SmartZoomPlanner.Magnify"/> is the answer.</param>
/// <param name="MaxScale">Largest zoom; keeps tiny blocks from becoming absurd.</param>
/// <param name="Margin">Breathing room, in pixels, between the block and the viewport edges after zooming.</param>
public sealed record SmartZoomPlanner(double MinScale = 1.1, double MaxScale = 3.0, int Margin = 16)
{
    /// <summary>Plans a zoom that fits <paramref name="block"/> into <paramref name="viewport"/>.</summary>
    /// <param name="block">Bounds to fit, physical pixels.</param>
    /// <param name="viewport">Visible area, physical pixels.</param>
    /// <param name="cursor">Cursor position; keeps the pointed-at line in view when the block is taller than the zoomed view.</param>
    /// <param name="insets">Extra keep-out from the viewport edges for the anchor.</param>
    /// <returns>The plan, or null when the block already fills the viewport (scale would be below <see cref="MinScale"/>).</returns>
    public ZoomPlan? Plan(PixelRect block, PixelRect viewport, ScreenPoint cursor, AnchorInsets insets = default)
    {
        if (block.IsEmpty || viewport.IsEmpty)
            return null;

        var scale = Math.Min((double)viewport.Width / (block.Width + (2 * Margin)), MaxScale);
        if (scale < MinScale)
            return null;

        // The region of the current viewport that will fill the screen after zooming, in viewport-relative pixels.
        var regionWidth = viewport.Width / scale;
        var regionHeight = viewport.Height / scale;
        var margin = Margin / scale;

        // Horizontal: centre the block. For a block that fills the width this is its left edge on the left
        // margin; a narrow one (a picture at the scale cap) stays where it is and grows in place, instead of
        // sliding to the margin, which reads as scrolling rather than zooming.
        var regionLeft = block.CenterX - viewport.Left - (regionWidth / 2);

        // Vertical: centre the block if it fits; otherwise keep the pointed-at line where it is.
        var blockCenterY = block.CenterY - viewport.Top;
        var regionTop = block.Height <= regionHeight - (2 * margin)
            ? blockCenterY - (regionHeight / 2)
            : (cursor.Y - viewport.Top) * (1 - (1 / scale));

        // Never look outside what is visible now.
        regionLeft = Math.Clamp(regionLeft, 0, Math.Max(0, viewport.Width - regionWidth));
        regionTop = Math.Clamp(regionTop, 0, Math.Max(0, viewport.Height - regionHeight));

        // The anchor A that maps the region's top-left corner to the viewport's top-left: A + (R - A) * s = 0.
        var anchor = new ScreenPoint(
            PixelRect.ClampWithInset(viewport.Left + (regionLeft * scale / (scale - 1)), viewport.Left, viewport.Right - 1, insets.X),
            PixelRect.ClampWithInset(viewport.Top + (regionTop * scale / (scale - 1)), viewport.Top, viewport.Bottom - 1, insets.Y));

        return new ZoomPlan(scale, anchor);
    }

    /// <summary>Zooms by <see cref="MaxScale"/> around the cursor.</summary>
    /// <param name="viewport">Visible area, physical pixels.</param>
    /// <param name="cursor">Cursor position; the one point the zoom leaves where it is.</param>
    /// <param name="insets">Extra keep-out from the viewport edges for the anchor.</param>
    /// <returns>The plan, or null when there is no viewport to zoom.</returns>
    /// <remarks>
    /// <para>
    /// One amount, wherever the press lands. Scaling the thing under the cursor to fill the width instead
    /// sounds like the smarter rule and is not: it makes how much a press zooms depend on how wide the thing
    /// you happened to point at is. A narrow image grew threefold and a paragraph most of the width of the
    /// window grew by a twelfth, which reads as the zoom being broken in places rather than as a rule.
    /// </para>
    /// <para>
    /// Anchoring on the cursor is what makes it predictable: the pixel under the pointer is the one the zoom
    /// holds still. The zoomed view is then a <c>1/MaxScale</c>-sized window around a point that is itself
    /// inside the viewport, so it can never fall outside — the page is never asked to scroll, and zooming
    /// back out is exact.
    /// </para>
    /// </remarks>
    public ZoomPlan? Magnify(PixelRect viewport, ScreenPoint cursor, AnchorInsets insets = default)
    {
        if (viewport.IsEmpty || MaxScale <= 1)
            return null;

        return new ZoomPlan(
            MaxScale,
            new ScreenPoint(
                PixelRect.ClampWithInset(cursor.X, viewport.Left, viewport.Right - 1, insets.X),
                PixelRect.ClampWithInset(cursor.Y, viewport.Top, viewport.Bottom - 1, insets.Y)));
    }
}
