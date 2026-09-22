namespace SmartZoom.Core.Settings;

/// <summary>How much a <see cref="SettingsProblem"/> matters.</summary>
public enum SettingsProblemSeverity
{
    /// <summary>Usable, but probably not what was meant. Worth showing; never worth refusing over.</summary>
    Warning,

    /// <summary>The settings cannot be applied as they are. Something would throw, or silently do nothing.</summary>
    Error,
}
