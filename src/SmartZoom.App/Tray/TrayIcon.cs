namespace SmartZoom.App.Tray;

/// <summary>The application's own icon, shared by the notification area and the settings window.</summary>
internal static class TrayIcon
{
    private const string Resource = "SmartZoom.App.Resources.SmartZoom.ico";

    // One instance for the life of the process: neither NotifyIcon nor Form disposes an icon handed to it,
    // and the fallback is a system icon that must never be disposed at all.
    private static readonly Lazy<Icon> Cached = new(Create);

    /// <summary>The icon at the size the notification area uses at this scaling, or the system default if the resource is missing.</summary>
    public static Icon Load() => Cached.Value;

    private static Icon Create()
    {
        using var stream = typeof(TrayIcon).Assembly.GetManifestResourceStream(Resource);
        return stream is null ? SystemIcons.Application : new Icon(stream, SystemInformation.SmallIconSize);
    }
}
