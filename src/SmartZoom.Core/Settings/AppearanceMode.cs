namespace SmartZoom.Core.Settings;

/// <summary>Which palette the settings window paints itself in.</summary>
/// <remarks>
/// A preference of the window rather than of zooming: nothing here changes what a trigger does. It lives in
/// the settings file all the same, because an appearance that is back to the default at the next launch is
/// not a preference.
/// </remarks>
public enum AppearanceMode
{
    /// <summary>Follow Windows, and change with it while the window is open.</summary>
    System = 0,

    /// <summary>Always the light palette, whatever Windows is set to.</summary>
    Light,

    /// <summary>Always the dark palette, whatever Windows is set to.</summary>
    Dark,
}
