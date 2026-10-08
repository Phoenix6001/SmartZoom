using Microsoft.Extensions.Logging.Abstractions;

using SmartZoom.Core.Input;
using SmartZoom.Core.Routing;
using SmartZoom.Core.Settings;
using SmartZoom.Core.Windows;
using SmartZoom.Core.Zoom;
using SmartZoom.Core.Zoom.Content;

namespace SmartZoom.Core.Tests.Zoom;

public sealed class BrowserAdapterTests
{
    private static readonly TargetInfo Brave = new(0x100, 0x101, 7, "brave", "Chrome_WidgetWin_1", "Chrome_RenderWidgetHostHWND");
    private static readonly ScreenPoint Cursor = new(1486, 1147);
    private static readonly PixelRect Viewport = PixelRect.FromSize(455, 420, 1874, 1527);

    private static readonly ContentHit ParagraphHit = new(
        [
            new ContentNode(ContentRole.Text, PixelRect.FromSize(910, 1137, 655, 70)),
            new ContentNode(ContentRole.Group, PixelRect.FromSize(910, 1133, 949, 105)),
            new ContentNode(ContentRole.Group, PixelRect.FromSize(455, 420, 1859, 1527)),
            new ContentNode(ContentRole.Document, Viewport),
        ],
        Viewport);

    private readonly FakeHitTester _hits = new();
    private readonly FakePinch _pinch = new();
    private readonly FakeScreenSampler _screen = new();

    // The 200% display the allowances in these tests were measured on.
    private readonly FakeDisplayScale _display = new() { Scale = 2.0 };

    private IZoomAdapter Create(bool animate = true, bool ctrlWheelWhenPinchBlocked = true, double amount = 3.0) =>
        new BrowserAdapter(
            _hits,
            _pinch,
            _screen,
            _display,
            new ZoomSettings
            {
                Animate = animate,
                MaxScale = amount,
                Smart = new SmartZoomTuning { AnimationMs = 180 },
                Browser = new BrowserZoomSettings { AnchorInsetPx = 0, CtrlWheelWhenPinchBlocked = ctrlWheelWhenPinchBlocked },
            },
            NullLogger<BrowserAdapter>.Instance);

    /// <summary>Where the pixel under the cursor ends up once the gesture has been performed.</summary>
    private static ScreenPoint Lands((ScreenPoint Anchor, double Factor, TimeSpan Duration, PixelRect Bounds) pinch, ScreenPoint point) => new(
        (int)Math.Round(pinch.Anchor.X + ((point.X - pinch.Anchor.X) * pinch.Factor)),
        (int)Math.Round(pinch.Anchor.Y + ((point.Y - pinch.Anchor.Y) * pinch.Factor)));

    [Fact]
    public async Task Zooms_by_the_configured_amount_and_brings_the_target_to_the_middle()
    {
        _hits.Result = ParagraphHit;

        var result = await Create(amount: 2.5).ZoomInAsync(Brave, Cursor, CancellationToken.None);

        Assert.Equal(ZoomInStatus.Applied, result.Status);

        // Exactly one gesture: the zoom the user asked for. Edge draws a pinch-out below 1.0 as a
        // shrink-and-rebound, so nothing may precede it.
        Assert.Single(_pinch.Calls);
        var pinch = _pinch.Calls[0];
        Assert.Equal(2.5, pinch.Factor, precision: 9);

        // The press is aimed below and right of the middle; what it aimed at ends up in the middle.
        var landed = Lands(pinch, Cursor);
        Assert.Equal(Viewport.CenterX, landed.X, tolerance: 1);
        Assert.Equal(Viewport.CenterY, landed.Y, tolerance: 1);

        Assert.Equal(TimeSpan.FromMilliseconds(180), pinch.Duration);
        Assert.Equal(pinch.Factor, Assert.IsType<BrowserAdapter.RestoreState>(result.RestoreState).Plan.Scale);
    }

    [Fact]
    public async Task The_amount_does_not_depend_on_what_the_cursor_happened_to_land_on()
    {
        // The whole point. A narrow image and a paragraph most of the width of the window used to zoom x3.00
        // and x1.12 respectively — the same press doing visibly different things a few pixels apart.
        var narrow = new ContentHit(
            [new ContentNode(ContentRole.Image, PixelRect.FromSize(900, 1100, 252, 190)), new ContentNode(ContentRole.Document, Viewport)],
            Viewport);
        var wide = new ContentHit(
            [new ContentNode(ContentRole.Group, PixelRect.FromSize(470, 800, 1630, 40)), new ContentNode(ContentRole.Document, Viewport)],
            Viewport);

        foreach (var hit in new[] { narrow, wide })
        {
            _pinch.Calls.Clear();
            _hits.Result = hit;

            Assert.Equal(ZoomInStatus.Applied, (await Create(amount: 3.0).ZoomInAsync(Brave, Cursor, CancellationToken.None)).Status);
            Assert.Equal(3.0, Assert.Single(_pinch.Calls).Factor, precision: 9);
        }
    }

    [Fact]
    public async Task Zoom_out_pinches_back_past_one_around_the_same_anchor()
    {
        _hits.Result = ParagraphHit;
        var adapter = Create();
        var result = await adapter.ZoomInAsync(Brave, Cursor, CancellationToken.None);
        var zoomIn = _pinch.Calls[0];

        await adapter.ZoomOutAsync(Brave, result.RestoreState!, CancellationToken.None);

        var zoomOut = _pinch.Calls[1];
        Assert.Equal(zoomIn.Anchor, zoomOut.Anchor);
        Assert.Equal(0.9 / zoomIn.Factor, zoomOut.Factor, precision: 9);
    }

    [Fact]
    public async Task No_content_is_reported_handled_so_the_coordinator_never_page_zooms_a_browser()
    {
        _hits.Result = null;

        var result = await Create().ZoomInAsync(Brave, Cursor, CancellationToken.None);

        Assert.Equal(ZoomInStatus.Handled, result.Status);
        Assert.Equal(ZoomReason.NoContent, result.Reason);
        Assert.Empty(_pinch.Calls);
    }

    [Fact]
    public async Task A_page_with_no_content_under_the_cursor_still_zooms_by_the_amount()
    {
        // A document and nothing else: no structure to reason about, and the press still asked for a zoom.
        _hits.Result = new ContentHit([new ContentNode(ContentRole.Document, Viewport)], Viewport);

        var result = await Create().ZoomInAsync(Brave, Cursor, CancellationToken.None);

        Assert.Equal(ZoomInStatus.Applied, result.Status);
        var pinch = Assert.Single(_pinch.Calls);
        Assert.Equal(3.0, pinch.Factor, precision: 9);

        var landed = Lands(pinch, Cursor);
        Assert.Equal(Viewport.CenterX, landed.X, tolerance: 1);
        Assert.Equal(Viewport.CenterY, landed.Y, tolerance: 1);
    }

    [Fact]
    public async Task A_zoom_that_found_its_block_sends_no_gesture_before_the_zoom()
    {
        // Regression guard: Edge renders a pinch-out below 1.0 as a visible shrink-and-rebound before the
        // zoom starts, so the zoom must be the first gesture. Anything added before it is seen by the user.
        _hits.Result = ParagraphHit;

        await Create().ZoomInAsync(Brave, Cursor, CancellationToken.None);

        var only = Assert.Single(_pinch.Calls);
        Assert.True(only.Factor > 1, "the one gesture should be the zoom itself, not a pinch-out below 1.0");
    }

    [Fact]
    public async Task Rejected_pinch_is_reported_handled()
    {
        _hits.Result = ParagraphHit;
        _pinch.Succeeds = false;

        var result = await Create().ZoomInAsync(Brave, Cursor, CancellationToken.None);

        Assert.Equal(ZoomInStatus.Handled, result.Status);
        Assert.Equal(ZoomReason.GestureRefused, result.Reason);
    }

    [Fact]
    public async Task A_pinch_that_changed_the_screen_is_applied_after_comparing_the_area_around_the_anchor()
    {
        _hits.Result = ParagraphHit;
        _screen.Frames.Enqueue(40);
        _screen.Frames.Enqueue(200);

        var result = await Create().ZoomInAsync(Brave, Cursor, CancellationToken.None);

        Assert.Equal(ZoomInStatus.Applied, result.Status);
        Assert.Equal(2, _screen.Regions.Count);
        Assert.Equal(_screen.Regions[0], _screen.Regions[1]);

        // ±300 × ±200 around the anchor, and no wider than the viewport.
        var anchor = _pinch.Calls[0].Anchor;
        var expected = new PixelRect(anchor.X - 300, anchor.Y - 200, anchor.X + 300, anchor.Y + 200).Intersect(Viewport);
        Assert.Equal(expected, _screen.Regions[0]);
        Assert.False(expected.IsEmpty);
    }

    [Fact]
    public async Task A_page_that_took_the_first_pinch_is_never_pinched_twice()
    {
        _hits.Result = ParagraphHit;
        _screen.Frames.Enqueue(40);
        _screen.Frames.Enqueue(200);

        var result = await Create().ZoomInAsync(Brave, Cursor, CancellationToken.None);

        Assert.Equal(ZoomInStatus.Applied, result.Status);
        Assert.Single(_pinch.Calls);
        Assert.Equal(2, _screen.Regions.Count);
    }

    [Fact]
    public async Task A_pinch_the_element_kept_is_retried_around_an_anchor_outside_that_element()
    {
        // The pinch went in cleanly (Jira's dialogs: touch-action none hands the gesture to the page's own
        // scripts), but the screen looks exactly as it did. The rest of the page does take it.
        _hits.Result = ParagraphHit;
        _screen.Frames.Enqueue(90);
        _screen.Frames.Enqueue(90);
        _screen.Frames.Enqueue(40);
        _screen.Frames.Enqueue(200);

        var adapter = Create();
        var result = await adapter.ZoomInAsync(Brave, Cursor, CancellationToken.None);

        Assert.Equal(ZoomInStatus.Applied, result.Status);
        Assert.Equal(2, _pinch.Calls.Count);

        // The same zoom, aimed the required clearance away from the point that refused it. Chromium reads
        // touch-action from where the contacts land, so the retry has to start clear of it.
        var first = _pinch.Calls[0];
        var retry = _pinch.Calls[1];
        Assert.Equal(first.Factor, retry.Factor);
        Assert.Equal(first.Duration, retry.Duration);
        Assert.Equal(first.Bounds, retry.Bounds);

        var moved = Math.Abs(retry.Anchor.X - first.Anchor.X) + Math.Abs(retry.Anchor.Y - first.Anchor.Y);
        Assert.Equal(RetryAnchor.OutsideGap, moved);
        Assert.True(first.Bounds.Contains(retry.Anchor));

        // The restore has to reverse the gesture that happened, not the one the page refused.
        var state = Assert.IsType<BrowserAdapter.RestoreState>(result.RestoreState);
        Assert.Equal(retry.Anchor, state.Plan.Anchor);
        Assert.Equal(first.Factor, state.Plan.Scale);

        await adapter.ZoomOutAsync(Brave, result.RestoreState!, CancellationToken.None);
        Assert.Equal(retry.Anchor, _pinch.Calls[2].Anchor);
    }

    [Fact]
    public async Task A_second_refusal_is_retried_around_the_viewport_centre()
    {
        _hits.Result = ParagraphHit;
        _screen.Frames.Enqueue(90);
        _screen.Frames.Enqueue(90);
        _screen.Frames.Enqueue(90);
        _screen.Frames.Enqueue(90);
        _screen.Frames.Enqueue(40);
        _screen.Frames.Enqueue(200);

        var result = await Create().ZoomInAsync(Brave, Cursor, CancellationToken.None);

        Assert.Equal(ZoomInStatus.Applied, result.Status);
        Assert.Equal(3, _pinch.Calls.Count);
        Assert.Equal(new ScreenPoint(1392, 1184), _pinch.Calls[2].Anchor);
        Assert.Equal(_pinch.Calls[2].Anchor, Assert.IsType<BrowserAdapter.RestoreState>(result.RestoreState).Plan.Anchor);
    }

    [Fact]
    public async Task A_page_that_blocks_every_anchor_is_unhandled_so_the_coordinator_can_page_zoom_instead()
    {
        _hits.Result = ParagraphHit;
        // Four gestures are verified: the zoom, its two retries, and the one after the page is cleared.
        for (var i = 0; i < 8; i++)
            _screen.Frames.Enqueue(90);

        var result = await Create(ctrlWheelWhenPinchBlocked: true).ZoomInAsync(Brave, Cursor, CancellationToken.None);

        Assert.Equal(ZoomInStatus.Unhandled, result.Status);
        Assert.Null(result.RestoreState);

        // The zoom, its two retries, the clearing pinch and one more attempt: a page that really does block
        // gestures costs five, and only then is the press handed to page zoom.
        Assert.Equal(5, _pinch.Calls.Count);
    }

    [Fact]
    public async Task A_page_that_blocks_every_anchor_is_reported_and_left_alone_when_page_zoom_is_not_wanted()
    {
        _hits.Result = ParagraphHit;
        for (var i = 0; i < 8; i++)
            _screen.Frames.Enqueue(90);

        var result = await Create(ctrlWheelWhenPinchBlocked: false).ZoomInAsync(Brave, Cursor, CancellationToken.None);

        Assert.Equal(ZoomInStatus.Handled, result.Status);
        Assert.Equal(ZoomReason.GestureRefused, result.Reason);
        Assert.Equal("pinch blocked by the page", result.Detail);
        Assert.Null(result.RestoreState);
        Assert.Equal(5, _pinch.Calls.Count);
    }

    [Fact]
    public async Task A_page_already_zoomed_as_far_as_it_goes_is_cleared_and_zoomed_rather_than_page_zoomed()
    {
        // The reason the fallback is not reached on the third refusal. Chromium clamps the visual viewport at
        // x4, so a page already there swallows every gesture exactly the way one with touch-action: none does
        // — and this is a zoom the process does not remember, which the check at the top of ZoomInAsync cannot
        // always see, because Chromium only bakes the scale into the accessibility tree when it next
        // re-serializes it. Falling back here would stack page zoom on top of a pinch zoom, and the next press
        // would take only one of the two off.
        _hits.Result = ParagraphHit;

        // The zoom and its two retries change nothing; the one after the page is cleared does.
        for (var i = 0; i < 6; i++)
            _screen.Frames.Enqueue(90);
        _screen.Frames.Enqueue(90);
        _screen.Frames.Enqueue(200);

        var result = await Create().ZoomInAsync(Brave, Cursor, CancellationToken.None);

        Assert.Equal(ZoomInStatus.Applied, result.Status);
        Assert.Equal(5, _pinch.Calls.Count);

        // The clearing pinch: below 1.0, and instant so it is not seen as a gesture of its own.
        Assert.True(_pinch.Calls[3].Factor < 1);
        Assert.Equal(TimeSpan.Zero, _pinch.Calls[3].Duration);

        // What is remembered is the gesture that actually took, so the next press undoes exactly it.
        var state = Assert.IsType<BrowserAdapter.RestoreState>(result.RestoreState);
        Assert.Equal(3.0, state.Plan.Scale, precision: 9);
        Assert.Equal(_pinch.Calls[4].Anchor, state.Plan.Anchor);
        Assert.Equal(3.0, _pinch.Calls[4].Factor, precision: 9);
    }

    [Fact]
    public async Task A_pinch_over_a_region_with_nothing_in_it_is_trusted_rather_than_read_as_a_refusal()
    {
        // A wide white margin, an empty panel, a flat image. The gesture works, but no cell of a featureless
        // region can move by more than the tolerance, so the check cannot see it. Reading that as "the page
        // blocks gestures" used to send two more pinches at the same scale on top of the zoom that had already
        // happened - each around an anchor placed OUTSIDE the block, i.e. further into the same emptiness - and
        // then stack a Ctrl+wheel page zoom on all of it, of which the next press undid only the last.
        _hits.Result = ParagraphHit;
        _screen.Featureless = true;
        _screen.Frames.Enqueue(90);
        _screen.Frames.Enqueue(90);

        var result = await Create().ZoomInAsync(Brave, Cursor, CancellationToken.None);

        Assert.Equal(ZoomInStatus.Applied, result.Status);
        Assert.Single(_pinch.Calls);
        Assert.Equal(
            _pinch.Calls[0].Factor,
            Assert.IsType<BrowserAdapter.RestoreState>(result.RestoreState).Plan.Scale);
    }

    [Fact]
    public async Task A_screen_that_cannot_be_read_skips_the_check_and_trusts_the_gesture()
    {
        _hits.Result = ParagraphHit;
        _screen.Frames.Enqueue(null);

        var result = await Create().ZoomInAsync(Brave, Cursor, CancellationToken.None);

        Assert.Equal(ZoomInStatus.Applied, result.Status);
        Assert.NotNull(result.RestoreState);

        // Nothing to compare against, so no second read either.
        Assert.Single(_screen.Regions);
    }

    [Fact]
    public async Task A_screen_that_stops_being_readable_after_the_pinch_is_trusted_too()
    {
        _hits.Result = ParagraphHit;
        _screen.Frames.Enqueue(90);
        _screen.Frames.Enqueue(null);

        var result = await Create().ZoomInAsync(Brave, Cursor, CancellationToken.None);

        Assert.Equal(ZoomInStatus.Applied, result.Status);
        Assert.NotNull(result.RestoreState);
    }

    [Fact]
    public async Task A_rejected_pinch_is_not_verified()
    {
        _hits.Result = ParagraphHit;
        _pinch.Succeeds = false;

        await Create().ZoomInAsync(Brave, Cursor, CancellationToken.None);

        Assert.Single(_screen.Regions);
    }

    [Fact]
    public void The_verified_region_is_clipped_to_the_viewport()
    {
        var viewport = PixelRect.FromSize(1000, 500, 800, 300);

        Assert.Equal(new PixelRect(1000, 500, 1400, 750), BrowserAdapter.VerifyRegion(new ScreenPoint(1100, 550), viewport));
        Assert.Equal(new PixelRect(1100, 500, 1700, 800), BrowserAdapter.VerifyRegion(new ScreenPoint(1400, 650), viewport));
    }

    [Fact]
    public async Task Animation_can_be_turned_off()
    {
        _hits.Result = ParagraphHit;

        await Create(animate: false).ZoomInAsync(Brave, Cursor, CancellationToken.None);

        Assert.Equal(TimeSpan.Zero, Assert.Single(_pinch.Calls).Duration);
    }

    [Fact]
    public async Task Contacts_are_confined_to_the_viewport_minus_the_scrollbar_and_the_anchor_is_not_clamped_for_it()
    {
        // A small image at the far right: the anchor may sit near the edge (the injector will orient the
        // contacts vertically), but the contact area must exclude the scrollbar strip.
        var image = new ContentNode(ContentRole.Image, PixelRect.FromSize(Viewport.Right - 270, 700, 250, 160));
        _hits.Result = new ContentHit([image, new ContentNode(ContentRole.Document, Viewport)], Viewport);

        var result = await Create().ZoomInAsync(Brave, new ScreenPoint(Viewport.Right - 150, 780), CancellationToken.None);

        var call = _pinch.Calls[0];
        Assert.Equal(Viewport.Right - 56, call.Bounds.Right);
        Assert.Equal(Viewport.Left + 24, call.Bounds.Left);
        Assert.True(call.Anchor.X > Viewport.Right - 200, "anchor should stay where the fit math puts it, near the right edge");

        await Create().ZoomOutAsync(Brave, result.RestoreState!, CancellationToken.None);
        Assert.Equal(call.Bounds, _pinch.Calls[1].Bounds);
    }

    [Fact]
    public async Task On_a_100_percent_display_the_contacts_may_come_closer_to_the_edges()
    {
        // A 100% scrollbar is half the width of a 200% one; the 200% allowance kept the fingers needlessly far in.
        _display.Scale = 1.0;
        var image = new ContentNode(ContentRole.Image, PixelRect.FromSize(Viewport.Right - 270, 700, 250, 160));
        _hits.Result = new ContentHit([image, new ContentNode(ContentRole.Document, Viewport)], Viewport);

        await Create().ZoomInAsync(Brave, new ScreenPoint(Viewport.Right - 150, 780), CancellationToken.None);

        var call = _pinch.Calls[0];
        Assert.Equal(Viewport.Right - 28, call.Bounds.Right);
        Assert.Equal(Viewport.Left + 12, call.Bounds.Left);
        Assert.Equal(Viewport.Top + 12, call.Bounds.Top);
    }

    [Fact]
    public async Task On_a_300_percent_display_the_contacts_keep_further_from_the_edges()
    {
        _display.Scale = 3.0;
        _hits.Result = ParagraphHit;

        await Create().ZoomInAsync(Brave, Cursor, CancellationToken.None);

        var call = _pinch.Calls[0];
        Assert.Equal(Viewport.Right - 84, call.Bounds.Right);
        Assert.Equal(Viewport.Bottom - 84, call.Bounds.Bottom);
        Assert.Equal(Viewport.Top + 36, call.Bounds.Top);
    }

    [Fact]
    public async Task The_display_is_asked_about_where_the_press_is()
    {
        // Each display has its own scale; a window on the second one is measured by the second one.
        _hits.Result = ParagraphHit;
        await Create().ZoomInAsync(Brave, Cursor, CancellationToken.None);

        Assert.Equal(Cursor, Assert.Single(_display.AskedAbout));
    }

    [Fact]
    public async Task The_anchor_keeps_clear_of_the_edge_by_the_allowance_for_the_display()
    {
        // A press in the top-left corner: the anchor is held inside the contact area plus the injector's margin.
        _display.Scale = 1.0;
        _hits.Result = new ContentHit([new ContentNode(ContentRole.Document, Viewport)], Viewport);

        await Create().ZoomInAsync(Brave, new ScreenPoint(Viewport.Left + 2, Viewport.Top + 2), CancellationToken.None);

        var anchor = _pinch.Calls[0].Anchor;
        Assert.True(anchor.X >= Viewport.Left + EdgeAllowances.For(1.0).AnchorInset, $"anchor x {anchor.X}");
        Assert.True(anchor.Y >= Viewport.Top + EdgeAllowances.For(1.0).AnchorInsetVertical, $"anchor y {anchor.Y}");
    }

    [Fact]
    public async Task A_press_at_the_bottom_edge_is_anchored_inside_where_the_contacts_may_go()
    {
        // Below the contact area the gesture would need a vertical pan, which leaks into the page's scroll.
        _display.Scale = 1.0;
        _hits.Result = new ContentHit([new ContentNode(ContentRole.Document, Viewport)], Viewport);

        await Create().ZoomInAsync(Brave, new ScreenPoint((Viewport.Left + Viewport.Right) / 2, Viewport.Bottom - 3), CancellationToken.None);

        var call = _pinch.Calls[0];
        Assert.True(call.Anchor.Y < call.Bounds.Bottom, $"anchor y {call.Anchor.Y}, contact bottom {call.Bounds.Bottom}");
    }

    [Fact]
    public async Task Reset_in_a_viewport_smaller_than_the_edge_insets_pinches_around_its_middle()
    {
        // Narrower than two edge insets: there is no inset range to clamp the cursor into.
        var tiny = PixelRect.FromSize(100, 100, 40, 30);
        _hits.Result = new ContentHit([new ContentNode(ContentRole.Document, tiny)], tiny);

        Assert.Equal(ZoomInStatus.Applied, (await Create().ZoomInAsync(Brave, new ScreenPoint(101, 101), CancellationToken.None)).Status);

        // With no inset range to clamp the cursor into, the anchor falls back to the middle of what there is.
        Assert.Equal(new ScreenPoint(120, 114), Assert.Single(_pinch.Calls).Anchor);
    }

    [Fact]
    public async Task Zoom_out_rejects_foreign_state() =>
        await Assert.ThrowsAsync<ArgumentException>(() => Create().ZoomOutAsync(Brave, "nope", CancellationToken.None));

    [Fact]
    public async Task A_page_still_carrying_a_zoom_the_app_forgot_is_reset_before_the_new_one()
    {
        // The tree reports the page pinch-zoomed while this is a zoom-IN, so it is a zoom the process no
        // longer remembers: it was restarted, or switched off while the page was zoomed, or a restore did
        // not take. Chromium clamps the visual viewport at x4, so a second zoom on top of it is refused -
        // and a refused gesture looks exactly like a page that blocks gestures, which would send the press
        // to Ctrl+wheel page zoom and leave two different zooms stacked on one page.
        _hits.Result = ParagraphHit with { PageScale = 1.93 };
        _hits.Next = ParagraphHit;

        var result = await Create().ZoomInAsync(Brave, Cursor, CancellationToken.None);

        Assert.Equal(ZoomInStatus.Applied, result.Status);
        Assert.Equal(2, _pinch.Calls.Count);

        // The reset: below 1.0, and instant so it is not counted as a gesture.
        Assert.True(_pinch.Calls[0].Factor < 1);
        Assert.Equal(TimeSpan.Zero, _pinch.Calls[0].Duration);

        // The zoom itself, by the configured amount, once the page is back at 1.0.
        Assert.Equal(3.0, _pinch.Calls[1].Factor, precision: 9);
        Assert.Equal(
            _pinch.Calls[1].Factor,
            Assert.IsType<BrowserAdapter.RestoreState>(result.RestoreState).Plan.Scale);
    }

    // Answers the first hit-test with Result and any later one with Next (when set).
    private sealed class FakeHitTester : IContentHitTester
    {
        public ContentHit? Result { get; set; }

        public ContentHit? Next { get; set; }

        public int Calls { get; private set; }

        public Task<ContentHit?> HitTestAsync(TargetInfo target, ScreenPoint point, CancellationToken cancellationToken) =>
            Task.FromResult(++Calls == 1 || Next is null ? Result : Next);
    }

    // Each read returns a sample built from the next scripted luma (null: the screen could not be read); after
    // the script runs out, every read differs from the last, so a pinch reads as taken unless a test says
    // otherwise. The cells alternate, because a sample has to carry the structure a real page has: on a
    // featureless one, nothing can change and "nothing changed" answers no question. Set Featureless to get
    // that case deliberately.
    private sealed class FakeDisplayScale : IDisplayScale
    {
        public double Scale { get; set; } = 1.0;

        public List<ScreenPoint> AskedAbout { get; } = [];

        public double ScaleAt(ScreenPoint point)
        {
            AskedAbout.Add(point);
            return Scale;
        }
    }

    private sealed class FakeScreenSampler : IScreenSampler
    {
        private byte _luma;

        public Queue<byte?> Frames { get; } = new();

        public List<PixelRect> Regions { get; } = [];

        /// <summary>Whether reads come back as one flat colour, the way an empty margin or a plain panel does.</summary>
        public bool Featureless { get; set; }

        public ScreenSample? Sample(PixelRect region)
        {
            Regions.Add(region);
            var luma = Frames.Count > 0 ? Frames.Dequeue() : _luma += 100;
            if (luma is not { } value)
                return null;

            var pixels = new byte[region.Width * region.Height];
            if (Featureless)
            {
                Array.Fill(pixels, value);
                return ScreenSample.FromLuma(region, pixels);
            }

            var (columns, rows) = ScreenSample.GridFor(region.Width, region.Height);
            var alternate = (byte)(value ^ 0x80);
            for (var y = 0; y < region.Height; y++)
            {
                for (var x = 0; x < region.Width; x++)
                {
                    var cell = (x * columns / region.Width) + (y * rows / region.Height);
                    pixels[(y * region.Width) + x] = cell % 2 == 0 ? value : alternate;
                }
            }

            return ScreenSample.FromLuma(region, pixels);
        }
    }

    private sealed class FakePinch : IPinchInjector
    {
        public bool Succeeds { get; set; } = true;

        public List<(ScreenPoint Anchor, double Factor, TimeSpan Duration, PixelRect Bounds)> Calls { get; } = [];

        public Task<bool> PinchAsync(ScreenPoint anchor, double factor, TimeSpan duration, PixelRect bounds, CancellationToken cancellationToken)
        {
            Calls.Add((anchor, factor, duration, bounds));
            return Task.FromResult(Succeeds);
        }
    }
}
