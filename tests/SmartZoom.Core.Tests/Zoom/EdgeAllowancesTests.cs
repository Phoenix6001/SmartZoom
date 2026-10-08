using SmartZoom.Core.Zoom;
using SmartZoom.Core.Zoom.Content;

namespace SmartZoom.Core.Tests.Zoom;

/// <summary>
/// The distances contacts keep from a browser's edges, which were measured on a 200% display as fixed pixels and
/// are now held in DIPs and scaled by the display the press is on.
/// </summary>
public sealed class EdgeAllowancesTests
{
    [Fact]
    public void On_the_display_they_were_measured_on_they_are_what_was_measured()
    {
        var edges = EdgeAllowances.For(2.0);

        Assert.Equal(56, edges.Scrollbar);
        Assert.Equal(24, edges.ResizeBorder);
        Assert.Equal(28, edges.AnchorInset);
        Assert.Equal(60, edges.AnchorInsetVertical);
    }

    [Fact]
    public void At_100_percent_they_are_half_as_wide()
    {
        // A 100% scrollbar is about 17 px; 56 px kept the fingers more than three scrollbars' width away.
        var edges = EdgeAllowances.For(1.0);

        Assert.Equal(28, edges.Scrollbar);
        Assert.Equal(12, edges.ResizeBorder);
    }

    [Fact]
    public void At_300_percent_they_are_wider_than_the_200_percent_values()
    {
        var edges = EdgeAllowances.For(3.0);

        Assert.Equal(84, edges.Scrollbar);
        Assert.Equal(36, edges.ResizeBorder);
    }

    [Theory]
    [InlineData(1.25, 35, 15)]
    [InlineData(1.5, 42, 18)]
    [InlineData(1.75, 49, 21)]
    [InlineData(2.5, 70, 30)]
    public void A_fractional_scale_rounds_up_rather_than_falling_a_pixel_short(double scale, int scrollbar, int border)
    {
        var edges = EdgeAllowances.For(scale);

        Assert.Equal(scrollbar, edges.Scrollbar);
        Assert.Equal(border, edges.ResizeBorder);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void A_scale_that_is_not_one_is_taken_as_100_percent(double scale) =>
        Assert.Equal(EdgeAllowances.For(1.0), EdgeAllowances.For(scale));

    [Fact]
    public void Contacts_keep_clear_of_the_window_resize_border_on_every_side()
    {
        // A touch contact on the viewport's outermost pixels grabs the window's resize border (measured at 200%:
        // 8 px inside the window rect resizes, 12 px does not) and a pinch then drags the window edge instead.
        var bounds = EdgeAllowances.For(2.0).ContactBounds(PixelRect.FromSize(1891, 85, 1793, 1527));

        Assert.Equal(new PixelRect(1891 + 24, 85 + 24, 1891 + 1793 - 56, 85 + 1527 - 56), bounds);
    }

    [Fact]
    public void Contacts_keep_clear_of_a_horizontal_scrollbar_at_the_bottom()
    {
        // A page that scrolls sideways has a scrollbar along the bottom, 17 DIPs high: at 100% the 12 px resize
        // allowance put contacts on its upper rows.
        var edges = EdgeAllowances.For(1.0);
        var bounds = edges.ContactBounds(PixelRect.FromSize(0, 0, 1600, 1200));

        Assert.True(1200 - bounds.Bottom >= 17, $"bottom allowance {1200 - bounds.Bottom}");
        Assert.True(edges.AnchorInsetVertical > 1200 - bounds.Bottom, "the anchor stays above the contact area's bottom");
    }

    [Fact]
    public void Contact_bounds_of_a_tiny_viewport_never_collapse()
    {
        var bounds = EdgeAllowances.For(2.0).ContactBounds(PixelRect.FromSize(100, 100, 30, 30));

        Assert.Equal(1, bounds.Width);
        Assert.Equal(1, bounds.Height);
    }
}
