using SmartZoom.Core.Input;
using SmartZoom.Core.Zoom.Content;

namespace SmartZoom.Core.Tests.Zoom.Content;

public sealed class RetryAnchorTests
{
    // Contacts may use the viewport minus 24 px of resize border on three sides and 56 px of scrollbar strip
    // on the right, so the usable area here is (24, 24) to (1944, 1176).
    private static readonly PixelRect Viewport = PixelRect.FromSize(0, 0, 2000, 1200);
    private static readonly ScreenPoint Centre = new(1000, 600);

    [Fact]
    public void First_candidate_is_outside_the_block_on_the_side_with_the_most_room()
    {
        // 776 px of room to the left, 744 to the right, 376 above and below.
        var block = PixelRect.FromSize(800, 400, 400, 400);

        var candidates = RetryAnchor.Candidates(block, Viewport, new ScreenPoint(1000, 600));

        Assert.Equal(new ScreenPoint(800 - RetryAnchor.OutsideGap, 600), candidates[0]);
        Assert.False(block.Contains(candidates[0]));
        Assert.True(block.Left - candidates[0].X >= RetryAnchor.OutsideGap, "the candidate keeps its clearance from the block");
    }

    [Fact]
    public void A_candidate_beside_the_block_stays_on_the_original_anchors_row()
    {
        var block = PixelRect.FromSize(200, 400, 400, 400);

        // Room to the right (1344 px) beats room to the left (176 px), so the candidate goes there.
        var candidates = RetryAnchor.Candidates(block, Viewport, new ScreenPoint(400, 700));

        Assert.Equal(new ScreenPoint(600 + RetryAnchor.OutsideGap, 700), candidates[0]);
    }

    [Fact]
    public void A_candidate_above_or_below_the_block_stays_on_the_original_anchors_column()
    {
        // A wide, short block: the room is above (376 px) and below (376 px), and above wins the tie.
        var block = PixelRect.FromSize(40, 400, 1880, 400);

        var candidates = RetryAnchor.Candidates(block, Viewport, new ScreenPoint(900, 600));

        Assert.Equal(new ScreenPoint(900, 400 - RetryAnchor.OutsideGap), candidates[0]);
    }

    [Fact]
    public void The_kept_coordinate_is_clamped_into_the_area_the_contacts_may_use()
    {
        var block = PixelRect.FromSize(800, 400, 400, 400);

        // An anchor in the bottom resize border: the candidate keeps its row as far down as contacts may go.
        var candidates = RetryAnchor.Candidates(block, Viewport, new ScreenPoint(1000, 1190));

        Assert.Equal(new ScreenPoint(block.Left - RetryAnchor.OutsideGap, 1175), candidates[0]);
    }

    [Fact]
    public void A_block_that_fills_the_viewport_leaves_only_the_centre()
    {
        var candidates = RetryAnchor.Candidates(Viewport, Viewport, new ScreenPoint(1000, 600));

        Assert.Equal(Centre, Assert.Single(candidates));
    }

    [Fact]
    public void A_block_with_no_room_to_any_side_leaves_only_the_centre()
    {
        // Every side has less than OutsideGap of usable room: the left edge is under the resize border and the
        // right edge is under the scrollbar strip.
        var block = new PixelRect(30, 30, 1930, 1170);

        var candidates = RetryAnchor.Candidates(block, Viewport, new ScreenPoint(1000, 600));

        Assert.Equal(Centre, Assert.Single(candidates));
    }

    [Fact]
    public void The_viewport_centre_is_always_the_last_resort()
    {
        var block = PixelRect.FromSize(800, 400, 400, 400);

        var candidates = RetryAnchor.Candidates(block, Viewport, new ScreenPoint(1000, 600));

        Assert.Equal(2, candidates.Count);
        Assert.Equal(Centre, candidates[^1]);
    }

    [Fact]
    public void No_more_than_two_anchors_are_ever_offered()
    {
        foreach (var block in new[]
        {
            PixelRect.FromSize(800, 400, 400, 400),
            PixelRect.FromSize(0, 0, 100, 100),
            PixelRect.FromSize(1900, 1100, 400, 400),
            Viewport,
        })
        {
            var candidates = RetryAnchor.Candidates(block, Viewport, new ScreenPoint(1000, 600));

            Assert.InRange(candidates.Count, 1, RetryAnchor.MaxCandidates);
        }
    }

    [Fact]
    public void A_centre_that_is_already_the_first_candidate_is_not_offered_twice()
    {
        // A block hugging the left edge whose roomier side is the right: its candidate lands on the centre.
        var block = new PixelRect(0, 400, (2000 / 2) - RetryAnchor.OutsideGap, 800);

        var candidates = RetryAnchor.Candidates(block, Viewport, new ScreenPoint(1000, 600));

        Assert.Equal(Centre, Assert.Single(candidates));
    }
}
