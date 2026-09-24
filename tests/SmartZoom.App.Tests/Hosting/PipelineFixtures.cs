using Microsoft.Extensions.Logging.Abstractions;

using SmartZoom.App.Hosting;
using SmartZoom.Core.Input;
using SmartZoom.Core.Routing;
using SmartZoom.Core.Settings;
using SmartZoom.Core.Zoom;
using SmartZoom.Core.Zoom.Content;
using SmartZoom.Core.Zoom.Office;
using SmartZoom.Core.Zoom.Reader;

namespace SmartZoom.App.Tests.Hosting;

/// <summary>
/// Pipelines for tests: the real <see cref="ZoomPipelineFactory"/> over inert fakes, so applying settings
/// builds the real adapters and the real router, and hand-rolled pipelines over whatever adapters a test
/// needs.
/// </summary>
internal static class PipelineFixtures
{
    /// <summary>A window under the cursor that the adapters in a pipeline can be pointed at.</summary>
    public static TargetInfo Target(string process, nint window = 0x10) => new(window, window, 1, process, "Root", "Hit");

    /// <summary>The real factory over fakes that see no content and inject nothing.</summary>
    public static ZoomPipelineFactory CreateFactory(WindowZoomStateStore? state = null) => new(
        new NoContentHitTester(),
        new AcceptingPinch(),
        new EmptyReaderView(),
        new AcceptingInjector(),
        new NoWordAutomation(),
        new NoExcelAutomation(),
        new ShortcutSender(new AcceptingInjector(), new AcceptingActivator(), TimeProvider.System, NullLogger<ShortcutSender>.Instance),
        state ?? new WindowZoomStateStore(),
        new AllWindowsAlive(),
        TimeProvider.System,
        NullLoggerFactory.Instance);

    /// <summary>A pipeline over exactly these adapters, routed by their own default processes.</summary>
    public static ZoomPipeline Pipeline(WindowZoomStateStore state, params IZoomAdapter[] adapters)
    {
        var router = new ZoomRouter(
            adapters.Select(a => a.Descriptor),
            new RoutingSettings { Apps = new Dictionary<string, AdapterId>(StringComparer.OrdinalIgnoreCase) },
            NullLogger<ZoomRouter>.Instance);
        var coordinator = new ZoomCoordinator(router, adapters, state, new AllWindowsAlive(), fallbackToCtrlWheel: false, NullLogger<ZoomCoordinator>.Instance);
        return new ZoomPipeline(coordinator, router, adapters.ToDictionary(a => a.Descriptor.Id, a => a.RestoreType));
    }

    private sealed class NoContentHitTester : IContentHitTester
    {
        public Task<ContentHit?> HitTestAsync(TargetInfo target, ScreenPoint point, CancellationToken cancellationToken) =>
            Task.FromResult<ContentHit?>(null);
    }

    private sealed class AcceptingPinch : IPinchInjector
    {
        public Task<bool> PinchAsync(ScreenPoint anchor, double factor, TimeSpan duration, PixelRect bounds, CancellationToken cancellationToken) =>
            Task.FromResult(true);
    }

    private sealed class EmptyReaderView : IReaderView
    {
        public PixelRect? Bounds(TargetInfo target) => null;

        public int ScrollBy(TargetInfo target, int pixels, CancellationToken cancellationToken) => 0;

        public bool ScrollBackTo(TargetInfo target, ReaderViewMark snapshot, CancellationToken cancellationToken) => true;

        public ReaderViewMark? Snapshot(TargetInfo target) => null;
    }

    private sealed class AcceptingInjector : IInputInjector
    {
        public bool TrySendModifier(ModifierKey key, bool isDown) => true;

        public bool TrySendWheel(int ticks) => true;

        public bool IsModifierDown(ModifierKey key) => false;

        public bool TrySendKeyCombo(KeyCombo combo) => true;
    }

    private sealed class AcceptingActivator : IWindowActivator
    {
        public nint ForegroundWindow { get; private set; }

        public bool TryActivate(nint rootWindow)
        {
            ForegroundWindow = rootWindow;
            return true;
        }
    }

    private sealed class NoWordAutomation : IWordAutomation
    {
        public Task<IWordWindow?> AttachAsync(TargetInfo target, CancellationToken cancellationToken) =>
            Task.FromResult<IWordWindow?>(null);
    }

    private sealed class NoExcelAutomation : IExcelAutomation
    {
        public Task<IExcelWindow?> AttachAsync(TargetInfo target, CancellationToken cancellationToken) =>
            Task.FromResult<IExcelWindow?>(null);
    }

    private sealed class AllWindowsAlive : IWindowInspector
    {
        public TargetInfo? GetTargetAt(ScreenPoint point) => throw new NotSupportedException();

        public bool IsWindowAlive(nint window, uint processId) => true;
    }
}
