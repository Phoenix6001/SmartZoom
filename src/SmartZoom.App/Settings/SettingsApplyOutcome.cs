namespace SmartZoom.App.Settings;

/// <summary>How far applying a set of settings got.</summary>
internal enum SettingsApplyOutcome
{
    /// <summary>Something was wrong with them; the previous settings are still running.</summary>
    Rejected,

    /// <summary>In force and written to the settings file.</summary>
    Applied,

    /// <summary>In force, but the file could not be written, so they will be lost on restart.</summary>
    AppliedButNotSaved,
}
