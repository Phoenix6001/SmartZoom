namespace SmartZoom.Core.Zoom.Content;

/// <summary>
/// The arithmetic of measuring a document reader from the screen: which strip of the window to sample, how a
/// distance becomes whole wheel notches, and how much drift is worth correcting.
/// </summary>
/// <remarks>
/// Readers expose no scroll position, so a scroll is requested with wheel input and then measured by
/// comparing a narrow strip of the window before and after. Everything here is about making that comparison
/// mean something: sample away from the edges, where toolbars and page shadows creep in, and never ask a
/// question the reader cannot answer.
/// </remarks>
public static class ReaderViewGeometry
{
    /// <summary>Pixels one wheel notch moves. Measured in Acrobat at every zoom level; also Windows' own default.</summary>
    public const int NotchPixels = 48;

    /// <summary>Width of the strip sampled from the middle of the pane.</summary>
    public const int StripWidth = 96;

    /// <summary>Rows near the pane's edges are skipped: toolbars and page shadows creep in there.</summary>
    public const int StripInset = 24;

    /// <summary>Whether a pane is big enough for a strip that means anything.</summary>
    /// <param name="pane">The reader's content area.</param>
    public static bool IsMeasurable(PixelRect pane) => pane.Width > 2 * StripInset && pane.Height > 4 * StripInset;

    /// <summary>The strip of screen to sample for a pane: a column down its middle, clear of both ends.</summary>
    /// <param name="pane">The reader's content area.</param>
    public static PixelRect Strip(PixelRect pane)
    {
        var half = Math.Min(StripWidth, pane.Width - (2 * StripInset)) / 2;
        var centre = (int)Math.Round(pane.CenterX);
        return new PixelRect(centre - half, pane.Top + StripInset, centre + half, pane.Bottom - StripInset);
    }

    /// <summary>Whole wheel notches for a distance in pixels, rounded to the nearest.</summary>
    /// <param name="pixels">Requested movement; positive moves the view towards the end of the document.</param>
    public static int Notches(int pixels) => (int)Math.Round(pixels / (double)NotchPixels);

    /// <summary>The distance whole notches actually cover.</summary>
    /// <param name="notches">Number of notches.</param>
    public static int Pixels(int notches) => notches * NotchPixels;

    /// <summary>
    /// The widest drift worth searching for after a gesture. A search no wider keeps a page of evenly spaced
    /// lines from matching at the wrong line.
    /// </summary>
    /// <param name="pane">The reader's content area.</param>
    public static int MaxDrift(PixelRect pane) => Math.Max(NotchPixels, pane.Height / 3);

    /// <summary>
    /// Whether a measured drift is worth correcting. Corrections come in whole notches, so anything under
    /// half a notch would overshoot by more than it fixed.
    /// </summary>
    /// <param name="driftPixels">The drift, signed.</param>
    public static bool IsWorthCorrecting(int driftPixels) => Math.Abs(driftPixels) >= NotchPixels / 2;
}
