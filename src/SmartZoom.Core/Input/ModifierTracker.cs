namespace SmartZoom.Core.Input;

/// <summary>
/// Tracks which modifiers are physically held, from the raw key stream. Left and right variants are
/// tracked separately so releasing one side while the other is held keeps the modifier active.
/// </summary>
public sealed class ModifierTracker
{
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
            case VirtualKeys.LeftShift: Set(ref _left, KeyModifiers.Shift, isDown); break;
            case VirtualKeys.RightShift: Set(ref _right, KeyModifiers.Shift, isDown); break;
            case VirtualKeys.LeftControl: Set(ref _left, KeyModifiers.Control, isDown); break;
            case VirtualKeys.RightControl: Set(ref _right, KeyModifiers.Control, isDown); break;
            case VirtualKeys.LeftAlt: Set(ref _left, KeyModifiers.Alt, isDown); break;
            case VirtualKeys.RightAlt: Set(ref _right, KeyModifiers.Alt, isDown); break;
            case VirtualKeys.Win: Set(ref _left, KeyModifiers.Win, isDown); break;
            case VirtualKeys.RightWin: Set(ref _right, KeyModifiers.Win, isDown); break;
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
