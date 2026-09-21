using System.Text.Json.Serialization;

using SmartZoom.Core.Input;

namespace SmartZoom.Core.Settings;

/// <summary>One trigger gesture. Set exactly one of <see cref="Mouse"/> or <see cref="Keys"/>.</summary>
public sealed class TriggerSettings
{
    /// <summary>The out-of-the-box trigger: double-tap of the Forward button.</summary>
    public static TriggerSettings CreateDefault() => new() { Mouse = MouseButton.XButton2 };

    /// <summary>Mouse button that fires the trigger. Leave unset for a hotkey trigger.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public MouseButton? Mouse { get; set; }

    /// <summary>Key combination that fires the trigger, e.g. "Ctrl+Alt+Z", "F9" or a bare "Ctrl" for double-tap.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Keys { get; set; }

    /// <summary>
    /// Presses per trigger: 2 (double-tap, the default, safe for an input that also has another job)
    /// or 1 (every press, ideal for an input dedicated to SmartZoom).
    /// </summary>
    public int TapCount { get; set; } = 2;

    /// <summary>For double-tap, the maximum time between presses in milliseconds; null uses the system double-click time.</summary>
    public uint? DoubleTapWindowMs { get; set; }

    /// <summary>
    /// Hide the trigger's presses from the target application. With double-tap, single presses are
    /// then delayed by the double-tap window before being replayed (noticeable on Back/Forward buttons).
    /// With single tap there is no delay.
    /// </summary>
    public bool SwallowClicks { get; set; }

    /// <summary>Builds the runtime definition.</summary>
    /// <param name="systemDoubleClickTimeMs">Used when <see cref="DoubleTapWindowMs"/> is null.</param>
    /// <exception cref="InvalidOperationException">Neither or both of <see cref="Mouse"/> and <see cref="Keys"/> are set.</exception>
    /// <exception cref="FormatException"><see cref="Keys"/> is not a valid combination.</exception>
    public TriggerDefinition ToDefinition(uint systemDoubleClickTimeMs)
    {
        var tap = new TapOptions(TapCount, DoubleTapWindowMs ?? systemDoubleClickTimeMs, SwallowClicks);

        return (Mouse, Keys) switch
        {
            (not null and not MouseButton.None, null) => new MouseButtonTrigger(Mouse.Value, tap),
            (null or MouseButton.None, { Length: > 0 }) => new HotkeyTrigger(KeyCombo.Parse(Keys), tap),
            (null or MouseButton.None, _) => throw new InvalidOperationException("A trigger needs either \"Mouse\" or \"Keys\"."),
            _ => throw new InvalidOperationException("A trigger can't have both \"Mouse\" and \"Keys\"; use two triggers."),
        };
    }

    /// <summary>Whether nothing here has been changed from its default.</summary>
    internal bool IsDefault() =>
        Mouse == MouseButton.XButton2 && Keys is null && TapCount == 2 && DoubleTapWindowMs is null && !SwallowClicks;
}
