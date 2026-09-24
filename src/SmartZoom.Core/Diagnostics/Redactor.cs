namespace SmartZoom.Core.Diagnostics;

/// <summary>Cleans one piece of text before it is rendered.</summary>
/// <param name="text">The text.</param>
/// <returns>The text with identifying parts removed.</returns>
public delegate string Redactor(string text);
