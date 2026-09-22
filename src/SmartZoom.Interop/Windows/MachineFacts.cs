using System.Reflection;
using System.Runtime.InteropServices;

using SmartZoom.Core.Diagnostics;

using Windows.Win32;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.HiDpi;

namespace SmartZoom.Interop.Windows;

/// <summary>Reads the machine's displays and Windows version.</summary>
/// <remarks>
/// Refresh rate is the reason this exists: the gesture frame interval follows the display, and a constant
/// measured on one panel was wrong on another for months with nothing in a bug report to show it.
/// </remarks>
public sealed class MachineFacts : IMachineFacts
{
    /// <inheritdoc />
    /// <remarks>
    /// The application's version, not this assembly's. It version-scopes the whole diagnostics record - a
    /// record from another version is discarded on load - and reading it from SmartZoom.Interop was only
    /// correct because every project in the repository shares one version today.
    /// </remarks>
    public string AppVersion { get; } =
        (Assembly.GetEntryAssembly() ?? typeof(MachineFacts).Assembly).GetName().Version?.ToString() ?? "unknown";

    /// <inheritdoc />
    public string OperatingSystem { get; } = RuntimeInformation.OSDescription;

    /// <inheritdoc />
    public IReadOnlyList<DisplayFacts> Displays => Enumerate();

    private static List<DisplayFacts> Enumerate()
    {
        var displays = new List<DisplayFacts>();

        for (uint i = 0; ; i++)
        {
            var device = new DISPLAY_DEVICEW { cb = (uint)Marshal.SizeOf<DISPLAY_DEVICEW>() };
            if (!PInvoke.EnumDisplayDevices(null, i, ref device, 0))
                break;

            // Mirroring drivers and detached adapters describe no screen anybody is looking at.
            if ((device.StateFlags & DISPLAY_DEVICE_STATE_FLAGS.DISPLAY_DEVICE_ATTACHED_TO_DESKTOP) == 0)
                continue;

            var mode = new DEVMODEW { dmSize = (ushort)Marshal.SizeOf<DEVMODEW>() };
            var deviceName = device.DeviceName.ToString();
            if (!PInvoke.EnumDisplaySettings(deviceName, ENUM_DISPLAY_SETTINGS_MODE.ENUM_CURRENT_SETTINGS, ref mode))
                continue;

            displays.Add(new DisplayFacts(
                (int)mode.dmPelsWidth,
                (int)mode.dmPelsHeight,
                (int)mode.dmDisplayFrequency,
                Scale(mode.dmPosition.x, mode.dmPosition.y),
                Primary: (device.StateFlags & DISPLAY_DEVICE_STATE_FLAGS.DISPLAY_DEVICE_PRIMARY_DEVICE) != 0));
        }

        return displays;
    }

    // DEVMODEW.dmLogPixels is not a usable source of scale: it is the legacy GDI-compatibility field, stuck at
    // 96 regardless of the monitor's actual per-monitor DPI, so it would silently under-report a scaled
    // display (e.g. it reads 96 -> 1.0 on a monitor Windows is actually driving at 200%). The real value comes
    // from GetDpiForMonitor for a point known to be inside this display (dmPosition + 1 px, since dmPosition is
    // the display's desktop-coordinate origin). If that call fails - no monitor at the point, or the API is
    // unavailable - fall back to 1.0 rather than reporting 0, which would read as a fault rather than as
    // "not reported": 1.0 here means "scale unknown", not "confirmed 100%".
    private static double Scale(int x, int y)
    {
        var monitor = PInvoke.MonitorFromPoint(new System.Drawing.Point(x + 1, y + 1), MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
        if (monitor.IsNull)
            return 1.0;

        var hr = PInvoke.GetDpiForMonitor(monitor, MONITOR_DPI_TYPE.MDT_EFFECTIVE_DPI, out var dpiX, out _);
        return hr.Succeeded && dpiX > 0 ? Math.Round(dpiX / 96.0, 2) : 1.0;
    }
}
