using System.Diagnostics;
using System.Runtime.InteropServices;

using Microsoft.Extensions.Logging;

using SmartZoom.Core.Diagnostics;
using SmartZoom.Core.Input;
using SmartZoom.Core.Zoom;
using SmartZoom.Core.Zoom.Content;
using SmartZoom.Core.Zoom.Gesture;
using SmartZoom.Interop.Windows;

using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.Input.Pointer;
using Windows.Win32.UI.WindowsAndMessaging;

namespace SmartZoom.Interop.Input;

/// <summary>Performs pinch gestures by injecting synthetic touch contacts.</summary>
/// <remarks>
/// The arithmetic — where the contacts go, how far they travel, what to do when they do not fit — lives in
/// <see cref="PinchGeometry"/>, in a project that can be tested. What is left here is the scheduling of the
/// frames and the calls into Win32.
/// </remarks>
public sealed partial class TouchPinchInjector(TouchDevices devices, ILogger<TouchPinchInjector> logger, IGesturePacingSink? pacing = null) : IPinchInjector
{
    // Fallback when the display refuses to say how fast it refreshes.
    private const int DefaultFrameMs = 8;


    // Injecting faster than the screen can show is not smoothness, it is waste — and measurably worse than
    // waste here: at 8 ms on a 59 Hz panel (16.9 ms a refresh) a third of the frames went out late, by up to
    // 13.7 ms, because the thread cannot reliably be woken that often. Two samples per refresh, arriving at
    // uneven times, leave it to the browser's input sampling which one it happens to see, and browsers do
    // that differently. One sample per refresh removes the beat entirely.
    private static readonly int FrameMs = RefreshPeriodMs();

    /// <summary>The display's refresh period in whole milliseconds, clamped to something sane.</summary>
    private static int RefreshPeriodMs()
    {
        var mode = new DEVMODEW { dmSize = (ushort)Marshal.SizeOf<DEVMODEW>() };
        if (!PInvoke.EnumDisplaySettings(null, ENUM_DISPLAY_SETTINGS_MODE.ENUM_CURRENT_SETTINGS, ref mode))
            return DefaultFrameMs;

        var hz = mode.dmDisplayFrequency;

        // 0 and 1 are the documented "default/unknown" answers. The clamp keeps a 240 Hz panel from asking
        // for a 4 ms cadence the thread cannot keep, and a misreported slow one from making the zoom stutter.
        return hz <= 1 ? DefaultFrameMs : Math.Clamp((int)Math.Round(1000.0 / hz), 8, 20);
    }
    private const int PanDurationMs = 140;
    private const int PanSettleMs = 60;
    private const uint TouchMaskContactAreaOrientationPressure = 0x1 | 0x2 | 0x4;
    private const POINTER_FLAGS DownFlags = POINTER_FLAGS.POINTER_FLAG_DOWN | POINTER_FLAGS.POINTER_FLAG_INRANGE | POINTER_FLAGS.POINTER_FLAG_INCONTACT;
    private const POINTER_FLAGS UpdateFlags = POINTER_FLAGS.POINTER_FLAG_UPDATE | POINTER_FLAGS.POINTER_FLAG_INRANGE | POINTER_FLAGS.POINTER_FLAG_INCONTACT;

    /// <inheritdoc />
    public Task<bool> PinchAsync(ScreenPoint anchor, double factor, TimeSpan duration, PixelRect bounds, CancellationToken cancellationToken)
    {
        if (factor <= 0 || double.IsNaN(factor))
            throw new ArgumentOutOfRangeException(nameof(factor), factor, "Factor must be positive.");

        return Task.Run(() => Pinch(anchor, factor, duration, bounds, cancellationToken), cancellationToken);
    }

    private bool Pinch(ScreenPoint anchor, double factor, TimeSpan duration, PixelRect bounds, CancellationToken cancellationToken)
    {
        var engine = Recognize(anchor);
        if (!devices.Ensure(engine))
            return false;

        var dpiScale = DpiScaleAt(anchor);
        var plan = PinchGeometry.Plan(anchor, factor, RecognizerProfile.SpanSlop(engine, dpiScale), bounds);

        // Only when the gap actually shrank: the same path also handles "the same gap, turned the other way
        // round", and reporting that as a narrowing was confusing in the log.
        if (plan.NarrowedHalfGap is { } narrowed && narrowed < PinchGeometry.HalfGap)
            LogNarrowed(anchor.X, anchor.Y, narrowed);

        if (plan.Shortfall is { } shortfall)
            LogShrunk(anchor.X, anchor.Y, (int)Math.Ceiling(shortfall.NeededHalfSpread), (int)shortfall.Room);

        // Windows moves the mouse pointer along with injected touch contacts; put it back afterwards so the
        // user's pointer (and therefore the target of their next press) stays where they left it.
        var hadCursor = PInvoke.GetCursorPos(out var cursorBefore);
        try
        {
            if (!PinchAround(plan, duration, engine, cancellationToken))
                return false;

            if (plan.Pan.X == 0 && plan.Pan.Y == 0)
                return true;

            LogPanning(anchor.X, anchor.Y, plan.Focus.X, plan.Focus.Y, plan.Pan.X, plan.Pan.Y);
            return Pan(plan.Pan, bounds, RecognizerProfile.TouchSlop(engine, dpiScale), engine, cancellationToken);
        }
        finally
        {
            RestoreCursor(hadCursor, cursorBefore);
        }
    }

    // Which recognizer will read the gesture, from the window it lands on. Only the thresholds differ, except
    // for Gecko, which also refuses touch from the ordinary injection device.
    private static GestureEngine Recognize(ScreenPoint anchor)
    {
        if (BrowserWindows.IsGeckoWindowAt(anchor))
            return GestureEngine.Gecko;

        return BrowserWindows.IsChromiumWindowAt(anchor) ? GestureEngine.Chromium : GestureEngine.Windows;
    }

    private bool PinchAround(PinchPlan plan, TimeSpan duration, GestureEngine engine, CancellationToken cancellationToken)
    {
        var interval = FrameMs;
        var frames = Math.Max(2, (int)Math.Round(duration.TotalMilliseconds / interval));
        var contacts = NewContacts(2);
        var clock = Stopwatch.StartNew();

        PlacePair(contacts, plan, plan.DownHalf);
        if (!devices.Inject(contacts, DownFlags, engine))
            return false;

        var completed = false;
        try
        {
            // Pre-roll: cross the recognizer's slop in one step before the visible animation starts.
            WaitUntil(clock, interval);
            PlacePair(contacts, plan, plan.PreRolledHalf);
            if (!devices.Inject(contacts, UpdateFlags, engine))
                return false;

            var animationStart = clock.Elapsed.TotalMilliseconds;
            var travel = plan.EndHalf - plan.PreRolledHalf;

            // How late each frame actually went out. A gesture is only as smooth as its pacing, and a thread
            // that loses the CPU mid-pinch produces a visible stutter that no amount of easing can hide —
            // which is indistinguishable, from the outside, from the browser rendering it badly.
            var worstLate = 0.0;
            var lateFrames = 0;

            for (var frame = 1; frame <= frames; frame++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Frames are scheduled against the clock, not chained sleeps, so timer jitter doesn't accumulate.
                var due = animationStart + (frame * interval);
                WaitUntil(clock, due);

                var late = clock.Elapsed.TotalMilliseconds - due;
                if (late > worstLate)
                    worstLate = late;
                if (late > interval)
                    lateFrames++;

                PlacePair(contacts, plan, plan.PreRolledHalf + (travel * Easing.SmoothStep(frame / (double)frames)));

                if (!devices.Inject(contacts, UpdateFlags, engine))
                    return false;
            }

            LogPacing(frames, interval, worstLate, lateFrames, clock.Elapsed.TotalMilliseconds - animationStart);
            pacing?.Paced(frames, interval, lateFrames, worstLate);

            completed = true;
            return true;
        }
        finally
        {
            // Never leave synthetic fingers on the screen, whatever happened above.
            if (!devices.Inject(contacts, POINTER_FLAGS.POINTER_FLAG_UP, engine) && completed)
                LogLiftFailed();
        }
    }

    // One-finger drag that moves the content by 'delta' (content follows the finger).
    private bool Pan(ScreenPoint delta, PixelRect bounds, double touchSlop, GestureEngine engine, CancellationToken cancellationToken)
    {
        foreach (var leg in PinchGeometry.PanLegs(delta, bounds, touchSlop))
        {
            if (!Drag(leg, engine, cancellationToken))
                return false;
        }

        return true;
    }

    private bool Drag(PanLeg leg, GestureEngine engine, CancellationToken cancellationToken)
    {
        var frames = Math.Max(2, PanDurationMs / FrameMs);
        var contacts = NewContacts(1);
        var clock = Stopwatch.StartNew();

        PlaceOne(contacts, leg.From);
        if (!devices.Inject(contacts, DownFlags, engine))
            return false;

        var completed = false;
        try
        {
            for (var frame = 1; frame <= frames; frame++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                WaitUntil(clock, frame * FrameMs);

                var t = Easing.SmoothStep(frame / (double)frames);
                PlaceOne(contacts, new ScreenPoint(
                    (int)Math.Round(leg.From.X + ((leg.To.X - leg.From.X) * t)),
                    (int)Math.Round(leg.From.Y + ((leg.To.Y - leg.From.Y) * t))));

                if (!devices.Inject(contacts, UpdateFlags, engine))
                    return false;
            }

            // Hold still before lifting so the browser doesn't turn the drag into a fling.
            WaitUntil(clock, (frames * FrameMs) + PanSettleMs);
            if (!devices.Inject(contacts, UpdateFlags, engine))
                return false;

            completed = true;
            return true;
        }
        finally
        {
            if (!devices.Inject(contacts, POINTER_FLAGS.POINTER_FLAG_UP, engine) && completed)
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

    private static void PlacePair(POINTER_TOUCH_INFO[] contacts, PinchPlan plan, double half)
    {
        var (first, second) = PinchGeometry.Contacts(half, plan.Focus, plan.Vertical);
        SetLocation(ref contacts[0], first.X, first.Y);
        SetLocation(ref contacts[1], second.X, second.Y);
    }

    private static void PlaceOne(POINTER_TOUCH_INFO[] contacts, ScreenPoint at) => SetLocation(ref contacts[0], at.X, at.Y);

    private static void SetLocation(ref POINTER_TOUCH_INFO contact, int x, int y)
    {
        contact.pointerInfo.ptPixelLocation = new System.Drawing.Point(x, y);
        contact.rcContact = new RECT { left = x - 2, top = y - 2, right = x + 2, bottom = y + 2 };
    }

    // Device scale factor of the monitor showing the point (1.0 at 96 DPI, 2.0 at 200%).
    private static double DpiScaleAt(ScreenPoint point)
    {
        var window = PInvoke.WindowFromPoint(new System.Drawing.Point(point.X, point.Y));
        var dpi = window.IsNull ? 0 : PInvoke.GetDpiForWindow(window);
        return dpi == 0 ? 1.0 : dpi / 96.0;
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not lift the synthetic touch contacts after a completed gesture.")]
    private partial void LogLiftFailed();

    [LoggerMessage(Level = LogLevel.Debug,
        Message = "Gesture pacing: {Frames} frames at {TargetMs} ms took {ActualMs:F0} ms; " +
            "worst frame was {WorstLateMs:F1} ms late, {LateFrames} frame(s) missed their slot.")]
    private partial void LogPacing(int frames, int targetMs, double worstLateMs, int lateFrames, double actualMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Pinch around ({X}, {Y}) needs {HalfSpread} px of room but the content area offers {Room} px; the gesture was shrunk and will zoom less than planned.")]
    private partial void LogShrunk(int x, int y, int halfSpread, int room);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Anchor ({X}, {Y}) is close to an edge; pinching with a {HalfGap} px half gap so the contacts fit around it.")]
    private partial void LogNarrowed(int x, int y, double halfGap);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Anchor ({AnchorX}, {AnchorY}) is too close to an edge for the contacts; pinched around ({FocusX}, {FocusY}) and panning by ({PanX}, {PanY}).")]
    private partial void LogPanning(int anchorX, int anchorY, int focusX, int focusY, int panX, int panY);
}
