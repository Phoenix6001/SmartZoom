using Microsoft.Extensions.Logging.Abstractions;

using SmartZoom.App.Diagnostics;
using SmartZoom.Core.Input;
using SmartZoom.Core.Routing;
using SmartZoom.Core.Settings;
using SmartZoom.Core.Zoom;
using SmartZoom.Core.Zoom.Content;

namespace SmartZoom.App.Tests.Diagnostics;

/// <summary>
/// The privacy contract, tested where the guarantee actually lives. Tested against
/// <see cref="Core.Diagnostics.DiagnosticKey"/> and <see cref="Core.Diagnostics.DiagnosticSample"/> directly it
/// cannot fail: neither type has a field for a window title, a URL, or a coordinate, so there is nothing a
/// regression could add data to. The guarantee lives in the code that builds a sample from live zoom data —
/// here, a real <see cref="BrowserAdapter"/> reading a real accessibility path with absolute screen
/// coordinates, through the real <see cref="ZoomCoordinator"/>, into <see cref="DiagnosticSampleFactory"/>.
/// </summary>
public sealed class DiagnosticSampleFactoryTests
{
    private static readonly TargetInfo Browser = new(0x100, 0x101, 7, "brave", "Chrome_WidgetWin_1", "Chrome_RenderWidgetHostHWND");
    private static readonly ScreenPoint Cursor = new(1462, 1376);
    private static readonly PixelRect Viewport = PixelRect.FromSize(0, 0, 1920, 1080);

    // A real accessibility path (the shape IContentHitTester returns in production): a node too small to
    // qualify as a zoomable block, positioned far from the origin so a coordinate leak cannot hide behind an
    // unremarkable number, above a document node that names the whole page.
    private static readonly ContentHit NothingToZoom = new(
        [
            new ContentNode(ContentRole.Group, PixelRect.FromSize(1438, 1361, 40, 30)),
            new ContentNode(ContentRole.Document, Viewport),
        ],
        Viewport);

    [Fact]
    public async Task A_sample_built_from_a_real_outcome_and_a_real_accessibility_path_carries_no_coordinates()
    {
        IZoomAdapter browser = new BrowserAdapter(
            new FakeHitTester { Result = NothingToZoom },
            new FakePinch(),
            new FakeScreenSampler(),
            new ZoomSettings { Animate = false, Smart = new SmartZoomTuning { AnimationMs = 0 }, Browser = new BrowserZoomSettings { AnchorInsetPx = 0 } },
            NullLogger<BrowserAdapter>.Instance);

        var router = new ZoomRouter(
            [browser.Descriptor],
            new RoutingSettings { Apps = new Dictionary<string, AdapterId>(StringComparer.OrdinalIgnoreCase) },
            NullLogger<ZoomRouter>.Instance);
        var coordinator = new ZoomCoordinator(
            router,
            [browser],
            new WindowZoomStateStore(),
            new FakeWindowInspector(),
            fallbackToCtrlWheel: false,
            NullLogger<ZoomCoordinator>.Instance);

        // A real ZoomOutcome, produced end to end by production code from a real accessibility chain that
        // carries absolute coordinates (1438, 1361).
        var outcome = await coordinator.HandleTriggerAsync(Browser, Cursor, CancellationToken.None);
        Assert.Equal(ZoomAction.Handled, outcome.Action);
        Assert.Equal(ZoomReason.NoBlock, outcome.Reason);

        // The code under test: what TriggerDispatcher actually calls to build what gets recorded.
        var (key, sample) = DiagnosticSampleFactory.ForZoomedNothing(outcome, DateTimeOffset.UtcNow);

        Assert.Equal("brave", key.Process);
        Assert.Equal("Browser", key.Adapter);
        Assert.Equal("NoBlock", key.Reason);

        Assert.NotNull(sample);
        var detail = sample.Detail;
        Assert.NotNull(detail);

        // This is the assertion that fails if someone later records the richer, coordinate-bearing string:
        // roles and sizes only, never a position.
        Assert.Equal("Group 40x30 < Document 1920x1080", detail);
        Assert.DoesNotContain("1438", detail, StringComparison.Ordinal);
        Assert.DoesNotContain("1361", detail, StringComparison.Ordinal);
        Assert.DoesNotContain("@(", detail, StringComparison.Ordinal);

        // No window title and no URL anywhere in the key or the sample: BrowserAdapter is never given one to
        // begin with (TargetInfo carries a process name, not a title; ContentNode carries no text), so there
        // is nothing for this assertion to catch today — it documents the guarantee rather than proving it,
        // which is exactly why the coordinate assertions above (where a real leak was possible) are the point.
        Assert.DoesNotContain("Quarterly results", detail, StringComparison.Ordinal);
    }

    [Fact]
    public void An_adapter_that_could_not_act_is_reported_as_a_reason_not_as_an_action()
    {
        var outcome = new ZoomOutcome(ZoomAction.Unhandled, "AcroRd32", new AdapterId("Reader"), ZoomReason.AdapterCouldNotAct);

        var (key, sample) = DiagnosticSampleFactory.ForZoomedNothing(outcome, DateTimeOffset.UnixEpoch);

        // The reason, never the action: "Unhandled" is the least informative label available for a Reader
        // or Ctrl+wheel no-op, and it is a ZoomAction, not a ZoomReason.
        Assert.Equal("AdapterCouldNotAct", key.Reason);
        Assert.Null(sample);
    }

    private sealed class FakeHitTester : IContentHitTester
    {
        public ContentHit? Result { get; set; }

        public Task<ContentHit?> HitTestAsync(TargetInfo target, ScreenPoint point, CancellationToken cancellationToken) =>
            Task.FromResult(Result);
    }

    private sealed class FakePinch : IPinchInjector
    {
        public Task<bool> PinchAsync(ScreenPoint anchor, double factor, TimeSpan duration, PixelRect bounds, CancellationToken cancellationToken) =>
            Task.FromResult(true);
    }

    // Every sample looks different from the last, so a pinch always reads as taken.
    private sealed class FakeScreenSampler : IScreenSampler
    {
        private byte _luma;

        public ScreenSample? Sample(PixelRect region)
        {
            var pixels = new byte[region.Width * region.Height];
            Array.Fill(pixels, _luma += 100);
            return ScreenSample.FromLuma(region, pixels);
        }
    }

    private sealed class FakeWindowInspector : IWindowInspector
    {
        public TargetInfo? GetTargetAt(ScreenPoint point) => throw new NotSupportedException();

        public bool IsWindowAlive(nint window, uint processId) => true;
    }
}
