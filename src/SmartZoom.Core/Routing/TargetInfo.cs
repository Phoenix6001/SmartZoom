namespace SmartZoom.Core.Routing;

/// <summary>What sits under the cursor when a trigger fires.</summary>
/// <param name="RootWindow">Root owner top-level window handle; the key for per-window toggle state.</param>
/// <param name="HitWindow">The possibly-child window handle returned by WindowFromPoint.</param>
/// <param name="ProcessId">Process that owns <paramref name="RootWindow"/>.</param>
/// <param name="ProcessName">Image name without extension (e.g. "chrome"), or null if the process could not be queried.</param>
/// <param name="RootClassName">Window class of <paramref name="RootWindow"/>.</param>
/// <param name="HitClassName">Window class of <paramref name="HitWindow"/>.</param>
public sealed record TargetInfo(
    nint RootWindow,
    nint HitWindow,
    uint ProcessId,
    string? ProcessName,
    string RootClassName,
    string HitClassName);
