using System.Globalization;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Serilog;
using Serilog.Core;
using Serilog.Events;

using SmartZoom.App.Diagnostics;
using SmartZoom.App.Hosting;
using SmartZoom.App.Settings;
using SmartZoom.App.Tray;
using SmartZoom.App.Ui.Pages;
using SmartZoom.App.Ui.Panel;
using SmartZoom.App.Ui.Shell;
using SmartZoom.App.Ui.Theming;
using SmartZoom.Core.Diagnostics;
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
    // Deliberately synchronous: WinForms needs an STA thread, and [STAThread] has no effect on async Main
    // (or top-level statements), whose continuations may resume on MTA thread-pool threads.
    [STAThread]
    private static int Main(string[] args)
    {
        // Writes the trigger the installer's wizard asked for and exits. Before the mutex, because it is a
        // job rather than an instance of the app, and the installer has already stopped any running copy.
        if (TriggerCommand.TryRun(args, out var triggerExitCode))
            return triggerExitCode;

        // The installer asks a running copy to close this way before it replaces the executable, so that the
        // tray icon comes down properly rather than being left behind by a killed process.
        var quitting = args.Any(a => string.Equals(a, "--quit", StringComparison.OrdinalIgnoreCase));

        // A second instance would install a second hook and double every zoom.
        using var singleInstance = new Mutex(initiallyOwned: true, AppIdentity.InstanceName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            if (quitting)
                return SecondInstanceListener.TryRequestQuit() ? 0 : 1;

            // Starting a tray app that is already running is somebody looking for its window, so the running
            // instance shows one instead of this one complaining and exiting.
            if (!SecondInstanceListener.TryRequestSettings())
                MessageBox.Show("SmartZoom is already running. Look for its icon in the notification area.", "SmartZoom", MessageBoxButtons.OK, MessageBoxIcon.Information);

            return 0;
        }

        // Nothing was running, so there was nothing to close.
        if (quitting)
            return 0;

        // Applies the csproj's ApplicationHighDpiMode (PerMonitorV2) before any window or hook thread exists.
        // Threads created afterwards inherit it, which keeps hook coordinates and WindowFromPoint consistent.
        ApplicationConfiguration.Initialize();

        var paths = AppPaths.CreateDefault();

        // The logger has to exist before the settings can be read (reading them is itself logged), so it
        // starts at Debug and the file's own level is applied to this switch a moment later. It is a singleton
        // rather than a local so the level can be changed again later, from the settings window.
        var level = new LoggingLevelSwitch(LogEventLevel.Debug);
        Log.Logger = CreateLogger(paths, level);

        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => Log.Error(e.Exception, "Unhandled exception on the UI thread.");
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Fatal(e.ExceptionObject as Exception, "Unhandled exception; terminating.");

        try
        {
            using var host = BuildHost(paths, level);

            // Wired here, once the DI container exists, rather than "before the host is built": the recorder
            // is a singleton shared with the flush timer and the gesture-pacing sink, and a second, separately
            // constructed one would split the record in two. Written synchronously in the handler rather than
            // left to the flush timer, because the timer will not run again after this — a crash record that
            // dies with the crash is exactly the failure this exists to prevent.
            var recorder = host.Services.GetRequiredService<DiagnosticRecorder>();
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                if (e.ExceptionObject is Exception ex)
                    Crash.Record(recorder, ex);
            };
            // The process survives these (CatchException above), so the record says which thread they were on.
            Application.ThreadException += (_, e) => Crash.Record(recorder, e.Exception, Crash.UiThread);

            // Built here, on the UI thread and before any hosted service can ask for it: the tray installs the
            // WinForms synchronization context it needs by creating its first control.
            var tray = host.Services.GetRequiredService<TrayApplicationContext>();

            host.Start();
            Log.ForContext(typeof(Program)).Information("SmartZoom {Version} started.", typeof(Program).Assembly.GetName().Version);

            Application.Run(tray);

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
            retainedFileCountLimit: 14,
            // The Diagnostics page reads today's file while this process is still writing it; the default
            // (non-shared) sink opens the file exclusively, and a plain File.ReadAllLines would always fail
            // with a sharing violation — exactly when the feature is most likely to be used.
            shared: true)
        .CreateLogger();

    private static IHost BuildHost(AppPaths paths, LoggingLevelSwitch logLevel)
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
        builder.Services.AddSingleton(logLevel);
        builder.Services.AddSingleton<SettingsStore>();
        builder.Services.AddSingleton(sp => new SettingsHolder(sp.GetRequiredService<SettingsStore>().Load()));
        builder.Services.AddSingleton<ITriggerSource>(CreateTriggerSource);

        // Everything below has no settings in it; one instance is shared by every pipeline the factory builds.
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<ISystemInput, SystemInput>();
        builder.Services.AddSingleton<IWindowInspector, WindowInspector>();
        builder.Services.AddSingleton<IInputInjector, SendInputInjector>();
        builder.Services.AddSingleton<ChromiumAccessibilityWake>();
        builder.Services.AddSingleton<IContentHitTester, MsaaContentHitTester>();
        builder.Services.AddSingleton<TouchDevices>();

        // The diagnostics record: local-only counters the injector reports its gesture pacing into. One
        // DiagnosticRecorder instance serves both DI-registered types so the totals accumulate in one place.
        builder.Services.AddSingleton<IMachineFacts, MachineFacts>();
        builder.Services.AddSingleton<DiagnosticStore>();
        builder.Services.AddSingleton(sp => new DiagnosticRecorder(
            sp.GetRequiredService<DiagnosticStore>(),
            sp.GetRequiredService<TimeProvider>(),
            sp.GetRequiredService<IMachineFacts>().AppVersion)
        {
            // The settings file owns this switch; SettingsApplier carries later changes to it live.
            Enabled = sp.GetRequiredService<SettingsHolder>().Current.Diagnostics.Enabled,
        });
        builder.Services.AddSingleton<IGesturePacingSink>(sp => sp.GetRequiredService<DiagnosticRecorder>());
        builder.Services.AddHostedService<DiagnosticFlushService>();

        builder.Services.AddSingleton<IPinchInjector, TouchPinchInjector>();
        builder.Services.AddSingleton<IWindowActivator, WindowActivator>();
        builder.Services.AddSingleton<IReaderView, ReaderView>();
        builder.Services.AddSingleton<IScreenSampler, ScreenSampler>();
        builder.Services.AddSingleton<ShortcutSender>();
        builder.Services.AddSingleton<IWordAutomation, WordAutomation>();
        builder.Services.AddSingleton<IExcelAutomation, ExcelAutomation>();
        builder.Services.AddSingleton<WindowZoomStateStore>();

        // The adapters, the routing table and the coordinator are built from a settings snapshot, so that
        // changing a setting can build a new set rather than restart the process. See ZoomPipelineFactory.
        builder.Services.AddSingleton<ZoomPipelineFactory>();
        builder.Services.AddSingleton(sp => new ZoomEngine(
            sp.GetRequiredService<ZoomPipelineFactory>().Build(sp.GetRequiredService<SettingsHolder>().Current),
            sp.GetRequiredService<WindowZoomStateStore>(),
            sp.GetRequiredService<ILogger<ZoomEngine>>()));
        builder.Services.AddSingleton<SettingsApplier>();

        // Hosted services start in registration order: capture must be running before the dispatcher reads from it.
        builder.Services.AddSingleton<ZoomActivity>();
        builder.Services.AddHostedService<TriggerCaptureService>();
        builder.Services.AddHostedService<SecondInstanceListener>();
        builder.Services.AddHostedService<TriggerDispatcher>();
        builder.Services.AddHostedService<TriggerWatchdog>();
        // The tray panel and the settings window. Everything under them is built on the UI thread, at the
        // moment the tray asks for it: the view models capture the dispatcher they are created on, and the
        // pages are WPF controls.
        builder.Services.AddSingleton<ThemeManager>();
        builder.Services.AddSingleton<PanelViewModel>();
        builder.Services.AddSingleton<OverviewViewModel>();
        builder.Services.AddSingleton<TriggersViewModel>();
        builder.Services.AddSingleton<ApplicationsViewModel>();
        builder.Services.AddSingleton<AdvancedViewModel>();
        builder.Services.AddSingleton<AboutViewModel>();
        builder.Services.AddSingleton(sp => new ShellViewModel(
            ShellPages.Build(
                sp.GetRequiredService<OverviewViewModel>(),
                sp.GetRequiredService<TriggersViewModel>(),
                sp.GetRequiredService<ApplicationsViewModel>(),
                sp.GetRequiredService<AdvancedViewModel>(),
                sp.GetRequiredService<AboutViewModel>()),
            [
                sp.GetRequiredService<OverviewViewModel>(),
                sp.GetRequiredService<TriggersViewModel>(),
                sp.GetRequiredService<ApplicationsViewModel>(),
                sp.GetRequiredService<AdvancedViewModel>(),
            ],
            sp.GetRequiredService<OverviewViewModel>(),
            sp.GetRequiredService<AboutViewModel>(),
            sp.GetRequiredService<ITriggerSource>(),
            sp.GetRequiredService<DiagnosticRecorder>(),
            sp.GetRequiredService<ZoomActivity>(),
            sp.GetRequiredService<SettingsApplier>(),
            sp.GetRequiredService<ThemeManager>(),
            sp.GetRequiredService<ILogger<ShellViewModel>>()));

        // Resolved through factories rather than injected, because the WPF application object has to exist
        // before the first of those view models constructs a control, and only SettingsShell knows when.
        builder.Services.AddSingleton<Func<PanelViewModel>>(sp => sp.GetRequiredService<PanelViewModel>);
        builder.Services.AddSingleton<Func<ShellViewModel>>(sp => sp.GetRequiredService<ShellViewModel>);
        builder.Services.AddSingleton<SettingsShell>();

        builder.Services.AddSingleton<TrayApplicationContext>();
        builder.Services.AddSingleton<ISettingsWindowOpener>(sp => sp.GetRequiredService<TrayApplicationContext>());

        return builder.Build();
    }

    private static LowLevelInputHook CreateTriggerSource(IServiceProvider services)
    {
        var settings = services.GetRequiredService<SettingsHolder>().Current;
        var systemDoubleClick = services.GetRequiredService<ISystemInput>().DoubleClickTimeMs;
        var triggers = settings.Triggers.Select(t => t.ToDefinition(systemDoubleClick)).ToList();

        return new LowLevelInputHook(triggers, services.GetRequiredService<ILogger<LowLevelInputHook>>())
        {
            Enabled = settings.Enabled,
        };
    }
}
