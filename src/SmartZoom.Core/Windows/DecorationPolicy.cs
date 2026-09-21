using SmartZoom.Core.Input;
using SmartZoom.Core.Zoom.Content;

namespace SmartZoom.Core.Windows;

/// <summary>
/// The rules for looking past a cursor decoration: a window too small to hold content, kept above everything
/// else, that is part of the pointer rather than a thing to zoom.
/// </summary>
/// <remarks>
/// Acrobat parks a 5x5 layered window under the pointer once it has seen touch input. Without this it becomes
/// the target of the next trigger and hides the document underneath, and the press does nothing useful.
///
/// The way past it is to walk the z-order, not to hide it: it belongs to another application, and a window
/// this one hides and shows again is a window whose owner may be mid-paint, mid-animation, or watching for
/// exactly that. The walk only reads.
/// </remarks>
public static class DecorationPolicy
{
    /// <summary>Largest window that may be treated as a decoration rather than a target.</summary>
    public const int MaxDecorationSize = 8;

    /// <summary>Extended styles that mark a window as part of the pointer rather than a thing to look at.</summary>
    [Flags]
    public enum DecorationStyles
    {
        /// <summary>None of them.</summary>
        None = 0,

        /// <summary>WS_EX_TRANSPARENT.</summary>
        Transparent = 0x0000_0020,

        /// <summary>WS_EX_TOOLWINDOW.</summary>
        ToolWindow = 0x0000_0080,

        /// <summary>WS_EX_LAYERED.</summary>
        Layered = 0x0008_0000,
    }

    /// <summary>
    /// How many windows that are actually on the screen to look at before giving up on finding what the
    /// decoration is sitting on.
    /// </summary>
    /// <remarks>
    /// The budget counts visible windows because the top of a real desktop's z-order is a long run of hidden
    /// top-level windows. Measured here: 64 handles between the decoration and the reader it belonged to, of
    /// which only 5 were on the screen. Counting every handle gave up 48 windows early, and the decoration
    /// skip never once worked.
    /// </remarks>
    public const int MaxWindowsBehind = 16;

    /// <summary>An absolute bound on the walk, so a desktop with thousands of hidden windows cannot make a trigger slow.</summary>
    public const int MaxZOrderSteps = 512;

    /// <summary>How deep into the window behind to descend for the child an adapter needs.</summary>
    public const int MaxChildDepth = 8;

    /// <summary>Whether a window's size and styles make it a cursor decoration.</summary>
    /// <param name="bounds">The window's rectangle in screen pixels.</param>
    /// <param name="styles">Its extended styles, masked to the ones that matter.</param>
    public static bool IsDecoration(PixelRect bounds, DecorationStyles styles) =>
        bounds.Width <= MaxDecorationSize
        && bounds.Height <= MaxDecorationSize
        && styles != DecorationStyles.None;

    /// <summary>Whether a window's rectangle contains a point.</summary>
    /// <param name="bounds">The window's rectangle in screen pixels.</param>
    /// <param name="point">The point to test.</param>
    public static bool Covers(PixelRect bounds, ScreenPoint point) => bounds.Contains(point);
}
