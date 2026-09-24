using SmartZoom.Core.Input;
using SmartZoom.Core.Zoom.Content;
using SmartZoom.Core.Zoom.Gesture;

namespace SmartZoom.Core.Tests.Zoom.Gesture;

/// <summary>
/// The geometry every gesture regression in this project has lived in. The numbers come from
/// docs/measurements.md; if one of these disagrees with that file, the code is wrong, not the file.
/// </summary>
public sealed class PinchGeometryTests
{
    /// <summary>A roomy content area, like a maximised browser on the 200% display the constants were measured on.</summary>
    private static readonly PixelRect Viewport = new(100, 100, 1900, 1200);

    private static readonly ScreenPoint Middle = new(1000, 650);

    /// <summary>Chromium's span slop at 200%: 23 DIP x 2.</summary>
    private const double ChromiumSlopAt200 = 46;

    public sealed class The_spread
    {
        [Fact]
        public void A_zoom_in_starts_at_the_resting_gap_and_ends_at_the_factor_times_the_pre_rolled_span()
        {
            var (down, preRolled, end) = PinchGeometry.ContactHalfSpread(2.0, halfSlop: 23, halfGap: 60);

            Assert.Equal(60, down);
            Assert.Equal(84, preRolled);         // 60 + 23 + 1 to cross the threshold for certain
            Assert.Equal(168, end);              // and the whole visible motion is then the factor
        }

        [Fact]
        public void The_visible_motion_is_the_whole_factor_not_the_factor_less_the_slop()
        {
            // The bug this exists to prevent: a requested 1.9x arrived as 1.5x, because the recognizer
            // swallowed the first 23 DIP and then measured from there.
            var (_, preRolled, end) = PinchGeometry.ContactHalfSpread(1.9, halfSlop: 23, halfGap: 60);

            Assert.Equal(1.9, end / preRolled, 6);
        }

        [Fact]
        public void A_zoom_out_ends_at_the_resting_gap_and_starts_wide_enough_to_close_into_it()
        {
            var (down, preRolled, end) = PinchGeometry.ContactHalfSpread(0.5, halfSlop: 23, halfGap: 60);

            Assert.Equal(120, preRolled);        // 60 / 0.5
            Assert.Equal(144, down);             // ... plus the slop, crossed inwards
            Assert.Equal(60, end);
            Assert.Equal(0.5, end / preRolled, 6);
        }
    }

    public sealed class Where_the_fingers_go
    {
        [Fact]
        public void A_point_with_room_around_it_is_pinched_exactly_there()
        {
            var plan = PinchGeometry.Plan(Middle, 2.0, ChromiumSlopAt200, Viewport);

            Assert.Equal(Middle, plan.Focus);
            Assert.False(plan.Vertical);
            Assert.Equal(new ScreenPoint(0, 0), plan.Pan);
            Assert.Null(plan.NarrowedHalfGap);
            Assert.Null(plan.Shortfall);
        }

        [Fact]
        public void The_contacts_straddle_the_focus_along_the_chosen_axis()
        {
            var (first, second) = PinchGeometry.Contacts(60, Middle, vertical: false);

            Assert.Equal(new ScreenPoint(940, 650), first);
            Assert.Equal(new ScreenPoint(1060, 650), second);
        }

        [Fact]
        public void A_vertical_spread_straddles_the_focus_the_other_way()
        {
            var (first, second) = PinchGeometry.Contacts(60, Middle, vertical: true);

            Assert.Equal(new ScreenPoint(1000, 590), first);
            Assert.Equal(new ScreenPoint(1000, 710), second);
        }

        [Fact]
        public void An_anchor_near_one_edge_is_still_pinched_at_the_anchor_using_the_roomier_axis()
        {
            // Moving the focus costs a drag afterwards, so it is the last resort. A spread the other way
            // round, or a narrower one, zooms by the same factor, because the recognizer measures a ratio.
            var anchor = new ScreenPoint(Viewport.Left + 8, 650);

            var plan = PinchGeometry.Plan(anchor, 2.0, ChromiumSlopAt200, Viewport);

            Assert.Equal(anchor, plan.Focus);
            Assert.True(plan.Vertical, "the row has 8 px to give and the column has 549");
            Assert.NotNull(plan.NarrowedHalfGap);
            Assert.True(plan.NarrowedHalfGap >= PinchGeometry.MinHalfGap);
            Assert.Equal(new ScreenPoint(0, 0), plan.Pan);
        }

        [Fact]
        public void An_anchor_in_a_corner_with_room_on_neither_axis_moves_along_the_row_and_pans_back()
        {
            var shallow = new PixelRect(100, 100, 1900, 300);
            var corner = new ScreenPoint(shallow.Left + 8, shallow.Top + 8);

            var plan = PinchGeometry.Plan(corner, 2.0, ChromiumSlopAt200, shallow);

            Assert.NotEqual(corner, plan.Focus);
            Assert.Equal(corner.Y, plan.Focus.Y);       // the row, never the column: see below
            Assert.NotEqual(0, plan.Pan.X);
            Assert.Equal(0, plan.Pan.Y);
        }

        [Fact]
        public void A_focus_is_moved_along_the_row_even_when_the_column_is_closer()
        {
            // A focus moved up or down needs a vertical drag, and that leaked into the page's own scroll:
            // a Wikipedia table of contents came back 123 px off after the restore.
            var nearTheTop = new ScreenPoint(1000, Viewport.Top + 8);

            var plan = PinchGeometry.Plan(nearTheTop, 3.0, ChromiumSlopAt200, Viewport);

            Assert.False(plan.Vertical);
            Assert.Equal(0, plan.Pan.Y);
        }

        [Fact]
        public void The_pan_puts_the_content_where_a_pinch_at_the_anchor_would_have()
        {
            var shallow = new PixelRect(100, 100, 1900, 300);
            var anchor = new ScreenPoint(shallow.Left + 8, shallow.Top + 8);
            const double Factor = 2.0;

            var plan = PinchGeometry.Plan(anchor, Factor, ChromiumSlopAt200, shallow);

            var expected = (int)Math.Round((anchor.X - plan.Focus.X) * (1 - Factor));
            Assert.Equal(expected, plan.Pan.X);
        }

        [Fact]
        public void A_zoom_out_spreads_horizontally_wherever_it_fits_on_the_row()
        {
            // A vertical spread, or one shrunk near an edge, made Chromium scroll the page by ~54 px on a
            // zoom-out, and nothing undoes that. The focal point of a zoom-out does not matter: it clamps.
            var anchor = new ScreenPoint(Viewport.Left + 4, Viewport.Top + 4);

            var plan = PinchGeometry.Plan(anchor, 0.5, ChromiumSlopAt200, Viewport);

            Assert.False(plan.Vertical);
            Assert.Equal(new ScreenPoint(0, 0), plan.Pan);
        }

        [Fact]
        public void A_window_too_small_for_the_gesture_shrinks_it_rather_than_letting_a_contact_escape()
        {
            // A contact within ~8 px of a window edge grabs its resize border: that is what dragged Brave's
            // left edge 154 px inward and put the next trigger on the Acrobat window behind it.
            var tiny = new PixelRect(0, 0, 120, 120);

            var plan = PinchGeometry.Plan(new ScreenPoint(60, 60), 2.0, ChromiumSlopAt200, tiny);

            Assert.NotNull(plan.Shortfall);
            var widest = Math.Max(plan.DownHalf, Math.Max(plan.PreRolledHalf, plan.EndHalf));
            Assert.True(widest <= plan.Shortfall!.Room + 1, $"a contact would land {widest} px from the focus in {plan.Shortfall.Room} px of room");
        }

        [Fact]
        public void A_viewport_narrower_than_the_edge_margins_puts_the_focus_at_its_rounded_middle()
        {
            // No inset range exists on either axis, so the middle is the only answer; it rounds like every
            // other planned coordinate (x: (100 + 107) / 2 = 103.5 -> 104, y: (100 + 104) / 2 = 102).
            var tiny = new PixelRect(100, 100, 108, 105);

            var plan = PinchGeometry.Plan(new ScreenPoint(101, 101), 0.5, ChromiumSlopAt200, tiny);

            Assert.Equal(new ScreenPoint(104, 102), plan.Focus);
            Assert.NotNull(plan.Shortfall);
        }

        [Fact]
        public void An_anchor_outside_the_content_area_never_hosts_a_contact()
        {
            // An anchor in the window's resize-border zone is exactly the case above.
            var outside = new ScreenPoint(Viewport.Left - 10, 650);

            var plan = PinchGeometry.Plan(outside, 2.0, ChromiumSlopAt200, Viewport);

            Assert.NotEqual(outside, plan.Focus);
            Assert.Null(plan.NarrowedHalfGap);
        }
    }

    public sealed class The_drag
    {
        [Fact]
        public void A_drag_that_fits_is_one_leg_centred_in_the_content_area()
        {
            var legs = PinchGeometry.PanLegs(new ScreenPoint(400, 0), Viewport, touchSlop: 16);

            var leg = Assert.Single(legs);
            Assert.Equal(400 + 16, leg.To.X - leg.From.X);   // the slop first, then the distance asked for
            Assert.Equal(0, leg.To.Y - leg.From.Y);
        }

        [Fact]
        public void A_drag_longer_than_the_window_is_split_into_legs_that_fit()
        {
            var legs = PinchGeometry.PanLegs(new ScreenPoint(4000, 0), Viewport, touchSlop: 16);

            Assert.True(legs.Count > 1);
            foreach (var leg in legs)
            {
                Assert.InRange(leg.From.X, Viewport.Left, Viewport.Right - 1);
                Assert.InRange(leg.To.X, Viewport.Left, Viewport.Right - 1);
            }
        }

        [Fact]
        public void Nothing_to_move_is_no_legs() =>
            Assert.Empty(PinchGeometry.PanLegs(new ScreenPoint(0, 0), Viewport, touchSlop: 16));
    }

    public sealed class The_recognizers
    {
        [Theory]
        [InlineData(GestureEngine.Chromium, 2.0, 46)]        // 23 DIP at 200%
        [InlineData(GestureEngine.Chromium, 1.0, 23)]
        [InlineData(GestureEngine.Windows, 2.0, 12)]         // 6 DIP, measured in Acrobat
        [InlineData(GestureEngine.Gecko, 2.0, 35)]           // already physical pixels: does not scale
        [InlineData(GestureEngine.Gecko, 1.0, 35)]
        public void Span_slop_matches_what_was_measured(GestureEngine engine, double dpiScale, double expected) =>
            Assert.Equal(expected, RecognizerProfile.SpanSlop(engine, dpiScale), 6);

        [Theory]
        [InlineData(GestureEngine.Chromium, 2.0, 16)]
        [InlineData(GestureEngine.Windows, 2.0, 0)]          // it starts moving immediately
        [InlineData(GestureEngine.Gecko, 2.0, 19.2)]         // 0.1 inch
        public void Touch_slop_matches_what_was_measured(GestureEngine engine, double dpiScale, double expected) =>
            Assert.Equal(expected, RecognizerProfile.TouchSlop(engine, dpiScale), 6);
    }

    public sealed class The_easing
    {
        [Theory]
        [InlineData(0.0, 0.0)]
        [InlineData(0.5, 0.5)]
        [InlineData(1.0, 1.0)]
        public void Smooth_step_pins_both_ends_and_the_middle(double t, double expected) =>
            Assert.Equal(expected, Easing.SmoothStep(t), 6);

        [Fact]
        public void Smooth_step_starts_slowly() => Assert.True(Easing.SmoothStep(0.1) < 0.1);

        [Fact]
        public void Smooth_step_finishes_slowly() => Assert.True(Easing.SmoothStep(0.9) > 0.9);
    }
}
