using Windows.Win32;

namespace SmartZoom.Interop;

/// <summary>System-wide input settings.</summary>
public static class SystemInput
{
    /// <summary>The user's configured double-click time in milliseconds (Control Panel, Mouse).</summary>
    public static uint DoubleClickTimeMs => PInvoke.GetDoubleClickTime();
}
