using SmartZoom.Core.Input;

namespace SmartZoom.Core.Zoom.Content;

/// <summary>How to zoom: scale the view by <see cref="Scale"/> around <see cref="Anchor"/>, which stays fixed on screen.</summary>
/// <param name="Scale">Zoom factor, greater than 1.</param>
/// <param name="Anchor">Screen point that does not move during the zoom, in physical pixels.</param>
public readonly record struct ZoomPlan(double Scale, ScreenPoint Anchor);

/// <summary>
/// Minimum distance from the anchor to the viewport edges, per axis. A touch pinch places its two
/// contacts on a horizontal line through the anchor, so <see cref="X"/> must cover the contact spread
/// (plus the browser's scrollbar), while <see cref="Y"/> only needs to keep the contacts off the edge.
/// </summary>
/// <param name="X">Horizontal keep-out, pixels.</param>
/// <param name="Y">Vertical keep-out, pixels.</param>
public readonly record struct AnchorInsets(int X, int Y);

/// <summary>
/// Computes the scale and anchor that make a block fill the viewport width, the way a pinch
/// gesture would: every point P maps to <c>A + (P - A) * scale</c> for anchor A.
/// </summary>
/// <param name="MinScale">Smallest zoom worth doing; below it the trigger is a no-op.</param>
/// <param name="MaxScale">Largest zoom; keeps tiny blocks from becoming absurd.</param>
/// <param name="Margin">Breathing room, in pixels, between the block and the viewport edges after zooming.</param>
public sealed record SmartZoomPlanner(double MinScale = 1.1, double MaxScale = 3.0, int Margin = 16)
{
    /// <summary>Plans a zoom that fits <paramref name="block"/> into <paramref name="viewport"/>.</summary>
    /// <param name="block">Bounds to fit, physical pixels.</param>
    /// <param name="viewport">Visible area, physical pixels.</param>
    /// <param name="cursor">Cursor position; keeps the pointed-at line in view when the block is taller than the viewport.</param>
    /// <param name="insets">Keep-out from the viewport edges for the anchor. An anchor that has to be clamped no longer maps the block exactly.</param>
    /// <returns>The plan, or null when the block already fills the viewport (scale would be below <see cref="MinScale"/>).</returns>
    public ZoomPlan? Plan(PixelRect block, PixelRect viewport, ScreenPoint cursor, AnchorInsets insets = default)
    {
        if (block.IsEmpty || viewport.IsEmpty)
            return null;

        var scale = Math.Min((double)viewport.Width / (block.Width + (2 * Margin)), MaxScale);
        if (scale < MinScale)
            return null;

        // Horizontal: after scaling around A, the block's left edge should land on the viewport's left margin.
        // Solve  A + (L - A) * s = vL + m  for A.
        var ax = Solve(target: viewport.Left + Margin, source: block.Left, scale);

        // Vertical: centre the block if it fits after scaling; otherwise keep the pointed-at line where it is,
        // so the reader doesn't lose their place.
        var ay = block.Height * scale <= viewport.Height - (2 * Margin)
            ? Solve(target: viewport.CenterY, source: block.CenterY, scale)
            : Math.Clamp(cursor.Y, block.Top, block.Bottom - 1);

        var anchor = new ScreenPoint(
            (int)Math.Round(Clamp(ax, viewport.Left, viewport.Right - 1, insets.X)),
            (int)Math.Round(Clamp(ay, viewport.Top, viewport.Bottom - 1, insets.Y)));

        return new ZoomPlan(scale, anchor);
    }

    /// <summary>The anchor A for which <c>A + (source - A) * scale == target</c>.</summary>
    private static double Solve(double target, double source, double scale) =>
        (target - (source * scale)) / (1 - scale);

    // Clamp into [min + inset, max - inset]; if the inset is wider than the range, use the middle.
    private static double Clamp(double value, int min, int max, int inset)
    {
        var low = min + inset;
        var high = max - inset;
        return low > high ? (min + max) / 2.0 : Math.Clamp(value, low, high);
    }
}
