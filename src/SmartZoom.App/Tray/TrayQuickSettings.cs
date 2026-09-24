using System.Collections.Immutable;

using SmartZoom.App.Settings;
using SmartZoom.Core.Routing;
using SmartZoom.Core.Settings;

namespace SmartZoom.App.Tray;

/// <summary>
/// The settings the tray menu can change, as functions over a settings object rather than as click handlers.
/// </summary>
/// <remarks>
/// Every one of these returns a copy: the settings already published are never edited underneath whoever is
/// reading them, and the copy goes to <see cref="SettingsApplier"/>, which remains the only writer of the file.
/// </remarks>
internal static class TrayQuickSettings
{
    /// <summary>
    /// The zoom amounts the menu offers. A short list of picks rather than a spinner, because a tray menu is
    /// for the choice someone makes without thinking about it; the exact value stays in the settings file.
    /// </summary>
    public static readonly ImmutableArray<double> ZoomAmounts = [1.5, 2.0, 3.0];

    /// <summary>How near <see cref="ZoomSettings.MaxScale"/> must be to count as one of the offered amounts.</summary>
    private const double AmountTolerance = 0.001;

    /// <summary>Whether the largest zoom is currently the given amount.</summary>
    /// <param name="settings">The settings in force.</param>
    /// <param name="amount">One of <see cref="ZoomAmounts"/>.</param>
    public static bool IsZoomAmount(SmartZoomSettings settings, double amount)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return Math.Abs(settings.Zoom.MaxScale - amount) < AmountTolerance;
    }

    /// <summary>A copy whose largest zoom is the given amount.</summary>
    /// <param name="settings">The settings in force.</param>
    /// <param name="amount">The new largest zoom.</param>
    public static SmartZoomSettings WithZoomAmount(SmartZoomSettings settings, double amount)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var copy = SettingsStore.Clone(settings);
        copy.Zoom.MaxScale = amount;
        return copy;
    }

    /// <summary>Whether an application is currently routed to <see cref="AdapterId.None"/>.</summary>
    /// <param name="settings">The settings in force.</param>
    /// <param name="process">Process image name, with or without ".exe".</param>
    public static bool IsIgnored(SmartZoomSettings settings, string process)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return !string.IsNullOrWhiteSpace(process)
            && settings.Routing.Apps.TryGetValue(process, out var id)
            && id == AdapterId.None;
    }

    /// <summary>
    /// A copy in which an application is either switched off or handed back to whichever strategy claims it.
    /// </summary>
    /// <param name="settings">The settings in force.</param>
    /// <param name="process">Process image name, with or without ".exe".</param>
    /// <param name="ignored">
    /// True to route it to <see cref="AdapterId.None"/>. False removes the entry rather than naming a strategy,
    /// so the application goes back to the one that claims it by default — which is also how it picks up a
    /// better strategy in a later version.
    /// </param>
    public static SmartZoomSettings WithIgnored(SmartZoomSettings settings, string process, bool ignored)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(process);

        var copy = SettingsStore.Clone(settings);
        if (ignored)
            copy.Routing.Apps[process] = AdapterId.None;
        else
            copy.Routing.Apps.Remove(process);

        return copy;
    }

    /// <summary>
    /// A copy whose first trigger is the given one, keeping every other trigger.
    /// </summary>
    /// <param name="settings">The settings in force.</param>
    /// <param name="trigger">The trigger the recorder captured.</param>
    /// <remarks>
    /// The menu item edits the trigger it showed, which is the first one. Replacing the whole list instead
    /// would silently delete a second trigger — a hotkey kept alongside a mouse button, typically — that the
    /// user never saw on screen.
    /// </remarks>
    public static SmartZoomSettings WithFirstTrigger(SmartZoomSettings settings, TriggerSettings trigger)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(trigger);

        var copy = SettingsStore.Clone(settings);
        if (copy.Triggers.Count == 0)
            copy.Triggers.Add(trigger);
        else
            copy.Triggers[0] = trigger;

        return copy;
    }

    /// <summary>The trigger the "change trigger" item should open the recorder with, or null when there is none.</summary>
    /// <param name="settings">The settings in force.</param>
    public static TriggerSettings? FirstTrigger(SmartZoomSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings.Triggers.Count == 0 ? null : settings.Triggers[0];
    }
}
