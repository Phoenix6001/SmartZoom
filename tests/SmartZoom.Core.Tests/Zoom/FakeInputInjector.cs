using SmartZoom.Core.Zoom;

namespace SmartZoom.Core.Tests.Zoom;

/// <summary>Records injected input as a readable script, e.g. "Ctrl↓ +1 +1 Ctrl↑".</summary>
internal sealed class FakeInputInjector : IInputInjector
{
    public List<string> Log { get; } = [];

    public bool ControlPhysicallyDown { get; set; }

    /// <summary>Modifiers the user is holding, released after <see cref="ReleaseHeldAfterPolls"/> state reads.</summary>
    public SmartZoom.Core.Input.KeyModifiers HeldModifiers { get; set; }

    /// <summary>How many <see cref="IsModifierDown"/> rounds report <see cref="HeldModifiers"/> before they lift (-1 = never).</summary>
    public int ReleaseHeldAfterPolls { get; set; } = -1;

    public int ModifierPolls { get; private set; }

    /// <summary>Wheel calls that should fail (0-based index among wheel calls). Simulates UIPI rejection mid-burst.</summary>
    public HashSet<int> FailWheelAt { get; } = [];

    public bool FailModifierDown { get; set; }

    public bool FailKeys { get; set; }

    private int _wheelCalls;

    public bool TrySendModifier(ModifierKey key, bool isDown)
    {
        if (isDown && FailModifierDown)
        {
            return false;
        }

        Log.Add(isDown ? "Ctrl↓" : "Ctrl↑");
        return true;
    }

    public bool TrySendWheel(int ticks)
    {
        if (FailWheelAt.Contains(_wheelCalls++))
        {
            return false;
        }

        Log.Add(ticks > 0 ? $"+{ticks}" : ticks.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return true;
    }

    public bool IsModifierDown(ModifierKey key)
    {
        if (key == ModifierKey.Control)
            ModifierPolls++;
        if (ReleaseHeldAfterPolls >= 0 && ModifierPolls > ReleaseHeldAfterPolls)
            HeldModifiers = SmartZoom.Core.Input.KeyModifiers.None;

        var flag = key switch
        {
            ModifierKey.Control => SmartZoom.Core.Input.KeyModifiers.Control,
            ModifierKey.Alt => SmartZoom.Core.Input.KeyModifiers.Alt,
            ModifierKey.Shift => SmartZoom.Core.Input.KeyModifiers.Shift,
            _ => SmartZoom.Core.Input.KeyModifiers.Win,
        };
        return (key == ModifierKey.Control && ControlPhysicallyDown) || HeldModifiers.HasFlag(flag);
    }

    public bool TrySendKeyCombo(SmartZoom.Core.Input.KeyCombo combo)
    {
        if (FailKeys)
        {
            return false;
        }

        Log.Add(combo.ToString());
        return true;
    }

    public string Script => string.Join(' ', Log);
}
