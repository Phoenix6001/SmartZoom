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

    // The frame interval is clamped to this range: below it a 240 Hz panel would ask for a 4 ms cadence the
    // thread cannot keep, above it a misreported slow mode would make the zoom stutter.
    private const int MinFrameMs = 8;
    private const int MaxFrameMs = 20;

    private const int PanDurationMs = 140;
    private const int PanSettleMs = 60;
    private const uint TouchMaskContactAreaOrientationPressure = 0x1 | 0x2 | 0x4;

    // The shape of a synthetic finger. Chosen, not measured: any plausible contact is accepted, and every
    // gesture in docs/measurements.md was measured with these.
    private const uint ContactOrientation = 90;
    private const uint ContactPressure = 32000;
    private const int ContactHalfSize = 2;
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
        var frameMs = RefreshPeriodMs(anchor);
        var plan = PinchGeometry.Plan(anchor, factor, RecognizerProfile.SpanSlop(engine, dpiScale), bounds);

        // Only when the gap shrank: the same path also handles "the same gap, turned the other way round",
        // which is not a narrowing and must not be logged as one.
        if (plan.NarrowedHalfGap is { } narrowed && narrowed < PinchGeometry.HalfGap)
            LogNarrowed(anchor.X, anchor.Y, narrowed);

        if (plan.Shortfall is { } shortfall)
            LogShrunk(anchor.X, anchor.Y, (int)Math.Ceiling(shortfall.NeededHalfSpread), (int)shortfall.Room);

        // Windows moves the mouse pointer along with injected touch contacts; put it back afterwards so the
        // user's pointer (and therefore the target of their next press) stays where they left it.
        var hadCursor = PInvoke.GetCursorPos(out var cursorBefore);
        try
        {
            if (!PinchAround(plan, duration, frameMs, engine, cancellationToken))
                return false;

            if (plan.Pan.X == 0 && plan.Pan.Y == 0)
                return true;

            LogPanning(anchor.X, anchor.Y, plan.Focus.X, plan.Focus.Y, plan.Pan.X, plan.Pan.Y);
            return Pan(plan.Pan, bounds, RecognizerProfile.TouchSlop(engine, dpiScale), frameMs, engine, cancellationToken);
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

    // Injecting faster than the screen can show is not smoothness, it is waste — and measurably worse than
    // waste: at 8 ms on a 59 Hz panel (16.9 ms a refresh) a third of the frames go out late, by up to 13.7 ms,
    // because the thread cannot reliably be woken that often. Two samples per refresh, arriving at uneven
    // times, leave it to the browser's input sampling which one it happens to see, and browsers do that
    // differently. One sample per refresh removes the beat entirely.
    /// <summary>
    /// The refresh period of the monitor showing a point, in whole milliseconds, clamped to
    /// <see cref="MinFrameMs"/>..<see cref="MaxFrameMs"/>.
    /// </summary>
    /// <remarks>
    /// Read per gesture, once per press, so a display plugged in or unplugged while the app runs is honoured
    /// by the next press, and a gesture on a second monitor is paced for that monitor.
    /// </remarks>
    private static unsafe int RefreshPeriodMs(ScreenPoint point)
    {
        var monitor = PInvoke.MonitorFromPoint(new System.Drawing.Point(point.X, point.Y), MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
        if (monitor.IsNull)
            return DefaultFrameMs;

        var info = new MONITORINFOEXW();
        info.monitorInfo.cbSize = (uint)sizeof(MONITORINFOEXW);
        if (!PInvoke.GetMonitorInfo(monitor, (MONITORINFO*)&info))
            return DefaultFrameMs;

        var mode = new DEVMODEW { dmSize = (ushort)Marshal.SizeOf<DEVMODEW>() };
        if (!PInvoke.EnumDisplaySettings(info.szDevice.ToString(), ENUM_DISPLAY_SETTINGS_MODE.ENUM_CURRENT_SETTINGS, ref mode))
            return DefaultFrameMs;

        // 0 and 1 are the documented "default/unknown" answers.
        var hz = mode.dmDisplayFrequency;
        return hz <= 1 ? DefaultFrameMs : Math.Clamp((int)Math.Round(1000.0 / hz), MinFrameMs, MaxFrameMs);
    }

    private bool PinchAround(PinchPlan plan, TimeSpan duration, int interval, GestureEngine engine, CancellationToken cancellationToken)
    {
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

            // Only animated gestures are reported: a zero-duration pinch (the stale-zoom state reset a caller
            // can ask for) still runs the minimum 2 frames above, which are trivially on time and would dilute
            // the late-frame ratio this section exists to surface. Nobody sees that pinch happen, so the report
            // must not describe it as one.
            if (duration > TimeSpan.Zero)
                ReportPacing(frames, interval, lateFrames, worstLate);

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
    private bool Pan(ScreenPoint delta, PixelRect bounds, double touchSlop, int frameMs, GestureEngine engine, CancellationToken cancellationToken)
    {
        foreach (var leg in PinchGeometry.PanLegs(delta, bounds, touchSlop))
        {
            if (!Drag(leg, frameMs, engine, cancellationToken))
                return false;
        }

        return true;
    }

    private bool Drag(PanLeg leg, int frameMs, GestureEngine engine, CancellationToken cancellationToken)
    {
        var frames = Math.Max(2, PanDurationMs / frameMs);
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
                WaitUntil(clock, frame * frameMs);

                var t = Easing.SmoothStep(frame / (double)frames);
                PlaceOne(contacts, new ScreenPoint(
                    (int)Math.Round(leg.From.X + ((leg.To.X - leg.From.X) * t)),
                    (int)Math.Round(leg.From.Y + ((leg.To.Y - leg.From.Y) * t))));

                if (!devices.Inject(contacts, UpdateFlags, engine))
                    return false;
            }

            // Hold still before lifting so the browser doesn't turn the drag into a fling.
            WaitUntil(clock, (frames * frameMs) + PanSettleMs);
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
            contacts[i].orientation = ContactOrientation;
            contacts[i].pressure = ContactPressure;
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
        contact.rcContact = new RECT { left = x - ContactHalfSize, top = y - ContactHalfSize, right = x + ContactHalfSize, bottom = y + ContactHalfSize };
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

    /// <summary>
    /// Hands the gesture's pacing to diagnostics, swallowing anything the sink throws.
    /// </summary>
    /// <remarks>
    /// Diagnostics must never abort a gesture with contacts down. This is the only recording site on the zoom
    /// path itself, in the middle of a gesture whose contacts are still on the screen, so it carries its own
    /// guard: a record that fails costs a log line, not a half-finished pinch with synthetic fingers left behind.
    /// </remarks>
    private void ReportPacing(int frames, int intervalMs, int lateFrames, double worstLateMs)
    {
        try
        {
            pacing?.Paced(frames, intervalMs, lateFrames, worstLateMs);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogPacingNotRecorded(ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not lift the synthetic touch contacts after a completed gesture.")]
    private partial void LogLiftFailed();

    [LoggerMessage(Level = LogLevel.Debug, Message = "Failed to record this gesture's pacing; the gesture itself is unaffected.")]
    private partial void LogPacingNotRecorded(Exception exception);

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
