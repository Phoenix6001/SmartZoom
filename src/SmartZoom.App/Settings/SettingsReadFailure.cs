namespace SmartZoom.App.Settings;

/// <summary>Why the settings file could not be read as it is.</summary>
internal enum SettingsReadFailure
{
    /// <summary>It was read.</summary>
    None,

    /// <summary>There is no settings file.</summary>
    Missing,

    /// <summary>It is there but could not be opened: another process holds it, or it may not be read.</summary>
    Unopenable,

    /// <summary>It was read, but it is not valid JSON.</summary>
    Invalid,
}
