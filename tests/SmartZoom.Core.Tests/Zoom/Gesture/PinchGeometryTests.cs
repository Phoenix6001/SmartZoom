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

    /// <summary>
    /// Chromium ignores the scaling of a pinch while the contacts are closer than its minimum scaling span, and
    /// measures the zoom from the moment they reach it. Measured in Brave and Edge at 100%, x3 asked: half gaps
    /// of 50 / 40 / 30 / 20 / 12 px reached x2.89 / 2.48 / 2.03 / 1.54 / 1.20, which all put the start of
    /// counting at a 61-65 px half span. Firefox reached x3.00 at every gap down to 14 px.
    /// </summary>
    /// <summary>
    /// Firefox keeps the page point under a pinch's centre where it is, even at the end of a zoom-out, where the
    /// only way to do that is to scroll the page. Zooming out around any point but the one the zoom-in kept still
    /// therefore leaves the page scrolled by (anchor - focus) * (1 - 1/scale), measured at 141-152 px near an edge,
    /// where the contacts cannot straddle the anchor. Chromium clamps the view into the page at 1.0 instead, so it
    /// is not affected. These tests drive a model of the Gecko behaviour.
    /// </summary>
    public sealed class Undoing_a_zoom_around_a_moved_focus
    {
        private const double Scale = 3;
        private static readonly ScreenPoint Anchor = new(1560, 500);
        private static readonly ScreenPoint Focus = new(1340, 500);

        [Fact]
        public void A_zoom_out_around_a_moved_focus_alone_leaves_the_page_scrolled()
        {
            var page = GeckoPage.ZoomedInAround(Anchor, Scale);

            page.Pinch(Focus, 1 / Scale);

            Assert.Equal((Anchor.X - Focus.X) * (1 - (1 / Scale)), page.Scroll, precision: 6);
        }

        [Fact]
        public void Panning_back_first_brings_the_page_back_exactly()
        {
            var page = GeckoPage.ZoomedInAround(Anchor, Scale);

            page.Drag(PinchGeometry.UndoPan(Anchor, Focus, Scale));
            page.Pinch(Focus, 1 / Scale);

            Assert.Equal(0, page.Scroll, precision: 0);
        }

        [Fact]
        public void A_zoom_in_made_around_a_moved_focus_plus_a_pan_is_undone_the_same_way()
        {
            // How an edge zoom is made: a pinch around a focus with room, then a drag to where the anchor would be.
            var page = new GeckoPage();
            var zoomFocus = new ScreenPoint(1320, 500);
            page.Pinch(zoomFocus, Scale);
            page.Drag(new ScreenPoint((int)Math.Round((Anchor.X - zoomFocus.X) * (1 - Scale)), 0));

            page.Drag(PinchGeometry.UndoPan(Anchor, Focus, Scale));
            page.Pinch(Focus, 1 / Scale);

            Assert.Equal(0, page.Scroll, precision: 0);
        }

        [Fact]
        public void Nothing_is_panned_when_the_zoom_out_is_around_the_anchor_itself() =>
            Assert.Equal(default, PinchGeometry.UndoPan(Anchor, Anchor, Scale));

        [Fact]
        public void Nothing_is_panned_when_the_page_is_not_zoomed() =>
            Assert.Equal(default, PinchGeometry.UndoPan(Anchor, Focus, 1));

        [Fact]
        public void The_pan_back_is_the_zoom_ins_pan_reversed_when_the_focus_is_the_same()
        {
            var plan = PinchGeometry.Plan(new ScreenPoint(1568, 500), Scale, 23, PixelRect.FromSize(12, 12, 1544, 1000));

            Assert.NotEqual(default, plan.Pan);
            Assert.Equal(new ScreenPoint(-plan.Pan.X, -plan.Pan.Y), PinchGeometry.UndoPan(new ScreenPoint(1568, 500), plan.Focus, Scale));
        }

        /// <summary>
        /// A page in Gecko, along one axis: the page's scroll, and the zoomed view's offset inside it. A pinch keeps
        /// the page point under its centre still; at 1.0 the view is the page, so what is left goes to the scroll.
        /// A drag while zoomed moves the view, and the content by the drag's length on screen.
        /// </summary>
        private sealed class GeckoPage
        {
            private double _scale = 1;
            private double _view;

            public double Scroll { get; private set; }

            public static GeckoPage ZoomedInAround(ScreenPoint anchor, double scale)
            {
                var page = new GeckoPage();
                page.Pinch(anchor, scale);
                return page;
            }

            public void Pinch(ScreenPoint centre, double factor)
            {
                var held = Scroll + _view + (centre.X / _scale);
                _scale *= factor;
                if (_scale <= 1 + 1e-9)
                {
                    _scale = 1;
                    _view = 0;
                    Scroll = held - centre.X;
                }
                else
                {
                    _view = held - (centre.X / _scale) - Scroll;
                }
            }

            public void Drag(ScreenPoint delta) => _view -= delta.X / _scale;
        }
    }

    /// <summary>
    /// Chromium moves a pinch's centre onto the viewport's edge when it is within 100 DIP of it. At the bottom
    /// that snapped pinch leaked 4-6 px into the page's scroll on every zoom-in (measured at 100%: centres 32-90 px
    /// from the bottom leaked, 100 px and more did not), which zooming out does not undo. A zoom-in centred just
    /// above the zone and dragged down to the anchor lands exactly where it was aimed and leaks nothing.
    /// </summary>
    public sealed class Chromiums_bottom_snap
    {
        private static readonly PixelRect Viewport = PixelRect.FromSize(608, 387, 1569, 1105);
        private static readonly PixelRect Bounds = new(620, 399, 2149, 1464);
        private const double Zone = 100;

        [Fact]
        public void A_zoom_in_centred_in_the_zone_is_pinched_just_above_it_and_dragged_down()
        {
            var anchor = new ScreenPoint(1388, Viewport.Bottom - 32);

            var bounds = PinchGeometry.KeepFocusAboveBottomSnap(anchor, 3, Bounds, Viewport.Bottom, Zone);
            var plan = PinchGeometry.Plan(anchor, 3, 23, bounds, 125);

            Assert.True(plan.Focus.Y < Viewport.Bottom - Zone, $"focus y {plan.Focus.Y}");
            Assert.True(plan.Focus.Y >= Viewport.Bottom - Zone - 2, "the focus moves no further up than it has to");
            Assert.Equal(anchor.X, plan.Focus.X);
            Assert.Equal((int)Math.Round((anchor.Y - plan.Focus.Y) * (1 - 3.0)), plan.Pan.Y);
        }

        [Fact]
        public void A_zoom_in_centred_above_the_zone_is_left_alone()
        {
            var anchor = new ScreenPoint(1388, Viewport.Bottom - 130);

            Assert.Equal(Bounds, PinchGeometry.KeepFocusAboveBottomSnap(anchor, 3, Bounds, Viewport.Bottom, Zone));
        }

        [Fact]
        public void A_zoom_out_is_left_alone()
        {
            // The page ends clamped at 1.0, so where a zoom-out is centred does not matter in Chromium.
            var anchor = new ScreenPoint(1388, Viewport.Bottom - 32);

            Assert.Equal(Bounds, PinchGeometry.KeepFocusAboveBottomSnap(anchor, 0.3, Bounds, Viewport.Bottom, Zone));
        }

        [Fact]
        public void An_engine_without_the_snap_is_left_alone()
        {
            var anchor = new ScreenPoint(1388, Viewport.Bottom - 32);

            Assert.Equal(Bounds, PinchGeometry.KeepFocusAboveBottomSnap(anchor, 3, Bounds, Viewport.Bottom, 0));
        }

        [Fact]
        public void A_viewport_shorter_than_the_zone_keeps_its_contact_area()
        {
            // Nowhere is out of the zone; a contact area squeezed to nothing would be worse than the leak.
            var small = new PixelRect(620, 1400, 2149, 1464);
            var anchor = new ScreenPoint(1388, Viewport.Bottom - 32);

            Assert.Equal(small, PinchGeometry.KeepFocusAboveBottomSnap(anchor, 3, small, Viewport.Bottom, Zone));
        }

        [Theory]
        [InlineData(GestureEngine.Chromium, 1.0, 100)]
        [InlineData(GestureEngine.Chromium, 2.0, 200)]
        [InlineData(GestureEngine.Chromium, 1.25, 125)]
        [InlineData(GestureEngine.Gecko, 1.0, 0)]
        [InlineData(GestureEngine.Windows, 1.0, 0)]
        public void The_zone_is_chromiums_100_dip(GestureEngine engine, double dpiScale, double expected) =>
            Assert.Equal(expected, RecognizerProfile.BottomSnapZone(engine, dpiScale));
    }

    public sealed class Chromiums_minimum_scaling_span
    {
        /// <summary>Chromium's span slop at 100%: 23 DIP.</summary>
        private const double ChromiumSlopAt100 = 23;

        private static readonly PixelRect Shallow = new(100, 100, 1900, 300);

        [Fact]
        public void The_zoom_starts_counting_no_closer_than_the_minimum_span()
        {
            var (_, preRolled, end) = PinchGeometry.ContactHalfSpread(3.0, halfSlop: 11.5, halfGap: 20, minimumHalfSpan: 63);

            Assert.Equal(63, preRolled);
            Assert.Equal(3.0, end / preRolled, 6);
        }

        [Fact]
        public void A_gap_too_narrow_for_the_minimum_span_moves_the_focus_instead()
        {
            // 150 px beside the anchor: a narrowed gap would start the zoom at a 50 px half span, where Chromium
            // counts none of it, and the press reached x1.5 of x3. Moving the focus reaches the whole zoom.
            var anchor = new ScreenPoint(Shallow.Left + 150, Shallow.Top + 8);

            var plan = PinchGeometry.Plan(anchor, 3.0, ChromiumSlopAt100, Shallow, minimumScalingSpan: 125);

            Assert.Null(plan.NarrowedHalfGap);
            Assert.NotEqual(anchor, plan.Focus);
            Assert.NotEqual(0, plan.Pan.X);
        }

        [Fact]
        public void Without_a_minimum_the_same_anchor_is_still_narrowed()
        {
            // Firefox and Windows' recognizer: nothing measured says they need it, and Firefox measured exact.
            var anchor = new ScreenPoint(Shallow.Left + 150, Shallow.Top + 8);

            var plan = PinchGeometry.Plan(anchor, 3.0, ChromiumSlopAt100, Shallow);

            Assert.NotNull(plan.NarrowedHalfGap);
            Assert.Equal(anchor, plan.Focus);
        }

        [Fact]
        public void A_gap_that_leaves_the_minimum_span_is_narrowed_as_before()
        {
            var anchor = new ScreenPoint(Shallow.Left + 200, Shallow.Top + 8);

            var plan = PinchGeometry.Plan(anchor, 3.0, ChromiumSlopAt100, Shallow, minimumScalingSpan: 125);

            Assert.NotNull(plan.NarrowedHalfGap);
            Assert.Equal(anchor, plan.Focus);
            Assert.True(plan.PreRolledHalf >= 62.5, $"the zoom starts at a {plan.PreRolledHalf:0.#} px half span");
            Assert.Equal(3.0, plan.EndHalf / plan.PreRolledHalf, 6);
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
        public void A_diagonal_drag_is_made_one_axis_at_a_time()
        {
            // Chromium locks a drag within about 20 degrees of an axis to that axis: in a bottom corner the
            // (428, -136) drag moved the view sideways only, and the zoom fell short of the corner by 45 px.
            var legs = PinchGeometry.PanLegs(new ScreenPoint(428, -136), Viewport, touchSlop: 16);

            Assert.All(legs, leg => Assert.True(leg.From.X == leg.To.X || leg.From.Y == leg.To.Y, $"{leg} moves on both axes"));
            Assert.Equal(428 + 16, legs.Where(l => l.From.Y == l.To.Y).Sum(l => l.To.X - l.From.X));
            Assert.Equal(-136 - 16, legs.Where(l => l.From.X == l.To.X).Sum(l => l.To.Y - l.From.Y));
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
        [InlineData(GestureEngine.Chromium, 125)]
        [InlineData(GestureEngine.Gecko, 0)]
        [InlineData(GestureEngine.Windows, 0)]
        public void Minimum_scaling_span_matches_what_was_measured(GestureEngine engine, double expected) =>
            Assert.Equal(expected, RecognizerProfile.MinimumScalingSpan(engine), 6);

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
