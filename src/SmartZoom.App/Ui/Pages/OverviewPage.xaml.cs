// WinForms is in this project's implicit usings and has a UserControl of its own.
using UserControl = System.Windows.Controls.UserControl;

namespace SmartZoom.App.Ui.Pages;

/// <summary>The Overview page: what SmartZoom is doing, and the settings people change most.</summary>
/// <remarks>Everything it shows comes from <see cref="OverviewViewModel"/> in its data context.</remarks>
internal sealed partial class OverviewPage : UserControl
{
    /// <summary>Creates the page.</summary>
    public OverviewPage() => InitializeComponent();
}
