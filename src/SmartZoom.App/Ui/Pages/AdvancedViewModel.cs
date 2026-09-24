using System.Diagnostics;
using System.Globalization;
using System.Windows.Input;
using System.Windows.Threading;

using Microsoft.Extensions.Logging;

using SmartZoom.App.Diagnostics;
using SmartZoom.App.Settings;
using SmartZoom.App.Ui.Mvvm;
using SmartZoom.Core.Diagnostics;
using SmartZoom.Core.Settings;

namespace SmartZoom.App.Ui.Pages;

/// <summary>
/// Everything a user has to go looking for: how far a zoom goes, how PDFs are zoomed, what is written to the
/// log, and the local record of what did not work.
/// </summary>
/// <remarks>
/// <para>
/// There is no Save button. Every control applies through <see cref="SettingsApplier"/> as it is moved, and
/// the page is re-read from <see cref="SettingsHolder.Current"/> afterwards, so what is on screen is what the
/// application is running. The two sliders wait for the dragging to stop first: a slider that applied on every
/// pixel would rebuild the zoom pipeline a hundred times on the way across.
/// </para>
/// <para>
/// The diagnostics section is the old Diagnostics tab. Showing the report to the person who owns the machine
/// is the whole consent mechanism — nothing here is ever sent anywhere — which is why the report is built and
/// read in place rather than copied silently by a menu item.
/// </para>
/// </remarks>
internal sealed partial class AdvancedViewModel : ObservableObject, IPageModel
{
    /// <summary>How many lines from the end of the log the report carries when asked to.</summary>
    private const int LogTailLines = 200;

    /// <summary>
    /// How long a slider has to be still before the change is applied. Long enough to cover the pause between
    /// two nudges of the arrow keys, short enough that letting go feels like it took effect at once.
    /// </summary>
    private static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(400);

    private readonly SettingsApplier _applier;
    private readonly SettingsHolder _holder;
    private readonly DiagnosticRecorder _recorder;
    private readonly IMachineFacts _facts;
    private readonly AppPaths _paths;
    private readonly ILogger<AdvancedViewModel> _logger;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly DispatcherTimer _settle;

    private double _maxScale = 3;
    private double _minScale = 1.1;
    private bool _animate = true;
    private bool _ctrlWheelFallback = true;
    private double _readerMagnification = 2;
    private bool _readerPinch = true;
    private LogLevelChoice _logLevel = LogLevelChoice.All[0];
    private bool _recordEnabled = true;
    private bool _includeLog;
    private string _report = string.Empty;
    private string _status = string.Empty;
    private bool _statusFailed;
    private bool _building;
    private IReadOnlyList<ProblemLine> _problems = [];

    /// <summary>True while the controls are being filled in, so setting one does not apply it back.</summary>
    private bool _loading;

    /// <summary>Creates the page's view model over the settings the application is running.</summary>
    /// <param name="applier">The only writer of the settings file.</param>
    /// <param name="holder">Where the settings in force live; only read.</param>
    /// <param name="recorder">What has gone wrong so far. Only ever read through a snapshot.</param>
    /// <param name="facts">What this machine is, for the report.</param>
    /// <param name="paths">Where the settings file and the logs live.</param>
    /// <param name="logger">Logger.</param>
    public AdvancedViewModel(
        SettingsApplier applier,
        SettingsHolder holder,
        DiagnosticRecorder recorder,
        IMachineFacts facts,
        AppPaths paths,
        ILogger<AdvancedViewModel> logger)
    {
        _applier = applier;
        _holder = holder;
        _recorder = recorder;
        _facts = facts;
        _paths = paths;
        _logger = logger;

        _settle = new DispatcherTimer(DispatcherPriority.Background, _dispatcher) { Interval = SettleDelay };
        _settle.Tick += (_, _) =>
        {
            _settle.Stop();
            ApplyScales();
        };

        ToggleAnimate = new RelayCommand(() => Animate = !Animate);
        ToggleCtrlWheelFallback = new RelayCommand(() => CtrlWheelFallback = !CtrlWheelFallback);
        ToggleRecording = new RelayCommand(() => RecordEnabled = !RecordEnabled);
        RefreshReport = new RelayCommand(_ => Render(announce: true), _ => !_building);
        CopyReport = new RelayCommand(_ => Copy(), _ => !_building);
        SaveReport = new RelayCommand(_ => Save(), _ => !_building);
        ClearRecord = new RelayCommand(_ => Clear(), _ => !_building);
        OpenLogFolder = new RelayCommand(() => Open(_paths.LogDirectory));
        OpenSettingsFile = new RelayCommand(() => Open(_paths.SettingsFile));

        Refresh();
        Render(announce: false);
    }

    /// <summary>Every logging level, in the order they get quieter.</summary>
    public static IReadOnlyList<LogLevelChoice> LogLevels => LogLevelChoice.All;

    /// <summary>The largest a smart zoom will ever make anything.</summary>
    public double MaxScale
    {
        get => _maxScale;
        set
        {
            if (Set(ref _maxScale, value))
                ScaleChanged(nameof(MaxScaleLabel));
        }
    }

    /// <summary>The largest zoom the way somebody would say it: "3× bigger".</summary>
    public string MaxScaleLabel => Times(_maxScale) + " bigger";

    /// <summary>Below this, a zoom is not worth the motion and the press does nothing.</summary>
    public double MinScale
    {
        get => _minScale;
        set
        {
            if (Set(ref _minScale, value))
                ScaleChanged(nameof(MinScaleLabel));
        }
    }

    /// <summary>The smallest zoom, the way somebody would say it.</summary>
    public string MinScaleLabel => Times(_minScale) + " bigger";

    /// <summary>How much a PDF reader is magnified by one press.</summary>
    public double ReaderMagnification
    {
        get => _readerMagnification;
        set
        {
            if (Set(ref _readerMagnification, value))
                ScaleChanged(nameof(ReaderMagnificationLabel));
        }
    }

    /// <summary>The reader magnification, the way somebody would say it.</summary>
    public string ReaderMagnificationLabel => Times(_readerMagnification) + " bigger";

    /// <summary>Whether zooms glide instead of jumping.</summary>
    public bool Animate
    {
        get => _animate;
        set
        {
            if (Set(ref _animate, value) && !_loading)
                Write(settings => settings.Zoom.Animate = value);
        }
    }

    /// <summary>Whether an application no strategy can handle gets a crude Ctrl+wheel zoom instead of nothing.</summary>
    public bool CtrlWheelFallback
    {
        get => _ctrlWheelFallback;
        set
        {
            if (Set(ref _ctrlWheelFallback, value) && !_loading)
                Write(settings => settings.Zoom.FallbackToCtrlWheel = value);
        }
    }

    /// <summary>Whether PDFs are zoomed by a pinch around the cursor rather than by the reader's own shortcut.</summary>
    public bool ReaderPinch
    {
        get => _readerPinch;
        set
        {
            if (!Set(ref _readerPinch, value))
                return;

            Raise(nameof(ReaderShortcuts));
            if (!_loading)
                Write(settings => settings.Zoom.Reader.Mode = value ? ReaderZoomMode.Pinch : ReaderZoomMode.Shortcuts);
        }
    }

    /// <summary>The other half of that choice; the two radio buttons are one setting.</summary>
    public bool ReaderShortcuts
    {
        get => !_readerPinch;
        set
        {
            if (value)
                ReaderPinch = false;
        }
    }

    /// <summary>The quietest level still written to the log file.</summary>
    public LogLevelChoice Level
    {
        get => _logLevel;
        set
        {
            // A ComboBox clears its selection while its list changes; there is always a level.
            if (value is null || !Set(ref _logLevel, value) || _loading)
                return;

            Write(settings => settings.Logging.Level = value.Level);
        }
    }

    /// <summary>Whether presses that zoomed nothing, adapters that threw and crashes are counted at all.</summary>
    public bool RecordEnabled
    {
        get => _recordEnabled;
        set
        {
            if (Set(ref _recordEnabled, value) && !_loading)
                Write(settings => settings.Diagnostics.Enabled = value);
        }
    }

    /// <summary>Whether the report carries the end of the log file. Opt-in, and not a setting: it is one report.</summary>
    public bool IncludeLog
    {
        get => _includeLog;
        set
        {
            if (Set(ref _includeLog, value))
                Render(announce: false);
        }
    }

    /// <summary>The report as it stands, ready to be read and then pasted into a bug report.</summary>
    public string Report
    {
        get => _report;
        private set => Set(ref _report, value);
    }

    /// <summary>What Copy or Save actually did, or when the report was built.</summary>
    public string Status
    {
        get => _status;
        private set
        {
            if (Set(ref _status, value))
                Raise(nameof(HasStatus));
        }
    }

    /// <summary>Whether there is a status line to show.</summary>
    public bool HasStatus => _status.Length > 0;

    /// <summary>
    /// Whether that line is reporting a failure. A clipboard held open by another process must never look
    /// identical to a successful copy: reading the report is the consent step, so the user has to be able to
    /// tell whether the thing they are about to paste made it anywhere.
    /// </summary>
    public bool StatusFailed
    {
        get => _statusFailed;
        private set => Set(ref _statusFailed, value);
    }

    /// <summary>Whether the report is being built right now, which disables everything that acts on it.</summary>
    public bool IsBuilding
    {
        get => _building;
        private set
        {
            if (Set(ref _building, value))
                CommandManager.InvalidateRequerySuggested();
        }
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

    /// <summary>Whether there is anything to show about the last change.</summary>
    public bool HasProblems => _problems.Count > 0;

    /// <summary>Flips <see cref="Animate"/>; the switch asks rather than flipping itself.</summary>
    public ICommand ToggleAnimate { get; }

    /// <summary>Flips <see cref="CtrlWheelFallback"/>.</summary>
    public ICommand ToggleCtrlWheelFallback { get; }

    /// <summary>Flips <see cref="RecordEnabled"/>.</summary>
    public ICommand ToggleRecording { get; }

    /// <summary>Rebuilds the report from the record as it stands now.</summary>
    public ICommand RefreshReport { get; }

    /// <summary>Puts the report on the clipboard.</summary>
    public ICommand CopyReport { get; }

    /// <summary>Writes the report to a file the user chooses.</summary>
    public ICommand SaveReport { get; }

    /// <summary>Throws away everything recorded so far.</summary>
    public ICommand ClearRecord { get; }

    /// <summary>Opens the folder the log files are written to.</summary>
    public ICommand OpenLogFolder { get; }

    /// <summary>Opens the settings file, for everything this page deliberately leaves out.</summary>
    public ICommand OpenSettingsFile { get; }

    /// <inheritdoc />
    public void Refresh()
    {
        var settings = _holder.Current;

        _loading = true;
        try
        {
            MaxScale = settings.Zoom.MaxScale;
            MinScale = settings.Zoom.MinScale;
            Animate = settings.Zoom.Animate;
            CtrlWheelFallback = settings.Zoom.FallbackToCtrlWheel;
            ReaderMagnification = settings.Zoom.Reader.Magnification;
            ReaderPinch = settings.Zoom.Reader.Mode != ReaderZoomMode.Shortcuts;
            Level = LogLevels.FirstOrDefault(c => c.Level == settings.Logging.Level) ?? LogLevels[0];
            RecordEnabled = settings.Diagnostics.Enabled;
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>A zoom factor the way a person says it: "3×", "2.5×".</summary>
    private static string Times(double scale) => string.Create(CultureInfo.CurrentCulture, $"{scale:0.##}×");

    /// <summary>
    /// A slider moved. The label follows at once, because it is what the slider is read from; the settings
    /// wait for the dragging to stop.
    /// </summary>
    private void ScaleChanged(string label)
    {
        Raise(label);
        if (_loading)
            return;

        _settle.Stop();
        _settle.Start();
    }

    private void ApplyScales() => Write(settings =>
    {
        settings.Zoom.MaxScale = _maxScale;
        settings.Zoom.MinScale = _minScale;
        settings.Zoom.Reader.Magnification = _readerMagnification;
    });

    /// <summary>Makes a change on a copy of the settings in force and puts it through the applier.</summary>
    private void Write(Action<SmartZoomSettings> change)
    {
        var settings = SettingsStore.Clone(_holder.Current);
        change(settings);
        Apply(settings);
    }

    /// <summary>
    /// Puts a changed copy into force off the UI thread — the applier's gate may be held by a zoom in flight —
    /// and then re-reads the page, so it shows what took effect rather than what was asked for.
    /// </summary>
    private void Apply(SmartZoomSettings settings)
    {
        Problems = [];

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
                problems = ProblemLine.Error("Advanced: " + ex.Message);
            }

            await _dispatcher.BeginInvoke(() =>
            {
                Refresh();
                Problems = problems;
            });
        });
    }

    /// <summary>
    /// Builds the report off the UI thread: the log tail and the settings file are disk reads, and the display
    /// facts are hardware queries. The buttons that act on the report wait until it is there.
    /// </summary>
    /// <param name="announce">Whether to put the build time in the status line.</param>
    private void Render(bool announce)
    {
        var includeLog = _includeLog;
        IsBuilding = true;

        _ = Task.Run(async () =>
        {
            string? text = null;
            string? failure = null;
            try
            {
                var tail = includeLog ? LogTailOrNull() : null;
                text = DiagnosticReport.Render(_recorder.Snapshot(), _facts, ReadSettingsJson(), tail, Redact);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                LogReportFailed(ex);
                failure = ex.Message;
            }

            await _dispatcher.BeginInvoke(() =>
            {
                if (text is not null)
                {
                    Report = text;
                    if (announce)
                        ShowStatus($"Report built at {DateTime.Now.ToString("HH:mm:ss", CultureInfo.CurrentCulture)}.", failed: false);
                }
                else
                {
                    ShowStatus($"Couldn't build the report: {failure}", failed: true);
                }

                IsBuilding = false;
            });
        });
    }

    /// <summary>Guarded because a clipboard held open by another process must not become a recorded crash.</summary>
    private void Copy()
    {
        try
        {
            System.Windows.Clipboard.SetText(_report);
            ShowStatus("Copied to the clipboard.", failed: false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            ShowStatus($"Couldn't copy to the clipboard: {ex.Message}", failed: true);
        }
    }

    /// <summary>Guarded for the same reason as <see cref="Copy"/>: a bad path or a permission error must not
    /// reach the app-wide handler and be recorded as a crash.</summary>
    private void Save()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog { FileName = "smartzoom-diagnostics.md", Filter = "Markdown|*.md" };
        if (dialog.ShowDialog() != true)
            return;

        try
        {
            File.WriteAllText(dialog.FileName, _report);
            ShowStatus($"Saved to {dialog.FileName}.", failed: false);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            ShowStatus($"Couldn't save: {ex.Message}", failed: true);
        }
    }

    private void Clear()
    {
        _recorder.Clear();
        ShowStatus("The recorded data was cleared.", failed: false);
        Render(announce: false);
    }

    private static string Redact(string text) => DiagnosticText.Redact(text);

    private void ShowStatus(string text, bool failed)
    {
        Status = text;
        StatusFailed = failed;
    }

    /// <summary>Reads the settings file for the report. A missing or locked file yields an empty object, not a crash.</summary>
    private string ReadSettingsJson()
    {
        try
        {
            return File.Exists(_paths.SettingsFile) ? File.ReadAllText(_paths.SettingsFile) : "{}";
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return "{}";
        }
    }

    /// <summary>The end of the newest log file, or null when the log can't be read for any reason.</summary>
    private string? LogTailOrNull()
    {
        try
        {
            var newest = Directory.EnumerateFiles(_paths.LogDirectory, "*.log")
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .FirstOrDefault();

            return newest is null ? null : LogTail.ReadLast(newest.FullName, LogTailLines);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null;
        }
    }

    private void Open(string path)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            LogOpenFailed(ex, path);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "A change made on the Advanced page failed.")]
    private partial void LogChangeFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The diagnostic report could not be built.")]
    private partial void LogReportFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to open {Path}.")]
    private partial void LogOpenFailed(Exception exception, string path);
}
