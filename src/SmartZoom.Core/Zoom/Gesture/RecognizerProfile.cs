namespace SmartZoom.Core.Zoom.Gesture;

/// <summary>
/// How much movement each gesture recognizer swallows before it accepts that a gesture is happening. Every
/// number here was measured on this project's 200% display; see <c>docs/measurements.md</c> for what each
/// measurement was, and reproduce before changing one.
/// </summary>
/// <remarks>
/// These matter because a recognizer does not merely ignore the movement below its threshold: it starts
/// measuring scale from the span at the moment the threshold is crossed. Feeding it a gesture that ignores
/// the slop therefore comes out visibly short — a requested 1.9x arrived as 1.5x.
/// </remarks>
public static class RecognizerProfile
{
    /// <summary>
    /// Chromium's pinch threshold in device-independent pixels. Its nominal value is 16 DIP; 23 is what a
    /// 200% display actually measured, because 16 delivered 1.75x for a requested 1.9x.
    /// </summary>
    public const double ChromiumSpanSlopDips = 23;

    /// <summary>Movement a single contact must make before Chromium treats it as a scroll rather than a tap.</summary>
    public const double ChromiumTouchSlopDips = 8;

    /// <summary>
    /// Gecko's APZ starts a pinch once the span has changed by this many <em>physical</em> pixels
    /// (<c>PINCH_START_THRESHOLD</c>), and, like Chromium, measures scale from the span at that moment.
    /// </summary>
    public const double GeckoSpanSlopPx = 35;

    /// <summary>Gecko's touch-start tolerance (<c>apz.touch_start_tolerance</c>): 0.1 inch, i.e. 9.6 DIP.</summary>
    public const double GeckoTouchSlopDips = 9.6;

    /// <summary>
    /// Windows' own gesture recognizer, which every application that does not handle raw touch is left with,
    /// PDF readers among them. It asks for far less span than a browser does: 6 DIP, measured in Acrobat.
    /// </summary>
    public const double WindowsSpanSlopDips = 6;

    /// <summary>The same recognizer's threshold for a one-finger drag: it starts moving immediately.</summary>
    public const double WindowsTouchSlopDips = 0;

    /// <summary>The span change, in physical pixels, this engine swallows before it starts measuring a pinch.</summary>
    /// <param name="engine">The recognizer that will read the gesture.</param>
    /// <param name="dpiScale">Device scale factor of the monitor (1.0 at 96 DPI, 2.0 at 200%).</param>
    public static double SpanSlop(GestureEngine engine, double dpiScale) => engine switch
    {
        // Gecko's threshold is in physical pixels already, so it does not scale.
        GestureEngine.Gecko => GeckoSpanSlopPx,
        GestureEngine.Windows => WindowsSpanSlopDips * dpiScale,
        _ => ChromiumSpanSlopDips * dpiScale,
    };

    /// <summary>The movement, in physical pixels, a single contact must make before this engine scrolls.</summary>
    /// <param name="engine">The recognizer that will read the gesture.</param>
    /// <param name="dpiScale">Device scale factor of the monitor.</param>
    public static double TouchSlop(GestureEngine engine, double dpiScale) => engine switch
    {
        GestureEngine.Gecko => GeckoTouchSlopDips * dpiScale,
        GestureEngine.Windows => WindowsTouchSlopDips * dpiScale,
        _ => ChromiumTouchSlopDips * dpiScale,
    };
}
