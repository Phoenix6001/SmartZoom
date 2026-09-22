using SmartZoom.Core.Diagnostics;

namespace SmartZoom.Core.Zoom.Content;

/// <summary>Formats an accessibility chain as text, for the log and for diagnostics.</summary>
/// <remarks>
/// Two forms, deliberately kept apart. <see cref="Describe"/> is for the log file: it includes absolute
/// screen coordinates, useful to a developer with a screenshot in front of them. <see cref="Shape"/> is the
/// only form that may reach a <see cref="DiagnosticSample"/>: role and size only, never a position, and
/// never the text inside a node — <see cref="ContentNode"/> does not carry any. Never pass
/// <see cref="Describe"/>'s output to diagnostics.
/// </remarks>
public static class ContentPath
{
    /// <summary>The chain, leaf first, with role, size and absolute position. Log use only.</summary>
    /// <param name="chain">The ancestor chain from a <see cref="ContentHit"/>.</param>
    public static string Describe(IReadOnlyList<ContentNode> chain) =>
        string.Join(" < ", chain.Select(n =>
            $"{RoleName(n)} {n.Bounds.Width}x{n.Bounds.Height}@({n.Bounds.Left},{n.Bounds.Top})"));

    /// <summary>The chain, leaf first, with role and size only — safe to record in a diagnostic sample.</summary>
    /// <param name="chain">The ancestor chain from a <see cref="ContentHit"/>.</param>
    public static string Shape(IReadOnlyList<ContentNode> chain) =>
        string.Join(" < ", chain.Select(n => $"{RoleName(n)} {n.Bounds.Width}x{n.Bounds.Height}"));

    private static string RoleName(ContentNode node) =>
        node.Role == ContentRole.Other ? $"Other({node.RawRole})" : node.Role.ToString();
}
