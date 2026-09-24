using SmartZoom.Core.Input;

using SmartZoom.Core.Zoom.Gesture;

namespace SmartZoom.Core.Zoom.Content;

/// <summary>
/// Where to try a pinch again when the page refused the first one. Chromium refuses a pinch whose contacts go
/// down on an element with <c>touch-action: none</c> (dialogs, drag-and-drop surfaces, maps); the rest of the
/// page usually does not set it, and a pinch scales the whole visual viewport around its anchor, so the same
/// zoom is available by starting the gesture somewhere else on screen.
/// </summary>
/// <remarks>
/// The order is "nearest usable point outside the blocking element, then the viewport centre". What comes back
/// is a visual-viewport zoom rather than a fit of the element: the element the user pointed at is still in view
/// because the viewport scales around the anchor, and the exact restore is unaffected.
/// </remarks>
public static class RetryAnchor
{
    /// <summary>How far outside the blocking element a retry anchor sits, in pixels.</summary>
    /// <remarks>
    /// Chosen, not measured: far enough that the contacts and their recognizer slop start clear of the
    /// element's edge, close enough that the retry still zooms the part of the page the user pointed at.
    /// </remarks>
    public const int OutsideGap = PinchGeometry.HalfGap + ContactSlop;

    /// <summary>
    /// Allowance for the spread a pinch adds beyond <see cref="PinchGeometry.HalfGap"/>: the recognizer's span
    /// slop, plus the growth of the contacts as the gesture opens. Chromium reads <c>touch-action</c> from where
    /// the contacts go down, not from the anchor, so a retry has to clear the blocking element by the whole
    /// contact span rather than by a token margin.
    /// </summary>
    public const int ContactSlop = 100;

    /// <summary>Most anchors <see cref="Candidates"/> ever offers: one beside the block, then the centre.</summary>
    public const int MaxCandidates = 2;

    /// <summary>
    /// Lists the anchors to retry the same zoom around, best first: the nearest usable point outside the
    /// blocking element, then the viewport centre.
    /// </summary>
    /// <param name="block">Bounds of the element that refused the gesture, in physical pixels.</param>
    /// <param name="viewport">The page's viewport, in physical pixels.</param>
    /// <param name="anchor">The anchor the refused pinch used.</param>
    /// <returns>
    /// At most <see cref="MaxCandidates"/> anchors, all inside the area the contacts may use. The list is
    /// built once per refused press, never per frame.
    /// </returns>
    public static IReadOnlyList<ScreenPoint> Candidates(PixelRect block, PixelRect viewport, ScreenPoint anchor)
    {
        // Where the contacts may land at all; a candidate outside it would be pinched around a substitute
        // focus plus a compensating pan, which is exactly what the retry is trying to avoid.
        var usable = BrowserAdapter.ContactBounds(viewport);

        // The coordinate the candidate does not move keeps the original anchor's value, so the zoom stays
        // aimed at the same row (for a candidate beside the block) or column (for one above or below it).
        var row = PixelRect.ClampWithInset(anchor.Y, usable.Top, usable.Bottom - 1, 0);
        var column = PixelRect.ClampWithInset(anchor.X, usable.Left, usable.Right - 1, 0);

        // Each side's point already carries the required clearance, so a point that still fits inside the
        // usable area is a point with at least OutsideGap of room; the side with the most room wins.
        ReadOnlySpan<(int Room, ScreenPoint Point)> sides =
        [
            (block.Left - usable.Left, new ScreenPoint(block.Left - OutsideGap, row)),
            (usable.Right - block.Right, new ScreenPoint(block.Right + OutsideGap, row)),
            (block.Top - usable.Top, new ScreenPoint(column, block.Top - OutsideGap)),
            (usable.Bottom - block.Bottom, new ScreenPoint(column, block.Bottom + OutsideGap)),
        ];

        var best = -1;
        for (var i = 0; i < sides.Length; i++)
        {
            if (usable.Contains(sides[i].Point) && (best < 0 || sides[i].Room > sides[best].Room))
                best = i;
        }

        var candidates = new List<ScreenPoint>(MaxCandidates);
        if (best >= 0)
            candidates.Add(sides[best].Point);

        // The viewport centre is the last resort: a dialog that fills the viewport leaves no point outside it,
        // and a page that blocks the pinch everywhere has to be told apart from one that blocks it here.
        var centre = new ScreenPoint(
            PixelRect.ClampWithInset(viewport.CenterX, usable.Left, usable.Right - 1, 0),
            PixelRect.ClampWithInset(viewport.CenterY, usable.Top, usable.Bottom - 1, 0));
        if (candidates.Count == 0 || candidates[0] != centre)
            candidates.Add(centre);

        return candidates;
    }
}
