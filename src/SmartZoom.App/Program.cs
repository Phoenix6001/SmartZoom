using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
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
        Log.Logger = CreateLogger(paths);

        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => Log.Error(e.Exception, "Unhandled exception on the UI thread.");
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Fatal(e.ExceptionObject as Exception, "Unhandled exception; terminating.");

        try
        {
            using var host = BuildHost(paths);
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

    private static Serilog.Core.Logger CreateLogger(AppPaths paths) => new LoggerConfiguration()
        .MinimumLevel.Debug()
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
        builder.Services.AddSingleton(sp => new ZoomRouter(sp.GetRequiredService<SmartZoomSettings>().Routing));
        builder.Services.AddSingleton<IWindowInspector, WindowInspector>();
        builder.Services.AddSingleton<ITriggerSource>(CreateTriggerSource);

        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<IInputInjector, SendInputInjector>();
        builder.Services.AddSingleton<IZoomAdapter>(sp => new CtrlWheelAdapter(
            sp.GetRequiredService<IInputInjector>(),
            sp.GetRequiredService<SmartZoomSettings>().Zoom.CtrlWheel,
            sp.GetRequiredService<TimeProvider>()));
        builder.Services.AddSingleton<IContentHitTester, MsaaContentHitTester>();
        builder.Services.AddSingleton<IPinchInjector, TouchPinchInjector>();
        builder.Services.AddSingleton<IZoomAdapter>(sp => new BrowserAdapter(
            sp.GetRequiredService<IContentHitTester>(),
            sp.GetRequiredService<IPinchInjector>(),
            sp.GetRequiredService<SmartZoomSettings>().Zoom,
            sp.GetRequiredService<ILogger<BrowserAdapter>>()));
        builder.Services.AddSingleton<IWindowActivator, WindowActivator>();
        builder.Services.AddSingleton<IZoomAdapter>(sp => new KeyZoomAdapter(
            sp.GetRequiredService<IInputInjector>(),
            sp.GetRequiredService<IWindowActivator>(),
            sp.GetRequiredService<SmartZoomSettings>().Zoom.Keys,
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<KeyZoomAdapter>>()));
        builder.Services.AddSingleton<IWordAutomation, WordAutomation>();
        // Word: object-model steps only. Driving the motion with a touch pinch is smoother, but Word commits
        // the pinch result asynchronously and the exact restore became unreliable; see WordComAdapter.
        builder.Services.AddSingleton<IZoomAdapter>(sp => new WordComAdapter(
            sp.GetRequiredService<IWordAutomation>(),
            pinch: null,
            sp.GetRequiredService<SmartZoomSettings>().Zoom,
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<ILogger<WordComAdapter>>()));
        builder.Services.AddSingleton<WindowZoomStateStore>();
        builder.Services.AddSingleton(sp => new ZoomCoordinator(
            sp.GetRequiredService<ZoomRouter>(),
            sp.GetServices<IZoomAdapter>(),
            sp.GetRequiredService<WindowZoomStateStore>(),
            sp.GetRequiredService<IWindowInspector>(),
            sp.GetRequiredService<SmartZoomSettings>().Zoom.FallbackToCtrlWheel,
            sp.GetRequiredService<ILogger<ZoomCoordinator>>()));

        // Hosted services start in registration order: capture must be running before the dispatcher reads from it.
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
