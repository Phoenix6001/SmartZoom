namespace SmartZoom.Core.Input;

/// <summary>Recognizes presses and releases of one <see cref="KeyCombo"/> in the raw key stream.</summary>
/// <remarks>
/// A press requires the combo's modifiers to be held *exactly* (Ctrl+Z does not fire Ctrl+Shift+Z).
/// The release is matched to the key alone, because users routinely lift the modifier first.
/// Auto-repeat is reported separately so a swallowed press doesn't leak repeated keystrokes.
/// </remarks>
public sealed class HotkeyMatcher(KeyCombo combo)
{
    private readonly KeyCombo _combo = combo;
    private readonly KeyModifiers _keyAsModifier = VirtualKeys.TryGetModifier(combo.Key, out var m) ? m : KeyModifiers.None;
    private bool _pressed;

    /// <summary>The combination this matcher recognizes.</summary>
    public KeyCombo Combo => _combo;

    /// <summary>Whether a press has been reported and not yet released.</summary>
    public bool IsPressed => _pressed;

    /// <summary>Classifies a raw key event.</summary>
    /// <param name="virtualKey">Raw (unfolded) virtual key from the hook.</param>
    /// <param name="isDown">True for key-down, false for key-up.</param>
    /// <param name="heldModifiers">Modifiers held after this event (from <see cref="ModifierTracker"/>).</param>
    public KeyMatch OnKey(ushort virtualKey, bool isDown, KeyModifiers heldModifiers)
    {
        if (VirtualKeys.Fold(virtualKey) != _combo.Key)
            return KeyMatch.None;

        if (!isDown)
        {
            if (!_pressed)
                return KeyMatch.None;

            _pressed = false;
            return KeyMatch.Release;
        }

        if (_pressed)
            return KeyMatch.Repeat;

        // When the main key is itself a modifier (bare "Ctrl"), it has just been added to the held set; ignore it.
        if ((heldModifiers & ~_keyAsModifier) != _combo.Modifiers)
            return KeyMatch.None;

        _pressed = true;
        return KeyMatch.Press;
    }

    /// <summary>Forgets an in-progress press.</summary>
    public void Reset() => _pressed = false;
}
