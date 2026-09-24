namespace SmartZoom.Core.Zoom.Content;

/// <summary>
/// What a region of the screen looked like at one moment, reduced to a small grid of brightness cells. Two
/// samples of the same region taken around a gesture say whether anything on screen changed, which is the only
/// honest test of whether a page took a pinch: a browser's accessibility rectangles do not follow visual zoom.
/// </summary>
/// <remarks>
/// The grid is coarse on purpose. A cell averages a block of pixels, so a caret blinking or an antialiased edge
/// shifting by a pixel changes no cell, while a zoom moves every edge in the region and changes most of them.
/// </remarks>
/// <param name="Region">The screen rectangle the sample was taken from.</param>
/// <param name="Columns">Cells across.</param>
/// <param name="Rows">Cells down.</param>
/// <param name="Cells">Mean luma (0–255) of each cell, row-major, <paramref name="Columns"/> × <paramref name="Rows"/> of them.</param>
public sealed record ScreenSample(PixelRect Region, int Columns, int Rows, ReadOnlyMemory<byte> Cells)
{
    /// <summary>Most cells across; a wider region is averaged down to this many.</summary>
    public const int MaxColumns = 64;

    /// <summary>Most cells down; a taller region is averaged down to this many.</summary>
    public const int MaxRows = 48;

    /// <summary>
    /// How far a cell's luma may move (of 255) before it counts as changed. A change smaller than this is
    /// noise: dithering, a hover highlight fading, the last frame of a scrollbar animation.
    /// </summary>
    public const int LumaTolerance = 12;

    /// <summary>Mean luma of each cell, row-major.</summary>
    public ReadOnlyMemory<byte> Cells { get; } = Cells.Length == Columns * Rows
        ? Cells
        : throw new ArgumentException($"Expected {Columns * Rows} cells for a {Columns}x{Rows} grid, got {Cells.Length}.", nameof(Cells));

    /// <summary>The grid a region of the given size is reduced to: one cell per pixel up to the maximum.</summary>
    /// <param name="width">Region width in pixels.</param>
    /// <param name="height">Region height in pixels.</param>
    public static (int Columns, int Rows) GridFor(int width, int height) =>
        (Math.Clamp(width, 1, MaxColumns), Math.Clamp(height, 1, MaxRows));

    /// <summary>Builds a sample from the luma of every pixel in a region, averaging blocks of pixels into cells.</summary>
    /// <param name="region">The screen rectangle the luma was read from.</param>
    /// <param name="luma">One luma value (0–255) per pixel of <paramref name="region"/>, row-major, with no padding.</param>
    /// <exception cref="ArgumentException">The region is empty, or <paramref name="luma"/> is not one value per pixel.</exception>
    public static ScreenSample FromLuma(PixelRect region, ReadOnlySpan<byte> luma)
    {
        if (region.IsEmpty)
            throw new ArgumentException("The region has no area.", nameof(region));
        if (luma.Length != region.Width * region.Height)
            throw new ArgumentException($"Expected {region.Width * region.Height} luma values for a {region.Width}x{region.Height} region, got {luma.Length}.", nameof(luma));

        var (columns, rows) = GridFor(region.Width, region.Height);
        var cells = new byte[columns * rows];

        for (var row = 0; row < rows; row++)
        {
            // Cell edges are the proportional split of the region, so every pixel lands in exactly one cell and
            // the last cells are never a pixel short.
            var top = row * region.Height / rows;
            var bottom = (row + 1) * region.Height / rows;

            for (var column = 0; column < columns; column++)
            {
                var left = column * region.Width / columns;
                var right = (column + 1) * region.Width / columns;

                var total = 0;
                for (var y = top; y < bottom; y++)
                {
                    var line = luma.Slice(y * region.Width, region.Width);
                    for (var x = left; x < right; x++)
                        total += line[x];
                }

                cells[(row * columns) + column] = (byte)(total / ((bottom - top) * (right - left)));
            }
        }

        return new ScreenSample(region, columns, rows, cells);
    }

    /// <summary>
    /// The fraction of cells (0 to 1) whose luma moved by more than <see cref="LumaTolerance"/> between this
    /// sample and another of the same region.
    /// </summary>
    /// <param name="other">The later sample.</param>
    /// <returns>
    /// 0 when nothing moved, 1 when every cell did — and 1 when the two samples are not of the same size and
    /// so cannot be compared, so that a mismatch never reads as "nothing changed".
    /// </returns>
    public double Difference(ScreenSample other)
    {
        ArgumentNullException.ThrowIfNull(other);

        if (other.Columns != Columns || other.Rows != Rows || other.Region.Width != Region.Width || other.Region.Height != Region.Height)
            return 1.0;

        var mine = Cells.Span;
        var theirs = other.Cells.Span;
        var changed = 0;
        for (var i = 0; i < mine.Length; i++)
        {
            if (Math.Abs(mine[i] - theirs[i]) > LumaTolerance)
                changed++;
        }

        return (double)changed / mine.Length;
    }
}
