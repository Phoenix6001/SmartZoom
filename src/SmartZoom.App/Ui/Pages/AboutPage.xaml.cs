// WinForms is in this project's implicit usings and has a UserControl of its own.
using UserControl = System.Windows.Controls.UserControl;

namespace SmartZoom.App.Ui.Pages;

/// <summary>The About page: which build this is, where the project lives, and what it records.</summary>
/// <remarks>Everything it shows comes from <see cref="AboutViewModel"/> in its data context.</remarks>
internal sealed partial class AboutPage : UserControl
{
    /// <summary>Creates the page.</summary>
    public AboutPage() => InitializeComponent();
}
