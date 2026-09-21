namespace SmartZoom.Core.Input;

/// <summary>How presses of one input turn into a trigger.</summary>
/// <param name="TapCount">Presses per trigger: 1 (every press) or 2 (double-tap).</param>
/// <param name="DoubleTapWindowMs">For double-tap, the maximum time between the two presses, in milliseconds.</param>
/// <param name="SwallowInput">Hide the trigger's presses from the target application.</param>
public sealed record TapOptions(int TapCount, uint DoubleTapWindowMs, bool SwallowInput);
