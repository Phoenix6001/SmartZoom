using System.Globalization;
using System.Windows.Input;
using System.Windows.Threading;

using Microsoft.Extensions.Logging;

using SmartZoom.App.Hosting;
using SmartZoom.App.Settings;
using SmartZoom.App.Tray;
using SmartZoom.App.Ui.Mvvm;
using SmartZoom.App.Ui.Recorder;
using SmartZoom.Core.Input;
using SmartZoom.Core.Settings;
using SmartZoom.Core.Zoom;

namespace SmartZoom.App.Ui.Panel;

/// <summary>
/// What the tray panel shows: whether SmartZoom is on, the trigger, how far it zooms, and whether to leave
/// the last application alone.
/// </summary>
/// <remarks>
/// <para>
/// This is the application's front door, so everything in it is read from the running state at the moment the
/// panel opens — <see cref="SettingsHolder.Current"/>, the trigger source and <see cref="ZoomActivity"/> —
/// and every change goes out through <see cref="SettingsApplier"/>, the only writer of the settings file.
/// </para>
/// <para>
/// The three settings it changes are computed by <see cref="TrayQuickSettings"/>, the same functions the tray
/// menu uses, so the panel and the menu can never disagree about what "don't zoom here" means.
/// </para>
/// </remarks>
internal sealed partial class PanelViewModel : ObservableObject
{
    private readonly ITriggerSource _triggers;
    private readonly SettingsApplier _applier;
    private readonly SettingsHolder _holder;
    private readonly ISystemInput _systemInput;
    private readonly ZoomActivity _activity;
    private readonly ILogger<PanelViewModel> _logger;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;

    private bool _isEnabled = true;
    private string _triggerName = string.Empty;
    private string? _lastProcess;
    private bool _isIgnored;
    private string _footer = string.Empty;
    private bool _busy;

    /// <summary>Creates the panel's view model over the state the application is running on.</summary>
    /// <param name="triggers">The truth about whether zooming is on; also silenced while a trigger is recorded.</param>
    /// <param name="applier">The only writer of the settings file.</param>
    /// <param name="holder">Where the settings in force live; only read.</param>
    /// <param name="systemInput">Supplies the system double-click time the recorder offers as the default window.</param>
    /// <param name="activity">The last zoom, for the footer and for the "don't zoom here" row.</param>
    /// <param name="logger">Logger.</param>
    public PanelViewModel(
        ITriggerSource triggers,
        SettingsApplier applier,
        SettingsHolder holder,
        ISystemInput systemInput,
        ZoomActivity activity,
        ILogger<PanelViewModel> logger)
    {
        _triggers = triggers;
        _applier = applier;
        _holder = holder;
        _systemInput = systemInput;
        _activity = activity;
        _logger = logger;

        ZoomAmounts = [.. TrayQuickSettings.ZoomAmounts.Select(a => new ZoomAmountChoice(a))];

        ToggleEnabled = new RelayCommand(_ => Toggle(), _ => !_busy);
        ChangeTrigger = new RelayCommand(_ => Record(), _ => !_busy);
        SetZoomAmount = new RelayCommand(SetAmount, _ => !_busy);
        ToggleIgnored = new RelayCommand(_ => Ignore(), _ => !_busy && HasLastProcess);
        OpenSettings = new RelayCommand(() => SettingsRequested?.Invoke(this, EventArgs.Empty));

        Refresh();
    }

    /// <summary>Raised when the gear is pressed; whoever owns the panel opens the settings window.</summary>
    public event EventHandler? SettingsRequested;

    /// <summary>
    /// Raised when something has been changed that the panel cannot stay open across — recording a trigger
    /// puts a modal dialog on top of it, and the panel closes when it loses activation.
    /// </summary>
    public event EventHandler? DismissRequested;

    /// <summary>The three amounts the panel offers, as one choice.</summary>
    public IReadOnlyList<ZoomAmountChoice> ZoomAmounts { get; }

    /// <summary>Turns zooming on or off, through the applier.</summary>
    public ICommand ToggleEnabled { get; }

    /// <summary>Opens the trigger recorder and applies whatever is pressed.</summary>
    public ICommand ChangeTrigger { get; }

    /// <summary>Applies one of <see cref="ZoomAmounts"/>; the parameter is the choice.</summary>
    public ICommand SetZoomAmount { get; }

    /// <summary>Switches SmartZoom off for the last zoomed application, or hands it back to its strategy.</summary>
    public ICommand ToggleIgnored { get; }

    /// <summary>Opens the settings window.</summary>
    public ICommand OpenSettings { get; }

    /// <summary>Whether triggers are being acted on right now.</summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        private set
        {
            if (Set(ref _isEnabled, value))
                Raise(nameof(StateHeadline));
        }
    }

    /// <summary>"Enabled" or "Disabled", in the panel's large type.</summary>
    public string StateHeadline => IsEnabled ? "Enabled" : "Disabled";

    /// <summary>What the pill beside it says it will do.</summary>
    public string ToggleLabel => IsEnabled ? "Turn off" : "Turn on";

    /// <summary>The first trigger, as the recorder and the log spell it.</summary>
    public string TriggerName
    {
        get => _triggerName;
        private set => Set(ref _triggerName, value);
    }

    /// <summary>Whether anything has been zoomed yet, which is what shows the "don't zoom here" row.</summary>
    public bool HasLastProcess => _lastProcess is { Length: > 0 };

    /// <summary>What that row says, naming the application of the last zoom.</summary>
    public string IgnoreLabel => _lastProcess is { Length: > 0 } process
        ? string.Create(CultureInfo.CurrentCulture, $"Don't zoom in {process}")
        : string.Empty;

    /// <summary>Whether that application is currently switched off.</summary>
    public bool IsIgnored
    {
        get => _isIgnored;
        private set => Set(ref _isIgnored, value);
    }

    /// <summary>The last action, in the muted line at the bottom.</summary>
    public string Footer
    {
        get => _footer;
        private set => Set(ref _footer, value);
    }

    /// <summary>Re-reads everything. Called whenever the panel is about to be shown.</summary>
    public void Refresh()
    {
        var settings = _holder.Current;

        IsEnabled = _triggers.Enabled;
        Raise(nameof(ToggleLabel));
        TriggerName = Describe(TrayQuickSettings.FirstTrigger(settings));

        foreach (var choice in ZoomAmounts)
            choice.IsSelected = TrayQuickSettings.IsZoomAmount(settings, choice.Amount);

        _lastProcess = _activity.LastOutcome?.Process;
        IsIgnored = HasLastProcess && TrayQuickSettings.IsIgnored(settings, _lastProcess!);
        Raise(nameof(HasLastProcess));
        Raise(nameof(IgnoreLabel));

        Footer = LastAction();
    }

    /// <summary>The trigger as the recorder and the log spell it, or the raw text when the file is not valid.</summary>
    private string Describe(TriggerSettings? trigger)
    {
        if (trigger is null)
            return "None set";

        try
        {
            // ToDefinition is what the hook is built from, so its DisplayName is the trigger's real name.
            return trigger.ToDefinition(_systemInput.DoubleClickTimeMs).DisplayName;
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException or ArgumentException)
        {
            return trigger.Keys ?? trigger.Mouse?.ToString() ?? "Not valid";
        }
    }

    /// <summary>"Zoomed in brave · 2 minutes ago", or what to say before anything has happened.</summary>
    private string LastAction()
    {
        if (_activity.LastOutcome is not { } outcome || _activity.LastOutcomeAt is not { } when)
            return _activity.LastTrigger is null ? "No press seen yet" : "A press arrived, but nothing was zoomed";

        return string.Create(CultureInfo.CurrentCulture, $"{Summarise(outcome)} · {Ago(DateTimeOffset.UtcNow - when)}");
    }

    private static string Summarise(ZoomOutcome outcome) =>
        outcome.Process is { Length: > 0 } process
            ? string.Create(CultureInfo.CurrentCulture, $"{outcome.Action} in {process}")
            : outcome.Action.ToString();

    private static string Ago(TimeSpan idle) => idle switch
    {
        { TotalSeconds: < 60 } => "just now",
        { TotalMinutes: < 60 } => string.Create(CultureInfo.CurrentCulture, $"{(int)idle.TotalMinutes} minute{Plural((int)idle.TotalMinutes)} ago"),
        { TotalHours: < 24 } => string.Create(CultureInfo.CurrentCulture, $"{(int)idle.TotalHours} hour{Plural((int)idle.TotalHours)} ago"),
        _ => string.Create(CultureInfo.CurrentCulture, $"{(int)idle.TotalDays} day{Plural((int)idle.TotalDays)} ago"),
    };

    private static string Plural(int count) => count == 1 ? string.Empty : "s";

    private void Toggle()
    {
        var wanted = !_triggers.Enabled;
        Run(() => _applier.SetEnabledAsync(wanted));
    }

    private void SetAmount(object? parameter)
    {
        if (parameter is not ZoomAmountChoice choice)
            return;

        Run(() => _applier.ApplyAsync(TrayQuickSettings.WithZoomAmount(_holder.Current, choice.Amount)));
    }

    private void Ignore()
    {
        if (_lastProcess is not { Length: > 0 } process)
            return;

        var wanted = !IsIgnored;
        Run(() => _applier.ApplyAsync(TrayQuickSettings.WithIgnored(_holder.Current, process, wanted)));
    }

    /// <summary>
    /// Records the trigger again, starting from the one in force. The recorder is modal on the UI thread and
    /// takes activation, which dismisses the panel, so the panel is asked to close before it opens.
    /// </summary>
    private void Record()
    {
        var settings = _holder.Current;
        DismissRequested?.Invoke(this, EventArgs.Empty);

        TriggerSettings captured;

        // The recorder listens for the very input that would otherwise zoom whatever is behind it.
        var wasEnabled = _triggers.Enabled;
        try
        {
            _triggers.Enabled = false;
            var recorder = new TriggerRecorderWindow(
                _triggers, _systemInput.DoubleClickTimeMs, TrayQuickSettings.FirstTrigger(settings));

            if (recorder.ShowDialog() != true)
                return;

            captured = recorder.Result;
        }
        finally
        {
            _triggers.Enabled = wasEnabled;
        }

        Run(() => _applier.ApplyAsync(TrayQuickSettings.WithFirstTrigger(settings, captured)));
    }

    /// <summary>
    /// Applies a change off the UI thread, because the applier's gate may be held by a zoom in flight, and
    /// re-reads everything afterwards so the panel shows what actually took effect rather than what was asked.
    /// </summary>
    private void Run(Func<Task<SettingsApplyResult>> change)
    {
        _busy = true;
        CommandManager.InvalidateRequerySuggested();

        _ = Task.Run(async () =>
        {
            try
            {
                var result = await change().ConfigureAwait(false);
                if (!result.InForce)
                    LogRejected(string.Join("; ", result.Problems.Select(p => p.ToString())));
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                LogChangeFailed(ex);
            }

            await _dispatcher.BeginInvoke(() =>
            {
                _busy = false;
                Refresh();
                CommandManager.InvalidateRequerySuggested();
            });
        });
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "A change made in the SmartZoom panel failed.")]
    private partial void LogChangeFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A change made in the SmartZoom panel was rejected: {Problems}")]
    private partial void LogRejected(string problems);
}
