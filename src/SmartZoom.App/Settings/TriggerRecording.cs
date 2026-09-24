using SmartZoom.Core.Input;
using SmartZoom.Core.Settings;

namespace SmartZoom.App.Settings;

/// <summary>
/// The rules a trigger recorder needs, with no window attached: what a recorded press becomes in the settings
/// file, and what a settings entry looks like back in the recorder's slots.
/// </summary>
/// <remarks>
/// Kept apart from the recorder window because these are the only part of recording that can be tested without
/// a message loop, and because two windows have now asked the same questions of them — the one in the settings
/// window and the one the installer shows through <c>--record-trigger</c>.
/// </remarks>
internal static class TriggerRecording
{
    /// <summary>
    /// The settings entry for what was recorded. Modifiers are written only for a mouse trigger that has them;
    /// a hotkey carries its own inside <see cref="TriggerSettings.Keys"/>.
    /// </summary>
    /// <param name="button">The recorded button, or null for a hotkey.</param>
    /// <param name="modifiers">The modifiers held with the button.</param>
    /// <param name="combo">The recorded combination, or null for a mouse trigger.</param>
    /// <param name="doubleTap">Whether two presses make a trigger.</param>
    /// <param name="windowMs">The double-tap window; only written for a double tap.</param>
    /// <param name="swallow">Whether to hide the press from the application.</param>
    internal static TriggerSettings Compose(MouseButton? button, KeyModifiers modifiers, KeyCombo? combo, bool doubleTap, uint windowMs, bool swallow) => new()
    {
        Mouse = button,
        Modifiers = button is not null && modifiers != KeyModifiers.None ? KeyCombo.FormatModifiers(modifiers) : null,
        Keys = combo?.ToString(),
        TapCount = doubleTap ? 2 : 1,
        DoubleTapWindowMs = doubleTap ? windowMs : null,
        SwallowClicks = swallow,
    };

    /// <summary>
    /// The button (with its modifiers) and the combination a trigger stands for, exactly one of them set:
    /// whichever the trigger does not name is cleared, so restoring a trigger over another leaves nothing of
    /// the first behind. Modifiers that cannot be read count as none.
    /// </summary>
    /// <param name="trigger">The trigger to show.</param>
    internal static (MouseButton? Button, KeyModifiers Modifiers, KeyCombo? Combo) Recorded(TriggerSettings trigger)
    {
        ArgumentNullException.ThrowIfNull(trigger);

        var modifiers = trigger.Mouse is not null && KeyCombo.TryParseModifiers(trigger.Modifiers, out var held) ? held : KeyModifiers.None;
        return (trigger.Mouse, modifiers, KeyCombo.TryParse(trigger.Keys, out var combo) ? combo : null);
    }

    /// <summary>The trigger button a WinForms button stands for, or <see cref="MouseButton.None"/>.</summary>
    /// <param name="button">The button Windows reported.</param>
    /// <remarks>
    /// WinForms' <see cref="MouseButtons"/> rather than WPF's enumeration because it is a flags set covering
    /// every button at once, which is what both the message stream and WPF's own buttons map onto cleanly.
    /// </remarks>
    internal static MouseButton ButtonOf(MouseButtons button) => button switch
    {
        MouseButtons.Left => MouseButton.Left,
        MouseButtons.Right => MouseButton.Right,
        MouseButtons.Middle => MouseButton.Middle,
        MouseButtons.XButton1 => MouseButton.XButton1,
        MouseButtons.XButton2 => MouseButton.XButton2,
        _ => MouseButton.None,
    };

    /// <summary>
    /// The modifiers in a Windows key state. Only Ctrl, Alt and Shift: the Windows key is not reported there,
    /// so a recorder that wants it has to ask its own framework.
    /// </summary>
    /// <param name="keys">The key state, usually <see cref="Control.ModifierKeys"/>.</param>
    internal static KeyModifiers ModifiersOf(Keys keys)
    {
        var modifiers = KeyModifiers.None;
        if ((keys & Keys.Control) != 0)
            modifiers |= KeyModifiers.Control;
        if ((keys & Keys.Alt) != 0)
            modifiers |= KeyModifiers.Alt;
        if ((keys & Keys.Shift) != 0)
            modifiers |= KeyModifiers.Shift;
        return modifiers;
    }
}
