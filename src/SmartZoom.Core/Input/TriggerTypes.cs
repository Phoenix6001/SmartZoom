namespace SmartZoom.Core.Input;

/// <summary>A point in physical (per-monitor, unscaled) screen pixels, as reported by the low-level hook.</summary>
/// <param name="X">Horizontal position in physical pixels on the virtual screen.</param>
/// <param name="Y">Vertical position in physical pixels on the virtual screen.</param>
public readonly record struct ScreenPoint(int X, int Y);

/// <summary>A detected trigger gesture.</summary>
/// <param name="Position">Cursor position when the gesture completed, in physical pixels.</param>
/// <param name="TimestampMs">Hook event time on the GetTickCount clock.</param>
public readonly record struct TriggerEvent(ScreenPoint Position, uint TimestampMs);

/// <summary>
/// Input that was swallowed while waiting for a possible second press and must now be re-injected
/// so the target application still receives an ordinary single press.
/// </summary>
[Flags]
public enum ReplayAction : byte
{
    /// <summary>Nothing to replay.</summary>
    None = 0,

    /// <summary>Replay the press only; the physical release will follow unmodified.</summary>
    Down = 1,

    /// <summary>Replay the release only.</summary>
    Up = 2,

    /// <summary>Replay a complete press and release.</summary>
    DownUp = Down | Up,
}

/// <summary>Instruction to the hook callback for the event currently being processed.</summary>
/// <param name="Swallow">Block the event so the target application never sees it.</param>
/// <param name="Triggered">A trigger gesture completed on this event.</param>
/// <param name="Replay">
/// Previously swallowed input to re-inject. It logically precedes the current event, which is
/// always swallowed when a replay is requested, so ordering is preserved.
/// </param>
public readonly record struct HookDecision(bool Swallow, bool Triggered, ReplayAction Replay);

/// <summary>How presses of one input turn into a trigger.</summary>
/// <param name="TapCount">Presses per trigger: 1 (every press) or 2 (double-tap).</param>
/// <param name="DoubleTapWindowMs">For double-tap, the maximum time between the two presses, in milliseconds.</param>
/// <param name="SwallowInput">Hide the trigger's presses from the target application.</param>
public sealed record TapOptions(int TapCount, uint DoubleTapWindowMs, bool SwallowInput);

/// <summary>An input gesture that fires SmartZoom.</summary>
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
