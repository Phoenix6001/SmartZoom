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
internal sealed partial class TrayApplicationContext : ApplicationContext, ISettingsWindowOpener
{
    /// <summary>How long a balloon stays up; the shell treats it as a hint and may show it for less.</summary>
    private const int BalloonMs = 5000;

    /// <summary>The notification area truncates a tooltip silently past this many characters on older shells.</summary>
    private const int MaxTooltipLength = 63;

    private readonly ITriggerSource _triggerSource;
    private readonly SettingsApplier _applier;
    private readonly SettingsHolder _holder;
    private readonly ZoomEngine _engine;
    private readonly ISystemInput _systemInput;
    private readonly AppPaths _paths;
    private readonly DiagnosticRecorder _recorder;
    private readonly IMachineFacts _facts;
    private SettingsForm? _settingsWindow;
    private readonly ZoomActivity _activity;
    private readonly ILogger<TrayApplicationContext> _logger;
    private readonly Font _menuBold = new(SystemFonts.MenuFont!, FontStyle.Bold);
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
        ISystemInput systemInput,
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
        _systemInput = systemInput;
        _paths = paths;
        _recorder = recorder;
        _facts = facts;
        _activity = activity;
        _logger = logger;

        // The trigger source is the truth about whether zooming is on; the item only shows it. Settings applied
        // from the window or the file change it too, so the check mark is re-read whenever it is about to be seen.
        _enabledItem = new ToolStripMenuItem("&Enabled") { Checked = triggerSource.Enabled };
        _enabledItem.Click += (_, _) => ToggleEnabled();

        _menu = new ContextMenuStrip();
        _menu.Items.AddRange(
        [
            _enabledItem,
            new ToolStripSeparator(),
            new ToolStripMenuItem("&Settings…", image: null, (_, _) => OpenSettings()) { Font = _menuBold },
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
        _menu.Opening += (_, _) => UpdateTooltip();

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
            _menuBold.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <inheritdoc />
    public void RequestSettings() => _uiContext.Post(_ => OpenSettings(), null);

    /// <summary>
    /// Flips the switch through the applier, the only writer of the settings file. Off the UI thread, because
    /// the applier's gate may be held by a settings change that is waiting for a zoom.
    /// </summary>
    private void ToggleEnabled()
    {
        var enabled = !_triggerSource.Enabled;
        _ = Task.Run(async () =>
        {
            try
            {
                await _applier.SetEnabledAsync(enabled).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                LogChangeFailed(ex);
            }

            _uiContext.Post(_ => UpdateTooltip(), null);
        });
    }

    /// <summary>Opens the settings window on the given tab, or brings it to the front if it is already open.</summary>
    /// <param name="tab">
    /// The tab to show, even on an already-open window, or null to leave an open window on whatever tab the
    /// user has it.
    /// </param>
    private void OpenSettings(SettingsTab? tab = null)
    {
        if (_settingsWindow is { IsDisposed: false } open)
        {
            if (open.WindowState == FormWindowState.Minimized)
                open.WindowState = FormWindowState.Normal;

            if (tab is { } requested)
                open.ShowTab(requested);

            open.Activate();
            return;
        }

        _settingsWindow = new SettingsForm(
            _applier, _holder, _engine, _triggerSource, _systemInput, _paths, _recorder, _facts, tab ?? SettingsTab.Triggers);
        _settingsWindow.FormClosed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    /// <summary>
    /// Applies the settings file as it stands, for people who edit the JSON rather than use the window.
    /// Runs off the UI thread: applying waits for any zoom in flight. Whatever happens, the user hears about it.
    /// </summary>
    private void ReloadSettings() => _ = Task.Run(async () =>
    {
        string summary;
        var good = false;
        try
        {
            var result = await _applier.ReloadAsync().ConfigureAwait(false);
            good = result.InForce;
            summary = good
                ? "Settings reloaded."
                : "The settings file was not applied:" + Environment.NewLine + Environment.NewLine
                    + string.Join(Environment.NewLine, result.Problems.Select(p => p.ToString()));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            LogChangeFailed(ex);
            summary = "The settings file was not applied: " + ex.Message;
        }

        _uiContext.Post(
            _ =>
            {
                Notify(summary, good);
                UpdateTooltip();
            },
            null);
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
        _notifyIcon.ShowBalloonTip(BalloonMs, "SmartZoom", text, good ? ToolTipIcon.Info : ToolTipIcon.Warning);

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
        var enabled = _triggerSource.Enabled;
        _enabledItem.Checked = enabled;

        var header = enabled ? "SmartZoom" : "SmartZoom (disabled)";
        var text = header + Environment.NewLine + _lastAction + Since();

        // A sentence that ends in an ellipsis reads better than one that simply stops.
        _notifyIcon.Text = text.Length <= MaxTooltipLength ? text : string.Concat(text.AsSpan(0, MaxTooltipLength - 1), "…");
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

    [LoggerMessage(Level = LogLevel.Error, Message = "A settings change from the tray failed.")]
    private partial void LogChangeFailed(Exception exception);
}
