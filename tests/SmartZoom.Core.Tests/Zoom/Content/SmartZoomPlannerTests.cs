using SmartZoom.Core.Input;
using SmartZoom.Core.Zoom.Content;

namespace SmartZoom.Core.Tests.Zoom.Content;

public sealed class SmartZoomPlannerTests
{
    private static readonly PixelRect Viewport = PixelRect.FromSize(455, 420, 1874, 1527);
    private static readonly PixelRect Paragraph = PixelRect.FromSize(910, 1133, 949, 105);
    private static readonly ScreenPoint Cursor = new(1486, 1147);

    private readonly SmartZoomPlanner _planner = new(MinScale: 1.1, MaxScale: 3.0, Margin: 16);

    [Fact]
    public void Scale_makes_the_block_plus_margins_fill_the_viewport_width()
    {
        var plan = _planner.Plan(Paragraph, Viewport, Cursor)!.Value;

        Assert.Equal(1874.0 / (949 + 32), plan.Scale, precision: 6);
    }

    [Fact]
    public void Scaling_around_the_anchor_puts_the_block_left_edge_on_the_left_margin()
    {
        var plan = _planner.Plan(Paragraph, Viewport, Cursor)!.Value;

        var mappedLeft = plan.Anchor.X + ((Paragraph.Left - plan.Anchor.X) * plan.Scale);
        Assert.InRange(mappedLeft, Viewport.Left + 16 - 1.0, Viewport.Left + 16 + 1.0);
    }

    [Fact]
    public void Block_that_fits_vertically_is_centred()
    {
        var plan = _planner.Plan(Paragraph, Viewport, Cursor)!.Value;

        var mappedCenterY = plan.Anchor.Y + ((Paragraph.CenterY - plan.Anchor.Y) * plan.Scale);
        Assert.InRange(mappedCenterY, Viewport.CenterY - 1.0, Viewport.CenterY + 1.0);
    }

    [Fact]
    public void Tall_block_keeps_the_cursor_line_in_place()
    {
        var tall = PixelRect.FromSize(910, 629, 949, 3000);

        var plan = _planner.Plan(tall, Viewport, Cursor)!.Value;

        Assert.Equal(Cursor.Y, plan.Anchor.Y);
    }

    [Theory]
    [InlineData(2000, 1800, 250, 100)] // bottom-right corner
    [InlineData(460, 425, 250, 100)]   // top-left corner
    [InlineData(1500, 1850, 300, 90)]  // bottom edge, middle
    public void Zoomed_view_never_reaches_outside_the_current_viewport(int left, int top, int width, int height)
    {
        var block = PixelRect.FromSize(left, top, width, height);
        var cursor = new ScreenPoint(left + (width / 2), top + (height / 2));

        var plan = _planner.Plan(block, Viewport, cursor)!.Value;

        // The region that fills the screen after zooming: its top-left maps to the viewport's top-left.
        var s = plan.Scale;
        var regionLeft = plan.Anchor.X + ((Viewport.Left - plan.Anchor.X) / s) - Viewport.Left;
        var regionTop = plan.Anchor.Y + ((Viewport.Top - plan.Anchor.Y) / s) - Viewport.Top;
        Assert.InRange(regionLeft, -1.0, Viewport.Width - (Viewport.Width / s) + 1.0);
        Assert.InRange(regionTop, -1.0, Viewport.Height - (Viewport.Height / s) + 1.0);

        // And the block is still on screen after zooming.
        var mappedLeft = plan.Anchor.X + ((block.Left - plan.Anchor.X) * s);
        var mappedTop = plan.Anchor.Y + ((block.Top - plan.Anchor.Y) * s);
        Assert.InRange(mappedLeft, Viewport.Left - 1.0, Viewport.Right + 1.0);
        Assert.InRange(mappedTop, Viewport.Top - 1.0, Viewport.Bottom + 1.0);
    }

    [Fact]
    public void Scale_is_capped_at_max()
    {
        var narrow = PixelRect.FromSize(1000, 1000, 250, 100);

        Assert.Equal(3.0, _planner.Plan(narrow, Viewport, Cursor)!.Value.Scale);
    }

    [Fact]
    public void Block_already_filling_the_viewport_yields_no_plan()
    {
        var wide = PixelRect.FromSize(470, 800, 1800, 300);

        Assert.Null(_planner.Plan(wide, Viewport, Cursor));
    }

    [Fact]
    public void Anchor_is_kept_inside_the_viewport_with_the_insets()
    {
        var farRight = PixelRect.FromSize(2000, 1000, 300, 100);

        var plan = _planner.Plan(farRight, Viewport, Cursor, new AnchorInsets(X: 280, Y: 8))!.Value;

        Assert.InRange(plan.Anchor.X, Viewport.Left + 280, Viewport.Right - 1 - 280);
        Assert.InRange(plan.Anchor.Y, Viewport.Top + 8, Viewport.Bottom - 1 - 8);
    }

    [Fact]
    public void Inset_wider_than_the_viewport_falls_back_to_the_middle()
    {
        var narrowViewport = PixelRect.FromSize(0, 0, 400, 800);
        var block = PixelRect.FromSize(20, 100, 150, 50);

        var plan = _planner.Plan(block, narrowViewport, new ScreenPoint(50, 120), new AnchorInsets(X: 300, Y: 8))!.Value;

        Assert.Equal(200, plan.Anchor.X, 1.0);
    }

    [Fact]
    public void Empty_rectangles_yield_no_plan()
    {
        Assert.Null(_planner.Plan(new PixelRect(10, 10, 10, 50), Viewport, Cursor));
        Assert.Null(_planner.Plan(Paragraph, new PixelRect(0, 0, 0, 0), Cursor));
    }
}
