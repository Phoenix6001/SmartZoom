using SmartZoom.Core.Zoom.Content;

namespace SmartZoom.Core.Tests.Zoom.Content;

public sealed class ScreenSampleTests
{
    private static readonly PixelRect Region = PixelRect.FromSize(100, 200, 10, 4);

    private static ScreenSample Flat(byte luma, PixelRect? region = null)
    {
        var r = region ?? Region;
        var pixels = new byte[r.Width * r.Height];
        Array.Fill(pixels, luma);
        return ScreenSample.FromLuma(r, pixels);
    }

    public sealed class Difference
    {
        [Fact]
        public void Identical_samples_differ_by_nothing() =>
            Assert.Equal(0.0, Flat(90).Difference(Flat(90)));

        [Fact]
        public void Samples_that_differ_everywhere_differ_by_everything() =>
            Assert.Equal(1.0, Flat(0).Difference(Flat(255)));

        [Fact]
        public void Regions_of_different_sizes_are_incomparable_and_never_read_as_unchanged()
        {
            var wider = Flat(90, PixelRect.FromSize(100, 200, 11, 4));

            Assert.Equal(1.0, Flat(90).Difference(wider));
        }

        [Fact]
        public void The_same_size_elsewhere_on_screen_is_still_comparable()
        {
            var moved = Flat(90, PixelRect.FromSize(300, 400, 10, 4));

            Assert.Equal(0.0, Flat(90).Difference(moved));
        }

        [Fact]
        public void A_cell_counts_as_changed_only_past_the_tolerance()
        {
            var within = (byte)(90 + ScreenSample.LumaTolerance);
            var beyond = (byte)(within + 1);

            Assert.Equal(0.0, Flat(90).Difference(Flat(within)));
            Assert.Equal(1.0, Flat(90).Difference(Flat(beyond)));
            Assert.Equal(1.0, Flat(beyond).Difference(Flat(90)));
        }

        [Fact]
        public void Counts_the_fraction_of_cells_that_moved()
        {
            var pixels = new byte[Region.Width * Region.Height];
            var later = new byte[pixels.Length];
            for (var i = 0; i < 12; i++)
                later[i] = 200;     // 12 of 40 one-pixel cells

            Assert.Equal(0.3, ScreenSample.FromLuma(Region, pixels).Difference(ScreenSample.FromLuma(Region, later)), precision: 9);
        }
    }

    public sealed class FromLuma
    {
        [Fact]
        public void A_small_region_keeps_one_cell_per_pixel()
        {
            var sample = Flat(90);

            Assert.Equal((10, 4), (sample.Columns, sample.Rows));
            Assert.Equal(40, sample.Cells.Length);
            Assert.Equal(Region, sample.Region);
        }

        [Fact]
        public void A_large_region_is_averaged_down_to_the_grid()
        {
            // 128 px across: two pixels per cell, alternating 0 and 100, so every cell averages to 50.
            var region = PixelRect.FromSize(0, 0, 128, 96);
            var pixels = new byte[128 * 96];
            for (var i = 0; i < pixels.Length; i++)
                pixels[i] = (byte)(i % 2 == 0 ? 0 : 100);

            var sample = ScreenSample.FromLuma(region, pixels);

            Assert.Equal((ScreenSample.MaxColumns, ScreenSample.MaxRows), (sample.Columns, sample.Rows));
            Assert.All(sample.Cells.ToArray(), cell => Assert.Equal(50, cell));
        }

        [Fact]
        public void A_region_that_does_not_divide_evenly_still_covers_every_pixel()
        {
            // 65 px into 64 cells: one cell is two pixels wide; the bright last pixel must land in it.
            var region = PixelRect.FromSize(0, 0, 65, 1);
            var pixels = new byte[65];
            pixels[64] = 200;

            var sample = ScreenSample.FromLuma(region, pixels);

            Assert.Equal(100, sample.Cells.Span[63]);
        }

        [Fact]
        public void Rejects_luma_that_is_not_one_value_per_pixel() =>
            Assert.Throws<ArgumentException>(() => ScreenSample.FromLuma(Region, new byte[3]));

        [Fact]
        public void Rejects_an_empty_region() =>
            Assert.Throws<ArgumentException>(() => ScreenSample.FromLuma(PixelRect.FromSize(0, 0, 0, 5), []));
    }

    [Fact]
    public void Cells_must_fill_the_grid() =>
        Assert.Throws<ArgumentException>(() => new ScreenSample(Region, 2, 2, new byte[3]));
}
