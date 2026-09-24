using System.Globalization;
using System.Windows.Input;
using System.Windows.Threading;

using Microsoft.Extensions.Logging;

using SmartZoom.App.Settings;
using SmartZoom.App.Ui.Mvvm;
using SmartZoom.App.Ui.Recorder;
using SmartZoom.Core.Input;
using SmartZoom.Core.Settings;

namespace SmartZoom.App.Ui.Pages;

/// <summary>The gestures that start a zoom: what they are, what they do, and how to change them.</summary>
/// <remarks>
/// <para>
/// A trigger is recorded rather than described: <see cref="TriggerRecorderWindow"/> asks the user to perform
/// the gesture, which is the only instruction that needs no knowledge of what Windows calls the button behind
/// the scroll wheel. Add and Edit open the same window; Edit opens it filled in.
/// </para>
/// <para>
/// Every change goes out through <see cref="SettingsApplier"/>, the only writer of the settings file, and the
/// list is re-read from <see cref="SettingsHolder.Current"/> afterwards — so what is on screen is what the
/// hook is actually running, not what was asked for. Whatever the check has to say about the change is shown
/// under the list instead of being thrown away.
/// </para>
/// </remarks>
internal sealed partial class TriggersViewModel : ObservableObject, IPageModel
{
    private readonly ITriggerSource _triggers;
    private readonly SettingsApplier _applier;
    private readonly SettingsHolder _holder;
    private readonly ISystemInput _systemInput;
    private readonly ILogger<TriggersViewModel> _logger;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;

    private IReadOnlyList<TriggerRow> _rows = [];
    private IReadOnlyList<ProblemLine> _problems = [];
    private bool _busy;

    /// <summary>Creates the page's view model over the triggers the hook is running.</summary>
    /// <param name="triggers">Silenced while a trigger is being recorded.</param>
    /// <param name="applier">The only writer of the settings file.</param>
    /// <param name="holder">Where the settings in force live; only read.</param>
    /// <param name="systemInput">Supplies the double-tap window a trigger that sets none falls back to.</param>
    /// <param name="logger">Logger.</param>
    public TriggersViewModel(
        ITriggerSource triggers,
        SettingsApplier applier,
        SettingsHolder holder,
        ISystemInput systemInput,
        ILogger<TriggersViewModel> logger)
    {
        _triggers = triggers;
        _applier = applier;
        _holder = holder;
        _systemInput = systemInput;
        _logger = logger;

        AddTrigger = new RelayCommand(_ => Add(), _ => !_busy);
        EditTrigger = new RelayCommand(Edit, _ => !_busy);
        RemoveTrigger = new RelayCommand(Remove, _ => !_busy);

        Refresh();
    }

    /// <summary>The configured triggers, in the order the settings file holds them.</summary>
    public IReadOnlyList<TriggerRow> Triggers
    {
        get => _rows;
        private set => Set(ref _rows, value);
    }

    /// <summary>What the last change was told about itself; empty when there was nothing to say.</summary>
    public IReadOnlyList<ProblemLine> Problems
    {
        get => _problems;
        private set
        {
            if (Set(ref _problems, value))
                Raise(nameof(HasProblems));
        }
    }

    /// <summary>Whether there is anything under the list to show.</summary>
    public bool HasProblems => _problems.Count > 0;

    /// <summary>Records a new trigger and adds it.</summary>
    public ICommand AddTrigger { get; }

    /// <summary>Records over an existing trigger; the parameter is its <see cref="TriggerRow"/>.</summary>
    public ICommand EditTrigger { get; }

    /// <summary>Removes a trigger; the parameter is its <see cref="TriggerRow"/>.</summary>
    public ICommand RemoveTrigger { get; }

    /// <inheritdoc />
    public void Refresh()
    {
        var triggers = _holder.Current.Triggers;
        Triggers = [.. triggers.Select((trigger, index) => new TriggerRow(index, Describe(trigger), Behaviour(trigger)))];
    }

    private void Add()
    {
        if (Record(existing: null) is not { } captured)
            return;

        var settings = SettingsStore.Clone(_holder.Current);
        settings.Triggers.Add(captured);
        Apply(settings);
    }

    private void Edit(object? parameter)
    {
        if (parameter is not TriggerRow row)
            return;

        var settings = SettingsStore.Clone(_holder.Current);
        if (row.Index < 0 || row.Index >= settings.Triggers.Count)
            return;

        if (Record(settings.Triggers[row.Index]) is not { } captured)
            return;

        settings.Triggers[row.Index] = captured;
        Apply(settings);
    }

    private void Remove(object? parameter)
    {
        if (parameter is not TriggerRow row)
            return;

        var settings = SettingsStore.Clone(_holder.Current);
        if (row.Index < 0 || row.Index >= settings.Triggers.Count)
            return;

        // Refused here rather than by the check after the fact, and said out loud: an application with no
        // trigger cannot be started by anything, and a list that silently emptied itself would look like a bug.
        if (settings.Triggers.Count == 1)
        {
            Problems = ProblemLine.Error(
                "Triggers: this is the only way to start a zoom. Add another trigger before removing this one.");
            return;
        }

        settings.Triggers.RemoveAt(row.Index);
        Apply(settings);
    }

    /// <summary>
    /// Shows the recorder and returns what was pressed, or null when it was cancelled. Modal on the UI thread:
    /// the window it belongs to is the one the user is looking at, and the change that follows goes off-thread.
    /// </summary>
    private TriggerSettings? Record(TriggerSettings? existing)
    {
        // The recorder listens for the very input that would otherwise zoom whatever is behind it. It silences
        // the trigger source itself; this restores whatever it found, even if showing it threw.
        var wasEnabled = _triggers.Enabled;
        try
        {
            var recorder = new TriggerRecorderWindow(_triggers, _systemInput.DoubleClickTimeMs, existing);
            return recorder.ShowDialog() == true ? recorder.Result : null;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            LogRecorderFailed(ex);
            Problems = ProblemLine.Error("Triggers: the recorder could not be opened.");
            return null;
        }
        finally
        {
            _triggers.Enabled = wasEnabled;
        }
    }

    /// <summary>
    /// Puts a changed copy into force off the UI thread — the applier's gate may be held by a zoom in flight —
    /// and then re-reads the list, so it shows what took effect rather than what was asked for.
    /// </summary>
    private void Apply(SmartZoomSettings settings)
    {
        _busy = true;
        Problems = [];
        CommandManager.InvalidateRequerySuggested();

        _ = Task.Run(async () =>
        {
            IReadOnlyList<ProblemLine> problems;
            try
            {
                var result = await _applier.ApplyAsync(settings).ConfigureAwait(false);
                problems = ProblemLine.From(result.Problems);
                if (result.Outcome == SettingsApplyOutcome.AppliedButNotSaved)
                {
                    problems =
                    [
                        .. problems,
                        new ProblemLine(
                            "Settings file: the change is in force but could not be written, so it will be lost on restart.",
                            IsError: true),
                    ];
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                LogChangeFailed(ex);
                problems = ProblemLine.Error("Triggers: " + ex.Message);
            }

            await _dispatcher.BeginInvoke(() =>
            {
                _busy = false;
                Refresh();
                Problems = problems;
                CommandManager.InvalidateRequerySuggested();
            });
        });
    }

    /// <summary>The trigger as the recorder and the log spell it, or the raw text when the file is not valid.</summary>
    private string Describe(TriggerSettings trigger)
    {
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

    /// <summary>What a press of it does, in the two facts a user can act on.</summary>
    private string Behaviour(TriggerSettings trigger)
    {
        var presses = trigger.TapCount == 2
            ? string.Create(CultureInfo.CurrentCulture, $"Double-tap within {trigger.DoubleTapWindowMs ?? _systemInput.DoubleClickTimeMs} ms")
            : "Every press";

        return presses + (trigger.SwallowClicks ? " · the app never sees it" : " · the app still sees it");
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "The trigger recorder could not be opened.")]
    private partial void LogRecorderFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "A trigger change from the settings window failed.")]
    private partial void LogChangeFailed(Exception exception);
}
