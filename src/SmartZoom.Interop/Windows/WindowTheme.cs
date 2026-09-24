using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;

namespace SmartZoom.Interop.Windows;

/// <summary>Tells the desktop window manager which palette a window's frame belongs to.</summary>
/// <remarks>
/// A window that paints its own dark chrome still gets a light border and a light system menu unless it says
/// so, which shows up as a pale hairline around a dark window and a white caption when it is maximised. The
/// attribute is honoured from Windows 10 1809 onwards, which is this build's floor; a shell that does not
/// know it simply returns a failure and the frame stays as it was.
/// </remarks>
public static class WindowTheme
{
    /// <summary>Asks for the dark or the light window frame.</summary>
    /// <param name="window">The window handle; ignored when zero.</param>
    /// <param name="dark">True for the dark frame, false for the light one.</param>
    /// <returns>True when the shell accepted the request.</returns>
    public static unsafe bool SetDarkFrame(nint window, bool dark)
    {
        if (window == 0)
            return false;

        var value = dark ? 1 : 0;
        return PInvoke.DwmSetWindowAttribute(
            (HWND)window,
            DWMWINDOWATTRIBUTE.DWMWA_USE_IMMERSIVE_DARK_MODE,
            &value,
            sizeof(int)).Succeeded;
    }
}
