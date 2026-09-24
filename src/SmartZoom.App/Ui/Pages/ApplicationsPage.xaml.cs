// WinForms is in this project's implicit usings and has a UserControl of its own.
using UserControl = System.Windows.Controls.UserControl;

namespace SmartZoom.App.Ui.Pages;

/// <summary>The Applications page: which application is zoomed by which strategy.</summary>
/// <remarks>Everything it shows comes from <see cref="ApplicationsViewModel"/> in its data context.</remarks>
internal sealed partial class ApplicationsPage : UserControl
{
    /// <summary>Creates the page.</summary>
    public ApplicationsPage() => InitializeComponent();
}
