using SmartZoom.Core.Zoom.Content;

namespace SmartZoom.Core.Tests.Zoom.Content;

public sealed class ReaderViewGeometryTests
{
    private static readonly PixelRect Pane = new(100, 100, 1900, 1300);

    [Theory]
    [InlineData(48, 1)]
    [InlineData(96, 2)]
    [InlineData(710, 15)]        // 14.8 notches: the reader can only move in whole ones
    [InlineData(20, 0)]          // less than half a notch is not worth a turn of the wheel
    [InlineData(-96, -2)]
    public void A_distance_becomes_whole_wheel_notches(int pixels, int expected) =>
        Assert.Equal(expected, ReaderViewGeometry.Notches(pixels));

    [Fact]
    public void Notches_convert_back_to_the_distance_they_really_cover() =>
        Assert.Equal(720, ReaderViewGeometry.Pixels(ReaderViewGeometry.Notches(710)));

    [Fact]
    public void The_strip_is_a_column_down_the_middle_clear_of_both_ends()
    {
        var strip = ReaderViewGeometry.Strip(Pane);

        Assert.Equal(ReaderViewGeometry.StripWidth, strip.Width);
        Assert.Equal(Pane.CenterX, strip.CenterX);
        Assert.Equal(Pane.Top + ReaderViewGeometry.StripInset, strip.Top);
        Assert.Equal(Pane.Bottom - ReaderViewGeometry.StripInset, strip.Bottom);
    }

    [Fact]
    public void A_narrow_pane_gets_a_narrower_strip_rather_than_one_that_spills_out()
    {
        var narrow = new PixelRect(0, 0, 60, 400);

        var strip = ReaderViewGeometry.Strip(narrow);

        Assert.True(strip.Left >= narrow.Left, "the strip starts inside the pane");
        Assert.True(strip.Right <= narrow.Right, "the strip ends inside the pane");
    }

    [Theory]
    [InlineData(200, 200, true)]
    [InlineData(40, 400, false)]     // too narrow for a strip
    [InlineData(400, 80, false)]     // too short once both insets are taken off
    public void A_pane_too_small_to_sample_is_not_measurable(int width, int height, bool expected) =>
        Assert.Equal(expected, ReaderViewGeometry.IsMeasurable(PixelRect.FromSize(0, 0, width, height)));

    [Fact]
    public void The_search_for_drift_is_bounded_by_a_third_of_the_pane()
    {
        // Wider and a page of evenly spaced lines starts matching at the wrong line.
        Assert.Equal(400, ReaderViewGeometry.MaxDrift(Pane));
    }

    [Fact]
    public void A_short_pane_still_searches_at_least_one_notch() =>
        Assert.Equal(ReaderViewGeometry.NotchPixels, ReaderViewGeometry.MaxDrift(PixelRect.FromSize(0, 0, 400, 60)));

    [Theory]
    [InlineData(0, false)]
    [InlineData(19, false)]      // a measured 19 px residue cannot be taken out in whole notches
    [InlineData(24, true)]
    [InlineData(-48, true)]
    public void Only_drift_of_at_least_half_a_notch_is_worth_correcting(int drift, bool expected) =>
        Assert.Equal(expected, ReaderViewGeometry.IsWorthCorrecting(drift));
}
