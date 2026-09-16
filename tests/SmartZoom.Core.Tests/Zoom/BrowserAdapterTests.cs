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

    private BrowserAdapter Create(bool animate = true) =>
        new(_hits, _pinch, new ZoomSettings { Animate = animate, Browser = new BrowserZoomSettings { AnimationMs = 180, AnchorInsetPx = 0 } }, NullLogger<BrowserAdapter>.Instance);

    [Fact]
    public async Task Zooms_the_paragraph_and_remembers_the_plan()
    {
        _hits.Result = ParagraphHit;

        var result = await Create().ZoomInAsync(Brave, Cursor, CancellationToken.None);

        Assert.Equal(ZoomInStatus.Applied, result.Status);
        // Zoom-in is preceded by an instant baseline pinch-out that undoes any leftover visual zoom.
        Assert.Equal(2, _pinch.Calls.Count);
        Assert.Equal(0.9 / 3.0, _pinch.Calls[0].Factor, precision: 9);
        Assert.Equal(TimeSpan.Zero, _pinch.Calls[0].Duration);
        var pinch = _pinch.Calls[1];
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
        var zoomIn = _pinch.Calls[1];

        await adapter.ZoomOutAsync(Brave, result.RestoreState!, CancellationToken.None);

        var zoomOut = _pinch.Calls[2];
        Assert.Equal(zoomIn.Anchor, zoomOut.Anchor);
        Assert.Equal(0.9 / zoomIn.Factor, zoomOut.Factor, precision: 9);
    }

    [Fact]
    public async Task No_content_is_reported_handled_so_the_coordinator_never_page_zooms_a_browser()
    {
        _hits.Result = null;

        Assert.Equal(ZoomInStatus.SelfManaged, (await Create().ZoomInAsync(Brave, Cursor, CancellationToken.None)).Status);
        Assert.Empty(_pinch.Calls);
    }

    [Fact]
    public async Task No_block_is_reported_handled()
    {
        _hits.Result = new ContentHit([new ContentNode(ContentRole.Document, Viewport)], Viewport);

        Assert.Equal(ZoomInStatus.SelfManaged, (await Create().ZoomInAsync(Brave, Cursor, CancellationToken.None)).Status);
    }

    [Fact]
    public async Task Block_that_already_fits_is_handled_without_a_gesture()
    {
        _hits.Result = new ContentHit(
            [new ContentNode(ContentRole.Group, PixelRect.FromSize(470, 800, 1800, 300)), new ContentNode(ContentRole.Document, Viewport)],
            Viewport);

        // 1800 of 1874 px is 96% of the viewport: too wide for the block selector, so nothing to zoom.
        Assert.Equal(ZoomInStatus.SelfManaged, (await Create().ZoomInAsync(Brave, Cursor, CancellationToken.None)).Status);

        _hits.Result = new ContentHit(
            [new ContentNode(ContentRole.Group, PixelRect.FromSize(470, 800, 1680, 300)), new ContentNode(ContentRole.Document, Viewport)],
            Viewport);

        // 1680 px qualifies as a block but the resulting scale (1.09) is below MinScale.
        Assert.Equal(ZoomInStatus.SelfManaged, (await Create().ZoomInAsync(Brave, Cursor, CancellationToken.None)).Status);
        Assert.Empty(_pinch.Calls);
    }

    [Fact]
    public async Task Rejected_pinch_is_reported_handled()
    {
        _hits.Result = ParagraphHit;
        _pinch.Succeeds = false;

        Assert.Equal(ZoomInStatus.SelfManaged, (await Create().ZoomInAsync(Brave, Cursor, CancellationToken.None)).Status);
    }

    [Fact]
    public async Task Animation_can_be_turned_off()
    {
        _hits.Result = ParagraphHit;

        await Create(animate: false).ZoomInAsync(Brave, Cursor, CancellationToken.None);

        Assert.Equal(TimeSpan.Zero, _pinch.Calls[1].Duration);
    }

    [Fact]
    public async Task Contacts_are_confined_to_the_viewport_minus_the_scrollbar_and_the_anchor_is_not_clamped_for_it()
    {
        // A small image at the far right: the anchor may sit near the edge (the injector will orient the
        // contacts vertically), but the contact area must exclude the scrollbar strip.
        var image = new ContentNode(ContentRole.Image, PixelRect.FromSize(Viewport.Right - 270, 700, 250, 160));
        _hits.Result = new ContentHit([image, new ContentNode(ContentRole.Document, Viewport)], Viewport);

        var result = await Create().ZoomInAsync(Brave, new ScreenPoint(Viewport.Right - 150, 780), CancellationToken.None);

        var call = _pinch.Calls[1];
        Assert.Equal(Viewport.Right - 56, call.Bounds.Right);
        Assert.Equal(Viewport.Left, call.Bounds.Left);
        Assert.True(call.Anchor.X > Viewport.Right - 200, "anchor should stay where the fit math puts it, near the right edge");

        await Create().ZoomOutAsync(Brave, result.RestoreState!, CancellationToken.None);
        Assert.Equal(call.Bounds, _pinch.Calls[2].Bounds);
    }

    [Fact]
    public async Task Zoom_out_rejects_foreign_state() =>
        await Assert.ThrowsAsync<ArgumentException>(() => Create().ZoomOutAsync(Brave, "nope", CancellationToken.None));

    private sealed class FakeHitTester : IContentHitTester
    {
        public ContentHit? Result { get; set; }

        public Task<ContentHit?> HitTestAsync(TargetInfo target, ScreenPoint point, CancellationToken cancellationToken) => Task.FromResult(Result);
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
