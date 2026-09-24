// WinForms is in this project's implicit usings and has a UserControl of its own.
using UserControl = System.Windows.Controls.UserControl;

namespace SmartZoom.App.Ui.Pages;

/// <summary>Placeholder for the applications table; filled in the second wave.</summary>
internal sealed partial class ApplicationsPage : UserControl
{
    /// <summary>Creates the page.</summary>
    public ApplicationsPage() => InitializeComponent();
}
