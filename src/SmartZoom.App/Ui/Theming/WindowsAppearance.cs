using Microsoft.Win32;

namespace SmartZoom.App.Ui.Theming;

/// <summary>Reads the palette Windows is using for applications right now.</summary>
/// <remarks>
/// There is no supported API for this, only the value the Settings app writes, so it is read from the registry
/// every time rather than cached: the user can change it while the window is open, and the broadcast that
/// announces it carries no value of its own. Anything unreadable is reported as the light theme, which is what
/// a fresh Windows install uses.
/// </remarks>
internal static class WindowsAppearance
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string AppsUseLightTheme = "AppsUseLightTheme";

    /// <summary>True when Windows is showing applications in the dark palette.</summary>
    public static bool IsDark
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
                return key?.GetValue(AppsUseLightTheme) is int light && light == 0;
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
                return false;
            }
        }
    }
}
