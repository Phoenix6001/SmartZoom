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

    /// <summary>True when the rectangle has positive area.</summary>
    public bool IsEmpty => Width <= 0 || Height <= 0;

    /// <summary>Whether the point lies inside (edges: left/top inclusive, right/bottom exclusive).</summary>
    public bool Contains(ScreenPoint point) => point.X >= Left && point.X < Right && point.Y >= Top && point.Y < Bottom;

    /// <summary>Creates a rectangle from an origin and size.</summary>
    public static PixelRect FromSize(int left, int top, int width, int height) => new(left, top, left + width, top + height);
}
