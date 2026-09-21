namespace SmartZoom.Core.Settings;

/// <summary>Root of the user settings file (<c>%APPDATA%\SmartZoom\settings.json</c>).</summary>
/// <remarks>
/// There is deliberately no migration code here. Nothing has been released, so the only settings files in
/// existence are the author's, and a model that the settings UI will bind to should not start life carrying
/// shapes nobody has. Renames since the last build are listed in CHANGELOG.md under "breaking".
/// </remarks>
public sealed class SmartZoomSettings
{
    /// <summary>Master switch; when false the hook passes all input through.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Gestures that fire SmartZoom. Any of them works; mouse buttons and hotkeys can be mixed.</summary>
    public IList<TriggerSettings> Triggers { get; set; } = [TriggerSettings.CreateDefault()];

    /// <summary>Which applications are handled, and how.</summary>
    public RoutingSettings Routing { get; set; } = new();

    /// <summary>Zoom behavior shared by all adapters.</summary>
    public ZoomSettings Zoom { get; set; } = new();

    /// <summary>How much is written to the log file.</summary>
    public LoggingSettings Logging { get; set; } = new();
}
