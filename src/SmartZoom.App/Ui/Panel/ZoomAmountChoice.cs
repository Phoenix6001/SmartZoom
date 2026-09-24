using System.Globalization;

using SmartZoom.App.Ui.Mvvm;

namespace SmartZoom.App.Ui.Panel;

/// <summary>One of the segmented "zoom amount" buttons in the tray panel.</summary>
/// <remarks>
/// A view model rather than a record because the selected one moves as the user presses them, and the three
/// buttons are one choice: the panel rebuilds none of them, it just re-reads which is <see cref="IsSelected"/>.
/// </remarks>
/// <param name="amount">The largest zoom this button applies, e.g. 2.0.</param>
internal sealed class ZoomAmountChoice(double amount) : ObservableObject
{
    private bool _isSelected;

    /// <summary>The largest zoom this button applies.</summary>
    public double Amount { get; } = amount;

    /// <summary>How it is written on the button: "1.5×", "2×", "3×".</summary>
    public string Label { get; } = string.Create(CultureInfo.CurrentCulture, $"{amount:0.#}×");

    /// <summary>Whether this is the amount in force.</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set => Set(ref _isSelected, value);
    }
}
