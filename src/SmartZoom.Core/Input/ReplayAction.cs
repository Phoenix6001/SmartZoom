namespace SmartZoom.Core.Input;

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
