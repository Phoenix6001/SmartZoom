using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Windows.Input;
using System.Windows.Threading;

using Microsoft.Extensions.Logging;

using SmartZoom.App.Diagnostics;
using SmartZoom.App.Settings;
using SmartZoom.App.Ui.Applications;
using SmartZoom.App.Ui.Mvvm;
using SmartZoom.Core.Diagnostics;
using SmartZoom.Core.Settings;

namespace SmartZoom.App.Ui.Pages;

/// <summary>
/// The local record of what did not work, and the report built from it.
/// </summary>
/// <remarks>
/// <para>
/// A page rather than a section of Advanced, because two other surfaces send people here: the shell's status
/// card counts recorded issues and the tray offers "Diagnostic report…". A page the application points at is
/// not one the user goes looking for, and it used to sit below three sections of tuning they had to scroll past.
/// </para>
/// <para>
/// Showing the report to the person who owns the machine is the whole consent mechanism — nothing here is ever
/// sent anywhere — which is why it is built and read in place rather than copied silently by a menu item.
/// </para>
/// </remarks>
internal sealed partial class DiagnosticsViewModel : ObservableObject, IPageModel
{
    /// <summary>How many lines from the end of the log the report carries when asked to.</summary>
    private const int LogTailLines = 200;

    /// <summary>
    /// How many kinds of problem are listed before the rest go behind "show more". Grouping already bounds the
    /// list to a handful, so this is usually not reached at all.
    /// </summary>
    private const int MostIssuesShown = 5;

    /// <summary>
    /// The bug form, straight to the template rather than the chooser: this button is for one kind of report.
    /// SmartZoom opens no connection of its own — the browser does, which is what keeps SECURITY.md true.
    /// </summary>
    private const string BugFormUrl = "https://github.com/Phoenix6001/SmartZoom/issues/new?template=bug_report.yml";

    /// <summary>
    /// How long the pre-filled address may get. GitHub's issue forms take their initial values from the query
    /// string, and a long enough URL is answered with 414 rather than a form — so a field that would not fit is
    /// left out and falls back to the clipboard. The report without its log is about 2 KB, four once escaped,
    /// which fits; the log is thirty and never will.
    /// </summary>
    private const int MostUrlCharacters = 8000;


    /// <summary>
    /// Mouse software that re-emits buttons as injected input, which the form asks about because it is the
    /// single most common reason a press never reaches SmartZoom at all.
    /// </summary>
    /// <remarks>
    /// Best effort by design: a vendor not on this list simply leaves the line out, which is no worse than the
    /// empty box it replaces. It is never used to decide anything, only to save the reporter a question.
    /// </remarks>
    private static readonly (string Process, string Name)[] VendorMouseSoftware =
    [
        ("logioptionsplus_agent", "Logi Options+"),
        ("LogiOptionsMgr", "Logitech Options"),
        ("LCore", "Logitech Gaming Software"),
        ("Razer Synapse Service Process", "Razer Synapse"),
        ("SteelSeriesEngine", "SteelSeries GG"),
        ("CorsairIcue", "Corsair iCUE"),
    ];

    private readonly SettingsApplier _applier;
    private readonly SettingsHolder _holder;
    private readonly DiagnosticRecorder _recorder;
    private readonly IMachineFacts _facts;
    private readonly AppPaths _paths;
    private readonly ILogger<DiagnosticsViewModel> _logger;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;

    private bool _recordEnabled = true;
    private bool _includeLog;
    private string _report = string.Empty;
    private string _status = string.Empty;
    private bool _statusFailed;
    private bool _building;
    private IReadOnlyList<ProblemLine> _problems = [];
    private IReadOnlyList<DiagnosticIssue> _all = [];
    private IReadOnlyList<DiagnosticIssue> _issues = [];
    private string _overflow = string.Empty;
    private bool _expanded;

    /// <summary>True while the controls are being filled in, so setting one does not apply it back.</summary>
    private bool _loading;

    /// <summary>Creates the page's view model over the settings the application is running.</summary>
    /// <param name="applier">The only writer of the settings file.</param>
    /// <param name="holder">Where the settings in force live; only read.</param>
    /// <param name="recorder">What has gone wrong so far. Only ever read through a snapshot.</param>
    /// <param name="facts">What this machine is, for the report.</param>
    /// <param name="paths">Where the settings file and the logs live.</param>
    /// <param name="logger">Logger.</param>
    public DiagnosticsViewModel(
        SettingsApplier applier,
        SettingsHolder holder,
        DiagnosticRecorder recorder,
        IMachineFacts facts,
        AppPaths paths,
        ILogger<DiagnosticsViewModel> logger)
    {
        _applier = applier;
        _holder = holder;
        _recorder = recorder;
        _facts = facts;
        _paths = paths;
        _logger = logger;

        ToggleRecording = new RelayCommand(() => RecordEnabled = !RecordEnabled);
        ToggleIssues = new RelayCommand(() =>
        {
            _expanded = !_expanded;
            Show();
        });
        RefreshReport = new RelayCommand(_ => Render(announce: true), _ => !_building);
        CopyReport = new RelayCommand(_ => Copy(), _ => !_building);
        SaveReport = new RelayCommand(_ => Save(), _ => !_building);
        ClearRecord = new RelayCommand(_ => Clear(), _ => !_building);
        ReportProblem = new RelayCommand(_ => StartReport(), _ => !_building);

        Refresh();
        Render(announce: false);
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

    /// <summary>
    /// What has gone wrong, in sentences, above the report. The status card counts these; this is where the
    /// count becomes something you can act on without reading a table of enum names.
    /// </summary>
    public IReadOnlyList<DiagnosticIssue> Issues
    {
        get => _issues;
        private set
        {
            if (Set(ref _issues, value))
                Raise(nameof(HasIssues));
        }
    }

    /// <summary>Whether anything has been recorded, which is what chooses between the list and the all-clear.</summary>
    public bool HasIssues => _issues.Count > 0;

    /// <summary>
    /// What the list above left out: kinds of problem past <see cref="MostIssuesShown"/>, and events the record
    /// itself stopped counting once it hit its key ceiling. Empty when it left nothing out.
    /// </summary>
    public string Overflow
    {
        get => _overflow;
        private set
        {
            if (Set(ref _overflow, value))
                Raise(nameof(HasOverflow));
        }
    }

    /// <summary>Whether there is an overflow line to show.</summary>
    public bool HasOverflow => _overflow.Length > 0;

    /// <summary>Whether there are more issues than are being shown, which is what puts the link there.</summary>
    public bool HasMore => _all.Count > MostIssuesShown;

    /// <summary>What that link says: how many are hidden, or how to fold them away again.</summary>
    public string MoreLabel => _expanded
        ? "Show fewer"
        : string.Create(CultureInfo.CurrentCulture, $"Show {_all.Count - MostIssuesShown} more");

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

    /// <summary>Flips <see cref="RecordEnabled"/>; the switch asks rather than flipping itself.</summary>
    public ICommand ToggleRecording { get; }

    /// <summary>Shows the rest of the issues, or folds them away again.</summary>
    public ICommand ToggleIssues { get; }

    /// <summary>Rebuilds the report from the record as it stands now.</summary>
    public ICommand RefreshReport { get; }

    /// <summary>Puts the report on the clipboard.</summary>
    public ICommand CopyReport { get; }

    /// <summary>Writes the report to a file the user chooses.</summary>
    public ICommand SaveReport { get; }

    /// <summary>Throws away everything recorded so far.</summary>
    public ICommand ClearRecord { get; }

    /// <summary>
    /// Puts the whole report, log included, on the clipboard and opens the bug form ready to paste it into.
    /// </summary>
    public ICommand ReportProblem { get; }

    /// <inheritdoc />
    public void Refresh()
    {
        _loading = true;
        try
        {
            RecordEnabled = _holder.Current.Diagnostics.Enabled;
        }
        finally
        {
            _loading = false;
        }
    }

    private static string Redact(string text) => DiagnosticText.Redact(text);

    /// <summary>
    /// The line saying what the record itself stopped counting once it hit its key ceiling, or nothing when it
    /// counted everything. Unlike the issues behind "show more", these cannot be shown: they were never kept.
    /// </summary>
    /// <param name="omitted">Distinct events the record stopped counting.</param>
    private static string Overflowed(int omitted) => omitted == 0
        ? string.Empty
        : string.Create(
            CultureInfo.CurrentCulture,
            $"{omitted} further distinct event{(omitted == 1 ? " was" : "s were")} not counted, because the record holds only so many kinds at once.");

    /// <summary>Publishes as much of the list as is being shown, and the state of the link under it.</summary>
    private void Show()
    {
        Issues = _expanded || _all.Count <= MostIssuesShown ? _all : [.. _all.Take(MostIssuesShown)];
        Raise(nameof(HasMore));
        Raise(nameof(MoreLabel));
    }

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
                problems = ProblemLine.Error("Diagnostics: " + ex.Message);
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
    /// <param name="then">Run on the UI thread once the report is on screen, or not at all if it failed.</param>
    private void Render(bool announce, Action? then = null)
    {
        var includeLog = _includeLog;
        IsBuilding = true;

        _ = Task.Run(async () =>
        {
            string? text = null;
            string? failure = null;
            IReadOnlyList<DiagnosticIssue> issues = [];
            var overflow = string.Empty;
            try
            {
                var record = _recorder.Snapshot();
                var now = DateTimeOffset.Now;

                // Grouped over the application, which is the only part of a key that grows with the machine:
                // ten applications that all found nothing to magnify are one finding, not ten lines.
                issues = DiagnosticIssue.Summarise(record.Counters, now);
                overflow = Overflowed(record.OmittedKeys);

                var tail = includeLog ? LogTailOrNull() : null;
                text = DiagnosticReport.Render(record, _facts, ReadSettingsJson(), tail, Redact);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                LogReportFailed(ex);
                failure = ex.Message;
            }

            await _dispatcher.BeginInvoke(() =>
            {
                _all = issues;
                _expanded = false;
                Show();
                Overflow = overflow;
                if (text is not null)
                {
                    Report = text;
                    then?.Invoke();
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

    /// <summary>
    /// One click from "something is wrong" to a filled-in bug report.
    /// </summary>
    /// <remarks>
    /// The log goes in, because a report without one usually costs a round trip — but it is ticked visibly and
    /// the report on screen is rebuilt to include it BEFORE anything is copied, because showing the report is
    /// the whole consent mechanism and a button that quietly widened what gets shared would defeat it.
    /// </remarks>
    private void StartReport()
    {
        if (_includeLog)
        {
            Hand();
            return;
        }

        _includeLog = true;
        Raise(nameof(IncludeLog));
        Render(announce: false, then: Hand);
    }

    /// <summary>
    /// The bug form with everything this machine already knows filled in: the build, the display, the
    /// application the top issue names, and that issue as a starting description.
    /// </summary>
    /// <remarks>
    /// The report and the log are not among them and cannot be: they are tens of kilobytes and a query string
    /// is not. Those two are what the clipboard is for, which is also why the user sees them first.
    /// </remarks>
    private string FormUrl()
    {
        var fields = new List<(string Field, string Value)>(4);

        if (_facts.AppVersion is { Length: > 0 } version)
            fields.Add(("version", version));

        if (_facts.GetDisplays().FirstOrDefault(d => d.Primary) is { } display)
        {
            fields.Add((
                "display",
                string.Create(CultureInfo.CurrentCulture, $"{display.Width}x{display.Height} at {display.Scale * 100:0}%")));
        }

        // The issue at the top of the list is the one the report is most likely about.
        if (_issues.Count > 0)
        {
            var issue = _issues[0];
            if (issue.Application is { Length: > 0 } application)
                fields.Add(("application", InstalledApplications.Describe(application).DisplayName));

            // A title somebody can scan in a list, and a description that is a starting point, not a substitute
            // for what the user meant to say: they know what they pointed at and SmartZoom does not.
            fields.Add(("title", issue.Headline));
            fields.Add(("what-happened", issue.Headline + "." + Environment.NewLine + issue.Detail));
        }

        if (Context() is { Length: > 0 } context)
            fields.Add(("anything-else", context));

        var url = new StringBuilder(BugFormUrl);
        foreach (var (field, value) in fields)
        {
            var addition = $"&{field}={Uri.EscapeDataString(value)}";

            // Dropped rather than truncated: half a sentence in a bug report is worse than an empty box.
            if (url.Length + addition.Length <= MostUrlCharacters)
                url.Append(addition);
        }

        return url.ToString();
    }

    /// <summary>
    /// The three things the form's "anything else" box asks about that this machine can answer for itself:
    /// how many displays there are, whether they are scaled differently from one another, and whether any
    /// vendor mouse software is running.
    /// </summary>
    /// <returns>A line per fact, or empty when there is nothing unusual to say.</returns>
    private string Context()
    {
        var lines = new List<string>(3);
        var displays = _facts.GetDisplays();

        if (displays.Count > 1)
        {
            var sizes = string.Join(", ", displays.Select(d =>
                string.Create(CultureInfo.CurrentCulture, $"{d.Width}x{d.Height} at {d.Scale * 100:0}%")));
            lines.Add(string.Create(CultureInfo.CurrentCulture, $"{displays.Count} displays: {sizes}"));

            // Called out rather than left to be noticed: several measured constants assume one scale factor.
            if (displays.Select(d => d.Scale).Distinct().Count() > 1)
                lines.Add("The displays are scaled differently from one another.");
        }

        var vendors = VendorMouseSoftware
            .Where(v => Process.GetProcessesByName(v.Process).Length > 0)
            .Select(v => v.Name)
            .ToList();

        if (vendors.Count > 0)
            lines.Add(string.Join(" and ", vendors) + " is running.");

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>Copies the report as it now stands and opens the form to paste it into.</summary>
    private void Hand()
    {
        var url = FormUrl();

        try
        {
            System.Windows.Clipboard.SetText(_report);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // With nothing on the clipboard there is nothing to paste, so the browser would only confuse.
            ShowStatus($"Couldn't copy to the clipboard: {ex.Message}", failed: true);
            return;
        }


        try
        {
            using var browser = Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            ShowStatus(
                "The form is filled in. The report, log included, is on your clipboard — press Ctrl+V in the “Diagnostic report” box.",
                failed: false);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            LogOpenFailed(ex, BugFormUrl);
            ShowStatus("Copied, but the browser would not open. The form is at " + BugFormUrl, failed: false);
        }
    }

    private void Clear()
    {
        _recorder.Clear();
        ShowStatus("The recorded data was cleared.", failed: false);
        Render(announce: false);
    }

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

    [LoggerMessage(Level = LogLevel.Error, Message = "A change made on the Diagnostics page failed.")]
    private partial void LogChangeFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The diagnostic report could not be built.")]
    private partial void LogReportFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to open {Url}.")]
    private partial void LogOpenFailed(Exception exception, string url);
}
