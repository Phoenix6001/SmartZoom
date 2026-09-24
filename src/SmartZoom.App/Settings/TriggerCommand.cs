using Microsoft.Extensions.Logging.Abstractions;

using SmartZoom.Core.Input;
using SmartZoom.Core.Settings;
using SmartZoom.Interop;

namespace SmartZoom.App.Settings;

/// <summary>
/// Writes a single trigger into the settings file and exits, without starting the app.
/// </summary>
/// <remarks>
/// This is how the installer's "how do you want to start a zoom?" page takes effect. It goes through
/// <see cref="SettingsStore"/> rather than writing JSON from the installer script, so the file is produced
/// by the same serializer that reads it, an existing file keeps everything else it had, and a trigger the
/// app could not actually use is refused here rather than at the next start.
/// </remarks>
internal static class TriggerCommand
{
    /// <summary>
    /// Handles <c>--trigger &lt;spec&gt; [--taps 1|2] [--swallow]</c> and <c>--record-trigger</c>. The spec is a
    /// mouse button (<c>Middle</c>, <c>XButton1</c>, <c>XButton2</c>), a click with modifiers
    /// (<c>Ctrl+LeftClick</c>, <c>Alt+RightClick</c>, <c>Ctrl+MiddleClick</c>) or a key combination
    /// (<c>Ctrl+Alt+Z</c>, <c>F9</c>); a bare <c>Left</c> or <c>Right</c> is the arrow key.
    /// </summary>
    /// <param name="args">The process arguments.</param>
    /// <param name="exitCode">0 when a trigger was written, 1 when it was not.</param>
    /// <returns>False when these arguments are not a trigger command, and the app should start normally.</returns>
    public static bool TryRun(string[] args, out int exitCode)
    {
        exitCode = 0;

        if (args.Any(a => string.Equals(a, "--record-trigger", StringComparison.OrdinalIgnoreCase)))
        {
            exitCode = Record();
            return true;
        }

        var index = Array.FindIndex(args, a => string.Equals(a, "--trigger", StringComparison.OrdinalIgnoreCase));
        if (index < 0)
            return false;

        if (index + 1 >= args.Length)
        {
            exitCode = 1;
            return true;
        }

        exitCode = Write(args[index + 1], Taps(args), args.Any(a => string.Equals(a, "--swallow", StringComparison.OrdinalIgnoreCase)));
        return true;
    }

    /// <summary>
    /// Shows the same recorder the settings window uses, and writes whatever was pressed. This is how the
    /// installer asks how to start a zoom, rather than listing presets on a wizard page: a setup script cannot
    /// see a mouse's side buttons at all, so only SmartZoom itself can record the one most people want.
    /// </summary>
    /// <remarks>
    /// It opens filled in with the trigger already configured, so reinstalling is "confirm or change" rather
    /// than "start again" — Cancel keeps exactly what was there, and nothing is written.
    /// </remarks>
    private static int Record()
    {
        // No host and no tray here: this is a dialog and an exit. Per-Monitor V2 comes from the csproj.
        ApplicationConfiguration.Initialize();

        // Nothing is running to silence: the installer stopped any previous copy before it got this far. There
        // is no host either, so the Interop implementation is constructed directly.
        var store = DefaultStore();
        var doubleClickMs = new SystemInput().DoubleClickTimeMs;
        using var recorder = new TriggerRecorderDialog(triggers: null, doubleClickMs, Existing(store));
        if (recorder.ShowDialog() != DialogResult.OK)
            return 1;

        return Persist(recorder.Result, store, doubleClickMs);
    }

    /// <summary>
    /// The trigger to show as the starting point, or null on a machine with nothing configured yet. Reads
    /// without writing: Cancel must leave a fresh machine with no settings file.
    /// </summary>
    /// <param name="store">Where the settings file is.</param>
    internal static TriggerSettings? Existing(SettingsStore store) =>
        store.TryLoad(out var settings, out _) ? settings.Triggers.FirstOrDefault() : null;

    /// <summary>The value after <c>--taps</c>, or one press when it is missing or not a number.</summary>
    /// <param name="args">The process arguments.</param>
    internal static int Taps(string[] args)
    {
        var index = Array.FindIndex(args, a => string.Equals(a, "--taps", StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length && int.TryParse(args[index + 1], out var taps) ? taps : 1;
    }

    /// <summary>
    /// The buttons a spec can end in. <c>Left</c> and <c>Right</c> on their own are deliberately absent: they are
    /// arrow keys, and a click on those buttons is asked for as <c>LeftClick</c> / <c>RightClick</c> so the
    /// hotkey form keeps meaning what it always has.
    /// </summary>
    private static readonly Dictionary<string, MouseButton> ClickTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        ["LeftClick"] = MouseButton.Left,
        ["RightClick"] = MouseButton.Right,
        ["MiddleClick"] = MouseButton.Middle,
        ["Middle"] = MouseButton.Middle,
        ["XButton1"] = MouseButton.XButton1,
        ["XButton2"] = MouseButton.XButton2,
    };

    /// <summary>
    /// A mouse button by name, a click with modifiers (<c>Ctrl+LeftClick</c>, <c>Ctrl+Middle</c>), or anything
    /// else read as a key combination.
    /// </summary>
    /// <param name="spec">The text after <c>--trigger</c>.</param>
    /// <returns>Null when the text names none of these.</returns>
    internal static TriggerSettings? Parse(string spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        var plus = spec.LastIndexOf('+');
        var last = spec[(plus + 1)..].Trim();
        if (ClickTokens.TryGetValue(last, out var button))
        {
            if (plus < 0)
                return new TriggerSettings { Mouse = button };

            // Everything before the button must be modifiers, by the same rules as a hotkey's.
            return KeyCombo.TryParseModifiers(spec[..plus], out var modifiers)
                ? new TriggerSettings { Mouse = button, Modifiers = KeyCombo.FormatModifiers(modifiers) }
                : null;
        }

        return KeyCombo.TryParse(spec, out var combo) ? new TriggerSettings { Keys = combo.ToString() } : null;
    }

    /// <summary>
    /// Replaces the first trigger — the one the recorder shows — and leaves every other trigger, and every
    /// other setting, as it was. A user with a mouse button and a hotkey keeps the hotkey.
    /// </summary>
    /// <param name="trigger">The trigger to write.</param>
    /// <param name="store">Where the settings file is.</param>
    /// <param name="systemDoubleClickMs">The double-tap window for a trigger that does not set its own.</param>
    /// <returns>0 when the file was written, 1 when the trigger was refused or the file could not be.</returns>
    internal static int Persist(TriggerSettings trigger, SettingsStore store, uint systemDoubleClickMs)
    {
        try
        {
            // Refuse anything the hook would refuse, while there is still an installer on screen to say so.
            _ = new TapDetector(trigger.ToDefinition(systemDoubleClickMs).Tap);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or FormatException)
        {
            return 1;
        }

        try
        {
            var settings = store.Load();
            if (settings.Triggers.Count > 0)
                settings.Triggers[0] = trigger;
            else
                settings.Triggers.Add(trigger);

            store.Save(settings);
            return 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 1;
        }
    }

    private static int Write(string spec, int taps, bool swallow)
    {
        if (Parse(spec) is not { } trigger)
            return 1;

        trigger.TapCount = taps;
        trigger.SwallowClicks = swallow;
        return Persist(trigger, DefaultStore(), new SystemInput().DoubleClickTimeMs);
    }

    private static SettingsStore DefaultStore() => new(AppPaths.CreateDefault(), NullLogger<SettingsStore>.Instance);
}
