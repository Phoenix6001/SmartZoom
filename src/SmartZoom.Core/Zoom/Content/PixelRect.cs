using SmartZoom.Core.Input;

namespace SmartZoom.Core.Zoom.Content;

/// <summary>An axis-aligned rectangle in physical screen pixels.</summary>
/// <param name="Left">Left edge, inclusive.</param>
/// <param name="Top">Top edge, inclusive.</param>
/// <param name="Right">Right edge, exclusive.</param>
/// <param name="Bottom">Bottom edge, exclusive.</param>
public readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    /// <summary>Width in pixels; zero or negative for an empty rectangle.</summary>
    public int Width => Right - Left;

    /// <summary>Height in pixels; zero or negative for an empty rectangle.</summary>
    public int Height => Bottom - Top;

    /// <summary>Horizontal centre.</summary>
    public double CenterX => (Left + Right) / 2.0;

    /// <summary>Vertical centre.</summary>
    public double CenterY => (Top + Bottom) / 2.0;

    /// <summary>True when the rectangle has no area: a zero or negative width or height.</summary>
    public bool IsEmpty => Width <= 0 || Height <= 0;

    /// <summary>Whether the point lies inside (edges: left/top inclusive, right/bottom exclusive).</summary>
    public bool Contains(ScreenPoint point) => point.X >= Left && point.X < Right && point.Y >= Top && point.Y < Bottom;

    /// <summary>Creates a rectangle from an origin and size.</summary>
    public static PixelRect FromSize(int left, int top, int width, int height) => new(left, top, left + width, top + height);

    /// <summary>The overlap of this rectangle and another; empty (see <see cref="IsEmpty"/>) when they do not meet.</summary>
    /// <param name="other">The rectangle to clip to.</param>
    public PixelRect Intersect(PixelRect other) => new(
        Math.Max(Left, other.Left),
        Math.Max(Top, other.Top),
        Math.Min(Right, other.Right),
        Math.Min(Bottom, other.Bottom));

    /// <summary>
    /// Clamps a coordinate into <c>[min + inset, max - inset]</c>, rounded to a whole pixel. A range narrower than
    /// two insets (a tiny viewport) has no such interval, and yields its middle instead.
    /// </summary>
    /// <param name="value">The coordinate to clamp.</param>
    /// <param name="min">The lowest coordinate in the range, inclusive.</param>
    /// <param name="max">The highest coordinate in the range, inclusive.</param>
    /// <param name="inset">How far inside each end the result must stay.</param>
    internal static int ClampWithInset(double value, int min, int max, double inset)
    {
        var low = min + inset;
        var high = max - inset;
        return (int)Math.Round(low > high ? (min + max) / 2.0 : Math.Clamp(value, low, high));
    }
}
