using System.Diagnostics;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using SmartZoom.App.Hosting;
using SmartZoom.App.Settings;
using SmartZoom.Core.Input;
using SmartZoom.Core.Settings;
using SmartZoom.Core.Zoom;

namespace SmartZoom.App.Tray;

/// <summary>Owns the notification-area icon and its menu; runs on the UI thread for the app's lifetime.</summary>
internal sealed partial class TrayApplicationContext : ApplicationContext
{
    private readonly ITriggerSource _triggerSource;
    private readonly SmartZoomSettings _settings;
    private readonly SettingsStore _settingsStore;
    private readonly AppPaths _paths;
    private readonly ZoomActivity _activity;
    private readonly ILogger<TrayApplicationContext> _logger;
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _enabledItem;
    private readonly CancellationTokenRegistration _hostStoppingRegistration;
    private readonly SynchronizationContext _uiContext;
    private string _lastAction = "no zoom yet";

    public TrayApplicationContext(
        ITriggerSource triggerSource,
        SmartZoomSettings settings,
        SettingsStore settingsStore,
        AppPaths paths,
        ZoomActivity activity,
        IHostApplicationLifetime lifetime,
        ILogger<TrayApplicationContext> logger)
    {
        _triggerSource = triggerSource;
        _settings = settings;
        _settingsStore = settingsStore;
        _paths = paths;
        _activity = activity;
        _logger = logger;

        _enabledItem = new ToolStripMenuItem("&Enabled") { CheckOnClick = true, Checked = triggerSource.Enabled };
        _enabledItem.CheckedChanged += OnEnabledChanged;

        _menu = new ContextMenuStrip();
        _menu.Items.AddRange(
        [
            _enabledItem,
            new ToolStripSeparator(),
            new ToolStripMenuItem("Open &settings file (restart to apply)", image: null, (_, _) => OpenWithShell(_paths.SettingsFile)),
            new ToolStripMenuItem("Open &log folder", image: null, (_, _) => OpenWithShell(_paths.LogDirectory)),
            new ToolStripSeparator(),
            new ToolStripMenuItem("E&xit", image: null, (_, _) => ExitThread()),
        ]);

        _notifyIcon = new NotifyIcon
        {
            Icon = LoadIcon(),
            ContextMenuStrip = _menu,
            Visible = true,
        };
        UpdateTooltip();

        // If the host stops on its own (e.g. a hosted service faulted), don't leave a tray icon with no hook
        // behind it. Creating the menu installed the WinForms synchronization context; capture it to marshal back.
        _uiContext = SynchronizationContext.Current
            ?? throw new InvalidOperationException("TrayApplicationContext must be created on the UI thread.");
        _hostStoppingRegistration = lifetime.ApplicationStopping.Register(() => _uiContext.Post(_ => ExitThread(), null));

        // Zooms are reported from the dispatcher's thread; the tooltip belongs to the UI thread.
        _activity.Happened += OnZoomHappened;
    }

    /// <summary>The application's own icon, at whatever size this display's scaling wants in the tray.</summary>
    private static Icon LoadIcon()
    {
        using var stream = typeof(TrayApplicationContext).Assembly.GetManifestResourceStream("SmartZoom.App.Resources.SmartZoom.ico");
        return stream is null ? SystemIcons.Application : new Icon(stream, SystemInformation.SmallIconSize);
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
            _hostStoppingRegistration.Dispose();
            _notifyIcon.Dispose();
            _menu.Dispose();
        }

        base.Dispose(disposing);
    }

    private void OnEnabledChanged(object? sender, EventArgs e)
    {
        _triggerSource.Enabled = _enabledItem.Checked;
        _settings.Enabled = _enabledItem.Checked;
        UpdateTooltip();

        try
        {
            _settingsStore.Save(_settings);
        }
        catch (IOException ex)
        {
            LogSaveFailed(ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            LogSaveFailed(ex);
        }
    }

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
        var text = header + Environment.NewLine + _lastAction;

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

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to save settings.")]
    private partial void LogSaveFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to open {Path}.")]
    private partial void LogOpenFailed(Exception exception, string path);
}
