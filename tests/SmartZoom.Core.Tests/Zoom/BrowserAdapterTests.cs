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
        var pinch = Assert.Single(_pinch.Calls);
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
    public async Task No_content_is_unhandled_so_the_coordinator_can_fall_back()
    {
        _hits.Result = null;

        Assert.Equal(ZoomInStatus.Unhandled, (await Create().ZoomInAsync(Brave, Cursor, CancellationToken.None)).Status);
        Assert.Empty(_pinch.Calls);
    }

    [Fact]
    public async Task No_block_is_unhandled()
    {
        _hits.Result = new ContentHit([new ContentNode(ContentRole.Document, Viewport)], Viewport);

        Assert.Equal(ZoomInStatus.Unhandled, (await Create().ZoomInAsync(Brave, Cursor, CancellationToken.None)).Status);
    }

    [Fact]
    public async Task Block_that_already_fits_is_handled_without_a_gesture()
    {
        _hits.Result = new ContentHit(
            [new ContentNode(ContentRole.Group, PixelRect.FromSize(470, 800, 1800, 300)), new ContentNode(ContentRole.Document, Viewport)],
            Viewport);

        // 1800 of 1874 px is 96% of the viewport: too wide for the block selector, so nothing to zoom.
        Assert.Equal(ZoomInStatus.Unhandled, (await Create().ZoomInAsync(Brave, Cursor, CancellationToken.None)).Status);

        _hits.Result = new ContentHit(
            [new ContentNode(ContentRole.Group, PixelRect.FromSize(470, 800, 1680, 300)), new ContentNode(ContentRole.Document, Viewport)],
            Viewport);

        // 1680 px qualifies as a block but the resulting scale (1.09) is below MinScale.
        Assert.Equal(ZoomInStatus.SelfManaged, (await Create().ZoomInAsync(Brave, Cursor, CancellationToken.None)).Status);
        Assert.Empty(_pinch.Calls);
    }

    [Fact]
    public async Task Rejected_pinch_is_unhandled()
    {
        _hits.Result = ParagraphHit;
        _pinch.Succeeds = false;

        Assert.Equal(ZoomInStatus.Unhandled, (await Create().ZoomInAsync(Brave, Cursor, CancellationToken.None)).Status);
    }

    [Fact]
    public async Task Animation_can_be_turned_off()
    {
        _hits.Result = ParagraphHit;

        await Create(animate: false).ZoomInAsync(Brave, Cursor, CancellationToken.None);

        Assert.Equal(TimeSpan.Zero, _pinch.Calls[0].Duration);
    }

    [Fact]
    public async Task Anchor_stays_clear_of_the_edges_by_the_contact_spread_plus_scrollbar()
    {
        // A small image at the far right wants an anchor near the right edge; the fake spreads 60 * 3 = 180 px
        // at MaxScale, so with the 24 px scrollbar allowance the anchor must stay 204 px inside.
        var image = new ContentNode(ContentRole.Image, PixelRect.FromSize(Viewport.Right - 270, 700, 250, 160));
        _hits.Result = new ContentHit([image, new ContentNode(ContentRole.Document, Viewport)], Viewport);

        await Create().ZoomInAsync(Brave, new ScreenPoint(Viewport.Right - 150, 780), CancellationToken.None);

        var anchor = Assert.Single(_pinch.Calls).Anchor;
        Assert.InRange(anchor.X, Viewport.Left + 204, Viewport.Right - 1 - 204);
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

        public int MaxContactOffset(double factor) => (int)Math.Ceiling(60 * Math.Max(factor, 1 / factor));

        public List<(ScreenPoint Anchor, double Factor, TimeSpan Duration)> Calls { get; } = [];

        public Task<bool> PinchAsync(ScreenPoint anchor, double factor, TimeSpan duration, CancellationToken cancellationToken)
        {
            Calls.Add((anchor, factor, duration));
            return Task.FromResult(Succeeds);
        }
    }
}
