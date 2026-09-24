using SmartZoom.App.Ui.Mvvm;

namespace SmartZoom.App.Ui.Pages;

/// <summary>One of the user's own routes: an application, and the strategy it is handled by.</summary>
/// <remarks>
/// The strategy is settable because the row's combo box is how it is changed. Changing it raises
/// <see cref="StrategyChanged"/> rather than writing anything: the page owns the settings file, this owns
/// only what the row shows.
/// </remarks>
internal sealed class ApplicationRoute : ObservableObject
{
    private StrategyChoice _strategy;

    /// <summary>Creates a row.</summary>
    /// <param name="process">The process image name, as the settings file spells it.</param>
    /// <param name="strategy">The strategy it is routed to.</param>
    public ApplicationRoute(string process, StrategyChoice strategy)
    {
        Process = process;
        _strategy = strategy;
    }

    /// <summary>Raised when the combo box has been moved to another strategy.</summary>
    public event EventHandler? StrategyChanged;

    /// <summary>The process image name. Not editable: a renamed route is a removal and an addition.</summary>
    public string Process { get; }

    /// <summary>The strategy this application is routed to.</summary>
    public StrategyChoice Strategy
    {
        get => _strategy;
        set
        {
            // A ComboBox clears its selection while its list changes; a row always has a strategy.
            if (value is null || !Set(ref _strategy, value))
                return;

            Raise(nameof(Description));
            StrategyChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>What that strategy does, in the muted line under the combo.</summary>
    public string Description => _strategy.Description;
}
