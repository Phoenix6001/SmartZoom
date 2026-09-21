using System.Globalization;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Serilog;
using Serilog.Core;
using Serilog.Events;

using SmartZoom.App.Hosting;
using SmartZoom.App.Settings;
using SmartZoom.App.Tray;
using SmartZoom.Core.Input;
using SmartZoom.Core.Routing;
using SmartZoom.Core.Settings;
using SmartZoom.Core.Zoom;
using SmartZoom.Core.Zoom.Content;
using SmartZoom.Core.Zoom.Office;
using SmartZoom.Core.Zoom.Reader;
using SmartZoom.Interop;
using SmartZoom.Interop.Accessibility;
using SmartZoom.Interop.Input;
using SmartZoom.Interop.Office;
using SmartZoom.Interop.Windows;

namespace SmartZoom.App;

internal static class Program
{
    // A second instance would install a second hook and double every zoom. "Local\" scopes the mutex to
    // the current logon session, so other signed-in users can still run their own copy.
    private const string SingleInstanceMutexName = @"Local\SmartZoom.App-9C7B1E52-3F0A-4C1F-8B7D-2E6A5D4C3B21";

    // Deliberately synchronous: WinForms needs an STA thread, and [STAThread] has no effect on async Main
    // (or top-level statements), whose continuations may resume on MTA thread-pool threads.
    [STAThread]
    private static int Main()
    {
        using var singleInstance = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            MessageBox.Show("SmartZoom is already running. Look for its icon in the notification area.", "SmartZoom", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }

        // Applies the csproj's ApplicationHighDpiMode (PerMonitorV2) before any window or hook thread exists.
        // Threads created afterwards inherit it, which keeps hook coordinates and WindowFromPoint consistent.
        ApplicationConfiguration.Initialize();

        var paths = AppPaths.CreateDefault();

        // The logger has to exist before the settings can be read (reading them is itself logged), so it
        // starts at Debug and the file's own level is applied to this switch a moment later.
        var level = new LoggingLevelSwitch(LogEventLevel.Debug);
        Log.Logger = CreateLogger(paths, level);

        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => Log.Error(e.Exception, "Unhandled exception on the UI thread.");
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Fatal(e.ExceptionObject as Exception, "Unhandled exception; terminating.");

        try
        {
            using var host = BuildHost(paths);
            level.MinimumLevel = Serilog(host.Services.GetRequiredService<SmartZoomSettings>().Logging.Level);
            host.Start();
            Log.ForContext(typeof(Program)).Information("SmartZoom {Version} started.", typeof(Program).Assembly.GetName().Version);

            Application.Run(host.Services.GetRequiredService<TrayApplicationContext>());

            host.StopAsync().GetAwaiter().GetResult();
            return 0;
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "SmartZoom failed to start.");
            MessageBox.Show($"SmartZoom failed to start:\n\n{ex.Message}\n\nDetails are in {paths.LogDirectory}.", "SmartZoom", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    /// <summary>Maps the setting's level onto Serilog's, which is the same ladder under another name.</summary>
    private static LogEventLevel Serilog(LogLevel level) => level switch
    {
        LogLevel.Trace => LogEventLevel.Verbose,
        LogLevel.Debug => LogEventLevel.Debug,
        LogLevel.Information => LogEventLevel.Information,
        LogLevel.Warning => LogEventLevel.Warning,
        LogLevel.Error => LogEventLevel.Error,
        LogLevel.Critical => LogEventLevel.Fatal,
        _ => LogEventLevel.Fatal,
    };

    private static Serilog.Core.Logger CreateLogger(AppPaths paths, LoggingLevelSwitch level) => new LoggerConfiguration()
        .MinimumLevel.ControlledBy(level)
        .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
        .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
        .Enrich.FromLogContext()
        .WriteTo.File(
            Path.Combine(paths.LogDirectory, "smartzoom-.log"),
            outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}",
            formatProvider: CultureInfo.InvariantCulture,
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 14)
        .CreateLogger();

    private static IHost BuildHost(AppPaths paths)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            ContentRootPath = AppContext.BaseDirectory,
        });

        builder.Logging.ClearProviders();
        builder.Services.AddSerilog();

        // "Press Ctrl+C to shut down" and friends are meaningless for a tray app with no console.
        builder.Services.Configure<ConsoleLifetimeOptions>(options => options.SuppressStatusMessages = true);

        builder.Services.AddSingleton(paths);
        builder.Services.AddSingleton<SettingsStore>();
        builder.Services.AddSingleton(sp => sp.GetRequiredService<SettingsStore>().Load());
        builder.Services.AddSingleton<IWindowInspector, WindowInspector>();
        builder.Services.AddSingleton<ITriggerSource>(CreateTriggerSource);

        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<IInputInjector, SendInputInjector>();
        builder.Services.AddSingleton<IZoomAdapter>(sp => new CtrlWheelAdapter(
            sp.GetRequiredService<IInputInjector>(),
            sp.GetRequiredService<SmartZoomSettings>().Zoom.CtrlWheel,
            sp.GetRequiredService<TimeProvider>()));
        builder.Services.AddSingleton<ChromiumAccessibilityWake>();
        builder.Services.AddSingleton<IContentHitTester, MsaaContentHitTester>();
        builder.Services.AddSingleton<TouchDevices>();
        builder.Services.AddSingleton<IPinchInjector, TouchPinchInjector>();
        builder.Services.AddSingleton<IZoomAdapter>(sp => new BrowserAdapter(
            sp.GetRequiredService<IContentHitTester>(),
            sp.GetRequiredService<IPinchInjector>(),
            sp.GetRequiredService<SmartZoomSettings>().Zoom,
            sp.GetRequiredService<ILogger<BrowserAdapter>>()));
        builder.Services.AddSingleton<IWindowActivator, WindowActivator>();
        builder.Services.AddSingleton<IReaderView, ReaderView>();
        builder.Services.AddSingleton<ShortcutSender>();

        // The two reader strategies share one id, so exactly one of them is registered; "Zoom.Reader.Mode"
        // is the choice, and it is made here rather than inside an adapter that is secretly two adapters.
        builder.Services.AddSingleton<IZoomAdapter>(sp =>
        {
            var zoom = sp.GetRequiredService<SmartZoomSettings>().Zoom;
            var shortcuts = sp.GetRequiredService<ShortcutSender>();

            if (zoom.Reader.Mode == ReaderZoomMode.Shortcuts)
            {
                return new ReaderShortcutAdapter(
                    shortcuts,
                    sp.GetRequiredService<IReaderView>(),
                    zoom.Reader,
                    sp.GetRequiredService<ILogger<ReaderShortcutAdapter>>());
            }

            return new ReaderPinchAdapter(
                sp.GetRequiredService<IPinchInjector>(),
                sp.GetRequiredService<IReaderView>(),
                shortcuts,
                zoom.Reader,
                TimeSpan.FromMilliseconds(zoom.Animate ? zoom.Reader.AnimationMs : 0),
                sp.GetRequiredService<ILogger<ReaderPinchAdapter>>());
        });
        builder.Services.AddSingleton<IWordAutomation, WordAutomation>();
        builder.Services.AddSingleton<IZoomAdapter>(sp => new WordComAdapter(
            sp.GetRequiredService<IWordAutomation>(),
            sp.GetRequiredService<SmartZoomSettings>().Zoom,
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<WordComAdapter>>()));
        builder.Services.AddSingleton<IExcelAutomation, ExcelAutomation>();
        builder.Services.AddSingleton<IZoomAdapter>(sp => new ExcelComAdapter(
            sp.GetRequiredService<IExcelAutomation>(),
            sp.GetRequiredService<SmartZoomSettings>().Zoom,
            sp.GetRequiredService<ILogger<ExcelComAdapter>>()));
        builder.Services.AddSingleton<WindowZoomStateStore>();

        // Routing is built from the adapters that are actually registered above, so an application can
        // never be routed to a strategy this build does not contain, and new defaults reach users who
        // already have a settings file.
        builder.Services.AddSingleton(sp => new ZoomRouter(
            sp.GetServices<IZoomAdapter>().Select(a => a.Descriptor),
            sp.GetRequiredService<SmartZoomSettings>().Routing,
            sp.GetRequiredService<ILogger<ZoomRouter>>()));
        builder.Services.AddSingleton(sp => new ZoomCoordinator(
            sp.GetRequiredService<ZoomRouter>(),
            sp.GetServices<IZoomAdapter>(),
            sp.GetRequiredService<WindowZoomStateStore>(),
            sp.GetRequiredService<IWindowInspector>(),
            sp.GetRequiredService<SmartZoomSettings>().Zoom.FallbackToCtrlWheel,
            sp.GetRequiredService<ILogger<ZoomCoordinator>>()));

        // Hosted services start in registration order: capture must be running before the dispatcher reads from it.
        builder.Services.AddSingleton<ZoomActivity>();
        builder.Services.AddHostedService<TriggerCaptureService>();
        builder.Services.AddHostedService<TriggerDispatcher>();
        builder.Services.AddSingleton<TrayApplicationContext>();

        return builder.Build();
    }

    private static LowLevelInputHook CreateTriggerSource(IServiceProvider services)
    {
        var settings = services.GetRequiredService<SmartZoomSettings>();
        var systemDoubleClick = SystemInput.DoubleClickTimeMs;
        var triggers = settings.Triggers.Select(t => t.ToDefinition(systemDoubleClick)).ToList();

        return new LowLevelInputHook(triggers, services.GetRequiredService<ILogger<LowLevelInputHook>>())
        {
            Enabled = settings.Enabled,
        };
    }
}
