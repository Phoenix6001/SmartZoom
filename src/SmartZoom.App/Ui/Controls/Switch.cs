using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls.Primitives;

namespace SmartZoom.App.Ui.Controls;

/// <summary>An on/off switch that shows a setting and asks its command to change it, rather than flipping itself.</summary>
/// <remarks>
/// <para>
/// A <see cref="ToggleButton"/> so that a screen reader is told it is a switch and whether it is on; the plain
/// button it replaces was announced as an unnamed button with no state at all. <c>IsChecked</c> is bound one way
/// to the view model and the click runs <c>Command</c>, as the button did: the setting changes only when the
/// settings take the change, and the switch follows from the binding.
/// </para>
/// <para>
/// A screen reader does not click; it calls the toggle pattern, which in a stock <see cref="ToggleButton"/> flips
/// <c>IsChecked</c> and runs no command. <see cref="SwitchAutomationPeer"/> routes it through the same click
/// instead, so the two ways of turning a switch do the same thing.
/// </para>
/// </remarks>
internal sealed class Switch : ToggleButton
{
    /// <summary>Does what a click does: runs the command, which changes the setting the switch shows.</summary>
    internal void Flip() => OnClick();

    /// <inheritdoc />
    /// <remarks>Left to the command and the binding; a switch that flipped itself could show a change that was refused.</remarks>
    protected override void OnToggle()
    {
    }

    /// <inheritdoc />
    protected override AutomationPeer OnCreateAutomationPeer() => new SwitchAutomationPeer(this);

    /// <summary>Reports the switch as a toggle whose toggling is a click.</summary>
    /// <param name="owner">The switch.</param>
    private sealed class SwitchAutomationPeer(Switch owner) : ToggleButtonAutomationPeer(owner), IToggleProvider
    {
        ToggleState IToggleProvider.ToggleState => owner.IsChecked == true ? ToggleState.On : ToggleState.Off;

        void IToggleProvider.Toggle()
        {
            if (!IsEnabled())
                throw new ElementNotEnabledException();

            owner.Flip();
        }
    }
}
