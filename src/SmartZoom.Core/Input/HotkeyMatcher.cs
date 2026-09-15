namespace SmartZoom.Core.Input;

/// <summary>What a raw key event means for one <see cref="KeyCombo"/>.</summary>
public enum KeyMatch : byte
{
    /// <summary>Not this combination's key, or the modifiers didn't match.</summary>
    None,

    /// <summary>The combination was pressed.</summary>
    Press,

    /// <summary>Auto-repeat of a press we already reported. Swallow it if the press was swallowed.</summary>
    Repeat,

    /// <summary>The combination's key was released after a reported press.</summary>
    Release,
}

/// <summary>
/// Tracks which modifiers are physically held, from the raw key stream. Left and right variants are
/// tracked separately so releasing one side while the other is held keeps the modifier active.
/// </summary>
public sealed class ModifierTracker
{
    private const ushort LeftShift = 0xA0;
    private const ushort RightShift = 0xA1;
    private const ushort LeftControl = 0xA2;
    private const ushort RightControl = 0xA3;
    private const ushort LeftAlt = 0xA4;
    private const ushort RightAlt = 0xA5;
    private const ushort LeftWin = 0x5B;
    private const ushort RightWin = 0x5C;

    private byte _left;
    private byte _right;

    /// <summary>Modifiers currently held.</summary>
    public KeyModifiers Held => (KeyModifiers)(_left | _right);

    /// <summary>Records a key transition and returns the modifiers held afterwards.</summary>
    /// <param name="virtualKey">Raw (unfolded) virtual key from the hook.</param>
    /// <param name="isDown">True for key-down, false for key-up.</param>
    public KeyModifiers OnKey(ushort virtualKey, bool isDown)
    {
        switch (virtualKey)
        {
            case LeftShift: Set(ref _left, KeyModifiers.Shift, isDown); break;
            case RightShift: Set(ref _right, KeyModifiers.Shift, isDown); break;
            case LeftControl: Set(ref _left, KeyModifiers.Control, isDown); break;
            case RightControl: Set(ref _right, KeyModifiers.Control, isDown); break;
            case LeftAlt: Set(ref _left, KeyModifiers.Alt, isDown); break;
            case RightAlt: Set(ref _right, KeyModifiers.Alt, isDown); break;
            case LeftWin: Set(ref _left, KeyModifiers.Win, isDown); break;
            case RightWin: Set(ref _right, KeyModifiers.Win, isDown); break;
            case VirtualKeys.Shift: Set(ref _left, KeyModifiers.Shift, isDown); break;   // Generic codes: injected input
            case VirtualKeys.Control: Set(ref _left, KeyModifiers.Control, isDown); break; // sometimes uses them.
            case VirtualKeys.Alt: Set(ref _left, KeyModifiers.Alt, isDown); break;
            default: break;
        }

        return Held;
    }

    /// <summary>Forgets all held modifiers (e.g. after the hook was reinstalled).</summary>
    public void Reset() => _left = _right = 0;

    private static void Set(ref byte side, KeyModifiers modifier, bool isDown)
    {
        if (isDown)
            side |= (byte)modifier;
        else
            side &= (byte)~modifier;
    }
}

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
