using SmartZoom.Core.Input;
using SmartZoom.Core.Windows;

using Windows.Win32;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.HiDpi;

namespace SmartZoom.Interop.Windows;

/// <summary>Reads the scale of the monitor showing a point.</summary>
public sealed class DisplayScale : IDisplayScale
{
    /// <inheritdoc />
    public double ScaleAt(ScreenPoint point) => At(point);

    /// <summary>Device scale factor of the monitor showing the point (1.0 at 96 DPI, 2.0 at 200%).</summary>
    /// <remarks>
    /// Asked of the monitor, not of the window under the point. <c>GetDpiForWindow</c> answers "what DPI is
    /// this window being scaled for", which depends on the window's own DPI awareness: a system-aware
    /// application reports the primary display's DPI wherever it is, and an unaware one always reports 96.
    /// That is the wrong question here: what is wanted is a distance in physical pixels on the display the
    /// gesture lands on, so a system-aware PDF reader on a differently scaled second monitor would be
    /// measured for the wrong one. <c>MonitorFromPoint</c> + <c>GetDpiForMonitor</c> answer about the
    /// display, which is what the question is. 1.0 means "not reported", not "confirmed 100%".
    /// </remarks>
    /// <param name="point">A point on the desktop, in physical pixels.</param>
    internal static double At(ScreenPoint point)
    {
        var monitor = PInvoke.MonitorFromPoint(new System.Drawing.Point(point.X, point.Y), MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
        if (monitor.IsNull)
            return 1.0;

        var hr = PInvoke.GetDpiForMonitor(monitor, MONITOR_DPI_TYPE.MDT_EFFECTIVE_DPI, out var dpiX, out _);
        return hr.Succeeded && dpiX > 0 ? dpiX / 96.0 : 1.0;
    }
}
