using SmartZoom.Core.Input;
using SmartZoom.Core.Routing;

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

/// <summary>Coarse semantic role of a content node, normalized across accessibility APIs.</summary>
public enum ContentRole
{
    /// <summary>Anything not listed below (buttons, inputs, unknown).</summary>
    Other = 0,

    /// <summary>The page or document root; its bounds are the viewport.</summary>
    Document,

    /// <summary>Generic container: paragraph, section, div, article.</summary>
    Group,

    /// <summary>A run of text.</summary>
    Text,

    /// <summary>A hyperlink.</summary>
    Link,

    /// <summary>An image or figure.</summary>
    Image,

    /// <summary>A table.</summary>
    Table,

    /// <summary>A list.</summary>
    List,

    /// <summary>One item of a list.</summary>
    ListItem,

    /// <summary>A heading.</summary>
    Heading,
}

/// <summary>One node on the path from the element under the cursor up to the document.</summary>
/// <param name="Role">Normalized role.</param>
/// <param name="Bounds">Bounds in physical screen pixels (may extend beyond the viewport).</param>
public sealed record ContentNode(ContentRole Role, PixelRect Bounds);

/// <summary>Result of hit-testing a point in an application's content.</summary>
/// <param name="Chain">Ancestor chain, leaf first, ending with the <see cref="ContentRole.Document"/> node.</param>
/// <param name="Viewport">The visible content area in physical screen pixels.</param>
public sealed record ContentHit(IReadOnlyList<ContentNode> Chain, PixelRect Viewport);

/// <summary>Finds the content element under a screen point, with its ancestors, in a supported application.</summary>
public interface IContentHitTester
{
    /// <summary>Hit-tests the target's content.</summary>
    /// <param name="target">Window under the cursor.</param>
    /// <param name="point">Cursor position in physical pixels.</param>
    /// <param name="cancellationToken">Cancels a slow hit-test (e.g. while a page's accessibility tree is still being built).</param>
    /// <returns>The hit, or null if the target is not supported or exposes no content at that point.</returns>
    Task<ContentHit?> HitTestAsync(TargetInfo target, ScreenPoint point, CancellationToken cancellationToken);
}
