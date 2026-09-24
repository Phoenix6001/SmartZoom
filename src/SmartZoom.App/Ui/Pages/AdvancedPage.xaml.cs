// WinForms is in this project's implicit usings and has a UserControl of its own.
using UserControl = System.Windows.Controls.UserControl;

namespace SmartZoom.App.Ui.Pages;

/// <summary>Placeholder for the zoom, diagnostics and logging sections; filled in the second wave.</summary>
internal sealed partial class AdvancedPage : UserControl
{
    /// <summary>Creates the page.</summary>
    public AdvancedPage() => InitializeComponent();
}
