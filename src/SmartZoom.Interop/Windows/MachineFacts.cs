using System.Globalization;
using System.Runtime.InteropServices;

using SmartZoom.Core.Diagnostics;

using Windows.Win32;
using Windows.Win32.Graphics.Gdi;

namespace SmartZoom.Interop.Windows;

/// <summary>Reads the machine's displays and Windows version.</summary>
/// <remarks>
/// Refresh rate is the reason this exists: the gesture frame interval follows the display, and a constant
/// measured on one panel was wrong on another for months with nothing in a bug report to show it.
/// </remarks>
public sealed class MachineFacts : IMachineFacts
{
    /// <inheritdoc />
    public string AppVersion { get; } =
        typeof(MachineFacts).Assembly.GetName().Version?.ToString() ?? "unknown";

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
                Scale(mode),
                Primary: (device.StateFlags & DISPLAY_DEVICE_STATE_FLAGS.DISPLAY_DEVICE_PRIMARY_DEVICE) != 0));
        }

        return displays;
    }

    // dmLogPixels is not populated by EnumDisplaySettings on every driver, so fall back to 1.0 rather than
    // reporting a scale of zero, which would read as a fault rather than as "not reported".
    private static double Scale(DEVMODEW mode) =>
        mode.dmLogPixels > 0 ? Math.Round(mode.dmLogPixels / 96.0, 2) : 1.0;
}
