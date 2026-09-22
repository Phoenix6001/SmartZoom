namespace SmartZoom.App.Tray;

/// <summary>The application's own icon, shared by the notification area and the settings window.</summary>
internal static class TrayIcon
{
    private const string Resource = "SmartZoom.App.Resources.SmartZoom.ico";

    /// <summary>Loads the icon at the size the caller wants, or the system default if the resource is missing.</summary>
    /// <param name="size">Desired size; null asks for the size the notification area uses at this scaling.</param>
    public static Icon Load(Size? size = null)
    {
        using var stream = typeof(TrayIcon).Assembly.GetManifestResourceStream(Resource);
        return stream is null ? SystemIcons.Application : new Icon(stream, size ?? SystemInformation.SmallIconSize);
    }
}
