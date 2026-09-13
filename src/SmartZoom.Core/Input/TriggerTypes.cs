namespace SmartZoom.Core.Input;

/// <summary>A point in physical (per-monitor, unscaled) screen pixels, as reported by the low-level hook.</summary>
/// <param name="X">Horizontal position in physical pixels on the virtual screen.</param>
/// <param name="Y">Vertical position in physical pixels on the virtual screen.</param>
public readonly record struct ScreenPoint(int X, int Y);

/// <summary>A detected double-tap.</summary>
/// <param name="Position">Cursor position at the second press, in physical pixels.</param>
/// <param name="TimestampMs">Hook event time on the GetTickCount clock.</param>
public readonly record struct TriggerEvent(ScreenPoint Position, uint TimestampMs);

/// <summary>
/// Input that was swallowed while waiting for a possible second press and must now be re-injected
/// so the target application still receives an ordinary single click.
/// </summary>
[Flags]
public enum ReplayAction : byte
{
    /// <summary>Nothing to replay.</summary>
    None = 0,

    /// <summary>Replay the button-down only; the physical button-up will follow unmodified.</summary>
    Down = 1,

    /// <summary>Replay the button-up only.</summary>
    Up = 2,

    /// <summary>Replay a complete click.</summary>
    DownUp = Down | Up,
}

/// <summary>Instruction to the hook callback for the event currently being processed.</summary>
/// <param name="Swallow">Block the event so the target application never sees it.</param>
/// <param name="Triggered">A double-tap completed on this event.</param>
/// <param name="Replay">
/// Previously swallowed input to re-inject. It logically precedes the current event, which is
/// always swallowed when a replay is requested, so ordering is preserved.
/// </param>
public readonly record struct HookDecision(bool Swallow, bool Triggered, ReplayAction Replay);

/// <summary>Configuration for <see cref="DoubleTapDetector"/>.</summary>
/// <param name="Button">Button whose double-press fires the trigger.</param>
/// <param name="DoubleTapWindowMs">Maximum time between the two button-down events, in milliseconds.</param>
/// <param name="SwallowClicks">Hide trigger presses from the target application.</param>
public sealed record TriggerOptions(MouseButton Button, uint DoubleTapWindowMs, bool SwallowClicks);
