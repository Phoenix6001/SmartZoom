// WinForms is in this project's implicit usings and has a UserControl of its own.
using UserControl = System.Windows.Controls.UserControl;

namespace SmartZoom.App.Ui.Pages;

/// <summary>The Advanced page: zoom tuning, PDF readers, logging and the local record of what went wrong.</summary>
/// <remarks>Everything it shows comes from <see cref="AdvancedViewModel"/> in its data context.</remarks>
internal sealed partial class AdvancedPage : UserControl
{
    /// <summary>Creates the page.</summary>
    public AdvancedPage() => InitializeComponent();
}
