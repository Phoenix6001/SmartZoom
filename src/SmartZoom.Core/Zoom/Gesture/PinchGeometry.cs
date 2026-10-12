using SmartZoom.Core.Input;
using SmartZoom.Core.Zoom.Content;

namespace SmartZoom.Core.Zoom.Gesture;

/// <summary>
/// Where two synthetic fingers go for a given zoom, and what to do when they do not fit. This is where every
/// gesture regression in this project has lived, which is why it is here, in a project with no Win32 in it,
/// rather than next to the injection calls.
/// </summary>
public static class PinchGeometry
{
    /// <summary>Half the distance between the contacts at scale 1, in pixels.</summary>
    public const int HalfGap = 60;

    /// <summary>
    /// The smallest half gap worth using. Near a corner the full spread does not fit around the anchor; a
    /// narrower gap still zooms by the same factor (the recognizer measures a ratio) and keeps the focus on
    /// the anchor, which beats pinching elsewhere and dragging the content into place afterwards.
    /// </summary>
    public const int MinHalfGap = 12;

    /// <summary>How close to the edge of the content area a contact may be placed.</summary>
    public const int EdgeMargin = 4;

    /// <summary>Plans one pinch.</summary>
    /// <param name="anchor">The point the caller wants kept still.</param>
    /// <param name="factor">Zoom factor; above 1 magnifies, below 1 shrinks.</param>
    /// <param name="spanSlop">What the recognizer swallows, from <see cref="RecognizerProfile.SpanSlop"/>.</param>
    /// <param name="bounds">The content area the contacts must stay inside.</param>
    /// <param name="minimumScalingSpan">
    /// The span below which the recognizer does not count scaling (<see cref="RecognizerProfile.MinimumScalingSpan"/>).
    /// </param>
    public static PinchPlan Plan(ScreenPoint anchor, double factor, double spanSlop, PixelRect bounds, double minimumScalingSpan = 0)
    {
        var halfSlop = spanSlop / 2;
        var minimumHalf = minimumScalingSpan / 2;
        var (downHalf, preRolledHalf, endHalf) = ContactHalfSpread(factor, halfSlop, HalfGap, minimumHalf);
        var maxHalf = Math.Max(downHalf, Math.Max(preRolledHalf, endHalf));
        var (focus, vertical, room) = factor < 1 ? PlaceFocusForZoomOut(anchor, maxHalf, bounds) : PlaceFocus(anchor, maxHalf, bounds);
        double? narrowed = null;

        // The full spread does not fit around the anchor: before moving the focus (which costs a drag
        // afterwards), try a narrower gap that fits at the anchor itself, in whichever orientation has the most
        // room there. Only for an anchor inside the bounds: the roomier axis says nothing about the other one,
        // and an anchor outside the bounds (in the window's resize-border zone, say) must host no contact.
        if (factor > 1 && focus != anchor && bounds.Contains(anchor))
        {
            var (roomAtAnchor, verticalAtAnchor) = RoomAround(anchor, bounds);
            var narrowGap = Math.Min(HalfGap, NarrowestGap(factor, halfSlop, roomAtAnchor));
            var spread = ContactHalfSpread(factor, halfSlop, narrowGap, minimumHalf);

            // A narrower gap is no use once the zoom would have to start below the recognizer's minimum scaling
            // span: it would be counted from there, and the widened spread no longer fits beside the anchor.
            if (narrowGap >= MinHalfGap && spread.End <= roomAtAnchor + 1)
            {
                narrowed = narrowGap;
                (downHalf, preRolledHalf, endHalf) = spread;
                maxHalf = Math.Max(downHalf, Math.Max(preRolledHalf, endHalf));
                (focus, vertical, room) = (anchor, verticalAtAnchor, roomAtAnchor);
            }
        }

        PinchShortfall? shortfall = null;
        if (maxHalf > room + 1) // the focus is clamped to whole pixels; a one-pixel shortfall is not worth a word
        {
            // Even the middle of the content area can't host the spread (a tiny window): shrink the gesture
            // rather than let a contact leave the window. The zoom comes out smaller than planned; nothing else
            // is touched.
            shortfall = new PinchShortfall(maxHalf, room);
            var shrink = Math.Max(room, 4) / maxHalf;
            downHalf *= shrink;
            preRolledHalf *= shrink;
            endHalf *= shrink;
        }

        // Zooming in around a substitute focus leaves the content offset by (anchor - focus) * (1 - factor);
        // a drag by that vector puts it where the requested anchor would have.
        var pan = factor > 1 && focus != anchor
            ? new ScreenPoint(
                (int)Math.Round((anchor.X - focus.X) * (1 - factor)),
                (int)Math.Round((anchor.Y - focus.Y) * (1 - factor)))
            : default;

        return new PinchPlan(focus, vertical, downHalf, preRolledHalf, endHalf, pan, narrowed, shortfall);
    }

    /// <summary>
    /// The contact area for a zoom-in whose anchor is within the engine's bottom snap zone, lowered to keep the
    /// pinch's centre just above it; the anchor is then reached with a downward drag, as at any other edge.
    /// </summary>
    /// <remarks>
    /// Chromium moves a pinch's centre onto the viewport's edge when it is within 100 DIP of it. At the bottom
    /// that snapped pinch leaked 4-6 px into the page's scroll on every zoom-in, which zooming out does not undo;
    /// a pinch just above the zone followed by the drag lands exactly where it was aimed and leaks nothing
    /// (measured at 100%). The top and the sides snap without leaking, so they are left to snap.
    /// </remarks>
    /// <param name="anchor">The point the zoom should keep still.</param>
    /// <param name="factor">The zoom factor; only a zoom-in is affected.</param>
    /// <param name="bounds">The contact area the caller allows.</param>
    /// <param name="viewportBottom">The bottom edge of the page's viewport, which the zone is measured from.</param>
    /// <param name="snapZone">The zone's height in pixels, from <see cref="RecognizerProfile.BottomSnapZone"/>.</param>
    /// <returns>The contact area to plan with; <paramref name="bounds"/> itself when nothing needs to change.</returns>
    public static PixelRect KeepFocusAboveBottomSnap(ScreenPoint anchor, double factor, PixelRect bounds, int viewportBottom, double snapZone)
    {
        if (factor <= 1 || snapZone <= 0)
            return bounds;

        // The lowest row the focus may sit on, and the contact area whose clamp puts it there.
        var floor = viewportBottom - (int)Math.Ceiling(snapZone) - 1;
        var bottom = floor + EdgeMargin + 1;
        if (anchor.Y <= floor || bottom >= bounds.Bottom || bottom - bounds.Top < (2 * EdgeMargin) + 2)
            return bounds;

        return bounds with { Bottom = bottom };
    }

    /// <summary>
    /// The drag that makes a zoom-out around <paramref name="focus"/> undo a zoom made around
    /// <paramref name="anchor"/>: it moves the zoomed view to where a zoom around the focus would have put it,
    /// so the zoom-out ends exactly where the page started.
    /// </summary>
    /// <remarks>
    /// Needed where the browser keeps the page point under the pinch's centre still to the very end of a zoom-out
    /// (Gecko): at 1.0 that can only be done by scrolling the page, by (anchor - focus) * (1 - 1/scale). Near an
    /// edge the contacts cannot straddle the anchor, so the zoom-out's focus is moved and the page came back
    /// 141-152 px off. A drag made while still zoomed moves only the zoomed view, never the page. It is the zoom-in's
    /// own correcting drag reversed, measured from the zoom-out's focus.
    /// </remarks>
    /// <param name="anchor">The point the zoom-in kept still.</param>
    /// <param name="focus">Where the zoom-out's contacts are centred.</param>
    /// <param name="zoomedScale">How far the page is zoomed when the drag is made.</param>
    /// <returns>How far the content should move, in pixels; zero when there is nothing to make up.</returns>
    public static ScreenPoint UndoPan(ScreenPoint anchor, ScreenPoint focus, double zoomedScale) =>
        zoomedScale > 1 && focus != anchor
            ? new ScreenPoint(
                (int)Math.Round((anchor.X - focus.X) * (zoomedScale - 1)),
                (int)Math.Round((anchor.Y - focus.Y) * (zoomedScale - 1)))
            : default;

    /// <summary>
    /// Splits a drag into legs that fit inside the bounds, each placed so that the leg and the slop it must
    /// cross first both stay inside. Content follows the finger, so a leg moves the content by its own vector.
    /// </summary>
    /// <param name="delta">How far the content should move.</param>
    /// <param name="bounds">The content area the finger must stay inside.</param>
    /// <param name="touchSlop">What the recognizer swallows, from <see cref="RecognizerProfile.TouchSlop"/>.</param>
    /// <remarks>
    /// A drag on both axes is made as two, sideways first: Chromium locks a drag within about 20 degrees of an
    /// axis to that axis, so a diagonal one near a bottom corner moved the view sideways only.
    /// </remarks>
    public static IReadOnlyList<PanLeg> PanLegs(ScreenPoint delta, PixelRect bounds, double touchSlop) =>
        [.. AxisLegs(new ScreenPoint(delta.X, 0), bounds, touchSlop), .. AxisLegs(new ScreenPoint(0, delta.Y), bounds, touchSlop)];

    private static List<PanLeg> AxisLegs(ScreenPoint delta, PixelRect bounds, double touchSlop)
    {
        var legs = new List<PanLeg>(4);
        var remaining = delta;
        var legWidth = Math.Max(1, bounds.Width - (2 * EdgeMargin) - (int)Math.Ceiling(touchSlop));
        var legHeight = Math.Max(1, bounds.Height - (2 * EdgeMargin) - (int)Math.Ceiling(touchSlop));

        for (var leg = 0; leg < 4 && (remaining.X != 0 || remaining.Y != 0); leg++)
        {
            var dx = Math.Clamp(remaining.X, -legWidth, legWidth);
            var dy = Math.Clamp(remaining.Y, -legHeight, legHeight);

            var length = Math.Sqrt((dx * dx) + (dy * dy));
            var slopX = length == 0 ? 0 : dx / length * touchSlop;
            var slopY = length == 0 ? 0 : dy / length * touchSlop;
            var start = new ScreenPoint(
                PixelRect.ClampWithInset(bounds.CenterX - ((dx + slopX) / 2), bounds.Left, bounds.Right - 1, EdgeMargin),
                PixelRect.ClampWithInset(bounds.CenterY - ((dy + slopY) / 2), bounds.Top, bounds.Bottom - 1, EdgeMargin));
            var end = new ScreenPoint(
                (int)Math.Round(start.X + dx + slopX),
                (int)Math.Round(start.Y + dy + slopY));

            legs.Add(new PanLeg(start, end));
            remaining = new ScreenPoint(remaining.X - dx, remaining.Y - dy);
        }

        return legs;
    }

    /// <summary>The two contacts' positions along their axis, relative to the focus.</summary>
    /// <param name="half">Half the span between them.</param>
    /// <param name="focus">The midpoint.</param>
    /// <param name="vertical">Whether they are spread up and down rather than left and right.</param>
    /// <returns>The two points, in the order the injector reports them.</returns>
    public static (ScreenPoint First, ScreenPoint Second) Contacts(double half, ScreenPoint focus, bool vertical) =>
        (Offset(focus, -half, vertical), Offset(focus, half, vertical));

    /// <summary>
    /// The three spans of a pinch: where the fingers land, where they are after the invisible pre-roll that
    /// crosses the recognizer's slop, and where they end.
    /// </summary>
    /// <remarks>
    /// The recognizer only starts measuring once the span has changed by the slop, and then measures scale
    /// against the span at that moment. So the fingers go down, move by the slop in one step, and animate from
    /// there to factor times the pre-rolled span, which makes the whole visible motion count.
    ///
    /// These are the spans the recognizer is <em>asked</em> for, not what it delivers. Windows' own recognizer
    /// keeps back a share of a closing gesture, and how much depends on the zoom it starts from, so a gesture
    /// and its inverse do not cancel: measured in Acrobat, a x2 followed by a x0.5 leaves the reader 2%
    /// smaller each round trip. A caller that needs an exact return must land on a state the application
    /// defines, not on this arithmetic.
    /// </remarks>
    /// <param name="factor">Zoom factor.</param>
    /// <param name="halfSlop">Half the recognizer's span slop.</param>
    /// <param name="halfGap">Half the resting distance between the contacts.</param>
    /// <param name="minimumHalfSpan">Half the span below which the recognizer does not count scaling.</param>
    internal static (double Down, double PreRolled, double End) ContactHalfSpread(double factor, double halfSlop, double halfGap, double minimumHalfSpan = 0)
    {
        var crossing = halfSlop + 1; // one extra pixel so the threshold is definitely crossed
        if (factor >= 1)
        {
            // The zoom is counted from the pre-rolled span, so it may not start below the minimum scaling span.
            var preRolled = Math.Max(halfGap + crossing, minimumHalfSpan);
            return (halfGap, preRolled, factor * preRolled);
        }

        var reference = halfGap / factor;
        return (reference + crossing, reference, halfGap);
    }

    /// <summary>The largest half gap whose widest spread still fits in <paramref name="room"/>.</summary>
    internal static double NarrowestGap(double factor, double halfSlop, double room)
    {
        var crossing = halfSlop + 1;
        return factor >= 1 ? (room / factor) - crossing : (room - crossing) * factor;
    }

    /// <summary>How far the contacts may spread around a point inside the bounds, on the roomier axis.</summary>
    internal static (double Room, bool Vertical) RoomAround(ScreenPoint point, PixelRect bounds)
    {
        var horizontal = Math.Min(point.X - bounds.Left, bounds.Right - 1 - point.X);
        var vertical = Math.Min(point.Y - bounds.Top, bounds.Bottom - 1 - point.Y);
        return horizontal >= vertical ? (horizontal, false) : (vertical, true);
    }

    /// <summary>
    /// The focal point for a zoom-out. It ends clamped at the application's minimum scale, so the focal point
    /// does not matter and the contacts simply go wherever they fit on the anchor's row.
    /// </summary>
    /// <remarks>
    /// A vertical spread, or one shrunk to fit near an edge, made Chromium scroll the page by about 54 px on a
    /// zoom-out (measured twice), and nothing undoes that.
    /// </remarks>
    internal static (ScreenPoint Focus, bool Vertical, double Room) PlaceFocusForZoomOut(ScreenPoint anchor, double maxHalf, PixelRect bounds)
    {
        var focus = new ScreenPoint(
            PixelRect.ClampWithInset(anchor.X, bounds.Left, bounds.Right - 1, maxHalf),
            PixelRect.ClampWithInset(anchor.Y, bounds.Top, bounds.Bottom - 1, EdgeMargin));

        return (focus, false, Math.Min(focus.X - bounds.Left, bounds.Right - 1 - focus.X));
    }

    /// <summary>
    /// The focal point for a zoom-in: the anchor when the contacts fit around it, otherwise the nearest point
    /// on the anchor's row where they do, and only when the row is too short for the spread, the nearest point
    /// on the anchor's column.
    /// </summary>
    /// <remarks>
    /// A focus moved along the row is made up for by a sideways drag, which pages absorb in the zoomed view.
    /// A focus moved up or down needs a vertical drag, and that leaked into the page's own scroll position —
    /// the table of contents near the top of a Wikipedia page came back 123 px off after the restore — so the
    /// row is preferred whenever the spread fits on it at all.
    /// </remarks>
    internal static (ScreenPoint Focus, bool Vertical, double Room) PlaceFocus(ScreenPoint anchor, double maxHalf, PixelRect bounds)
    {
        var horizontal = new ScreenPoint(
            PixelRect.ClampWithInset(anchor.X, bounds.Left, bounds.Right - 1, maxHalf),
            PixelRect.ClampWithInset(anchor.Y, bounds.Top, bounds.Bottom - 1, EdgeMargin));
        var vertical = new ScreenPoint(
            PixelRect.ClampWithInset(anchor.X, bounds.Left, bounds.Right - 1, EdgeMargin),
            PixelRect.ClampWithInset(anchor.Y, bounds.Top, bounds.Bottom - 1, maxHalf));

        var horizontalRoom = Math.Min(horizontal.X - bounds.Left, bounds.Right - 1 - horizontal.X);
        var horizontalMove = Distance(anchor, horizontal);
        var verticalMove = Distance(anchor, vertical);
        if (horizontalMove <= verticalMove || horizontalRoom + 1 >= maxHalf) // +1: the clamped focus is a whole pixel
            return (horizontal, false, horizontalRoom);

        return (vertical, true, Math.Min(vertical.Y - bounds.Top, bounds.Bottom - 1 - vertical.Y));
    }

    private static ScreenPoint Offset(ScreenPoint focus, double offset, bool vertical) => new(
        (int)Math.Round(focus.X + (vertical ? 0 : offset)),
        (int)Math.Round(focus.Y + (vertical ? offset : 0)));

    private static double Distance(ScreenPoint a, ScreenPoint b)
    {
        double dx = a.X - b.X, dy = a.Y - b.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }
}
