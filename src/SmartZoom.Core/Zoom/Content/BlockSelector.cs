namespace SmartZoom.Core.Zoom.Content;

/// <summary>
/// Picks the "block" a smart zoom should fit: the reading unit around the cursor, such as a
/// paragraph, image or table, rather than a single text run or the whole page.
/// </summary>
/// <param name="MinWidth">Narrower candidates are skipped (icons, single words).</param>
/// <param name="MaxWidthFraction">Candidates wider than this fraction of the viewport are considered "the page" and skipped.</param>
/// <param name="MinHeight">Shorter candidates are skipped (hairlines, empty spans).</param>
public sealed record BlockSelector(int MinWidth = 200, double MaxWidthFraction = 0.9, int MinHeight = 16)
{
    /// <summary>Chooses the block for a hit, or null if nothing suitable is on the path.</summary>
    public ContentNode? Select(ContentHit hit)
    {
        ArgumentNullException.ThrowIfNull(hit);

        var chain = hit.Chain;
        if (chain.Count == 0)
            return null;

        var maxWidth = hit.Viewport.Width * MaxWidthFraction;

        for (var i = 0; i < chain.Count; i++)
        {
            var node = chain[i];
            if (node.Role == ContentRole.Document)
                break;

            if (!Qualifies(node, maxWidth))
                continue;

            // A text run is part of its paragraph; the paragraph is what the reader wants to see whole.
            // Prefer the nearest qualifying container if there is one directly above the run.
            if (node.Role is ContentRole.Text or ContentRole.Link && i + 1 < chain.Count)
            {
                var parent = chain[i + 1];
                if (parent.Role is ContentRole.Group or ContentRole.ListItem or ContentRole.Heading && Qualifies(parent, maxWidth))
                    return parent;
            }

            return node;
        }

        return null;
    }

    private bool Qualifies(ContentNode node, double maxWidth)
    {
        var b = node.Bounds;
        return node.Role != ContentRole.Other
            && b.Width >= MinWidth
            && b.Width <= maxWidth
            && b.Height >= MinHeight;
    }
}
