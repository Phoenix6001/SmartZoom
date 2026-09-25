// WinForms is in this project's implicit usings and has a UserControl of its own.
using UserControl = System.Windows.Controls.UserControl;

namespace SmartZoom.App.Ui.Pages;

/// <summary>The Diagnostics page: the local record of what went wrong, and the report built from it.</summary>
/// <remarks>Everything it shows comes from <see cref="DiagnosticsViewModel"/> in its data context.</remarks>
internal sealed partial class DiagnosticsPage : UserControl
{
    /// <summary>Creates the page.</summary>
    public DiagnosticsPage() => InitializeComponent();
}
