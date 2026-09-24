using Microsoft.Extensions.Logging.Abstractions;

using SmartZoom.Core.Input;
using SmartZoom.Core.Routing;
using SmartZoom.Core.Settings;
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

    private IZoomAdapter Create(bool animate = true, bool ctrlWheelWhenPinchBlocked = true) =>
        new BrowserAdapter(
            _hits,
            _pinch,
            _screen,
            new ZoomSettings
            {
                Animate = animate,
                Smart = new SmartZoomTuning { AnimationMs = 180 },
                Browser = new BrowserZoomSettings { AnchorInsetPx = 0, CtrlWheelWhenPinchBlocked = ctrlWheelWhenPinchBlocked },
            },
            NullLogger<BrowserAdapter>.Instance);

    [Fact]
    public async Task Zooms_the_paragraph_and_remembers_the_plan()
    {
        _hits.Result = ParagraphHit;

        var result = await Create().ZoomInAsync(Brave, Cursor, CancellationToken.None);

        Assert.Equal(ZoomInStatus.Applied, result.Status);

        // Exactly one gesture: the zoom the user asked for. Edge draws a pinch-out below 1.0 as a
        // shrink-and-rebound, so nothing may precede it.
        Assert.Single(_pinch.Calls);
        var pinch = _pinch.Calls[0];
        Assert.Equal(1874.0 / (949 + 32), pinch.Factor, precision: 6);
        Assert.Equal(TimeSpan.FromMilliseconds(180), pinch.Duration);
        Assert.Equal(pinch.Factor, Assert.IsType<BrowserAdapter.RestoreState>(result.RestoreState).Plan.Scale);
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
    public async Task No_block_resets_a_possibly_stuck_zoom_and_looks_again_before_giving_up()
    {
        _hits.Result = new ContentHit([new ContentNode(ContentRole.Document, Viewport)], Viewport);

        var result = await Create().ZoomInAsync(Brave, Cursor, CancellationToken.None);

        Assert.Equal(ZoomInStatus.Handled, result.Status);
        Assert.Equal(ZoomReason.NoBlock, result.Reason);

        // One instant pinch-out (invisible on an unzoomed page, a reset on a zoomed one) and one more look.
        var reset = Assert.Single(_pinch.Calls);
        Assert.Equal(0.9 / 3.0, reset.Factor, precision: 9);
        Assert.Equal(TimeSpan.Zero, reset.Duration);
        Assert.Equal(2, _hits.Calls);
    }

    [Fact]
    public async Task Block_found_after_the_reset_is_zoomed()
    {
        _hits.Result = new ContentHit([new ContentNode(ContentRole.Document, Viewport)], Viewport);
        _hits.Next = ParagraphHit;

        var result = await Create().ZoomInAsync(Brave, Cursor, CancellationToken.None);

        Assert.Equal(ZoomInStatus.Applied, result.Status);

        // The reset that rescued the hit-test, then the zoom itself — and nothing in between.
        Assert.Equal(2, _pinch.Calls.Count);
        Assert.Equal(0.9 / 3.0, _pinch.Calls[0].Factor, precision: 9);
        Assert.Equal(TimeSpan.Zero, _pinch.Calls[0].Duration);
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
    public async Task Block_that_already_fits_is_handled_without_a_gesture()
    {
        _hits.Result = new ContentHit(
            [new ContentNode(ContentRole.Group, PixelRect.FromSize(470, 800, 1800, 300)), new ContentNode(ContentRole.Document, Viewport)],
            Viewport);

        // 1800 of 1874 px is 96% of the viewport: too wide for the block selector, so nothing to zoom.
        var tooWide = await Create().ZoomInAsync(Brave, Cursor, CancellationToken.None);
        Assert.Equal(ZoomInStatus.Handled, tooWide.Status);
        Assert.Equal(ZoomReason.NoBlock, tooWide.Reason);

        _hits.Result = new ContentHit(
            [new ContentNode(ContentRole.Group, PixelRect.FromSize(470, 800, 1680, 300)), new ContentNode(ContentRole.Document, Viewport)],
            Viewport);

        // 1680 px qualifies as a block but the resulting scale (1.09) is below MinScale.
        var belowMinScale = await Create().ZoomInAsync(Brave, Cursor, CancellationToken.None);
        Assert.Equal(ZoomInStatus.Handled, belowMinScale.Status);
        Assert.Equal(ZoomReason.AlreadyFits, belowMinScale.Reason);

        // The first case made one instant reset pinch (see the stuck-zoom test); the second made no gesture at all.
        Assert.Single(_pinch.Calls);
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

        // The same zoom, only aimed above the block that refused it and on the original anchor's column.
        var first = _pinch.Calls[0];
        var retry = _pinch.Calls[1];
        Assert.Equal(first.Factor, retry.Factor);
        Assert.Equal(first.Duration, retry.Duration);
        Assert.Equal(first.Bounds, retry.Bounds);
        Assert.Equal(first.Anchor.X, retry.Anchor.X);
        Assert.Equal(1133 - RetryAnchor.OutsideGap, retry.Anchor.Y);

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
        for (var i = 0; i < 6; i++)
            _screen.Frames.Enqueue(90);

        var result = await Create(ctrlWheelWhenPinchBlocked: true).ZoomInAsync(Brave, Cursor, CancellationToken.None);

        Assert.Equal(ZoomInStatus.Unhandled, result.Status);
        Assert.Null(result.RestoreState);

        // The zoom and its two retries, and nothing more: a refused press costs three gestures.
        Assert.Equal(3, _pinch.Calls.Count);
    }

    [Fact]
    public async Task A_page_that_blocks_every_anchor_is_reported_and_left_alone_when_page_zoom_is_not_wanted()
    {
        _hits.Result = ParagraphHit;
        for (var i = 0; i < 6; i++)
            _screen.Frames.Enqueue(90);

        var result = await Create(ctrlWheelWhenPinchBlocked: false).ZoomInAsync(Brave, Cursor, CancellationToken.None);

        Assert.Equal(ZoomInStatus.Handled, result.Status);
        Assert.Equal(ZoomReason.GestureRefused, result.Reason);
        Assert.Equal("pinch blocked by the page", result.Detail);
        Assert.Null(result.RestoreState);
        Assert.Equal(3, _pinch.Calls.Count);
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
    public void Contacts_keep_clear_of_the_window_resize_border_on_every_side()
    {
        // A touch contact on the viewport's outermost pixels grabs the window's resize border (measured: 8 px
        // inside the window rect resizes, 12 px does not) and a pinch then drags the window edge instead.
        var bounds = BrowserAdapter.ContactBounds(PixelRect.FromSize(1891, 85, 1793, 1527));

        Assert.Equal(new PixelRect(1891 + 24, 85 + 24, 1891 + 1793 - 56, 85 + 1527 - 24), bounds);
    }

    [Fact]
    public void Contact_bounds_of_a_tiny_viewport_never_collapse()
    {
        var bounds = BrowserAdapter.ContactBounds(PixelRect.FromSize(100, 100, 30, 30));

        Assert.Equal(1, bounds.Width);
        Assert.Equal(1, bounds.Height);
    }

    [Fact]
    public async Task Reset_in_a_viewport_smaller_than_the_edge_insets_pinches_around_its_middle()
    {
        // Narrower than two edge insets: there is no inset range to clamp the cursor into.
        var tiny = PixelRect.FromSize(100, 100, 40, 30);
        _hits.Result = new ContentHit([new ContentNode(ContentRole.Document, tiny)], tiny);

        Assert.Equal(ZoomInStatus.Handled, (await Create().ZoomInAsync(Brave, new ScreenPoint(101, 101), CancellationToken.None)).Status);

        Assert.Equal(new ScreenPoint(120, 114), Assert.Single(_pinch.Calls).Anchor);
    }

    [Fact]
    public async Task Zoom_out_rejects_foreign_state() =>
        await Assert.ThrowsAsync<ArgumentException>(() => Create().ZoomOutAsync(Brave, "nope", CancellationToken.None));

    // Answers the first hit-test with Result and any later one with Next (when set).
    private sealed class FakeHitTester : IContentHitTester
    {
        public ContentHit? Result { get; set; }

        public ContentHit? Next { get; set; }

        public int Calls { get; private set; }

        public Task<ContentHit?> HitTestAsync(TargetInfo target, ScreenPoint point, CancellationToken cancellationToken) =>
            Task.FromResult(++Calls == 1 || Next is null ? Result : Next);
    }

    // Each read returns a flat sample of the next scripted luma (null: the screen could not be read); after the
    // script runs out, every read differs from the last, so a pinch reads as taken unless a test says otherwise.
    private sealed class FakeScreenSampler : IScreenSampler
    {
        private byte _luma;

        public Queue<byte?> Frames { get; } = new();

        public List<PixelRect> Regions { get; } = [];

        public ScreenSample? Sample(PixelRect region)
        {
            Regions.Add(region);
            var luma = Frames.Count > 0 ? Frames.Dequeue() : _luma += 100;
            if (luma is not { } value)
                return null;

            var pixels = new byte[region.Width * region.Height];
            Array.Fill(pixels, value);
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
