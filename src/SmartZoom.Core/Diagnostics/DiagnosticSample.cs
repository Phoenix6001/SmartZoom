namespace SmartZoom.Core.Diagnostics;

/// <summary>One concrete example, kept so a count can actually be diagnosed.</summary>
/// <param name="Key">What happened.</param>
/// <param name="When">When it happened.</param>
/// <param name="Detail">The accessibility path shape, roles and sizes only, or null.</param>
/// <param name="Exception">Type, redacted message and truncated stack, or null.</param>
public sealed record DiagnosticSample(DiagnosticKey Key, DateTimeOffset When, string? Detail, string? Exception);
