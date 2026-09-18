using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using SmartZoom.Core.Input;
using SmartZoom.Core.Zoom;
using SmartZoom.Core.Zoom.Content;
using SmartZoom.Interop.Windows;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.Pointer;
using Windows.Win32.UI.WindowsAndMessaging;

namespace SmartZoom.Interop.Input;

/// <summary>Performs pinch gestures by injecting synthetic touch contacts.</summary>
/// <remarks>
/// <para>Touch injection needs no admin rights and no touch hardware, but like all input injection it is
/// blocked by UIPI when the target window is elevated.</para>
/// <para>A pinch's focal point is the midpoint of its two contacts, so the contacts must fit symmetrically
/// around it inside the content area. When the requested anchor is too close to an edge for that, the pinch
/// is performed around the nearest reachable point and the page is then panned by the resulting offset with
/// a one-finger drag, which leaves the content exactly where a pinch around the requested anchor would have.
/// Zooming back below 1.0 resets the browser's viewport, so restores never need the pan.</para>
/// <para>Two virtual touch devices are used, chosen by the top-level window under the anchor. Chromium
/// browsers get <c>InjectTouchInput</c>. Gecko (Firefox) treats every two-finger gesture that comes from that
/// API's <c>\\?\VIRTUAL_DIGITIZER</c> device as a touchpad scroll (its workaround for Synaptics touchpads that
/// emulate touch through the same API, Mozilla bug 1355162), so a pinch from it never zooms; a device from
/// <c>CreateSyntheticPointerDevice</c> registers under a different name and is handled as a real touch screen.
/// Both devices live for the rest of the process, like the touch-injection registration itself.</para>
/// </remarks>
public sealed partial class TouchPinchInjector(ILogger<TouchPinchInjector> logger) : IPinchInjector
{
    /// <summary>Half the distance between the contacts at scale 1, in pixels.</summary>
    public const int HalfGap = 60;

    /// <summary>
    /// The smallest half gap worth using. Near a corner the full spread does not fit around the anchor; a
    /// narrower gap still zooms by the same factor (the recognizer measures a ratio) and keeps the focus on the
    /// anchor, which beats pinching elsewhere and dragging the content into place afterwards.
    /// </summary>
    private const int MinHalfGap = 12;

    /// <summary>
    /// Chromium's gesture recognizer ignores span changes smaller than this (in device-independent
    /// pixels) before it decides the gesture is a pinch, and then measures scale from the span at
    /// that moment. Without compensating, a requested 1.9x comes out as ~1.5x. Chromium's nominal value is
    /// 16 DIP; 23 is what a 200% display actually measured (1.9x requested came out at 1.75x with 16).
    /// </summary>
    private const double SpanSlopDips = 23;

    /// <summary>Movement a single contact must make before Chromium treats it as a scroll rather than a tap.</summary>
    private const double TouchSlopDips = 8;

    /// <summary>
    /// Gecko's APZ starts a pinch once the span has changed by this many physical pixels
    /// (<c>PINCH_START_THRESHOLD</c>) and, like Chromium, measures the scale from the span at that moment.
    /// </summary>
    private const double GeckoSpanSlopPx = 35;

    /// <summary>Gecko's touch-start tolerance (<c>apz.touch_start_tolerance</c>): 0.1 inch, i.e. 9.6 DIP.</summary>
    private const double GeckoTouchSlopDips = 9.6;

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
    private static DestroySyntheticPointerDeviceSafeHandle? s_syntheticDevice;
    private static bool s_syntheticDeviceFailed;

    /// <summary>The browser engine behind the window being pinched; decides the injection device and the recognizer slop.</summary>
    private enum Engine
    {
        Chromium,
        Gecko,
    }

    /// <inheritdoc />
    public Task<bool> PinchAsync(ScreenPoint anchor, double factor, TimeSpan duration, PixelRect bounds, CancellationToken cancellationToken)
    {
        if (factor <= 0 || double.IsNaN(factor))
            throw new ArgumentOutOfRangeException(nameof(factor), factor, "Factor must be positive.");

        return Task.Run(() => Pinch(anchor, factor, duration, bounds, cancellationToken), cancellationToken);
    }

    private bool Pinch(ScreenPoint anchor, double factor, TimeSpan duration, PixelRect bounds, CancellationToken cancellationToken)
    {
        var engine = BrowserWindows.IsGeckoWindowAt(anchor) ? Engine.Gecko : Engine.Chromium;
        if (!EnsureDevice(engine))
            return false;

        var dpiScale = DpiScaleAt(anchor);
        var halfSlop = SpanSlop(engine, dpiScale) / 2;
        var (downHalf, preRolledHalf, endHalf) = ContactHalfSpread(factor, halfSlop, HalfGap);
        var maxHalf = Math.Max(downHalf, Math.Max(preRolledHalf, endHalf));
        var (focus, vertical, room) = factor < 1 ? PlaceFocusForZoomOut(anchor, maxHalf, bounds) : PlaceFocus(anchor, maxHalf, bounds);

        // The full spread does not fit around the anchor: before moving the focus (which costs a drag afterwards),
        // try a narrower gap that fits at the anchor itself, in whichever orientation has the most room there.
        // Only for an anchor inside the bounds: the roomier axis says nothing about the other one, and an anchor
        // outside the bounds (in the window's resize-border zone, say) must not host a contact at all.
        if (factor > 1 && focus != anchor && bounds.Contains(anchor))
        {
            var (roomAtAnchor, verticalAtAnchor) = RoomAround(anchor, bounds);
            var narrowGap = Math.Min(HalfGap, NarrowestGap(factor, halfSlop, roomAtAnchor));
            if (narrowGap >= MinHalfGap)
            {
                LogNarrowed(anchor.X, anchor.Y, narrowGap);
                (downHalf, preRolledHalf, endHalf) = ContactHalfSpread(factor, halfSlop, narrowGap);
                maxHalf = Math.Max(downHalf, Math.Max(preRolledHalf, endHalf));
                (focus, vertical, room) = (anchor, verticalAtAnchor, roomAtAnchor);
            }
        }

        if (maxHalf > room + 1) // the focus is clamped to whole pixels; a one-pixel shortfall is not worth a warning
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
            if (!PinchAround(focus, vertical, downHalf, preRolledHalf, endHalf, duration, engine, cancellationToken))
                return false;

            // Zooming in around a substitute focus leaves the content offset by (anchor - focus) * (1 - factor);
            // a drag by that vector puts it where the requested anchor would have.
            if (factor > 1 && focus != anchor)
            {
                var pan = new ScreenPoint(
                    (int)Math.Round((anchor.X - focus.X) * (1 - factor)),
                    (int)Math.Round((anchor.Y - focus.Y) * (1 - factor)));
                LogPanning(anchor.X, anchor.Y, focus.X, focus.Y, pan.X, pan.Y);
                return Pan(pan, bounds, TouchSlop(engine, dpiScale), engine, cancellationToken);
            }

            return true;
        }
        finally
        {
            RestoreCursor(hadCursor, cursorBefore);
        }
    }

    // The span change (in physical pixels) the engine's recognizer swallows before it starts measuring a pinch.
    private static double SpanSlop(Engine engine, double dpiScale) =>
        engine == Engine.Gecko ? GeckoSpanSlopPx : SpanSlopDips * dpiScale;

    // The movement (in physical pixels) a single contact must make before the engine scrolls.
    private static double TouchSlop(Engine engine, double dpiScale) =>
        (engine == Engine.Gecko ? GeckoTouchSlopDips : TouchSlopDips) * dpiScale;

    // The recognizer only starts measuring once the span has changed by the slop, and then measures scale
    // against the span at that moment. So: put the fingers down, move them by the slop in one invisible
    // "pre-roll" step (outward when spreading, inward when closing), and animate from there to
    // factor * pre-rolled span, so the whole visible motion counts.
    private static (double Down, double PreRolled, double End) ContactHalfSpread(double factor, double halfSlop, double halfGap)
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

    // The largest half gap whose widest spread still fits in 'room' (the inverse of ContactHalfSpread's maximum).
    private static double NarrowestGap(double factor, double halfSlop, double room)
    {
        var crossing = halfSlop + 1;
        return factor >= 1 ? (room / factor) - crossing : (room - crossing) * factor;
    }

    // How far the contacts may spread around 'point' inside the bounds, on the roomier axis.
    private static (double Room, bool Vertical) RoomAround(ScreenPoint point, PixelRect bounds)
    {
        var horizontal = Math.Min(point.X - bounds.Left, bounds.Right - 1 - point.X);
        var vertical = Math.Min(point.Y - bounds.Top, bounds.Bottom - 1 - point.Y);
        return horizontal >= vertical ? (horizontal, false) : (vertical, true);
    }

    // A zoom-out ends clamped at the browser's minimum scale, so its focal point does not matter; the contacts
    // are spread horizontally wherever they fit on the anchor's row. A vertical spread, or a spread shrunk to fit
    // near an edge, made Chromium scroll the page by ~54 px on a zoom-out (measured twice), which nothing undoes.
    private static (ScreenPoint Focus, bool Vertical, double Room) PlaceFocusForZoomOut(ScreenPoint anchor, double maxHalf, PixelRect bounds)
    {
        var focus = new ScreenPoint(Clamp(anchor.X, bounds.Left, bounds.Right - 1, maxHalf), Clamp(anchor.Y, bounds.Top, bounds.Bottom - 1, EdgeMargin));
        return (focus, false, Math.Min(focus.X - bounds.Left, bounds.Right - 1 - focus.X));
    }

    // The focal point actually used: the anchor when the contacts fit around it, otherwise the nearest point on
    // the anchor's row where they do, and only when the row is too short for the spread the nearest point on the
    // anchor's column. Returns the room available along the chosen axis.
    private static (ScreenPoint Focus, bool Vertical, double Room) PlaceFocus(ScreenPoint anchor, double maxHalf, PixelRect bounds)
    {
        var horizontal = new ScreenPoint(Clamp(anchor.X, bounds.Left, bounds.Right - 1, maxHalf), Clamp(anchor.Y, bounds.Top, bounds.Bottom - 1, EdgeMargin));
        var vertical = new ScreenPoint(Clamp(anchor.X, bounds.Left, bounds.Right - 1, EdgeMargin), Clamp(anchor.Y, bounds.Top, bounds.Bottom - 1, maxHalf));

        // A focus moved along the row is made up for by a sideways drag, which pages absorb in the zoomed view.
        // A focus moved up or down needs a vertical drag, and that leaked into the page's own scroll position
        // (the table of contents near the top of a Wikipedia page came back 123 px off after the restore), so
        // the row is preferred whenever the spread fits on it at all.
        var horizontalRoom = Math.Min(horizontal.X - bounds.Left, bounds.Right - 1 - horizontal.X);
        var horizontalMove = Distance(anchor, horizontal);
        var verticalMove = Distance(anchor, vertical);
        if (horizontalMove <= verticalMove || horizontalRoom + 1 >= maxHalf) // +1: the clamped focus is a whole pixel
            return (horizontal, false, horizontalRoom);

        return (vertical, true, Math.Min(vertical.Y - bounds.Top, bounds.Bottom - 1 - vertical.Y));
    }

    private bool PinchAround(ScreenPoint focus, bool vertical, double downHalf, double preRolledHalf, double endHalf, TimeSpan duration, Engine engine, CancellationToken cancellationToken)
    {
        var frames = Math.Max(2, (int)Math.Round(duration.TotalMilliseconds / FrameMs));
        var contacts = NewContacts(2);
        var clock = Stopwatch.StartNew();

        PlacePair(contacts, focus, downHalf, vertical);
        if (!Inject(contacts, DownFlags, engine))
            return false;

        var completed = false;
        try
        {
            // Pre-roll: cross the recognizer's slop in one step before the visible animation starts.
            WaitUntil(clock, FrameMs);
            PlacePair(contacts, focus, preRolledHalf, vertical);
            if (!Inject(contacts, UpdateFlags, engine))
                return false;

            var animationStart = clock.Elapsed.TotalMilliseconds;
            for (var frame = 1; frame <= frames; frame++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Frames are scheduled against the clock, not chained sleeps, so timer jitter doesn't accumulate.
                WaitUntil(clock, animationStart + (frame * FrameMs));
                PlacePair(contacts, focus, preRolledHalf + ((endHalf - preRolledHalf) * SmoothStep(frame / (double)frames)), vertical);

                if (!Inject(contacts, UpdateFlags, engine))
                    return false;
            }

            completed = true;
            return true;
        }
        finally
        {
            // Never leave synthetic fingers on the screen, whatever happened above.
            if (!Inject(contacts, POINTER_FLAGS.POINTER_FLAG_UP, engine) && completed)
                LogLiftFailed();
        }
    }

    // One-finger drag that moves the content by 'delta' (content follows the finger), split into legs that fit
    // inside the bounds. Each leg starts with the touch slop so the scrolled distance is the full leg.
    private bool Pan(ScreenPoint delta, PixelRect bounds, double touchSlop, Engine engine, CancellationToken cancellationToken)
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

            if (!Drag(start, end, engine, cancellationToken))
                return false;

            remaining = new ScreenPoint(remaining.X - dx, remaining.Y - dy);
        }

        return true;
    }

    private bool Drag(ScreenPoint from, ScreenPoint to, Engine engine, CancellationToken cancellationToken)
    {
        var frames = Math.Max(2, PanDurationMs / FrameMs);
        var contacts = NewContacts(1);
        var clock = Stopwatch.StartNew();

        PlaceOne(contacts, from);
        if (!Inject(contacts, DownFlags, engine))
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
                if (!Inject(contacts, UpdateFlags, engine))
                    return false;
            }

            // Hold still before lifting so the browser doesn't turn the drag into a fling.
            WaitUntil(clock, (frames * FrameMs) + PanSettleMs);
            if (!Inject(contacts, UpdateFlags, engine))
                return false;

            completed = true;
            return true;
        }
        finally
        {
            if (!Inject(contacts, POINTER_FLAGS.POINTER_FLAG_UP, engine) && completed)
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

    private bool Inject(POINTER_TOUCH_INFO[] contacts, POINTER_FLAGS flags, Engine engine)
    {
        for (var i = 0; i < contacts.Length; i++)
            contacts[i].pointerInfo.pointerFlags = flags;

        var injected = engine == Engine.Gecko ? InjectSynthetic(contacts) : (bool)PInvoke.InjectTouchInput(contacts);
        if (injected)
            return true;

        LogInjectFailed(Marshal.GetLastPInvokeError());
        return false;
    }

    // The same contacts through the synthetic pointer device (see the class remarks).
    private static bool InjectSynthetic(POINTER_TOUCH_INFO[] contacts)
    {
        var pointers = new POINTER_TYPE_INFO[contacts.Length];
        for (var i = 0; i < contacts.Length; i++)
        {
            pointers[i].type = POINTER_INPUT_TYPE.PT_TOUCH;
            pointers[i].Anonymous.touchInfo = contacts[i];
        }

        return s_syntheticDevice is { } device && PInvoke.InjectSyntheticPointerInput(device, pointers);
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

    private bool EnsureDevice(Engine engine) => engine == Engine.Gecko ? EnsureSyntheticDevice() : EnsureInitialized();

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

    private bool EnsureSyntheticDevice()
    {
        lock (InitializationGate)
        {
            if (s_syntheticDevice is not null)
                return true;

            if (s_syntheticDeviceFailed)
                return false;

            var device = PInvoke.CreateSyntheticPointerDevice_SafeHandle(POINTER_INPUT_TYPE.PT_TOUCH, MaxContacts, POINTER_FEEDBACK_MODE.POINTER_FEEDBACK_NONE);
            if (device.IsInvalid)
            {
                s_syntheticDeviceFailed = true;
                LogSyntheticDeviceFailed(Marshal.GetLastPInvokeError());
                device.Dispose();
                return false;
            }

            s_syntheticDevice = device;
            return true;
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "InitializeTouchInjection failed (Win32 error {Error}); browser smart zoom is unavailable.")]
    private partial void LogInitFailed(int error);

    [LoggerMessage(Level = LogLevel.Error, Message = "CreateSyntheticPointerDevice failed (Win32 error {Error}); smart zoom in Firefox is unavailable.")]
    private partial void LogSyntheticDeviceFailed(int error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Touch injection failed (Win32 error {Error}).")]
    private partial void LogInjectFailed(int error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not lift the synthetic touch contacts after a completed gesture.")]
    private partial void LogLiftFailed();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Pinch around ({X}, {Y}) needs {HalfSpread} px of room but the content area offers {Room} px; the gesture was shrunk and will zoom less than planned.")]
    private partial void LogShrunk(int x, int y, int halfSpread, int room);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Anchor ({X}, {Y}) is close to an edge; pinching with a {HalfGap} px half gap so the contacts fit around it.")]
    private partial void LogNarrowed(int x, int y, double halfGap);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Anchor ({AnchorX}, {AnchorY}) is too close to an edge for the contacts; pinched around ({FocusX}, {FocusY}) and panning by ({PanX}, {PanY}).")]
    private partial void LogPanning(int anchorX, int anchorY, int focusX, int focusY, int panX, int panY);
}
