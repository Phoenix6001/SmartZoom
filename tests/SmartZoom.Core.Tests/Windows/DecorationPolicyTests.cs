using SmartZoom.Core.Input;
using SmartZoom.Core.Windows;
using SmartZoom.Core.Zoom.Content;

namespace SmartZoom.Core.Tests.Windows;

public sealed class DecorationPolicyTests
{
    /// <summary>Acrobat's real one: 5x5, layered, no-activate, tool window, topmost, parked at the cursor.</summary>
    private static readonly PixelRect AcrobatOverlay = PixelRect.FromSize(800, 600, 5, 5);

    [Fact]
    public void The_overlay_Acrobat_parks_under_the_pointer_is_a_decoration() =>
        Assert.True(DecorationPolicy.IsDecoration(
            AcrobatOverlay,
            DecorationPolicy.DecorationStyles.Layered | DecorationPolicy.DecorationStyles.ToolWindow));

    [Fact]
    public void A_small_window_with_none_of_the_styles_is_a_real_window() =>
        Assert.False(DecorationPolicy.IsDecoration(AcrobatOverlay, DecorationPolicy.DecorationStyles.None));

    [Theory]
    [InlineData(8, 8, true)]
    [InlineData(9, 8, false)]
    [InlineData(8, 9, false)]
    public void Anything_big_enough_to_hold_content_is_a_real_window(int width, int height, bool expected) =>
        Assert.Equal(expected, DecorationPolicy.IsDecoration(
            PixelRect.FromSize(0, 0, width, height),
            DecorationPolicy.DecorationStyles.Layered));

    [Fact]
    public void A_window_covers_a_point_inside_it() =>
        Assert.True(DecorationPolicy.Covers(PixelRect.FromSize(100, 100, 200, 200), new ScreenPoint(150, 150)));

    [Theory]
    [InlineData(99, 150)]
    [InlineData(300, 150)]       // right edge is exclusive
    [InlineData(150, 300)]
    public void A_window_does_not_cover_a_point_outside_it(int x, int y) =>
        Assert.False(DecorationPolicy.Covers(PixelRect.FromSize(100, 100, 200, 200), new ScreenPoint(x, y)));

    [Fact]
    public void The_z_order_budget_leaves_room_for_a_real_desktop()
    {
        // Measured here: 64 handles between the overlay and the reader it belonged to, of which 5 were
        // visible. A budget that counted every handle gave up 48 windows early and never once worked.
        Assert.True(DecorationPolicy.MaxWindowsBehind >= 5, "the measured case needs five visible windows");
        Assert.True(DecorationPolicy.MaxZOrderSteps >= 64, "... spread over sixty-four handles");
    }
}
