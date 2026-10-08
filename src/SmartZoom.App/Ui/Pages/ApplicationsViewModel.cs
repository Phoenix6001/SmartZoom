using System.Windows.Input;
using System.Windows.Threading;

using Microsoft.Extensions.Logging;

using SmartZoom.App.Hosting;
using SmartZoom.App.Settings;
using SmartZoom.App.Ui.Applications;
using SmartZoom.App.Ui.Mvvm;
using SmartZoom.Core.Routing;
using SmartZoom.Core.Settings;
using SmartZoom.Core.Zoom;

namespace SmartZoom.App.Ui.Pages;

/// <summary>Which application is zoomed by which strategy: the user's own routes, and what is already handled.</summary>
/// <remarks>
/// <para>
/// The list of strategies and the prose describing them come from the registered adapters through
/// <see cref="ZoomRouter.Adapters"/>, never from a list kept here, so this page needs no maintenance when one
/// is added and can never offer a strategy the running build does not have.
/// </para>
/// <para>
/// Only the user's own routes are editable. The defaults each adapter claims are shown read-only underneath:
/// a row that restated one would say the same thing and then go stale the next time the defaults moved.
/// </para>
/// </remarks>
internal sealed partial class ApplicationsViewModel : ObservableObject, IPageModel
{
    private readonly SettingsApplier _applier;
    private readonly SettingsHolder _holder;
    private readonly ZoomEngine _engine;
    private readonly ILogger<ApplicationsViewModel> _logger;
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;

    private IReadOnlyList<StrategyChoice> _strategies = [];
    private IReadOnlyList<ApplicationRoute> _routes = [];
    private IReadOnlyList<BuiltInGroup> _builtIn = [];
    private IReadOnlyList<InstalledApplication> _running = [];
    private InstalledApplication? _selectedRunning;
    private IReadOnlyList<ProblemLine> _problems = [];
    private StrategyChoice? _newStrategy;
    private string _newProcess = string.Empty;
    private bool _busy;

    /// <summary>Creates the page's view model over the router the application is running.</summary>
    /// <param name="applier">The only writer of the settings file.</param>
    /// <param name="holder">Where the settings in force live; only read.</param>
    /// <param name="engine">Supplies the router, and through it the registered adapters.</param>
    /// <param name="logger">Logger.</param>
    public ApplicationsViewModel(
        SettingsApplier applier,
        SettingsHolder holder,
        ZoomEngine engine,
        ILogger<ApplicationsViewModel> logger)
    {
        _applier = applier;
        _holder = holder;
        _engine = engine;
        _logger = logger;

        AddApplication = new RelayCommand(_ => Add(), _ => !_busy && _newProcess.Trim().Length > 0);
        RemoveApplication = new RelayCommand(Remove, _ => !_busy);

        Refresh();
    }

    /// <summary>Every strategy this build has, plus "Not handled".</summary>
    public IReadOnlyList<StrategyChoice> Strategies
    {
        get => _strategies;
        private set => Set(ref _strategies, value);
    }

    /// <summary>The routes in <c>Routing.Apps</c>, in alphabetical order.</summary>
    public IReadOnlyList<ApplicationRoute> Routes
    {
        get => _routes;
        private set
        {
            if (Set(ref _routes, value))
                Raise(nameof(HasRoutes));
        }
    }

    /// <summary>Whether the user has added anything of their own yet.</summary>
    public bool HasRoutes => _routes.Count > 0;

    /// <summary>What each strategy claims by default.</summary>
    public IReadOnlyList<BuiltInGroup> BuiltIn
    {
        get => _builtIn;
        private set => Set(ref _builtIn, value);
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

    /// <summary>Whether there is anything to show under the list.</summary>
    public bool HasProblems => _problems.Count > 0;

    /// <summary>The process name typed into the last row.</summary>
    public string NewProcess
    {
        get => _newProcess;
        set => Set(ref _newProcess, value);
    }

    /// <summary>The strategy chosen in the last row.</summary>
    public StrategyChoice? NewStrategy
    {
        get => _newStrategy;
        set
        {
            // A ComboBox clears its selection while its list changes; the row always has a strategy.
            if (value is not null)
                Set(ref _newStrategy, value);
        }
    }

    /// <summary>Adds <see cref="NewProcess"/> to the routing table.</summary>
    public ICommand AddApplication { get; }

    /// <summary>Removes a route; the parameter is its <see cref="ApplicationRoute"/>.</summary>
    public ICommand RemoveApplication { get; }

    /// <summary>
    /// The running application the picker is on. Selecting one fills in <see cref="NewProcess"/>; the field
    /// stays editable, because an application that is not running has no entry to pick.
    /// </summary>
    public InstalledApplication? SelectedRunning
    {
        get => _selectedRunning;
        set
        {
            if (!Set(ref _selectedRunning, value) || value is null)
                return;

            NewProcess = value.ImageName;
        }
    }

    /// <summary>
    /// The applications running with a window right now, for the picker. Empty until the first lookup returns.
    /// </summary>
    public IReadOnlyList<InstalledApplication> Running
    {
        get => _running;
        private set => Set(ref _running, value);
    }

    /// <inheritdoc />
    public void Refresh()
    {
        // What the last change said may no longer be true of the settings re-read here.
        Problems = [];

        var settings = _holder.Current;
        var adapters = _engine.Current.Router.Adapters;

        Strategies = [.. adapters.Select(StrategyChoice.From).OrderBy(s => s.DisplayName, StringComparer.CurrentCulture), StrategyChoice.NotHandled];
        // A strategy already picked for an application being added survives a refresh: the window now refreshes
        // whenever the settings change, which can be in the middle of filling the form in. Otherwise Ctrl+wheel,
        // because an application worth an exception is usually one that does nothing today and a crude zoom is
        // the improvement. Routing something to "Browsers" by accident would be a silent puzzle.
        _newStrategy = Strategies.FirstOrDefault(s => _newStrategy is not null && s.Id == _newStrategy.Id)
            ?? Strategies.FirstOrDefault(s => s.Id == CtrlWheelAdapter.Descriptor.Id)
            ?? Strategies.FirstOrDefault(s => s.Id != AdapterId.None)
            ?? StrategyChoice.NotHandled;
        Raise(nameof(NewStrategy));

        Routes =
        [
            .. settings.Routing.Apps
                .OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
                .Select(entry => Track(new ApplicationRoute(entry.Key, Choice(entry.Value)))),
        ];

        // Defaults the user has already overridden are left out: they are one of the editable rows above, and
        // showing the same process twice with two different answers is worse than showing it once.
        BuiltIn =
        [
            .. adapters
                .Select(adapter => new
                {
                    adapter.DisplayName,
                    adapter.Description,
                    Processes = adapter.DefaultProcesses.Where(p => !settings.Routing.Apps.ContainsKey(p)).ToList(),
                })
                .Where(group => group.Processes.Count > 0)
                .Select(group => new BuiltInGroup(
                    group.DisplayName,
                    group.Description,
                    [.. group.Processes.Select(p => new InstalledApplication(p, p, Icon: null))])),
        ];

        Look();
    }

    /// <summary>
    /// Fills in what this machine knows about those applications, and what is running, off the UI thread:
    /// both read the registry and open executables, and the page has to be on screen before either returns.
    /// </summary>
    /// <remarks>
    /// The lists are replaced wholesale rather than filled in place, so a row is never half a row. Everything
    /// it produces is frozen, which is what makes it safe to hand over.
    /// </remarks>
    private void Look()
    {
        var groups = _builtIn;

        _ = Task.Run(() =>
        {
            try
            {
                var described = groups
                    .Select(group => group with
                    {
                        Applications = [.. group.Applications.Select(a => InstalledApplications.Describe(a.ImageName))],
                    })
                    .ToList();
                var running = RunningApplications.List();

                _dispatcher.BeginInvoke(() =>
                {
                    BuiltIn = described;
                    Running = running;
                });
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // Names without icons are a far better outcome than a settings page that will not open
                // because one machine has an odd App Paths entry or a process that vanished mid-question.
                LogLookupFailed(ex);
            }
        });
    }

    /// <summary>The choice an id stands for, or the raw id when the file names a strategy this build lacks.</summary>
    private StrategyChoice Choice(AdapterId id) =>
        Strategies.FirstOrDefault(s => s.Id == id)
        ?? (id == AdapterId.None
            ? StrategyChoice.NotHandled
            : new StrategyChoice(id, id.Value, "This build has no strategy by that name, so the application falls back to Ctrl+wheel."));

    /// <summary>Wires a row's combo box to apply the moment it is moved.</summary>
    private ApplicationRoute Track(ApplicationRoute route)
    {
        route.StrategyChanged += (_, _) => Apply(settings => settings.Routing.Apps[route.Process] = route.Strategy.Id);
        return route;
    }

    private void Add()
    {
        var process = _newProcess.Trim();
        if (process.Length == 0)
            return;

        var strategy = _newStrategy ?? StrategyChoice.NotHandled;
        Apply(settings => settings.Routing.Apps[process] = strategy.Id);
        NewProcess = string.Empty;
        SelectedRunning = null;
    }

    private void Remove(object? parameter)
    {
        if (parameter is ApplicationRoute route)
            Apply(settings => settings.Routing.Apps.Remove(route.Process));
    }

    /// <summary>
    /// Puts a change into force off the UI thread — the applier's gate may be held by a zoom in flight —
    /// and then re-reads the page, so it shows what took effect rather than what was asked for.
    /// </summary>
    private void Apply(Action<SmartZoomSettings> change)
    {
        _busy = true;
        Problems = [];
        CommandManager.InvalidateRequerySuggested();

        _ = Task.Run(async () =>
        {
            IReadOnlyList<ProblemLine> problems;
            try
            {
                var result = await _applier.ApplyAsync(change).ConfigureAwait(false);
                problems = ProblemLine.From(result);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                LogChangeFailed(ex);
                problems = ProblemLine.Error("Applications: " + ex.Message);
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "The Applications page could not look up what this machine has installed or running.")]
    private partial void LogLookupFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "An application routing change from the settings window failed.")]
    private partial void LogChangeFailed(Exception exception);
}
