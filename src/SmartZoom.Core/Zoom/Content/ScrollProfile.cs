namespace SmartZoom.Core.Zoom.Content;

/// <summary>
/// Measures how far a document scrolled, by comparing one-dimensional brightness profiles of the same strip of
/// the window taken before and after. A row of text is a dark band in the profile, so a scrolled page shows the
/// same sequence of bands at a different offset.
/// </summary>
/// <remarks>
/// Matching profiles rather than images keeps this cheap: a strip a hundred pixels wide collapses to one number
/// per row, and the search is a single pass over the candidate offsets.
/// </remarks>
public static class ScrollProfile
{
    /// <summary>Rows at each end that the search may leave unmatched, so a shift always has overlap to score.</summary>
    private const int MinOverlap = 40;

    /// <summary>Offsets the search needs before "how a middling offset scores" means anything.</summary>
    private const int MinCandidates = 8;

    /// <summary>How far a scroll moved, and how much the answer can be trusted.</summary>
    /// <param name="Pixels">
    /// How far the content moved up the screen: <c>after[y]</c> matches <c>before[y + Pixels]</c>. Positive means
    /// the view moved towards the end of the document.
    /// </param>
    /// <param name="Score">Mean difference per row at the winning offset; 0 is a perfect match.</param>
    /// <param name="Typical">
    /// Mean difference per row at a middling offset. A page that really moved matches its own pixels far better
    /// at one offset than at any other, so a <see cref="Score"/> well below this is the mark of a real answer,
    /// whatever the page's contrast. Blank strips score alike everywhere and are rejected.
    /// </param>
    public readonly record struct Shift(int Pixels, double Score, double Typical)
    {
        /// <summary>
        /// Whether the offset explains the change well enough to act on. The margin is not generous: evenly
        /// spaced lines of text match tolerably at every line pitch, which flattens the difference between a
        /// right and a nearly-right offset, and being one line out matters far less than not measuring at all.
        /// </summary>
        public bool IsClear => Typical > 0 && Score < Typical * 0.8;
    }

    /// <summary>Finds the offset that best explains the difference between two profiles.</summary>
    /// <param name="before">Row brightness before the scroll.</param>
    /// <param name="after">Row brightness after the scroll.</param>
    /// <param name="lowest">Smallest offset to consider, in rows.</param>
    /// <param name="highest">Largest offset to consider, in rows.</param>
    /// <remarks>
    /// Keep the range to what could physically have happened. A document scrolls by at most what was asked and
    /// never the other way, and a narrow range is the only thing that stops a page of evenly spaced lines from
    /// matching at the wrong one.
    /// </remarks>
    public static Shift FindShift(ReadOnlySpan<int> before, ReadOnlySpan<int> after, int lowest, int highest)
    {
        if (before.Length != after.Length || before.Length < (2 * MinOverlap) || highest < lowest)
            return new Shift(0, 0, 0);

        var reach = before.Length - MinOverlap;
        var from = Math.Max(lowest, -reach);
        var to = Math.Min(highest, reach);
        if (to < from)
            return new Shift(0, 0, 0);

        var scores = new double[to - from + 1];
        var best = from;
        var bestScore = double.MaxValue;

        for (var shift = from; shift <= to; shift++)
        {
            var start = Math.Max(0, -shift);
            var end = Math.Min(after.Length, before.Length - shift);
            if (end - start < MinOverlap)
            {
                scores[shift - from] = double.MaxValue;
                continue;
            }

            long total = 0;
            for (var y = start; y < end; y++)
                total += Math.Abs(before[y + shift] - after[y]);

            var mean = (double)total / (end - start);
            scores[shift - from] = mean;
            if (mean < bestScore)
            {
                bestScore = mean;
                best = shift;
            }
        }

        // With only a handful of offsets to choose from there is no middling one to compare against, and a
        // confident-looking answer would be an accident.
        return new Shift(best, bestScore, Median(scores));
    }

    /// <summary>The middling score, or 0 when too few offsets were actually scored for one to mean anything.</summary>
    private static double Median(double[] scores)
    {
        // Offsets with too little overlap were never scored; counting them would let a range that looks wide
        // enough pass on the strength of two or three real candidates.
        var usable = scores.Where(s => s < double.MaxValue).ToArray();
        if (usable.Length < MinCandidates)
            return 0;

        Array.Sort(usable);
        return usable[usable.Length / 2];
    }
}
