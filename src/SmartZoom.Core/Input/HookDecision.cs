namespace SmartZoom.Core.Input;

/// <summary>Instruction to the hook callback for the event currently being processed.</summary>
/// <param name="Swallow">Block the event so the target application never sees it.</param>
/// <param name="Triggered">A trigger gesture completed on this event.</param>
/// <param name="Replay">
/// Previously swallowed input to re-inject. It logically precedes the current event, which is
/// always swallowed when a replay is requested, so ordering is preserved.
/// </param>
public readonly record struct HookDecision(bool Swallow, bool Triggered, ReplayAction Replay);
