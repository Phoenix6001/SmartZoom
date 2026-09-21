namespace SmartZoom.Core.Input;

/// <summary>A detected trigger gesture.</summary>
/// <param name="Position">Cursor position when the gesture completed, in physical pixels.</param>
/// <param name="TimestampMs">Hook event time on the GetTickCount clock.</param>
public readonly record struct TriggerEvent(ScreenPoint Position, uint TimestampMs);
