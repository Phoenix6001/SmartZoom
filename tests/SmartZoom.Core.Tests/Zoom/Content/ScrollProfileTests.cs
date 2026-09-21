using SmartZoom.Core.Zoom.Content;

namespace SmartZoom.Core.Tests.Zoom.Content;

public sealed class ScrollProfileTests
{
    // A page of text: bands of dark rows (lines) separated by light ones, with a little noise.
    private static int[] Page(int rows, int seed = 7)
    {
        var random = new Random(seed);
        var profile = new int[rows];
        for (var y = 0; y < rows; y++)
            profile[y] = (y % 23) < 9 ? 70 + random.Next(20) : 230 + random.Next(20);

        // Something unique, so one offset fits better than the repeating line pattern.
        for (var y = 120; y < 150; y++)
            profile[y] = 20;

        return profile;
    }

    private static int[] Shifted(int[] profile, int by)
    {
        var moved = new int[profile.Length];
        for (var y = 0; y < profile.Length; y++)
        {
            var from = y + by;
            moved[y] = from >= 0 && from < profile.Length ? profile[from] : 255;
        }

        return moved;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(48)]
    [InlineData(144)]
    [InlineData(-96)]
    public void Finds_how_far_the_content_moved(int shift)
    {
        var before = Page(600);

        var found = ScrollProfile.FindShift(before, Shifted(before, shift), -300, 300);

        Assert.Equal(shift, found.Pixels);
        Assert.True(found.IsClear, $"score {found.Score} should stand out from the typical {found.Typical}");
    }

    [Fact]
    public void A_blank_strip_gives_no_confident_answer()
    {
        var blank = new int[400];
        Array.Fill(blank, 250);

        Assert.False(ScrollProfile.FindShift(blank, blank, -200, 200).IsClear);
    }

    [Fact]
    public void An_unmoved_page_scores_a_perfect_match()
    {
        var before = Page(400);

        var found = ScrollProfile.FindShift(before, before, -200, 200);

        Assert.Equal(0, found.Pixels);
        Assert.Equal(0, found.Score);
        Assert.True(found.IsClear);
    }

    [Fact]
    public void A_movement_beyond_the_search_range_is_not_reported_as_a_confident_match()
    {
        // A page with no repeating rhythm, so no wrong offset can pass for the right one.
        var random = new Random(11);
        var before = new int[600];
        for (var y = 0; y < before.Length; y++)
            before[y] = random.Next(256);

        var found = ScrollProfile.FindShift(before, Shifted(before, 240), -100, 100);

        Assert.False(found.IsClear, $"a shift outside the search range scored {found.Score} against {found.Typical}");
    }

    [Fact]
    public void On_evenly_spaced_lines_a_wrong_offset_can_pass_for_the_right_one()
    {
        // Worth knowing rather than hiding: every line of a page of text looks like every other, so the search
        // range has to be bounded by how far the scroll was asked to go.
        var before = Page(600);

        var found = ScrollProfile.FindShift(before, Shifted(before, 240), -100, 100);

        Assert.NotEqual(240, found.Pixels);
    }

    [Fact]
    public void Two_unrelated_pages_do_not_look_like_a_match()
    {
        // Two screens with nothing in common: no offset lines them up better than any other.
        var first = new Random(3);
        var second = new Random(4);
        var before = new int[500];
        var after = new int[500];
        for (var y = 0; y < before.Length; y++)
        {
            before[y] = first.Next(256);
            after[y] = second.Next(256);
        }

        var found = ScrollProfile.FindShift(before, after, -200, 200);

        Assert.False(found.IsClear, $"unrelated pages scored {found.Score} against {found.Typical}");
    }

    [Fact]
    public void Profiles_too_short_to_compare_report_no_movement()
    {
        var found = ScrollProfile.FindShift(new int[10], new int[10], -50, 50);

        Assert.Equal(0, found.Pixels);
        Assert.False(found.IsClear);
    }

    [Fact]
    public void A_search_range_bounded_by_the_request_finds_a_clamped_scroll()
    {
        // The reader ran out of document and moved 90 px of the 480 px asked for.
        var before = Page(700);

        var found = ScrollProfile.FindShift(before, Shifted(before, 90), 0, 480);

        Assert.Equal(90, found.Pixels);
        Assert.True(found.IsClear);
    }

    [Fact]
    public void A_range_of_only_a_few_offsets_is_never_clear()
    {
        var before = Page(600);

        var found = ScrollProfile.FindShift(before, Shifted(before, 2), 0, 4);

        Assert.Equal(2, found.Pixels);
        Assert.Equal(0, found.Typical);
        Assert.False(found.IsClear, "five offsets are too few for a middling one to mean anything");
    }

    [Fact]
    public void An_empty_range_reports_no_movement() =>
        Assert.Equal(0, ScrollProfile.FindShift(Page(400), Page(400), 10, 5).Pixels);

    [Fact]
    public void Profiles_of_different_lengths_report_no_movement() =>
        Assert.Equal(0, ScrollProfile.FindShift(new int[200], new int[300], -50, 50).Pixels);
}
