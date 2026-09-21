using System.Drawing;
using System.Drawing.Imaging;
using Microsoft.Extensions.Logging;
using SmartZoom.Core.Input;
using SmartZoom.Core.Routing;
using SmartZoom.Core.Zoom;
using SmartZoom.Core.Zoom.Content;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace SmartZoom.Interop.Input;

/// <summary>A document reader's content area, scrolled with injected wheel input and measured from the screen.</summary>
/// <remarks>
/// A wheel notch moves a fixed number of pixels, so a distance converts to whole notches. What the reader does
/// with them is another matter: near the end of a document it scrolls less than asked and says nothing, so the
/// movement is measured by comparing a narrow strip of the window before and after
/// (<see cref="ScrollProfile"/>), and that measurement, not the request, is what the caller undoes.
/// </remarks>
public sealed partial class ReaderView(ILogger<ReaderView> logger) : IReaderView
{
    /// <summary>Pixels one wheel notch moves. Measured in Acrobat at every zoom level; also Windows' own default.</summary>
    private const int NotchPixels = 48;

    /// <summary>Width of the strip sampled from the middle of the pane.</summary>
    private const int StripWidth = 96;

    /// <summary>Rows near the pane's edges are skipped: toolbars and page shadows creep in there.</summary>
    private const int StripInset = 24;

    /// <summary>Every fourth column is enough to tell one row of text from another, and is four times as fast.</summary>
    private const int ColumnStep = 4;

    /// <summary>Time for the reader to finish its scrolling animation and repaint before the second measurement.</summary>
    private static readonly TimeSpan SettleTime = TimeSpan.FromMilliseconds(260);

    /// <inheritdoc />
    public PixelRect? Bounds(TargetInfo target)
    {
        ArgumentNullException.ThrowIfNull(target);

        var window = target.HitWindow == 0 ? target.RootWindow : target.HitWindow;
        if (window == 0 || !PInvoke.GetWindowRect(new HWND(window), out var rect))
            return null;

        var bounds = new PixelRect(rect.left, rect.top, rect.right, rect.bottom);
        return bounds.Width <= (2 * StripInset) || bounds.Height <= (4 * StripInset) ? null : bounds;
    }

    /// <inheritdoc />
    public int ScrollBy(TargetInfo target, int pixels, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);

        var notches = (int)Math.Round(pixels / (double)NotchPixels);
        if (notches == 0 || Bounds(target) is not { } bounds)
            return 0;

        var strip = Strip(bounds);
        var before = Capture(strip);

        if (!Wheel(Centre(bounds), notches))
        {
            LogWheelRejected(target.ProcessName);
            return 0;
        }

        Thread.Sleep(SettleTime);
        cancellationToken.ThrowIfCancellationRequested();

        var requested = notches * NotchPixels;
        var after = before is null ? null : Capture(strip);
        if (before is null || after is null)
        {
            // Without a measurement the request is the best estimate available; an exact return is then not possible.
            LogNoMeasurement(target.ProcessName, requested);
            return requested;
        }

        // The reader can only have moved somewhere between not at all and the whole request, so nothing else is
        // worth considering; offering wider choices only invites a wrong one on a page of evenly spaced lines.
        var shift = ScrollProfile.FindShift(before, after, Math.Min(0, requested), Math.Max(0, requested));
        if (!shift.IsClear)
        {
            LogUnclearMeasurement(target.ProcessName, requested, shift.Score, shift.Typical);
            return requested;
        }

        LogScrolled(target.ProcessName, requested, shift.Pixels);
        return shift.Pixels;
    }

    /// <inheritdoc />
    public object? Snapshot(TargetInfo target)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (Bounds(target) is not { } bounds)
            return null;

        var strip = Strip(bounds);
        var profile = Capture(strip);
        return profile is null ? null : new ViewMark(strip, profile);
    }

    /// <inheritdoc />
    public bool ScrollBackTo(TargetInfo target, object snapshot, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (snapshot is not ViewMark mark || Bounds(target) is not { } bounds)
            return false;

        var strip = Strip(bounds);
        if (strip != mark.Strip)
            return false;       // the window moved or was resized; the two views are not comparable

        var now = Capture(strip);
        if (now is null)
            return false;

        // Only the drift a gesture can leave behind is plausible, and a search no wider keeps a page of evenly
        // spaced lines from matching at the wrong line.
        var drift = ScrollProfile.FindShift(mark.Profile, now, -MaxDrift(bounds), MaxDrift(bounds));
        if (!drift.IsClear)
            return false;

        if (Math.Abs(drift.Pixels) >= NotchPixels / 2)
        {
            LogAligning(target.ProcessName, drift.Pixels);
            ScrollBy(target, -drift.Pixels, cancellationToken);
        }

        return true;
    }

    private static int MaxDrift(PixelRect bounds) => Math.Max(NotchPixels, bounds.Height / 3);

    private static ScreenPoint Centre(PixelRect bounds) =>
        new((int)Math.Round(bounds.CenterX), (int)Math.Round(bounds.CenterY));

    private static PixelRect Strip(PixelRect pane)
    {
        var half = Math.Min(StripWidth, pane.Width - (2 * StripInset)) / 2;
        var centre = (int)Math.Round(pane.CenterX);
        return new PixelRect(centre - half, pane.Top + StripInset, centre + half, pane.Bottom - StripInset);
    }

    /// <summary>Average brightness of each row of the strip, or null when the screen could not be read.</summary>
    private static int[]? Capture(PixelRect strip)
    {
        try
        {
            using var bitmap = new Bitmap(strip.Width, strip.Height, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(bitmap))
                graphics.CopyFromScreen(strip.Left, strip.Top, 0, 0, new Size(strip.Width, strip.Height), CopyPixelOperation.SourceCopy);

            return RowBrightness(bitmap);
        }
        catch (InvalidOperationException)
        {
            return null;    // the window moved or the display changed mid-capture
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;    // the screen could not be read (a secure desktop, a display mode change)
        }
    }

    private static unsafe int[] RowBrightness(Bitmap bitmap)
    {
        var rows = new int[bitmap.Height];
        var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var samples = 0;
            for (var x = 0; x < bitmap.Width; x += ColumnStep)
                samples++;

            for (var y = 0; y < bitmap.Height; y++)
            {
                var line = (byte*)data.Scan0 + ((long)y * data.Stride);
                var total = 0;
                for (var x = 0; x < bitmap.Width; x += ColumnStep)
                {
                    var pixel = line + (x * 4);
                    total += pixel[0] + pixel[1] + pixel[2];     // blue + green + red is enough to rank rows
                }

                rows[y] = total / (samples * 3);
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        return rows;
    }

    /// <summary>Sends wheel notches to the window under the point, leaving the cursor where it was.</summary>
    private static bool Wheel(ScreenPoint point, int notches)
    {
        var restore = PInvoke.GetCursorPos(out var cursor);
        try
        {
            PInvoke.SetCursorPos(point.X, point.Y);
            return InputInjection.TrySendWheel(-notches, out _);
        }
        finally
        {
            if (restore)
                PInvoke.SetCursorPos(cursor.X, cursor.Y);
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Scrolled {Process} by {Requested} px as asked; the content moved {Moved} px.")]
    private partial void LogScrolled(string? process, int requested, int moved);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Could not read the screen to measure the scroll in {Process}; assuming the requested {Requested} px.")]
    private partial void LogNoMeasurement(string? process, int requested);

    [LoggerMessage(Level = LogLevel.Debug, Message = "The scroll in {Process} could not be measured (best match {Score:F1} against {Typical:F1} for a middling offset); assuming the requested {Requested} px.")]
    private partial void LogUnclearMeasurement(string? process, int requested, double score, double typical);

    [LoggerMessage(Level = LogLevel.Debug, Message = "The gesture left {Process} {Drift} px from where it started; scrolling back.")]
    private partial void LogAligning(string? process, int drift);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Wheel input was rejected for {Process}; the view was not scrolled.")]
    private partial void LogWheelRejected(string? process);

    /// <summary>How a reader's view looked at one moment: the strip that was sampled and its row brightness.</summary>
    private sealed record ViewMark(PixelRect Strip, int[] Profile);
}
