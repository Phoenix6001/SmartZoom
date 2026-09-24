using System.Diagnostics;
using System.Globalization;
using System.Windows.Input;
using System.Windows.Threading;

using Microsoft.Extensions.Logging;

using SmartZoom.App.Hosting;
using SmartZoom.App.Settings;
using SmartZoom.App.Tray;
using SmartZoom.App.Ui.Applications;
using SmartZoom.App.Ui.Mvvm;
using SmartZoom.App.Ui.Shell;
using SmartZoom.Core.Input;
using SmartZoom.Core.Routing;
using SmartZoom.Core.Settings;

namespace SmartZoom.App.Ui.Pages;

/// <summary>What the Overview page shows: the master switch, the settings people change, and what is handled.</summary>
/// <remarks>
/// <para>
/// Nothing here holds a copy of the settings. Every value is read from <see cref="SettingsHolder.Current"/>
/// when <see cref="Refresh"/> runs, and the one setting this page changes goes out through
/// <see cref="SettingsApplier"/>, which is the only writer of the settings file. So a change made in the tray
/// panel, in the tray menu or by hand in the file shows up here the next time the window is looked at.
/// </para>
/// <para>
/// The tray panel shows four of the same facts. They are not shared through a common view model, because the
/// two present them differently — the panel is a list of switches, the page is a set of cards that explain
/// themselves — but both read the same state and both change it through
/// <see cref="TrayQuickSettings"/> and the applier, so they cannot disagree.
/// </para>
/// <para>
/// The application groups come from the router rather than from a list kept here, so an adapter added in the
/// future appears on this page without it being edited.
/// </para>
/// </remarks>
internal sealed partial class OverviewViewModel : ObservableObject, IPageModel
{
    private const string GlyphTrigger = "";
    private const string GlyphZoomIn = "";
    private const string GlyphAnimation = "";
    private const string GlyphApps = "";

    private readonly ITriggerSource _triggers;
    private readonly SettingsApplier _applier;
    private readonly SettingsHolder _holder;
    private readonly ISystemInput _systemInput;
    private readonly ZoomEngine _engine;
    private readonly AppPaths _paths;
    private readonly ILogger<OverviewViewModel> _logger;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;

    private bool _isEnabled = true;
    private string _zoomPill = string.Empty;
    private IReadOnlyList<QuickSetting> _quickSettings = [];
    private IReadOnlyList<SupportedApplicationGroup> _groups = [];
    private bool _changing;

    /// <summary>Creates the page's view model over the state the application is actually running on.</summary>
    /// <param name="triggers">The truth about whether zooming is on; also what the toggle flips.</param>
    /// <param name="applier">The only writer of the settings file.</param>
    /// <param name="holder">Where the settings in force live; only read.</param>
    /// <param name="systemInput">Supplies the system double-click time a trigger without its own window uses.</param>
    /// <param name="engine">Supplies the router, and through it the application groups.</param>
    /// <param name="paths">For the "open the settings file" link in the footer.</param>
    /// <param name="logger">Logger.</param>
    public OverviewViewModel(
        ITriggerSource triggers,
        SettingsApplier applier,
        SettingsHolder holder,
        ISystemInput systemInput,
        ZoomEngine engine,
        AppPaths paths,
        ILogger<OverviewViewModel> logger)
    {
        _triggers = triggers;
        _applier = applier;
        _holder = holder;
        _systemInput = systemInput;
        _engine = engine;
        _paths = paths;
        _logger = logger;

        ToggleEnabled = new RelayCommand(_ => Toggle(), _ => !_changing);
        OpenSettingsFile = new RelayCommand(OpenFile);

        // The parameter is a section, or its name: a card binds the one it carries, a fixed button names it.
        Navigate = new RelayCommand(target =>
        {
            if (target is NavigationSection section)
                NavigationRequested?.Invoke(this, section);
            else if (target is string name && Enum.TryParse<NavigationSection>(name, out var named))
                NavigationRequested?.Invoke(this, named);
        });

        Refresh();
    }

    /// <summary>Raised when one of the page's chevrons asks for another page.</summary>
    public event EventHandler<NavigationSection>? NavigationRequested;

    /// <summary>The one-line description under the name.</summary>
    public static string Tagline => "Bringing macOS's smart zoom to Windows.";

    /// <summary>Whether triggers are being acted on right now.</summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        private set
        {
            if (!Set(ref _isEnabled, value))
                return;

            Raise(nameof(StatusHeadline));
            Raise(nameof(StatusDescription));
            Raise(nameof(ToggleLabel));
        }
    }

    /// <summary>"Enabled" or "Disabled", in the status card's large type.</summary>
    public string StatusHeadline => IsEnabled ? "Enabled" : "Disabled";

    /// <summary>Two lines saying what that means for the next press.</summary>
    public string StatusDescription => IsEnabled
        ? "Your trigger is being watched. Press it over a browser, a PDF or a document and the block under the cursor grows to fill the window."
        : "Triggers are passed straight through to whatever is under the cursor. Nothing is zoomed until this is turned back on.";

    /// <summary>What the pill under the status card says it will do.</summary>
    public string ToggleLabel => IsEnabled ? "Turn off" : "Turn on";

    /// <summary>The badge on the live preview card, e.g. "Zoom 3×".</summary>
    public string ZoomPill
    {
        get => _zoomPill;
        private set => Set(ref _zoomPill, value);
    }

    /// <summary>The four cards under "Quick settings".</summary>
    public IReadOnlyList<QuickSetting> QuickSettings
    {
        get => _quickSettings;
        private set => Set(ref _quickSettings, value);
    }

    /// <summary>The tiles under "Supported applications".</summary>
    public IReadOnlyList<SupportedApplicationGroup> Groups
    {
        get => _groups;
        private set => Set(ref _groups, value);
    }

    /// <summary>Turns zooming on or off, through the applier.</summary>
    public ICommand ToggleEnabled { get; }

    /// <summary>Opens the settings file in whatever the user edits JSON with.</summary>
    public ICommand OpenSettingsFile { get; }

    /// <summary>Asks the shell for another page; the parameter is a <see cref="NavigationSection"/> or its name.</summary>
    public ICommand Navigate { get; }

    /// <summary>Re-reads everything from the settings and the router. Called whenever the window is shown.</summary>
    public void Refresh()
    {
        var settings = _holder.Current;
        var adapters = _engine.Current.Router.Adapters;

        IsEnabled = _triggers.Enabled;
        ZoomPill = "Zoom " + Times(settings.Zoom.MaxScale);
        QuickSettings = BuildQuickSettings(settings, adapters);

        var tiles = BuildGroups(settings, adapters);
        Groups = [.. tiles.Select(t => t.Group)];
        Probe(tiles);
    }

    /// <summary>
    /// A zoom factor the way a person says it: "3×", "2.5×". Multiples rather than percentages, because
    /// "300%" and "3× bigger" are the same fact and only one of them needs explaining.
    /// </summary>
    private static string Times(double scale) => string.Create(CultureInfo.CurrentCulture, $"{scale:0.##}×");

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

    /// <summary>
    /// The four cards. Everything a user has to know a word of art for — how PDFs are zoomed, the smallest
    /// zoom, how many milliseconds the animation takes, what is written to the log — belongs on Advanced, not
    /// here, and the values that do appear are written the way somebody would say them out loud.
    /// </summary>
    private IReadOnlyList<QuickSetting> BuildQuickSettings(SmartZoomSettings settings, IReadOnlyList<AdapterDescriptor> adapters)
    {
        var names = string.Join(", ", adapters.Select(a => a.DisplayName));

        return
        [
            new QuickSetting(
                GlyphTrigger,
                "Active trigger",
                Describe(TrayQuickSettings.FirstTrigger(settings)),
                "Press this to start a zoom.",
                NavigationSection.Triggers),
            new QuickSetting(
                GlyphZoomIn,
                "Largest zoom",
                Times(settings.Zoom.MaxScale) + " bigger",
                "Nothing is ever zoomed more than this.",
                NavigationSection.Advanced),
            new QuickSetting(
                GlyphAnimation,
                "Animation",
                settings.Zoom.Animate ? "On" : "Off",
                settings.Zoom.Animate ? "Zooms glide instead of jumping." : "Zooms happen in one step.",
                NavigationSection.Advanced),
            new QuickSetting(
                GlyphApps,
                "Supported apps",
                string.Create(CultureInfo.CurrentCulture, $"{adapters.Count} application groups"),
                names,
                NavigationSection.Applications),
        ];
    }

    /// <summary>
    /// The tiles, and the processes each one has to look up. The families come from the registered adapters
    /// rather than from a list kept here, so a strategy added later brings its own tile with it; the two
    /// Office adapters share one, because "Word and Excel" is one kind of application to a user. The
    /// Ctrl+wheel fallback claims almost nothing by default and is what a hand-routed application lands on,
    /// so it belongs in the last tile with the user's own routes rather than in one of its own.
    /// </summary>
    private static List<(SupportedApplicationGroup Group, IReadOnlyList<string> Processes)> BuildGroups(
        SmartZoomSettings settings,
        IReadOnlyList<AdapterDescriptor> adapters)
    {
        var tiles = new List<(SupportedApplicationGroup, IReadOnlyList<string>)>();
        var families = new Dictionary<string, (SupportedApplicationGroup Group, List<string> Processes)>(StringComparer.Ordinal);
        var ownRoutes = new List<string>(settings.Routing.Apps.Keys);

        foreach (var adapter in adapters)
        {
            var family = FamilyOf(adapter);
            if (family is null)
            {
                ownRoutes.AddRange(adapter.DefaultProcesses);
                continue;
            }

            var (title, glyph) = family.Value;
            if (!families.TryGetValue(title, out var existing))
            {
                existing = (new SupportedApplicationGroup(glyph, title, NavigationSection.Applications), []);
                families[title] = existing;
                tiles.Add((existing.Group, existing.Processes));
            }

            existing.Processes.AddRange(adapter.DefaultProcesses);
        }

        var own = new SupportedApplicationGroup("", "Your own routes", NavigationSection.Applications)
        {
            Applications = ownRoutes.Count == 0
                ? "Nothing routed by hand yet. Whatever you add under Applications appears here."
                : string.Join(", ", ownRoutes.Order(StringComparer.OrdinalIgnoreCase)),
        };

        tiles.Add((own, ownRoutes));
        return tiles;
    }

    /// <summary>The tile an adapter belongs on, or null when it has none of its own by being the fallback.</summary>
    private static (string Title, string Glyph)? FamilyOf(AdapterDescriptor adapter) => adapter.Id.Value switch
    {
        "Browser" => ("Browsers", ""),
        "Reader" => ("PDF readers", ""),
        "WordCom" or "ExcelCom" => ("Word and Excel", ""),
        "CtrlWheel" => null,
        _ => (adapter.DisplayName, ""),
    };

    /// <summary>
    /// The tile's list of applications. Capped, because a browser tile that names six products is four lines
    /// of small grey text and makes every tile in the row that tall.
    /// </summary>
    private static string Name(IReadOnlyList<InstalledApplication> found)
    {
        const int Shown = 3;

        var names = found.Select(a => a.DisplayName).ToList();
        return names.Count <= Shown + 1
            ? string.Join(", ", names)
            : string.Create(CultureInfo.CurrentCulture, $"{string.Join(", ", names.Take(Shown))} and {names.Count - Shown} more");
    }

    /// <summary>
    /// Fills the tiles in from the machine. Off the UI thread, because it reads the registry and opens
    /// executables; the images it makes are frozen, so handing them back costs one dispatcher hop.
    /// </summary>
    private void Probe(List<(SupportedApplicationGroup Group, IReadOnlyList<string> Processes)> tiles)
    {
        _ = Task.Run(() =>
        {
            foreach (var (group, processes) in tiles)
            {
                if (processes.Count == 0)
                    continue;

                try
                {
                    var found = processes.Select(InstalledApplications.Describe).ToList();
                    var names = Name(found);
                    var icon = found.Find(a => a.Icon is not null)?.Icon;

                    _dispatcher.BeginInvoke(() =>
                    {
                        group.Applications = names;
                        group.Icon = icon;
                    });
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    // A tile that keeps its fallback glyph is a far better outcome than a settings page that
                    // will not open because one machine has an odd App Paths entry.
                    LogProbeFailed(ex, group.Title);
                }
            }
        });
    }

    private void Toggle()
    {
        var wanted = !_triggers.Enabled;
        _changing = true;
        CommandManager.InvalidateRequerySuggested();

        _ = Task.Run(async () =>
        {
            try
            {
                await _applier.SetEnabledAsync(wanted).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                LogToggleFailed(ex);
            }

            await _dispatcher.BeginInvoke(() =>
            {
                _changing = false;
                IsEnabled = _triggers.Enabled;
                CommandManager.InvalidateRequerySuggested();
            });
        });
    }

    private void OpenFile()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(_paths.SettingsFile) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            LogOpenFailed(ex, _paths.SettingsFile);
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Could not look up the applications behind the \"{Group}\" tile; it keeps its fallback icon.")]
    private partial void LogProbeFailed(Exception exception, string group);

    [LoggerMessage(Level = LogLevel.Error, Message = "Turning SmartZoom on or off from the settings window failed.")]
    private partial void LogToggleFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to open {Path}.")]
    private partial void LogOpenFailed(Exception exception, string path);
}
