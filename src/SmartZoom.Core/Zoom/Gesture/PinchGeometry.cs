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
    public static PinchPlan Plan(ScreenPoint anchor, double factor, double spanSlop, PixelRect bounds)
    {
        var halfSlop = spanSlop / 2;
        var (downHalf, preRolledHalf, endHalf) = ContactHalfSpread(factor, halfSlop, HalfGap);
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
            if (narrowGap >= MinHalfGap)
            {
                narrowed = narrowGap;
                (downHalf, preRolledHalf, endHalf) = ContactHalfSpread(factor, halfSlop, narrowGap);
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
    /// Splits a drag into legs that fit inside the bounds, each placed so that the leg and the slop it must
    /// cross first both stay inside. Content follows the finger, so a leg moves the content by its own vector.
    /// </summary>
    /// <param name="delta">How far the content should move.</param>
    /// <param name="bounds">The content area the finger must stay inside.</param>
    /// <param name="touchSlop">What the recognizer swallows, from <see cref="RecognizerProfile.TouchSlop"/>.</param>
    public static IReadOnlyList<PanLeg> PanLegs(ScreenPoint delta, PixelRect bounds, double touchSlop)
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
                Clamp(bounds.CenterX - ((dx + slopX) / 2), bounds.Left, bounds.Right - 1, EdgeMargin),
                Clamp(bounds.CenterY - ((dy + slopY) / 2), bounds.Top, bounds.Bottom - 1, EdgeMargin));
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
    internal static (double Down, double PreRolled, double End) ContactHalfSpread(double factor, double halfSlop, double halfGap)
    {
        var crossing = halfSlop + 1; // one extra pixel so the threshold is definitely crossed
        if (factor >= 1)
        {
            var preRolled = halfGap + crossing;
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
            Clamp(anchor.X, bounds.Left, bounds.Right - 1, maxHalf),
            Clamp(anchor.Y, bounds.Top, bounds.Bottom - 1, EdgeMargin));

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
            Clamp(anchor.X, bounds.Left, bounds.Right - 1, maxHalf),
            Clamp(anchor.Y, bounds.Top, bounds.Bottom - 1, EdgeMargin));
        var vertical = new ScreenPoint(
            Clamp(anchor.X, bounds.Left, bounds.Right - 1, EdgeMargin),
            Clamp(anchor.Y, bounds.Top, bounds.Bottom - 1, maxHalf));

        var horizontalRoom = Math.Min(horizontal.X - bounds.Left, bounds.Right - 1 - horizontal.X);
        var horizontalMove = Distance(anchor, horizontal);
        var verticalMove = Distance(anchor, vertical);
        if (horizontalMove <= verticalMove || horizontalRoom + 1 >= maxHalf) // +1: the clamped focus is a whole pixel
            return (horizontal, false, horizontalRoom);

        return (vertical, true, Math.Min(vertical.Y - bounds.Top, bounds.Bottom - 1 - vertical.Y));
    }

    /// <summary>Clamps into [min + inset, max - inset]; if the inset is wider than the range, uses the middle.</summary>
    internal static int Clamp(double value, int min, int max, double inset)
    {
        var low = min + inset;
        var high = max - inset;
        return (int)Math.Round(low > high ? (min + max) / 2.0 : Math.Clamp(value, low, high));
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
