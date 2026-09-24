namespace SmartZoom.Core.Input;

/// <summary>
/// An input gesture that fires SmartZoom. The set of kinds is closed, and they live in this one file
/// because anything reading one of them needs to see the others to know what it is matching against.
/// </summary>
/// <param name="Tap">Tap behavior shared by all trigger kinds.</param>
public abstract record TriggerDefinition(TapOptions Tap)
{
    /// <summary>Human-readable form for logs and UI, e.g. "XButton2 x1", "Ctrl+Left x1" or "Ctrl+Alt+Z x2".</summary>
    public abstract string DisplayName { get; }
}

/// <summary>A mouse button trigger, optionally requiring modifier keys to be held: <c>Middle</c>, <c>Ctrl+Left</c>.</summary>
/// <remarks>
/// <see cref="MouseButton.Left"/> and <see cref="MouseButton.Right"/> cannot be a trigger on their own: a global
/// hook that swallows or delays bare primary clicks would make the machine unusable, so a trigger on either of
/// them exists only with at least one modifier.
/// </remarks>
public sealed record MouseButtonTrigger : TriggerDefinition
{
    /// <summary>Creates a trigger on a button held together with modifier keys.</summary>
    /// <param name="button">Button that fires the trigger.</param>
    /// <param name="modifiers">Modifiers that must be held, and no others, when the button is pressed.</param>
    /// <param name="tap">Tap behavior.</param>
    /// <exception cref="InvalidOperationException"><paramref name="button"/> is left or right and no modifier is required.</exception>
    public MouseButtonTrigger(MouseButton button, KeyModifiers modifiers, TapOptions tap)
        : base(tap)
    {
        if (button is MouseButton.Left or MouseButton.Right && modifiers == KeyModifiers.None)
            throw new InvalidOperationException("A left or right click needs a modifier key such as Ctrl or Alt to be a trigger.");

        Button = button;
        Modifiers = modifiers;
    }

    /// <summary>Creates a trigger on a button with no modifiers required.</summary>
    /// <param name="button">Button that fires the trigger.</param>
    /// <param name="tap">Tap behavior.</param>
    /// <exception cref="InvalidOperationException"><paramref name="button"/> is left or right, which need a modifier.</exception>
    public MouseButtonTrigger(MouseButton button, TapOptions tap)
        : this(button, KeyModifiers.None, tap)
    {
    }

    /// <summary>Button that fires the trigger.</summary>
    public MouseButton Button { get; }

    /// <summary>Modifiers that must be held, and no others, when <see cref="Button"/> is pressed.</summary>
    public KeyModifiers Modifiers { get; }

    /// <inheritdoc />
    public override string DisplayName => $"{Describe(Button, Modifiers)} x{Tap.TapCount}";

    /// <summary>The button with its modifiers, as the settings file and the UI spell them: "Middle", "Ctrl+Left", "Ctrl+Alt+Middle".</summary>
    /// <param name="button">The button.</param>
    /// <param name="modifiers">The modifiers held with it, or none.</param>
    public static string Describe(MouseButton button, KeyModifiers modifiers) =>
        modifiers == KeyModifiers.None ? button.ToString() : $"{KeyCombo.FormatModifiers(modifiers)}+{button}";

    /// <summary>
    /// What a trigger on this input costs elsewhere, or null when nothing worth saying. Ctrl+click and Shift+click
    /// are multi-select and range-select in browsers, Explorer and Excel, and a trigger on them takes that away
    /// for as long as it is set.
    /// </summary>
    /// <param name="button">The button.</param>
    /// <param name="modifiers">The modifiers held with it.</param>
    public static string? CautionFor(MouseButton button, KeyModifiers modifiers) =>
        button == MouseButton.Left && (modifiers & (KeyModifiers.Control | KeyModifiers.Shift)) != 0
            ? "Ctrl+click and Shift+click have jobs in browsers, Explorer and Excel; while this trigger is set, those stop working. Alt+click or Ctrl+middle click are safer."
            : null;
}

/// <summary>A keyboard trigger: a key with optional modifiers, or a bare modifier.</summary>
/// <param name="Keys">The combination.</param>
/// <param name="Tap">Tap behavior.</param>
public sealed record HotkeyTrigger(KeyCombo Keys, TapOptions Tap) : TriggerDefinition(Tap)
{
    /// <inheritdoc />
    public override string DisplayName => $"{Keys} x{Tap.TapCount}";
}
