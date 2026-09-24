using System.Globalization;
using System.Windows.Input;
using System.Windows.Threading;

using Microsoft.Extensions.Logging;

using SmartZoom.App.Diagnostics;
using SmartZoom.App.Hosting;
using SmartZoom.App.Settings;
using SmartZoom.App.Ui.Mvvm;
using SmartZoom.App.Ui.Pages;
using SmartZoom.App.Ui.Theming;
using SmartZoom.Core.Input;
using SmartZoom.Core.Settings;

namespace SmartZoom.App.Ui.Shell;

/// <summary>The window around the pages: the rail, its status card, the search box and the theme button.</summary>
/// <remarks>
/// Holds no copy of anything. The status card is read from the trigger source, the diagnostics record and
/// <see cref="ZoomActivity"/> every time <see cref="Refresh"/> runs, so it tells the truth about the running
/// application rather than about the moment the window was opened.
/// </remarks>
internal sealed partial class ShellViewModel : ObservableObject
{
    private const string GlyphSun = "";
    private const string GlyphMoon = "";

    private readonly ITriggerSource _triggers;
    private readonly DiagnosticRecorder _recorder;
    private readonly ZoomActivity _activity;
    private readonly SettingsApplier _applier;
    private readonly ThemeManager _theme;
    private readonly IReadOnlyList<IPageModel> _pages;
    private readonly ILogger<ShellViewModel> _logger;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;

    private NavigationItem _selected;
    private string _searchText = string.Empty;
    private string _statusHeadline = string.Empty;
    private string _statusDetail = string.Empty;
    private bool _hasIssues;

    /// <summary>Creates the shell over the pages it shows.</summary>
    /// <param name="sections">The rail's rows, in order, each carrying its page.</param>
    /// <param name="pages">
    /// The view models behind those rows, so the window can re-read every page at once. A page that showed
    /// what it was built with rather than what the app is running would disagree with the tray panel, the
    /// tray menu and the settings file as soon as any of them changed something.
    /// </param>
    /// <param name="overview">The landing page's view model; its chevrons navigate this rail.</param>
    /// <param name="about">The About page's view model; its links navigate this rail.</param>
    /// <param name="triggers">The truth about whether zooming is on.</param>
    /// <param name="recorder">What has gone wrong so far, for the status card's second line.</param>
    /// <param name="activity">Whether a trigger has ever reached SmartZoom.</param>
    /// <param name="applier">The only writer of the settings file; carries the appearance choice.</param>
    /// <param name="theme">The palette in force.</param>
    /// <param name="logger">Logger.</param>
    public ShellViewModel(
        IReadOnlyList<NavigationItem> sections,
        IReadOnlyList<IPageModel> pages,
        OverviewViewModel overview,
        AboutViewModel about,
        ITriggerSource triggers,
        DiagnosticRecorder recorder,
        ZoomActivity activity,
        SettingsApplier applier,
        ThemeManager theme,
        ILogger<ShellViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(sections);
        ArgumentNullException.ThrowIfNull(pages);
        ArgumentNullException.ThrowIfNull(overview);
        ArgumentNullException.ThrowIfNull(about);
        ArgumentNullException.ThrowIfNull(theme);

        Sections = sections;
        _pages = pages;
        _selected = sections[0];
        _triggers = triggers;
        _recorder = recorder;
        _activity = activity;
        _applier = applier;
        _theme = theme;
        _logger = logger;

        ShowIssues = new RelayCommand(_ => GoTo(NavigationSection.Advanced));
        overview.NavigationRequested += (_, section) => GoTo(section);
        about.NavigationRequested += (_, section) => GoTo(section);

        CycleTheme = new RelayCommand(NextTheme);
        _theme.Changed += (_, _) => RaiseTheme();

        Refresh();
    }

    /// <summary>The rail's rows, in the order they are shown.</summary>
    public IReadOnlyList<NavigationItem> Sections { get; }

    /// <summary>Cycles the appearance: light, then dark, then whatever Windows is using.</summary>
    public ICommand CycleTheme { get; }

    /// <summary>The row the rail has selected; the content area shows its page.</summary>
    public NavigationItem Selected
    {
        get => _selected;
        set
        {
            // A ListBox clears its selection while its items change; the rail always has a page.
            if (value is null || !Set(ref _selected, value))
                return;

            Raise(nameof(CurrentPage));
        }
    }

    /// <summary>What the content area shows.</summary>
    public object CurrentPage => _selected.Content;

    /// <summary>What has been typed into the title bar's search box. Not yet acted on.</summary>
    public string SearchText
    {
        get => _searchText;
        set => Set(ref _searchText, value);
    }

    /// <summary>The first line of the rail's status card.</summary>
    public string StatusHeadline
    {
        get => _statusHeadline;
        private set => Set(ref _statusHeadline, value);
    }

    /// <summary>The muted second line under it.</summary>
    public string StatusDetail
    {
        get => _statusDetail;
        private set => Set(ref _statusDetail, value);
    }

    /// <summary>Whether the second line is reporting something that went wrong, which colours it.</summary>
    public bool HasIssues
    {
        get => _hasIssues;
        private set => Set(ref _hasIssues, value);
    }

    /// <summary>Whether zooming is on, which colours the status card's dot.</summary>
    public bool IsEnabled => _triggers.Enabled;

    /// <summary>The glyph on the theme button: the sun in the dark palette, the moon in the light one.</summary>
    public string ThemeGlyph => _theme.IsDark ? GlyphSun : GlyphMoon;

    /// <summary>"Auto" while the appearance follows Windows; that is what shows the dot on the button.</summary>
    public string ThemeState => _theme.Mode == AppearanceMode.System ? "Auto" : "Fixed";

    /// <summary>What the theme button's tooltip says, since one glyph cannot say all three states.</summary>
    public string ThemeTooltip => _theme.Mode switch
    {
        AppearanceMode.Light => "Appearance: light. Click for dark.",
        AppearanceMode.Dark => "Appearance: dark. Click to follow Windows.",
        _ => "Appearance: following Windows. Click for light.",
    };

    /// <summary>
    /// Opens the page that holds the record of what did not work. The status card names a number of issues,
    /// and a number nobody can reach is not much of a report.
    /// </summary>
    public ICommand ShowIssues { get; }

    /// <summary>Shows a page.</summary>
    /// <param name="section">The page to show.</param>
    public void GoTo(NavigationSection section)
    {
        if (Sections.FirstOrDefault(s => s.Id == section) is { } item)
            Selected = item;
    }

    /// <summary>Re-reads everything the window shows. Called whenever the window is shown or comes back.</summary>
    public void Refresh()
    {
        foreach (var page in _pages)
            page.Refresh();

        var record = _recorder.Snapshot();
        var issues = record.Counters.Sum(c => c.Count);

        Raise(nameof(IsEnabled));
        StatusHeadline = _triggers.Enabled ? "SmartZoom is active" : "SmartZoom is paused";
        HasIssues = issues > 0;
        StatusDetail = issues > 0
            ? string.Create(CultureInfo.CurrentCulture, $"{issues} issue{(issues == 1 ? string.Empty : "s")} recorded")
            : _activity.LastTrigger is null
                ? "No press seen yet"
                : "No issues detected";
    }

    private void RaiseTheme()
    {
        Raise(nameof(ThemeGlyph));
        Raise(nameof(ThemeState));
        Raise(nameof(ThemeTooltip));
    }

    private void NextTheme()
    {
        var wanted = _theme.Next();
        _theme.Use(wanted);
        RaiseTheme();

        // Off the UI thread: the applier's gate can be held by a settings change waiting for a zoom in flight.
        _ = Task.Run(async () =>
        {
            try
            {
                await _applier.SetAppearanceAsync(wanted).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // The window is already wearing it; only remembering it for next time failed.
                await _dispatcher.BeginInvoke(() => LogAppearanceNotSaved(ex, wanted));
            }
        });
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The settings window is showing the {Appearance} appearance but could not remember it.")]
    private partial void LogAppearanceNotSaved(Exception exception, AppearanceMode appearance);
}
