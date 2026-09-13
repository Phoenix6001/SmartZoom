namespace SmartZoom.App;

/// <summary>Well-known per-user file locations.</summary>
internal sealed record AppPaths(string SettingsDirectory, string LogDirectory)
{
    public string SettingsFile => Path.Combine(SettingsDirectory, "settings.json");

    /// <summary>
    /// Settings roam (<c>%APPDATA%</c>) so preferences follow the user; logs are machine-local
    /// (<c>%LOCALAPPDATA%</c>) because they describe this machine's windows and processes.
    /// </summary>
    public static AppPaths CreateDefault() => new(
        SettingsDirectory: Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SmartZoom"),
        LogDirectory: Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SmartZoom", "logs"));
}
