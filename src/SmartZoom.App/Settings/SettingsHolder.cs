using SmartZoom.Core.Settings;

namespace SmartZoom.App.Settings;

/// <summary>
/// The settings the app is currently running on. One object, one owner, so nothing can hold a stale copy and
/// write it back over the file.
/// </summary>
/// <remarks>
/// Everything that needs settings either takes a snapshot when it is built (adapters, the router) or reads
/// <see cref="Current"/> at the moment it needs it (the tray, the settings window). Only
/// <see cref="SettingsApplier"/> replaces it.
/// </remarks>
internal sealed class SettingsHolder(SmartZoomSettings initial)
{
    private volatile SmartZoomSettings _current = initial;

    /// <summary>The settings in force right now.</summary>
    public SmartZoomSettings Current => _current;

    /// <summary>Publishes a new set. Called only by <see cref="SettingsApplier"/>, after they have been applied.</summary>
    public void Replace(SmartZoomSettings settings) => _current = settings;
}
