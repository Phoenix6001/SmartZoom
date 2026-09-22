namespace SmartZoom.Core.Settings;

/// <summary>Something wrong, or suspicious, about a settings file.</summary>
/// <param name="Severity">Whether this stops the settings being used.</param>
/// <param name="Section">
/// Where it is, in the settings file's own terms — <c>Triggers[1]</c>, <c>Zoom.Reader.Magnification</c> —
/// so the message can be put next to the control the user was editing.
/// </param>
/// <param name="Message">What is wrong, addressed to whoever has to fix it.</param>
public sealed record SettingsProblem(SettingsProblemSeverity Severity, string Section, string Message)
{
    /// <summary>The settings cannot be used as they are.</summary>
    /// <param name="section">Where it is.</param>
    /// <param name="message">What is wrong.</param>
    public static SettingsProblem Error(string section, string message) => new(SettingsProblemSeverity.Error, section, message);

    /// <summary>Usable, but probably not what was meant.</summary>
    /// <param name="section">Where it is.</param>
    /// <param name="message">What is suspicious.</param>
    public static SettingsProblem Warning(string section, string message) => new(SettingsProblemSeverity.Warning, section, message);

    /// <summary>The section and the message, for a log line or a one-line summary.</summary>
    public override string ToString() => $"{Section}: {Message}";
}
