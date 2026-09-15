using SmartZoom.Core.Zoom;

namespace SmartZoom.Core.Tests.Zoom;

/// <summary>Records injected input as a readable script, e.g. "Ctrl↓ +1 +1 Ctrl↑".</summary>
internal sealed class FakeInputInjector : IInputInjector
{
    public List<string> Log { get; } = [];

    public bool ControlPhysicallyDown { get; set; }

    /// <summary>Wheel calls that should fail (0-based index among wheel calls). Simulates UIPI rejection mid-burst.</summary>
    public HashSet<int> FailWheelAt { get; } = [];

    public bool FailModifierDown { get; set; }

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

    public bool IsModifierDown(ModifierKey key) => ControlPhysicallyDown;

    public string Script => string.Join(' ', Log);
}
