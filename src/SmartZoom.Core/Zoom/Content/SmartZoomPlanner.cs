using SmartZoom.Core.Input;

namespace SmartZoom.Core.Zoom.Content;

/// <summary>How to zoom: scale the view by <see cref="Scale"/> around <see cref="Anchor"/>, which stays fixed on screen.</summary>
/// <param name="Scale">Zoom factor, greater than 1.</param>
/// <param name="Anchor">Screen point that does not move during the zoom, in physical pixels.</param>
public readonly record struct ZoomPlan(double Scale, ScreenPoint Anchor);

/// <summary>
/// Minimum distance from the anchor to the viewport edges, per axis. Kept for callers that need the anchor
/// away from the very edge; the plan itself already keeps the zoomed view inside the viewport.
/// </summary>
/// <param name="X">Horizontal keep-out, pixels.</param>
/// <param name="Y">Vertical keep-out, pixels.</param>
public readonly record struct AnchorInsets(int X, int Y);

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
/// <param name="MinScale">Smallest zoom worth doing; below it the trigger is a no-op.</param>
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

        // Horizontal: block's left edge on the left margin.
        var regionLeft = block.Left - viewport.Left - margin;

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
            Clamp(viewport.Left + (regionLeft * scale / (scale - 1)), viewport.Left, viewport.Right - 1, insets.X),
            Clamp(viewport.Top + (regionTop * scale / (scale - 1)), viewport.Top, viewport.Bottom - 1, insets.Y));

        return new ZoomPlan(scale, anchor);
    }

    // Clamp into [min + inset, max - inset]; if the inset is wider than the range, use the middle.
    private static int Clamp(double value, int min, int max, int inset)
    {
        var low = min + inset;
        var high = max - inset;
        return (int)Math.Round(low > high ? (min + max) / 2.0 : Math.Clamp(value, low, high));
    }
}
