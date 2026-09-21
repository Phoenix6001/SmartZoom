namespace SmartZoom.Core.Input;

/// <summary>
/// An input gesture that fires SmartZoom. The set of kinds is closed, and they live in this one file
/// because anything reading one of them needs to see the others to know what it is matching against.
/// </summary>
/// <param name="Tap">Tap behavior shared by all trigger kinds.</param>
public abstract record TriggerDefinition(TapOptions Tap)
{
    /// <summary>Human-readable form for logs and UI, e.g. "XButton2 x1" or "Ctrl+Alt+Z x2".</summary>
    public abstract string DisplayName { get; }
}

/// <summary>A mouse button trigger.</summary>
/// <param name="Button">Button that fires the trigger.</param>
/// <param name="Tap">Tap behavior.</param>
public sealed record MouseButtonTrigger(MouseButton Button, TapOptions Tap) : TriggerDefinition(Tap)
{
    /// <inheritdoc />
    public override string DisplayName => $"{Button} x{Tap.TapCount}";
}

/// <summary>A keyboard trigger: a key with optional modifiers, or a bare modifier.</summary>
/// <param name="Keys">The combination.</param>
/// <param name="Tap">Tap behavior.</param>
public sealed record HotkeyTrigger(KeyCombo Keys, TapOptions Tap) : TriggerDefinition(Tap)
{
    /// <inheritdoc />
    public override string DisplayName => $"{Keys} x{Tap.TapCount}";
}
