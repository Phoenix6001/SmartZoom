using Microsoft.Extensions.Logging;

using SmartZoom.Core.Routing;
using SmartZoom.Core.Settings;
using SmartZoom.Core.Zoom;
using SmartZoom.Core.Zoom.Content;
using SmartZoom.Core.Zoom.Office;
using SmartZoom.Core.Zoom.Reader;

namespace SmartZoom.App.Hosting;

/// <summary>
/// Builds the zooming half of the application from a set of settings: the adapters, the routing table they
/// imply, and the coordinator over both.
/// </summary>
/// <remarks>
/// <para>
/// This is where an adapter is registered. Everything it needs — the hit tester, the injectors, the Office
/// automation — is a singleton with no settings in it, so it is injected here once and shared by every
/// pipeline this factory builds. The adapters themselves copy their settings when they are constructed, which
/// is why changing a setting means building a new pipeline rather than poking the old one.
/// </para>
/// <para>
/// See <c>docs/adding-an-application.md</c>. Adding a strategy is a class and one line in
/// <see cref="Adapters"/>.
/// </para>
/// </remarks>
internal sealed class ZoomPipelineFactory(
    IContentHitTester hitTester,
    IPinchInjector pinch,
    IScreenSampler screen,
    IReaderView readerView,
    IInputInjector injector,
    IWordAutomation word,
    IExcelAutomation excel,
    ShortcutSender shortcuts,
    WindowZoomStateStore state,
    IWindowInspector windows,
    TimeProvider time,
    ILoggerFactory loggers)
{
    /// <summary>Builds a pipeline. Nothing is published or started; the caller decides when it takes over.</summary>
    /// <param name="settings">The settings to build from. Not held onto: every value is copied.</param>
    /// <exception cref="ArgumentOutOfRangeException">A setting is outside what an adapter accepts.</exception>
    /// <exception cref="FormatException">A key combination in the settings is not one.</exception>
    /// <exception cref="InvalidOperationException">Two adapters share an id or claim the same application.</exception>
    public ZoomPipeline Build(SmartZoomSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var adapters = Adapters(settings.Zoom);
        var router = new ZoomRouter(adapters.Select(a => a.Descriptor), settings.Routing, loggers.CreateLogger<ZoomRouter>());
        var coordinator = new ZoomCoordinator(
            router,
            adapters,
            state,
            windows,
            settings.Zoom.FallbackToCtrlWheel,
            loggers.CreateLogger<ZoomCoordinator>());

        return new ZoomPipeline(coordinator, router, adapters.ToDictionary(a => a.Descriptor.Id, a => a.RestoreType));
    }

    private IReadOnlyList<IZoomAdapter> Adapters(ZoomSettings zoom) =>
    [
        new CtrlWheelAdapter(injector, zoom.CtrlWheel, time),
        new BrowserAdapter(hitTester, pinch, screen, zoom, loggers.CreateLogger<BrowserAdapter>()),
        Reader(zoom),
        new WordComAdapter(word, zoom, time, loggers.CreateLogger<WordComAdapter>()),
        new ExcelComAdapter(excel, zoom, loggers.CreateLogger<ExcelComAdapter>()),
    ];

    /// <summary>
    /// The two reader strategies share one id, so exactly one of them exists; <c>Zoom.Reader.Mode</c> is the
    /// choice, and it is made here rather than inside an adapter that is secretly two adapters.
    /// </summary>
    private IZoomAdapter Reader(ZoomSettings zoom) =>
        zoom.Reader.Mode == ReaderZoomMode.Shortcuts
            ? new ReaderShortcutAdapter(shortcuts, readerView, zoom.Reader, loggers.CreateLogger<ReaderShortcutAdapter>())
            : new ReaderPinchAdapter(
                pinch,
                readerView,
                shortcuts,
                zoom.Reader,
                TimeSpan.FromMilliseconds(zoom.Animate ? zoom.Reader.AnimationMs : 0),
                loggers.CreateLogger<ReaderPinchAdapter>());
}
