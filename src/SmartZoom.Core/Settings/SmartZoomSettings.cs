namespace SmartZoom.Core.Settings;

/// <summary>Root of the user settings file (<c>%APPDATA%\SmartZoom\settings.json</c>).</summary>
/// <remarks>
/// There is deliberately no migration code here: a model that the settings UI binds to carries no shapes from
/// files nobody has. Renamed settings are listed in CHANGELOG.md under "breaking".
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

    /// <summary>The local record of what SmartZoom failed to do.</summary>
    public DiagnosticsSettings Diagnostics { get; set; } = new();

    /// <summary>Which palette the settings window paints itself in; by default whichever Windows is using.</summary>
    public AppearanceMode Appearance { get; set; } = AppearanceMode.System;
}
