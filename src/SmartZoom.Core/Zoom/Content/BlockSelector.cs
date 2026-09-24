namespace SmartZoom.Core.Zoom.Content;

/// <summary>
/// Picks the "block" a smart zoom should fit: the reading unit around the cursor, such as a
/// paragraph, image or table, rather than a single text run or the whole page.
/// </summary>
/// <param name="MinWidth">
/// Narrower candidates are skipped (icons, single words) unless they are at least <see cref="MinColumnWidth"/> wide
/// and <paramref name="MinColumnHeight"/> tall: a sidebar or table of contents is a narrow column, and still the
/// reading unit under the cursor.
/// </param>
/// <param name="MaxWidthFraction">Candidates wider than this fraction of the viewport are considered "the page" and skipped.</param>
/// <param name="MinHeight">Shorter candidates are skipped (hairlines, empty spans).</param>
/// <param name="MaxHeightFraction">
/// Candidates taller than this many viewport heights are containers (an article, a main column), not
/// reading units, and are skipped. Without it, a window too narrow for its paragraphs to qualify would
/// zoom the whole article by a few percent instead of doing nothing.
/// </param>
/// <param name="MinColumnHeight">Height from which a candidate narrower than <paramref name="MinWidth"/> still counts as a column.</param>
public sealed record BlockSelector(int MinWidth = 200, double MaxWidthFraction = 0.9, int MinHeight = 16, double MaxHeightFraction = 2.0, int MinColumnHeight = 240)
{
    /// <summary>
    /// The narrowest a candidate may be and still count as a column: half of <see cref="MinWidth"/>. Anything
    /// narrower is a gutter or an icon strip whatever its height.
    /// </summary>
    public int MinColumnWidth => MinWidth / 2;

    /// <summary>Chooses the block for a hit, or null if nothing suitable is on the path.</summary>
    public ContentNode? Select(ContentHit hit)
    {
        ArgumentNullException.ThrowIfNull(hit);

        var chain = hit.Chain;
        if (chain.Count == 0)
            return null;

        var maxWidth = hit.Viewport.Width * MaxWidthFraction;
        var maxHeight = hit.Viewport.Height * MaxHeightFraction;

        for (var i = 0; i < chain.Count; i++)
        {
            var node = chain[i];
            if (node.Role == ContentRole.Document)
                break;

            if (!Qualifies(node, maxWidth, maxHeight))
                continue;

            // A text run is part of its paragraph; the paragraph is what the reader wants to see whole.
            // Prefer the nearest qualifying container if there is one directly above the run.
            if (node.Role is ContentRole.Text or ContentRole.Link && i + 1 < chain.Count)
            {
                var parent = chain[i + 1];
                if (parent.Role is ContentRole.Group or ContentRole.ListItem or ContentRole.Heading && Qualifies(parent, maxWidth, maxHeight))
                    return parent;
            }

            return node;
        }

        return null;
    }

    private bool Qualifies(ContentNode node, double maxWidth, double maxHeight)
    {
        var b = node.Bounds;
        return node.Role != ContentRole.Other
            && (b.Width >= MinWidth || (b.Width >= MinColumnWidth && b.Height >= MinColumnHeight))
            && b.Width <= maxWidth
            && b.Height >= MinHeight
            && b.Height <= maxHeight;
    }
}
