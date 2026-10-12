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

    /// <summary>The most nodes a shape keeps before it says how many it left out.</summary>
    /// <remarks>
    /// A shape is read by a person looking for the node that should have been zoomed, and that node is near
    /// the leaf. A deeply nested page can produce a chain dozens of nodes long, which buries the useful end
    /// and eats the sample ring's budget for nothing.
    /// </remarks>
    public const int MaxShapeNodes = 12;

    /// <summary>The chain, leaf first, with role and size only — safe to record in a diagnostic sample.</summary>
    /// <param name="chain">The ancestor chain from a <see cref="ContentHit"/>.</param>
    /// <returns>The shape, truncated after <see cref="MaxShapeNodes"/> nodes.</returns>
    public static string Shape(IReadOnlyList<ContentNode> chain)
    {
        ArgumentNullException.ThrowIfNull(chain);

        var kept = string.Join(" < ", chain.Take(MaxShapeNodes).Select(n => $"{RoleName(n)} {n.Bounds.Width}x{n.Bounds.Height}"));

        return chain.Count <= MaxShapeNodes
            ? kept
            : $"{kept} < … [{chain.Count - MaxShapeNodes} more]";
    }

    /// <summary>
    /// The path from the element under the cursor out to the page, with every nested document above the
    /// leaf turned into an ordinary container and everything above the page dropped.
    /// </summary>
    /// <param name="path">The path as the tree reported it, leaf first, out to the tree's root.</param>
    /// <returns>The path ending at the outermost document, or the path unchanged when it has no document.</returns>
    /// <remarks>
    /// A page assembled from iframes — a news site with embedded cards and advertisements is the usual case —
    /// has a document at every frame boundary. Stopping at the first one makes an advertisement's 656 px
    /// frame "the page": the viewport becomes the frame, the real containers above it are never seen, and a
    /// paragraph that fills its little frame is rejected for being wider than 90% of "the viewport". The
    /// press then does nothing at all. Only the outermost document is the page; the rest are boxes on it, and
    /// what lies above it is the browser's own window.
    /// </remarks>
    public static IReadOnlyList<ContentNode> ToPage(IReadOnlyList<ContentNode> path)
    {
        ArgumentNullException.ThrowIfNull(path);

        var page = -1;
        for (var i = path.Count - 1; i >= 0 && page < 0; i--)
        {
            if (path[i].Role == ContentRole.Document)
                page = i;
        }

        if (page < 0)
            return path;

        return [.. path.Take(page + 1).Select((n, i) => i < page && n.Role == ContentRole.Document ? n with { Role = ContentRole.Group } : n)];
    }

    /// <summary>The path with the window standing in as its page, for a tree that never reached a document.</summary>
    /// <param name="chain">What the tree answered, leaf first; may be empty.</param>
    /// <param name="window">The render window's rectangle, or null when it could not be read.</param>
    /// <returns>The chain ending in a document node with the window's bounds, or null when there is no window.</returns>
    /// <remarks>
    /// An empty chain still gets a page. A browser zoom needs only the page's rectangle, and a page that is
    /// still loading can answer every attempt with nothing; giving up there was the press that did nothing on
    /// a freshly opened page (issue #6).
    /// </remarks>
    public static IReadOnlyList<ContentNode>? WithWindowAsPage(IReadOnlyList<ContentNode> chain, PixelRect? window)
    {
        ArgumentNullException.ThrowIfNull(chain);

        return window is { } page ? [.. chain, new ContentNode(ContentRole.Document, page)] : null;
    }

    private static string RoleName(ContentNode node) =>
        node.Role == ContentRole.Other ? $"Other({node.RawRole})" : node.Role.ToString();
}
