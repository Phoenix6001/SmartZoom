using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using SmartZoom.Core.Input;
using SmartZoom.Core.Zoom;
using SmartZoom.Core.Zoom.Content;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.Pointer;
using Windows.Win32.UI.WindowsAndMessaging;

namespace SmartZoom.Interop.Input;

/// <summary>Performs pinch gestures by injecting synthetic touch contacts (<c>InjectTouchInput</c>).</summary>
/// <remarks>
/// <para>Touch injection needs no admin rights and no touch hardware, but like all input injection it is
/// blocked by UIPI when the target window is elevated.</para>
/// <para>A pinch's focal point is the midpoint of its two contacts, so the contacts must fit symmetrically
/// around it inside the content area. When the requested anchor is too close to an edge for that, the pinch
/// is performed around the nearest reachable point and the page is then panned by the resulting offset with
/// a one-finger drag, which leaves the content exactly where a pinch around the requested anchor would have.
/// Zooming back below 1.0 resets the browser's viewport, so restores never need the pan.</para>
/// </remarks>
public sealed partial class TouchPinchInjector(ILogger<TouchPinchInjector> logger) : IPinchInjector
{
    /// <summary>Half the distance between the contacts at scale 1, in pixels.</summary>
    public const int HalfGap = 60;

    /// <summary>
    /// Chromium's gesture recognizer ignores span changes smaller than this (in device-independent
    /// pixels) before it decides the gesture is a pinch, and then measures scale from the span at
    /// that moment. Without compensating, a requested 1.9x comes out as ~1.5x. Chromium's nominal value is
    /// 16 DIP; 23 is what a 200% display actually measured (1.9x requested came out at 1.75x with 16).
    /// </summary>
    private const double SpanSlopDips = 23;

    /// <summary>Movement a single contact must make before Chromium treats it as a scroll rather than a tap.</summary>
    private const double TouchSlopDips = 8;

    // Target frame interval. 8 ms feeds 120 Hz displays and halves the step size on 60 Hz ones.
    private const int FrameMs = 8;
    private const int PanDurationMs = 140;
    private const int PanSettleMs = 60;
    private const int EdgeMargin = 4;
    private const uint MaxContacts = 2;
    private const uint TouchMaskContactAreaOrientationPressure = 0x1 | 0x2 | 0x4;
    private const POINTER_FLAGS DownFlags = POINTER_FLAGS.POINTER_FLAG_DOWN | POINTER_FLAGS.POINTER_FLAG_INRANGE | POINTER_FLAGS.POINTER_FLAG_INCONTACT;
    private const POINTER_FLAGS UpdateFlags = POINTER_FLAGS.POINTER_FLAG_UPDATE | POINTER_FLAGS.POINTER_FLAG_INRANGE | POINTER_FLAGS.POINTER_FLAG_INCONTACT;

    private static readonly Lock InitializationGate = new();
    private static bool? s_initialized;

    /// <inheritdoc />
    public Task<bool> PinchAsync(ScreenPoint anchor, double factor, TimeSpan duration, PixelRect bounds, CancellationToken cancellationToken)
    {
        if (factor <= 0 || double.IsNaN(factor))
            throw new ArgumentOutOfRangeException(nameof(factor), factor, "Factor must be positive.");

        return Task.Run(() => Pinch(anchor, factor, duration, bounds, cancellationToken), cancellationToken);
    }

    private bool Pinch(ScreenPoint anchor, double factor, TimeSpan duration, PixelRect bounds, CancellationToken cancellationToken)
    {
        if (!EnsureInitialized())
            return false;

        var dpiScale = DpiScaleAt(anchor);
        var (downHalf, preRolledHalf, endHalf) = ContactHalfSpread(factor, halfSlop: SpanSlopDips * dpiScale / 2);
        var maxHalf = Math.Max(downHalf, Math.Max(preRolledHalf, endHalf));
        var (focus, vertical, room) = PlaceFocus(anchor, maxHalf, bounds);
        if (maxHalf > room)
        {
            // Even the middle of the content area can't host the spread (tiny window): shrink the gesture rather
            // than let a contact leave the window. The zoom comes out smaller than planned but nothing else is touched.
            LogShrunk(anchor.X, anchor.Y, (int)Math.Ceiling(maxHalf), (int)room);
            var shrink = Math.Max(room, 4) / maxHalf;
            downHalf *= shrink;
            preRolledHalf *= shrink;
            endHalf *= shrink;
        }

        // Windows moves the mouse pointer along with injected touch contacts; put it back afterwards so the
        // user's pointer (and therefore the target of their next press) stays where they left it.
        var hadCursor = PInvoke.GetCursorPos(out var cursorBefore);
        try
        {
            if (!PinchAround(focus, vertical, downHalf, preRolledHalf, endHalf, duration, cancellationToken))
                return false;

            // Zooming in around a substitute focus leaves the content offset by (anchor - focus) * (1 - factor);
            // a drag by that vector puts it where the requested anchor would have.
            if (factor > 1 && focus != anchor)
            {
                var pan = new ScreenPoint(
                    (int)Math.Round((anchor.X - focus.X) * (1 - factor)),
                    (int)Math.Round((anchor.Y - focus.Y) * (1 - factor)));
                LogPanning(anchor.X, anchor.Y, focus.X, focus.Y, pan.X, pan.Y);
                return Pan(pan, bounds, TouchSlopDips * dpiScale, cancellationToken);
            }

            return true;
        }
        finally
        {
            RestoreCursor(hadCursor, cursorBefore);
        }
    }

    // The recognizer only starts measuring once the span has changed by the slop, and then measures scale
    // against the span at that moment. So: put the fingers down, move them by the slop in one invisible
    // "pre-roll" step (outward when spreading, inward when closing), and animate from there to
    // factor * pre-rolled span, so the whole visible motion counts.
    private static (double Down, double PreRolled, double End) ContactHalfSpread(double factor, double halfSlop)
    {
        var crossing = halfSlop + 1; // one extra pixel so the threshold is definitely crossed
        if (factor >= 1)
        {
            var preRolled = HalfGap + crossing;
            return (HalfGap, preRolled, factor * preRolled);
        }

        var reference = HalfGap / factor;
        return (reference + crossing, reference, HalfGap);
    }

    // The focal point actually used: the anchor when the contacts fit around it, otherwise the nearest point
    // where they do, on whichever axis needs the smaller move. Returns the room available along that axis.
    private static (ScreenPoint Focus, bool Vertical, double Room) PlaceFocus(ScreenPoint anchor, double maxHalf, PixelRect bounds)
    {
        var horizontal = new ScreenPoint(Clamp(anchor.X, bounds.Left, bounds.Right - 1, maxHalf), Clamp(anchor.Y, bounds.Top, bounds.Bottom - 1, EdgeMargin));
        var vertical = new ScreenPoint(Clamp(anchor.X, bounds.Left, bounds.Right - 1, EdgeMargin), Clamp(anchor.Y, bounds.Top, bounds.Bottom - 1, maxHalf));

        var horizontalMove = Distance(anchor, horizontal);
        var verticalMove = Distance(anchor, vertical);
        if (horizontalMove <= verticalMove)
            return (horizontal, false, Math.Min(horizontal.X - bounds.Left, bounds.Right - 1 - horizontal.X));

        return (vertical, true, Math.Min(vertical.Y - bounds.Top, bounds.Bottom - 1 - vertical.Y));
    }

    private bool PinchAround(ScreenPoint focus, bool vertical, double downHalf, double preRolledHalf, double endHalf, TimeSpan duration, CancellationToken cancellationToken)
    {
        var frames = Math.Max(2, (int)Math.Round(duration.TotalMilliseconds / FrameMs));
        var contacts = NewContacts(2);
        var clock = Stopwatch.StartNew();

        PlacePair(contacts, focus, downHalf, vertical);
        if (!Inject(contacts, DownFlags))
            return false;

        var completed = false;
        try
        {
            // Pre-roll: cross the recognizer's slop in one step before the visible animation starts.
            WaitUntil(clock, FrameMs);
            PlacePair(contacts, focus, preRolledHalf, vertical);
            if (!Inject(contacts, UpdateFlags))
                return false;

            var animationStart = clock.Elapsed.TotalMilliseconds;
            for (var frame = 1; frame <= frames; frame++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Frames are scheduled against the clock, not chained sleeps, so timer jitter doesn't accumulate.
                WaitUntil(clock, animationStart + (frame * FrameMs));
                PlacePair(contacts, focus, preRolledHalf + ((endHalf - preRolledHalf) * SmoothStep(frame / (double)frames)), vertical);

                if (!Inject(contacts, UpdateFlags))
                    return false;
            }

            completed = true;
            return true;
        }
        finally
        {
            // Never leave synthetic fingers on the screen, whatever happened above.
            if (!Inject(contacts, POINTER_FLAGS.POINTER_FLAG_UP) && completed)
                LogLiftFailed();
        }
    }

    // One-finger drag that moves the content by 'delta' (content follows the finger), split into legs that fit
    // inside the bounds. Each leg starts with the touch slop so the scrolled distance is the full leg.
    private bool Pan(ScreenPoint delta, PixelRect bounds, double touchSlop, CancellationToken cancellationToken)
    {
        var remaining = delta;
        var legWidth = Math.Max(1, bounds.Width - (2 * EdgeMargin) - (int)Math.Ceiling(touchSlop));
        var legHeight = Math.Max(1, bounds.Height - (2 * EdgeMargin) - (int)Math.Ceiling(touchSlop));

        for (var leg = 0; leg < 4 && (remaining.X != 0 || remaining.Y != 0); leg++)
        {
            var dx = Math.Clamp(remaining.X, -legWidth, legWidth);
            var dy = Math.Clamp(remaining.Y, -legHeight, legHeight);

            // Start where the leg (plus its slop) stays inside the bounds.
            var length = Math.Sqrt((dx * dx) + (dy * dy));
            var slopX = length == 0 ? 0 : dx / length * touchSlop;
            var slopY = length == 0 ? 0 : dy / length * touchSlop;
            var start = new ScreenPoint(
                Clamp(bounds.CenterX - ((dx + slopX) / 2), bounds.Left, bounds.Right - 1, EdgeMargin),
                Clamp(bounds.CenterY - ((dy + slopY) / 2), bounds.Top, bounds.Bottom - 1, EdgeMargin));
            var end = new ScreenPoint(
                (int)Math.Round(start.X + dx + slopX),
                (int)Math.Round(start.Y + dy + slopY));

            if (!Drag(start, end, cancellationToken))
                return false;

            remaining = new ScreenPoint(remaining.X - dx, remaining.Y - dy);
        }

        return true;
    }

    private bool Drag(ScreenPoint from, ScreenPoint to, CancellationToken cancellationToken)
    {
        var frames = Math.Max(2, PanDurationMs / FrameMs);
        var contacts = NewContacts(1);
        var clock = Stopwatch.StartNew();

        PlaceOne(contacts, from);
        if (!Inject(contacts, DownFlags))
            return false;

        var completed = false;
        try
        {
            for (var frame = 1; frame <= frames; frame++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                WaitUntil(clock, frame * FrameMs);

                var t = SmoothStep(frame / (double)frames);
                PlaceOne(contacts, new ScreenPoint((int)Math.Round(from.X + ((to.X - from.X) * t)), (int)Math.Round(from.Y + ((to.Y - from.Y) * t))));
                if (!Inject(contacts, UpdateFlags))
                    return false;
            }

            // Hold still before lifting so the browser doesn't turn the drag into a fling.
            WaitUntil(clock, (frames * FrameMs) + PanSettleMs);
            if (!Inject(contacts, UpdateFlags))
                return false;

            completed = true;
            return true;
        }
        finally
        {
            if (!Inject(contacts, POINTER_FLAGS.POINTER_FLAG_UP) && completed)
                LogLiftFailed();
        }
    }

    private static POINTER_TOUCH_INFO[] NewContacts(int count)
    {
        var contacts = new POINTER_TOUCH_INFO[count];
        for (var i = 0; i < count; i++)
        {
            contacts[i].pointerInfo.pointerType = POINTER_INPUT_TYPE.PT_TOUCH;
            contacts[i].pointerInfo.pointerId = (uint)i;
            contacts[i].touchMask = TouchMaskContactAreaOrientationPressure;
            contacts[i].orientation = 90;
            contacts[i].pressure = 32000;
        }

        return contacts;
    }

    private static void PlacePair(POINTER_TOUCH_INFO[] contacts, ScreenPoint focus, double half, bool vertical)
    {
        for (var i = 0; i < 2; i++)
        {
            var offset = i == 0 ? -half : half;
            SetLocation(ref contacts[i], (int)Math.Round(focus.X + (vertical ? 0 : offset)), (int)Math.Round(focus.Y + (vertical ? offset : 0)));
        }
    }

    private static void PlaceOne(POINTER_TOUCH_INFO[] contacts, ScreenPoint at) => SetLocation(ref contacts[0], at.X, at.Y);

    private static void SetLocation(ref POINTER_TOUCH_INFO contact, int x, int y)
    {
        contact.pointerInfo.ptPixelLocation = new System.Drawing.Point(x, y);
        contact.rcContact = new RECT { left = x - 2, top = y - 2, right = x + 2, bottom = y + 2 };
    }

    private bool Inject(POINTER_TOUCH_INFO[] contacts, POINTER_FLAGS flags)
    {
        for (var i = 0; i < contacts.Length; i++)
            contacts[i].pointerInfo.pointerFlags = flags;

        if (PInvoke.InjectTouchInput(contacts))
            return true;

        LogInjectFailed(Marshal.GetLastPInvokeError());
        return false;
    }

    // Device scale factor of the monitor showing the point (1.0 at 96 DPI, 2.0 at 200%).
    private static double DpiScaleAt(ScreenPoint point)
    {
        var window = PInvoke.WindowFromPoint(new System.Drawing.Point(point.X, point.Y));
        var dpi = window.IsNull ? 0 : PInvoke.GetDpiForWindow(window);
        return dpi == 0 ? 1.0 : dpi / 96.0;
    }

    private static double SmoothStep(double t) => t * t * (3 - (2 * t));

    private static double Distance(ScreenPoint a, ScreenPoint b)
    {
        double dx = a.X - b.X, dy = a.Y - b.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    // Clamp into [min + inset, max - inset]; if the inset is wider than the range, use the middle.
    private static int Clamp(double value, int min, int max, double inset)
    {
        var low = min + inset;
        var high = max - inset;
        return (int)Math.Round(low > high ? (min + max) / 2.0 : Math.Clamp(value, low, high));
    }

    // Sleep while there is comfortably more than a timer tick to go, then spin the last stretch: Thread.Sleep
    // alone quantizes to 15.6 ms on Windows, which is what made the gesture look steppy.
    private static void WaitUntil(Stopwatch clock, double targetMs)
    {
        while (true)
        {
            var remaining = targetMs - clock.Elapsed.TotalMilliseconds;
            if (remaining <= 0)
                return;

            if (remaining > 2)
                Thread.Sleep(1);
            else
                Thread.SpinWait(200);
        }
    }

    private static void RestoreCursor(bool known, System.Drawing.Point position)
    {
        if (known)
            PInvoke.SetCursorPos(position.X, position.Y);
    }

    private bool EnsureInitialized()
    {
        lock (InitializationGate)
        {
            if (s_initialized is { } known)
                return known;

            var ok = PInvoke.InitializeTouchInjection(MaxContacts, TOUCH_FEEDBACK_MODE.TOUCH_FEEDBACK_NONE);
            if (!ok)
                LogInitFailed(Marshal.GetLastPInvokeError());

            s_initialized = ok;
            return ok;
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "InitializeTouchInjection failed (Win32 error {Error}); browser smart zoom is unavailable.")]
    private partial void LogInitFailed(int error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "InjectTouchInput failed (Win32 error {Error}).")]
    private partial void LogInjectFailed(int error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not lift the synthetic touch contacts after a completed gesture.")]
    private partial void LogLiftFailed();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Pinch around ({X}, {Y}) needs {HalfSpread} px of room but the content area offers {Room} px; the gesture was shrunk and will zoom less than planned.")]
    private partial void LogShrunk(int x, int y, int halfSpread, int room);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Anchor ({AnchorX}, {AnchorY}) is too close to an edge for the contacts; pinched around ({FocusX}, {FocusY}) and panning by ({PanX}, {PanY}).")]
    private partial void LogPanning(int anchorX, int anchorY, int focusX, int focusY, int panX, int panY);
}
