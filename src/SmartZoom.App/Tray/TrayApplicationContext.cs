using System.Diagnostics;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using SmartZoom.App.Diagnostics;
using SmartZoom.App.Hosting;
using SmartZoom.App.Settings;
using SmartZoom.App.Ui.Shell;
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
    private readonly ISystemInput _systemInput;
    private readonly AppPaths _paths;
    private readonly SettingsShell _shell;
    private readonly ZoomActivity _activity;
    private readonly ILogger<TrayApplicationContext> _logger;
    private readonly Font _menuBold = new(SystemFonts.MenuFont!, FontStyle.Bold);
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _enabledItem;
    private readonly ToolStripMenuItem _zoomAmountItem;
    private readonly ToolStripMenuItem[] _zoomAmountChoices;
    private readonly ToolStripMenuItem _lastAppItem;
    private string? _lastProcess;
    private readonly CancellationTokenRegistration _hostStoppingRegistration;
    private readonly SynchronizationContext _uiContext;
    private readonly System.Windows.Forms.Timer _tooltipClock = new() { Interval = 30_000 };
    private string _lastAction = "no press seen yet";

    public TrayApplicationContext(
        ITriggerSource triggerSource,
        SettingsApplier applier,
        SettingsHolder holder,
        ISystemInput systemInput,
        AppPaths paths,
        ZoomActivity activity,
        SettingsShell shell,
        IHostApplicationLifetime lifetime,
        ILogger<TrayApplicationContext> logger)
    {
        _triggerSource = triggerSource;
        _applier = applier;
        _holder = holder;
        _systemInput = systemInput;
        _paths = paths;
        _activity = activity;
        _shell = shell;
        _logger = logger;

        // The trigger source is the truth about whether zooming is on; the item only shows it. Settings applied
        // from the window or the file change it too, so the check mark is re-read whenever it is about to be seen.
        _enabledItem = new ToolStripMenuItem("&Enabled") { Checked = triggerSource.Enabled };
        _enabledItem.Click += (_, _) => ToggleEnabled();

        // The three things people actually change live here rather than behind a window: for an app whose whole
        // job is one gesture, opening a settings window to change that gesture is a detour.
        _zoomAmountChoices = [.. TrayQuickSettings.ZoomAmounts.Select(CreateZoomAmountItem)];
        _zoomAmountItem = new ToolStripMenuItem("&Zoom amount");
        _zoomAmountItem.DropDownItems.AddRange(_zoomAmountChoices);

        // Named after the application of the last zoom, so "stop doing that here" is one click instead of a
        // routing table. Hidden until there has been one.
        _lastAppItem = new ToolStripMenuItem(string.Empty, image: null, (_, _) => ToggleLastApp()) { Visible = false };

        _menu = new ContextMenuStrip();
        _menu.Items.AddRange(
        [
            _enabledItem,
            new ToolStripMenuItem("&Change trigger…", image: null, (_, _) => ChangeTrigger()),
            _zoomAmountItem,
            _lastAppItem,
            new ToolStripSeparator(),
            new ToolStripMenuItem("&Settings…", image: null, (_, _) => OpenShell()) { Font = _menuBold },
            new ToolStripMenuItem("Open &settings file", image: null, (_, _) => OpenWithShell(_paths.SettingsFile)),
            new ToolStripMenuItem("&Reload settings file", image: null, (_, _) => ReloadSettings()),
            new ToolStripMenuItem("Open &log folder", image: null, (_, _) => OpenWithShell(_paths.LogDirectory)),
            new ToolStripSeparator(),
            // Opens the page rather than copying silently: a tray item that filled the clipboard unread would
            // defeat the point of showing the report at all.
            new ToolStripMenuItem("&Diagnostic report…", image: null, (_, _) => ShowDiagnostics()),
            new ToolStripSeparator(),
            new ToolStripMenuItem("&About SmartZoom", image: null, (_, _) => ShowAbout()),
            new ToolStripMenuItem("E&xit", image: null, (_, _) => ExitThread()),
        ]);
        // Everything the menu shows is read here rather than kept in step with every change behind its back.
        _menu.Opening += (_, _) =>
        {
            UpdateTooltip();
            UpdateQuickItems();
        };

        _notifyIcon = new NotifyIcon
        {
            Icon = TrayIcon.Load(),
            ContextMenuStrip = _menu,
            Visible = true,
        };
        // Left click drops the panel out of the tray, the way Krisp and Windows' own flyouts do; a double
        // click opens the full window for the things the panel deliberately leaves out.
        _notifyIcon.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) TogglePanel(); };
        _notifyIcon.DoubleClick += (_, _) => OpenShell();
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
            _shell.Dispose();
            _notifyIcon.Dispose();
            _menu.Dispose();
            _menuBold.Dispose();
        }

        base.Dispose(disposing);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Called by the second-instance listener: starting SmartZoom while it is already running is somebody
    /// looking for its window, so the running instance shows one.
    /// </remarks>
    public void RequestSettings() => _uiContext.Post(_ => OpenShell(), null);

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

    /// <summary>One zoom amount, as a radio-checked item that applies it.</summary>
    private ToolStripMenuItem CreateZoomAmountItem(double amount) =>
        new(
            $"{amount:0.#}×",
            image: null,
            (_, _) => Apply(TrayQuickSettings.WithZoomAmount(_holder.Current, amount), $"Zoom amount is now {amount:0.#}×."))
        {
            // Radio marks rather than ticks: these three are one choice, not three switches.
            CheckOnClick = false,
            Tag = amount,
        };

    /// <summary>Re-reads the settings the menu shows, so it is right however they were last changed.</summary>
    private void UpdateQuickItems()
    {
        var settings = _holder.Current;

        foreach (var item in _zoomAmountChoices)
            item.Checked = TrayQuickSettings.IsZoomAmount(settings, (double)item.Tag!);

        // Only offer this for an application that was actually zoomed: routing one SmartZoom never saw would
        // be guesswork, and the name in the item is the evidence that it is the right one.
        if (_lastProcess is not { Length: > 0 } process)
        {
            _lastAppItem.Visible = false;
            return;
        }

        _lastAppItem.Visible = true;
        _lastAppItem.Text = TrayQuickSettings.IsIgnored(settings, process)
            ? $"Zoom in {process} again"
            : $"Don't zoom in {process}";
    }

    /// <summary>
    /// Records the trigger again, starting from the one in force, and applies whatever was pressed. The dialog
    /// is modal on the UI thread; only the change that follows it goes to the applier off-thread.
    /// </summary>
    private void ChangeTrigger()
    {
        var settings = _holder.Current;
        TriggerSettings? captured;

        try
        {
            captured = _shell.RecordTrigger(
                _triggerSource, _systemInput.DoubleClickTimeMs, TrayQuickSettings.FirstTrigger(settings));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            LogShellFailed(ex);
            Notify("The trigger recorder could not be opened.", good: false);
            return;
        }

        if (captured is null)
            return;

        Apply(
            TrayQuickSettings.WithFirstTrigger(settings, captured),
            $"The trigger is now {captured.ToDefinition(_systemInput.DoubleClickTimeMs).DisplayName}.");
    }

    /// <summary>Switches SmartZoom off for the application of the last zoom, or hands it back to its strategy.</summary>
    private void ToggleLastApp()
    {
        if (_lastProcess is not { Length: > 0 } process)
            return;

        var settings = _holder.Current;
        var ignore = !TrayQuickSettings.IsIgnored(settings, process);
        Apply(
            TrayQuickSettings.WithIgnored(settings, process, ignore),
            ignore ? $"SmartZoom now leaves {process} alone." : $"SmartZoom zooms {process} again.");
    }

    /// <summary>
    /// Puts a changed copy of the settings into force. Off the UI thread, because the applier's gate may be
    /// held by a zoom in flight, and the user hears the outcome either way.
    /// </summary>
    /// <param name="settings">The changed copy.</param>
    /// <param name="done">What to say when it worked.</param>
    private void Apply(SmartZoomSettings settings, string done) => _ = Task.Run(async () =>
    {
        string summary;
        var good = false;
        try
        {
            var result = await _applier.ApplyAsync(settings).ConfigureAwait(false);
            good = result.InForce;
            summary = good
                ? done
                : "That change was not applied:" + Environment.NewLine + Environment.NewLine
                    + string.Join(Environment.NewLine, result.Problems.Select(p => p.ToString()));
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            LogChangeFailed(ex);
            summary = "That change was not applied: " + ex.Message;
        }

        _uiContext.Post(
            _ =>
            {
                Notify(summary, good);
                UpdateTooltip();
            },
            null);
    });

    /// <summary>Shows the panel, or hides it when it is already showing.</summary>
    private void TogglePanel() => Shell(_shell.TogglePanel);

    /// <summary>Opens the About page of the settings window.</summary>
    private void ShowAbout() => Shell(_shell.ShowAbout);

    /// <summary>Opens the settings window, or brings it to the front when it is already open.</summary>
    private void OpenShell() => Shell(_shell.ShowSettings);

    /// <summary>Opens the Advanced page, where the diagnostic report is read and copied.</summary>
    private void ShowDiagnostics() => Shell(_shell.ShowAdvanced);

    /// <summary>Runs one of the window's entry points; a UI that will not open must not take the tray with it.</summary>
    private void Shell(Action show)
    {
        try
        {
            show();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            LogShellFailed(ex);
            Notify("The settings window could not be opened. Details are in the log.", good: false);
        }
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
                var (action, process) = ((string, string?))state!;
                _lastAction = action;

                // Kept here rather than on ZoomActivity: the tray is the only thing that needs it, and it is
                // already being told. Null when the process could not be identified, which hides the item.
                _lastProcess = process;
                UpdateTooltip();
            },
            (outcome.ToString(), outcome.Process));

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

    [LoggerMessage(Level = LogLevel.Error, Message = "The new settings window could not be opened.")]
    private partial void LogShellFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to open {Path}.")]
    private partial void LogOpenFailed(Exception exception, string path);

    [LoggerMessage(Level = LogLevel.Error, Message = "A settings change from the tray failed.")]
    private partial void LogChangeFailed(Exception exception);
}
