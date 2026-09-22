namespace SmartZoom.Core.Diagnostics;

/// <summary>What a counter counts. Deliberately holds no title, no text and no coordinates.</summary>
/// <param name="Kind">The class of event.</param>
/// <param name="Process">Image name of the application, or null when it could not be identified.</param>
/// <param name="Adapter">The strategy involved, or null when none was chosen.</param>
/// <param name="Reason">Why, as a stable label.</param>
public sealed record DiagnosticKey(DiagnosticKind Kind, string? Process, string? Adapter, string? Reason);
