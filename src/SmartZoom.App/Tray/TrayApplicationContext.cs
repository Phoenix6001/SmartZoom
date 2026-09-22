using System.Diagnostics;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using SmartZoom.App.Diagnostics;
using SmartZoom.App.Hosting;
using SmartZoom.App.Settings;
using SmartZoom.Core.Diagnostics;
using SmartZoom.Core.Input;
using SmartZoom.Core.Settings;
using SmartZoom.Core.Zoom;

namespace SmartZoom.App.Tray;

/// <summary>Owns the notification-area icon and its menu; runs on the UI thread for the app's lifetime.</summary>
internal sealed partial class TrayApplicationContext : ApplicationContext
{
    private readonly ITriggerSource _triggerSource;
    private readonly SettingsApplier _applier;
    private readonly SettingsHolder _holder;
    private readonly ZoomEngine _engine;
    private readonly AppPaths _paths;
    private readonly DiagnosticRecorder _recorder;
    private readonly IMachineFacts _facts;
    private SettingsForm? _settingsWindow;
    private readonly ZoomActivity _activity;
    private readonly ILogger<TrayApplicationContext> _logger;
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _enabledItem;
    private readonly CancellationTokenRegistration _hostStoppingRegistration;
    private readonly SynchronizationContext _uiContext;
    private readonly System.Windows.Forms.Timer _tooltipClock = new() { Interval = 30_000 };
    private string _lastAction = "no press seen yet";

    public TrayApplicationContext(
        ITriggerSource triggerSource,
        SettingsApplier applier,
        SettingsHolder holder,
        ZoomEngine engine,
        AppPaths paths,
        DiagnosticRecorder recorder,
        IMachineFacts facts,
        ZoomActivity activity,
        IHostApplicationLifetime lifetime,
        ILogger<TrayApplicationContext> logger)
    {
        _triggerSource = triggerSource;
        _applier = applier;
        _holder = holder;
        _engine = engine;
        _paths = paths;
        _recorder = recorder;
        _facts = facts;
        _activity = activity;
        _logger = logger;

        _enabledItem = new ToolStripMenuItem("&Enabled") { CheckOnClick = true, Checked = triggerSource.Enabled };
        _enabledItem.CheckedChanged += OnEnabledChanged;

        _menu = new ContextMenuStrip();
        _menu.Items.AddRange(
        [
            _enabledItem,
            new ToolStripSeparator(),
            new ToolStripMenuItem("&Settings…", image: null, (_, _) => OpenSettings()) { Font = new Font(SystemFonts.MenuFont!, FontStyle.Bold) },
            new ToolStripMenuItem("Open &settings file", image: null, (_, _) => OpenWithShell(_paths.SettingsFile)),
            new ToolStripMenuItem("&Reload settings file", image: null, (_, _) => ReloadSettings()),
            new ToolStripMenuItem("Open &log folder", image: null, (_, _) => OpenWithShell(_paths.LogDirectory)),
            new ToolStripSeparator(),
            // Opens the page rather than copying silently: a tray item that filled the clipboard unread would
            // defeat the point of showing the report at all.
            new ToolStripMenuItem("&Diagnostic report…", image: null, (_, _) => OpenSettings(SettingsTab.Diagnostics)),
            new ToolStripSeparator(),
            new ToolStripMenuItem("E&xit", image: null, (_, _) => ExitThread()),
        ]);

        _notifyIcon = new NotifyIcon
        {
            Icon = TrayIcon.Load(),
            ContextMenuStrip = _menu,
            Visible = true,
        };
        _notifyIcon.DoubleClick += (_, _) => OpenSettings();
        UpdateTooltip();

        // If the host stops on its own (e.g. a hosted service faulted), don't leave a tray icon with no hook
        // behind it. Creating the menu installed the WinForms synchronization context; capture it to marshal back.
        _uiContext = SynchronizationContext.Current
            ?? throw new InvalidOperationException("TrayApplicationContext must be created on the UI thread.");
        _hostStoppingRegistration = lifetime.ApplicationStopping.Register(() => _uiContext.Post(_ => ExitThread(), null));

        // Zooms are reported from the dispatcher's thread; the tooltip belongs to the UI thread.
        _activity.Happened += OnZoomHappened;
        _tooltipClock.Tick += (_, _) => UpdateTooltip();
        _tooltipClock.Start();
    }

    protected override void ExitThreadCore()
    {
        // Hide explicitly; otherwise a "ghost" icon lingers until the user hovers over the tray.
        _notifyIcon.Visible = false;
        base.ExitThreadCore();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _activity.Happened -= OnZoomHappened;
            _tooltipClock.Dispose();
            _hostStoppingRegistration.Dispose();
            _notifyIcon.Dispose();
            _menu.Dispose();
        }

        base.Dispose(disposing);
    }

    private void OnEnabledChanged(object? sender, EventArgs e)
    {
        // Through the applier, because it is the only thing that may write the settings file: a second writer
        // holding its own copy is how the file ends up describing settings the app is not running.
        _applier.SetEnabled(_enabledItem.Checked);
        UpdateTooltip();
    }

    /// <summary>Asks for the settings window from any thread; the window itself belongs to the UI thread.</summary>
    public void RequestSettings() => _uiContext.Post(_ => OpenSettings(), null);

    /// <summary>
    /// Opens the settings window on the given tab, or brings it to the front if it is already open. An
    /// already-open window does not jump to a different tab — it was opened for a reason, and switching it
    /// out from under whatever the user is doing there would be more surprising than helpful.
    /// </summary>
    private void OpenSettings(SettingsTab tab = SettingsTab.Triggers)
    {
        if (_settingsWindow is { IsDisposed: false } open)
        {
            if (open.WindowState == FormWindowState.Minimized)
                open.WindowState = FormWindowState.Normal;

            open.Activate();
            return;
        }

        _settingsWindow = new SettingsForm(_applier, _holder, _engine, _triggerSource, _paths, _recorder, _facts, tab);
        _settingsWindow.FormClosed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    /// <summary>
    /// Applies the settings file as it stands, for people who edit the JSON rather than use the window.
    /// Runs off the UI thread: applying waits for any zoom in flight.
    /// </summary>
    private void ReloadSettings() => _ = Task.Run(async () =>
    {
        var result = await _applier.ReloadAsync().ConfigureAwait(false);
        var summary = result.InForce
            ? "Settings reloaded."
            : "The settings file was not applied:" + Environment.NewLine + Environment.NewLine
                + string.Join(Environment.NewLine, result.Problems.Select(p => p.ToString()));

        _uiContext.Post(_ => Notify(summary, result.InForce), null);
    });

    /// <summary>How long ago a press last arrived, or nothing at all before the first one.</summary>
    private string Since()
    {
        if (_activity.LastTrigger is not { } last)
            return string.Empty;

        var idle = DateTimeOffset.UtcNow - last;
        return idle < TimeSpan.FromMinutes(1)
            ? " (just now)"
            : idle < TimeSpan.FromHours(1)
                ? $" ({idle.Minutes} min ago)"
                : $" ({(int)idle.TotalHours} h ago)";
    }

    private void Notify(string text, bool good) =>
        _notifyIcon.ShowBalloonTip(5000, "SmartZoom", text, good ? ToolTipIcon.Info : ToolTipIcon.Warning);

    private void OnZoomHappened(object? sender, ZoomOutcome outcome) =>
        _uiContext.Post(
            state =>
            {
                _lastAction = (string)state!;
                UpdateTooltip();
            },
            outcome.ToString());

    private void UpdateTooltip()
    {
        var header = _enabledItem.Checked ? "SmartZoom" : "SmartZoom (disabled)";
        var text = header + Environment.NewLine + _lastAction + Since();

        // The notification area truncates silently past 63 characters on older shells, and a sentence that
        // ends in an ellipsis reads better than one that simply stops.
        _notifyIcon.Text = text.Length <= 63 ? text : string.Concat(text.AsSpan(0, 62), "\u2026");
    }

    private void OpenWithShell(string path)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            LogOpenFailed(ex, path);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to open {Path}.")]
    private partial void LogOpenFailed(Exception exception, string path);
}
