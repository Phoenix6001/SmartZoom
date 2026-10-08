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
/// Everything a user has to go looking for: how far a zoom goes, how PDFs are zoomed, and what is written to
/// the log. The record of what did not work is its own page, because the app sends people to it.
/// </summary>
/// <remarks>
/// <para>
/// There is no Save button. Every control applies through <see cref="SettingsApplier"/> as it is moved, and
/// the page is re-read from <see cref="SettingsHolder.Current"/> afterwards, so what is on screen is what the
/// application is running. The two sliders wait for the dragging to stop first: a slider that applied on every
/// pixel would rebuild the zoom pipeline a hundred times on the way across.
/// </para>
/// </remarks>
internal sealed partial class AdvancedViewModel : ObservableObject, IPageModel
{
    /// <summary>
    /// How long a slider has to be still before the change is applied. Long enough to cover the pause between
    /// two nudges of the arrow keys, short enough that letting go feels like it took effect at once.
    /// </summary>
    private static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(400);

    private readonly SettingsApplier _applier;
    private readonly SettingsHolder _holder;
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
    private IReadOnlyList<ProblemLine> _problems = [];

    /// <summary>True while the controls are being filled in, so setting one does not apply it back.</summary>
    private bool _loading;

    /// <summary>Creates the page's view model over the settings the application is running.</summary>
    /// <param name="applier">The only writer of the settings file.</param>
    /// <param name="holder">Where the settings in force live; only read.</param>
    /// <param name="paths">Where the settings file and the logs live.</param>
    /// <param name="logger">Logger.</param>
    public AdvancedViewModel(
        SettingsApplier applier,
        SettingsHolder holder,
        AppPaths paths,
        ILogger<AdvancedViewModel> logger)
    {
        _applier = applier;
        _holder = holder;
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
        OpenLogFolder = new RelayCommand(() => Open(_paths.LogDirectory));
        OpenSettingsFile = new RelayCommand(() => Open(_paths.SettingsFile));

        Refresh();
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

    /// <summary>Word and Excel only: below this a fit is not worth having, and the amount applies instead.</summary>
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
                Apply(settings => settings.Zoom.Animate = value);
        }
    }

    /// <summary>Whether an application no strategy can handle gets a crude Ctrl+wheel zoom instead of nothing.</summary>
    public bool CtrlWheelFallback
    {
        get => _ctrlWheelFallback;
        set
        {
            if (Set(ref _ctrlWheelFallback, value) && !_loading)
                Apply(settings => settings.Zoom.FallbackToCtrlWheel = value);
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
                Apply(settings => settings.Zoom.Reader.Mode = value ? ReaderZoomMode.Pinch : ReaderZoomMode.Shortcuts);
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

            Apply(settings => settings.Logging.Level = value.Level);
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

    /// <summary>Opens the folder the log files are written to.</summary>
    public ICommand OpenLogFolder { get; }

    /// <summary>Opens the settings file, for everything this page deliberately leaves out.</summary>
    public ICommand OpenSettingsFile { get; }

    /// <inheritdoc />
    public void Refresh()
    {
        // What the last change said may no longer be true of the settings re-read here.
        Problems = [];

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

    private void ApplyScales() => Apply(settings =>
    {
        settings.Zoom.MaxScale = _maxScale;
        settings.Zoom.MinScale = _minScale;
        settings.Zoom.Reader.Magnification = _readerMagnification;
    });

    /// <summary>
    /// Puts a change into force off the UI thread — the applier's gate may be held by a zoom in flight —
    /// and then re-reads the page, so it shows what took effect rather than what was asked for.
    /// </summary>
    private void Apply(Action<SmartZoomSettings> change)
    {
        Problems = [];

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
                problems = ProblemLine.Error("Advanced: " + ex.Message);
            }

            await _dispatcher.BeginInvoke(() =>
            {
                Refresh();
                Problems = problems;
            });
        });
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to open {Path}.")]
    private partial void LogOpenFailed(Exception exception, string path);
}
