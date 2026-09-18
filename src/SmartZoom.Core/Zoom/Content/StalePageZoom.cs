using SmartZoom.Core.Input;

namespace SmartZoom.Core.Zoom.Content;

/// <summary>
/// The pinch zoom an accessibility tree still reports after the page itself has returned to scale 1.
/// </summary>
/// <remarks>
/// Chromium bakes the page's pinch-zoom scale and offset into every accessibility rectangle, but only when it
/// serializes the tree, and a pinch alone does not make it do so. When something else does in the middle of a
/// gesture (a zoom near the bottom of the viewport nudges the page's scroll position by a pixel or two, which
/// counts), the tree keeps the scale of that moment until the page next scrolls or changes: the document is
/// reported larger than the window that shows it, every block is too big and in the wrong place, and hit-tests
/// answer for the wrong spot. The document's rectangle gives the transform away, because at scale 1 it is the
/// render window's rectangle; with it, points and rectangles can be converted between what the tree reports
/// and what is on screen.
/// </remarks>
/// <param name="Scale">Reported pixels per actual pixel; always above 1.</param>
/// <param name="Reported">The document's rectangle as the tree reports it.</param>
/// <param name="Window">The render window's rectangle, where the document really is.</param>
public readonly record struct StalePageZoom(double Scale, PixelRect Reported, PixelRect Window)
{
    /// <summary>Scales closer to 1 than this are rounding, scrollbars or borders rather than a stale zoom.</summary>
    public const double MinScale = 1.02;

    /// <summary>Largest relative difference between the horizontal and the vertical scale of a uniform zoom.</summary>
    private const double AspectTolerance = 0.01;

    /// <summary>How far (in pixels) a zoomed document may fall short of covering its window.</summary>
    private const int CoverTolerance = 2;

    /// <summary>
    /// Recognizes a document rectangle that is a uniformly scaled-up version of its window and covers it:
    /// the signature of a stale pinch zoom. Anything else (a matching rectangle, a document of another shape)
    /// yields null.
    /// </summary>
    /// <param name="document">The document's reported rectangle.</param>
    /// <param name="window">The rectangle of the window rendering the document.</param>
    /// <returns>The transform, or null when the tree is not stale.</returns>
    public static StalePageZoom? Detect(PixelRect document, PixelRect window)
    {
        if (document.IsEmpty || window.IsEmpty)
            return null;

        var scaleX = document.Width / (double)window.Width;
        var scaleY = document.Height / (double)window.Height;
        if (scaleX < MinScale || Math.Abs(scaleX - scaleY) > AspectTolerance * scaleX)
            return null;

        var covers = document.Left <= window.Left + CoverTolerance
            && document.Top <= window.Top + CoverTolerance
            && document.Right >= window.Right - CoverTolerance
            && document.Bottom >= window.Bottom - CoverTolerance;

        return covers ? new StalePageZoom((scaleX + scaleY) / 2, document, window) : null;
    }

    /// <summary>Where the tree believes an on-screen point is.</summary>
    /// <param name="actual">A point on screen.</param>
    /// <returns>The point to use when asking the tree.</returns>
    public ScreenPoint ToReported(ScreenPoint actual) => new(
        (int)Math.Round(Reported.Left + ((actual.X - Window.Left) * Scale)),
        (int)Math.Round(Reported.Top + ((actual.Y - Window.Top) * Scale)));

    /// <summary>Where a rectangle from the tree really is on screen.</summary>
    /// <param name="reported">A rectangle as the tree reports it.</param>
    /// <returns>The rectangle on screen.</returns>
    public PixelRect ToActual(PixelRect reported) => new(
        ToActual(reported.Left, Reported.Left, Window.Left),
        ToActual(reported.Top, Reported.Top, Window.Top),
        ToActual(reported.Right, Reported.Left, Window.Left),
        ToActual(reported.Bottom, Reported.Top, Window.Top));

    private int ToActual(int reported, int reportedOrigin, int windowOrigin) =>
        (int)Math.Round(windowOrigin + ((reported - reportedOrigin) / Scale));
}
