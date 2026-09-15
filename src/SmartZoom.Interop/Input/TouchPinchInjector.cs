using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using SmartZoom.Core.Input;
using SmartZoom.Core.Zoom;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.Pointer;
using Windows.Win32.UI.WindowsAndMessaging;

namespace SmartZoom.Interop.Input;

/// <summary>Performs pinch gestures by injecting two synthetic touch contacts (<c>InjectTouchInput</c>).</summary>
/// <remarks>
/// <para>Touch injection needs no admin rights and no touch hardware, but like all input injection it is
/// blocked by UIPI when the target window is elevated.</para>
/// <para>Contacts move along a horizontal line through the anchor. Zooming in starts them close together and
/// spreads them; zooming out starts them spread and closes them. Their maximum spread is about
/// <see cref="HalfGap"/> / factor plus the slop allowance, which is what
/// <c>BrowserZoomSettings.AnchorInsetPx</c> must accommodate.</para>
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

    private const int FrameMs = 16;
    private const uint MaxContacts = 2;
    private const uint TouchMaskContactAreaOrientationPressure = 0x1 | 0x2 | 0x4;

    private static readonly Lock InitializationGate = new();
    private static bool? s_initialized;

    /// <inheritdoc />
    public int MaxContactOffset(double factor)
    {
        ValidateFactor(factor);

        // Worst case is a 200% display's slop; the caller adds its own safety margin on top.
        var (startHalf, endHalf) = ContactHalfSpread(factor, halfSlop: SpanSlopDips);
        return (int)Math.Ceiling(Math.Max(startHalf, endHalf));
    }

    /// <inheritdoc />
    public Task<bool> PinchAsync(ScreenPoint anchor, double factor, TimeSpan duration, CancellationToken cancellationToken)
    {
        ValidateFactor(factor);
        return Task.Run(() => Pinch(anchor, factor, duration, cancellationToken), cancellationToken);
    }

    private static void ValidateFactor(double factor)
    {
        if (factor <= 0 || double.IsNaN(factor))
            throw new ArgumentOutOfRangeException(nameof(factor), factor, "Factor must be positive.");
    }

    // The recognizer's reference span is the initial span moved by the slop (outward when spreading,
    // inward when closing), so aim the final span at factor * reference span.
    private static (double StartHalf, double EndHalf) ContactHalfSpread(double factor, double halfSlop)
    {
        if (factor >= 1)
            return (HalfGap, factor * (HalfGap + halfSlop));

        return ((HalfGap / factor) + halfSlop, HalfGap);
    }

    private bool Pinch(ScreenPoint anchor, double factor, TimeSpan duration, CancellationToken cancellationToken)
    {
        if (!EnsureInitialized())
            return false;

        var (startHalf, endHalf) = ContactHalfSpread(factor, halfSlop: SpanSlopDips * DpiScaleAt(anchor) / 2);
        var frames = Math.Max(2, (int)Math.Round(duration.TotalMilliseconds / FrameMs));

        var contacts = new POINTER_TOUCH_INFO[2];
        for (uint i = 0; i < 2; i++)
        {
            contacts[i].pointerInfo.pointerType = POINTER_INPUT_TYPE.PT_TOUCH;
            contacts[i].pointerInfo.pointerId = i;
            contacts[i].touchMask = TouchMaskContactAreaOrientationPressure;
            contacts[i].orientation = 90;
            contacts[i].pressure = 32000;
        }

        Place(contacts, anchor, startHalf);
        if (!Inject(contacts, POINTER_FLAGS.POINTER_FLAG_DOWN | POINTER_FLAGS.POINTER_FLAG_INRANGE | POINTER_FLAGS.POINTER_FLAG_INCONTACT))
            return false;

        var completed = false;
        try
        {
            for (var frame = 1; frame <= frames; frame++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Thread.Sleep(FrameMs);

                // Ease-out: most of the motion early, settling gently, like a real gesture.
                var t = frame / (double)frames;
                t = 1 - Math.Pow(1 - t, 3);
                Place(contacts, anchor, startHalf + ((endHalf - startHalf) * t));

                if (!Inject(contacts, POINTER_FLAGS.POINTER_FLAG_UPDATE | POINTER_FLAGS.POINTER_FLAG_INRANGE | POINTER_FLAGS.POINTER_FLAG_INCONTACT))
                    return false;
            }

            completed = true;
            return true;
        }
        finally
        {
            // Never leave synthetic fingers on the screen, whatever happened above.
            var lifted = Inject(contacts, POINTER_FLAGS.POINTER_FLAG_UP);
            if (!lifted && completed)
                LogLiftFailed();
        }
    }

    // Device scale factor of the monitor showing the anchor (1.0 at 96 DPI, 2.0 at 200%).
    private static double DpiScaleAt(ScreenPoint anchor)
    {
        var window = PInvoke.WindowFromPoint(new System.Drawing.Point(anchor.X, anchor.Y));
        var dpi = window.IsNull ? 0 : PInvoke.GetDpiForWindow(window);
        return dpi == 0 ? 1.0 : dpi / 96.0;
    }

    private static void Place(POINTER_TOUCH_INFO[] contacts, ScreenPoint anchor, double half)
    {
        for (var i = 0; i < 2; i++)
        {
            var x = (int)Math.Round(anchor.X + (i == 0 ? -half : half));
            contacts[i].pointerInfo.ptPixelLocation = new System.Drawing.Point(x, anchor.Y);
            contacts[i].rcContact = new RECT { left = x - 2, top = anchor.Y - 2, right = x + 2, bottom = anchor.Y + 2 };
        }
    }

    private bool Inject(POINTER_TOUCH_INFO[] contacts, POINTER_FLAGS flags)
    {
        contacts[0].pointerInfo.pointerFlags = flags;
        contacts[1].pointerInfo.pointerFlags = flags;

        if (PInvoke.InjectTouchInput(contacts))
            return true;

        LogInjectFailed(Marshal.GetLastPInvokeError());
        return false;
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not lift the synthetic touch contacts after a completed pinch.")]
    private partial void LogLiftFailed();
}
