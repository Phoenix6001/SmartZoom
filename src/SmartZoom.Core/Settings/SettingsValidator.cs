using Microsoft.Extensions.Logging.Abstractions;

using SmartZoom.Core.Input;
using SmartZoom.Core.Routing;

namespace SmartZoom.Core.Settings;

/// <summary>
/// Checks a settings object before anything is built from it, so a bad value is a message next to a control
/// rather than an exception on startup or a press that quietly does nothing.
/// </summary>
/// <remarks>
/// Wherever it can, this <em>runs the real constructor</em> and reports what it threw, rather than restating
/// its rules — those rules belong to the types that enforce them, and a copy here would drift. Only three
/// checks are restated, and each says which constructor owns the original.
/// </remarks>
public static class SettingsValidator
{
    /// <summary>Everything wrong with these settings, worst first.</summary>
    /// <param name="settings">The settings to check.</param>
    /// <param name="adapters">Descriptors of the adapters this build has, for the routing check.</param>
    /// <param name="systemDoubleClickTimeMs">Used for triggers that leave the double-tap window unset.</param>
    /// <returns>An empty list when the settings are usable as they are.</returns>
    public static IReadOnlyList<SettingsProblem> Validate(
        SmartZoomSettings settings,
        IReadOnlyList<AdapterDescriptor> adapters,
        uint systemDoubleClickTimeMs)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(adapters);

        var problems = new List<SettingsProblem>();

        CheckTriggers(settings, systemDoubleClickTimeMs, problems);
        CheckRouting(settings, adapters, problems);
        CheckZoom(settings.Zoom, problems);

        return [.. problems.OrderByDescending(p => p.Severity)];
    }

    private static void CheckTriggers(SmartZoomSettings settings, uint systemDoubleClickTimeMs, List<SettingsProblem> problems)
    {
        if (settings.Triggers.Count == 0)
        {
            // LowLevelInputHook's constructor refuses an empty set; without this the app would not start.
            problems.Add(SettingsProblem.Error("Triggers", "At least one trigger is needed, or nothing can start a zoom."));
            return;
        }

        var definitions = new List<TriggerDefinition?>(settings.Triggers.Count);
        for (var i = 0; i < settings.Triggers.Count; i++)
        {
            definitions.Add(Define(settings.Triggers[i], i, systemDoubleClickTimeMs, problems));
        }

        CheckForSharedInputs(definitions, problems);
    }

    /// <summary>Builds one trigger the way the app does, reporting whatever the real constructors refuse.</summary>
    private static TriggerDefinition? Define(TriggerSettings trigger, int index, uint systemDoubleClickTimeMs, List<SettingsProblem> problems)
    {
        var section = $"Triggers[{index}]";

        TriggerDefinition definition;
        try
        {
            definition = trigger.ToDefinition(systemDoubleClickTimeMs);
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException)
        {
            problems.Add(SettingsProblem.Error(section, ex.Message));
            return null;
        }

        try
        {
            // The tap count and the double-tap window are TapDetector's rules, and it is not built until the
            // hook is, so this is where a bad one would otherwise surface: at startup, as a crash.
            _ = new TapDetector(definition.Tap);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            problems.Add(SettingsProblem.Error(section, WithoutParameterName(ex.Message)));
            return null;
        }

        return definition;
    }

    /// <summary>Drops the "(Parameter 'options')" that argument exceptions append; it means nothing to a user.</summary>
    private static string WithoutParameterName(string message)
    {
        var suffix = message.IndexOf(" (Parameter", StringComparison.Ordinal);
        return suffix < 0 ? message : message[..suffix];
    }

    /// <summary>
    /// Restated from <c>LowLevelInputHook</c>'s constructor: each input may belong to one trigger. Stated
    /// again here only because the hook is not built until the settings have already been accepted.
    /// </summary>
    private static void CheckForSharedInputs(List<TriggerDefinition?> definitions, List<SettingsProblem> problems)
    {
        // Keyed by button and modifiers together: "Middle" and "Ctrl+Middle" are two inputs.
        var buttons = new Dictionary<(MouseButton Button, KeyModifiers Modifiers), int>();
        var combos = new Dictionary<KeyCombo, int>();

        for (var i = 0; i < definitions.Count; i++)
        {
            switch (definitions[i])
            {
                case MouseButtonTrigger mouse when !buttons.TryAdd((mouse.Button, mouse.Modifiers), i):
                    problems.Add(SettingsProblem.Error(
                        $"Triggers[{i}]",
                        $"{MouseButtonTrigger.Describe(mouse.Button, mouse.Modifiers)} is already used by trigger {buttons[(mouse.Button, mouse.Modifiers)] + 1}. Each button can only start one trigger."));
                    break;

                case MouseButtonTrigger mouse when MouseButtonTrigger.CautionFor(mouse.Button, mouse.Modifiers) is { } caution:
                    problems.Add(SettingsProblem.Warning($"Triggers[{i}]", caution));
                    break;

                case HotkeyTrigger hotkey when !combos.TryAdd(hotkey.Keys, i):
                    problems.Add(SettingsProblem.Error(
                        $"Triggers[{i}]",
                        $"{hotkey.Keys} is already used by trigger {combos[hotkey.Keys] + 1}. Each key combination can only start one trigger."));
                    break;

                default:
                    break;
            }
        }
    }

    private static void CheckRouting(SmartZoomSettings settings, IReadOnlyList<AdapterDescriptor> adapters, List<SettingsProblem> problems)
    {
        ZoomRouter router;
        try
        {
            // The real thing: it re-runs the duplicate-id and duplicate-process checks and works out which
            // ids the settings name that this build has no adapter for.
            router = new ZoomRouter(adapters, settings.Routing, NullLogger<ZoomRouter>.Instance);
        }
        catch (InvalidOperationException ex)
        {
            problems.Add(SettingsProblem.Error("Routing", ex.Message));
            return;
        }

        foreach (var unknown in router.UnknownAdapterIds)
        {
            var known = string.Join(", ", adapters.Select(a => a.Id.Value).Order(StringComparer.OrdinalIgnoreCase).Append(AdapterId.None.Value));
            problems.Add(SettingsProblem.Warning(
                "Routing.Apps",
                $"\"{unknown}\" is not a strategy this build has, so those applications fall back to Ctrl+wheel. Available: {known}."));
        }
    }

    private static void CheckZoom(ZoomSettings zoom, List<SettingsProblem> problems)
    {
        if (zoom.MinScale <= 1)
            problems.Add(SettingsProblem.Error("Zoom.MinScale", "The smallest zoom has to be above 1; at or below it, zooming in would make things smaller."));

        if (zoom.MaxScale <= zoom.MinScale)
            problems.Add(SettingsProblem.Error("Zoom.MaxScale", $"The largest zoom ({zoom.MaxScale}) has to be above the smallest ({zoom.MinScale})."));

        if (zoom.Smart.AnimationMs < 0)
            problems.Add(SettingsProblem.Error("Zoom.Smart.AnimationMs", "An animation cannot take less than no time."));

        if (zoom.Smart.MarginPx < 0)
            problems.Add(SettingsProblem.Error("Zoom.Smart.MarginPx", "A margin cannot be negative."));

        if (zoom.CtrlWheel.Ticks <= 0)
            problems.Add(SettingsProblem.Warning("Zoom.CtrlWheel.Ticks", "With no wheel ticks, the Ctrl+wheel strategy does nothing at all."));

        CheckReader(zoom.Reader, problems);
    }

    private static void CheckReader(ReaderZoomSettings reader, List<SettingsProblem> problems)
    {
        // Restated from ReaderPinchAdapter's constructor, which throws; only the pinch mode uses it.
        if (reader.Mode == ReaderZoomMode.Pinch && reader.Magnification <= 1)
            problems.Add(SettingsProblem.Error("Zoom.Reader.Magnification", "The pinch has to magnify by more than 1 to be a zoom."));

        if (reader.AnimationMs < 0)
            problems.Add(SettingsProblem.Error("Zoom.Reader.AnimationMs", "An animation cannot take less than no time."));

        // Both reader adapters parse these at construction; the pinch one needs only the second.
        CheckCombo(reader.ZoomInKeys, "Zoom.Reader.ZoomInKeys", problems);
        CheckCombo(reader.ZoomOutKeys, "Zoom.Reader.ZoomOutKeys", problems);
    }

    private static void CheckCombo(string keys, string section, List<SettingsProblem> problems)
    {
        if (!KeyCombo.TryParse(keys, out _))
            problems.Add(SettingsProblem.Error(section, $"\"{keys}\" is not a key combination. Expected something like \"Ctrl+2\" or \"F9\"."));
    }
}
