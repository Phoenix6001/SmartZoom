// WinForms is in this project's implicit usings and has a UserControl of its own.
using UserControl = System.Windows.Controls.UserControl;

namespace SmartZoom.App.Ui.Pages;

/// <summary>The Triggers page: the gestures that start a zoom, and the recorder that changes them.</summary>
/// <remarks>Everything it shows comes from <see cref="TriggersViewModel"/> in its data context.</remarks>
internal sealed partial class TriggersPage : UserControl
{
    /// <summary>Creates the page.</summary>
    public TriggersPage() => InitializeComponent();
}
