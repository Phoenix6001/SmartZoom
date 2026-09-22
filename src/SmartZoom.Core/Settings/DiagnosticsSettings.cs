namespace SmartZoom.Core.Settings;

/// <summary>The local record of what SmartZoom failed to do.</summary>
/// <remarks>
/// Lives in the settings file because it is the only control the user has over a privacy feature, and a
/// switch that is back on at the next launch is not a control. Nothing here changes whether or how a zoom
/// happens; see <c>docs/diagnostics-design.md</c>.
/// </remarks>
public sealed class DiagnosticsSettings
{
    /// <summary>
    /// Whether presses that zoomed nothing, adapters that threw and crashes are counted at all. On by
    /// default: the record holds strictly less than the log file that is already written, and never leaves
    /// the machine. Turning it off stops recording immediately; it does not erase what is already there,
    /// which is what the Diagnostics tab's "Clear recorded data" is for.
    /// </summary>
    public bool Enabled { get; set; } = true;
}
