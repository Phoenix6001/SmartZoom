using SmartZoom.Core.Routing;
using SmartZoom.Core.Zoom.Content;

namespace SmartZoom.Core.Zoom;

/// <summary>The content area of a document reader, and how to move it.</summary>
/// <remarks>
/// Readers expose no scroll position, so a scroll is requested with wheel input and then measured from the
/// screen. The measurement is what makes an exact return possible: a request near the end of a document moves
/// the view less than asked, and undoing the request rather than the movement would leave the reader adrift.
/// </remarks>
public interface IReaderView
{
    /// <summary>Screen bounds of the scrollable content area, or null when the window has none.</summary>
    /// <param name="target">The window the trigger landed on.</param>
    PixelRect? Bounds(TargetInfo target);

    /// <summary>Scrolls the document and measures the result.</summary>
    /// <param name="target">The window the trigger landed on.</param>
    /// <param name="pixels">Requested movement. Positive moves the view towards the end of the document.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>How far the content moved, in pixels, with the same sign convention as <paramref name="pixels"/>.</returns>
    int ScrollBy(TargetInfo target, int pixels, CancellationToken cancellationToken);

    /// <summary>
    /// Scrolls the view back to where a <see cref="Snapshot"/> was taken, whatever happened in between, and
    /// reports whether it could.
    /// </summary>
    /// <param name="target">The window the trigger landed on.</param>
    /// <param name="snapshot">A snapshot from <see cref="Snapshot"/>.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    bool ScrollBackTo(TargetInfo target, object snapshot, CancellationToken cancellationToken);

    /// <summary>Remembers what the view looks like now, so it can be returned to later.</summary>
    /// <param name="target">The window the trigger landed on.</param>
    /// <returns>An opaque token for <see cref="ScrollBackTo"/>, or null when the screen could not be read.</returns>
    object? Snapshot(TargetInfo target);
}
