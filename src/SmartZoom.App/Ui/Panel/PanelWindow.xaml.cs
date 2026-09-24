using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfPoint = System.Windows.Point;

namespace SmartZoom.App.Ui.Panel;

/// <summary>
/// The tray panel: a small, frameless, always-on-top window that appears above the notification area when the
/// tray icon is clicked, and goes away the moment attention moves elsewhere.
/// </summary>
/// <remarks>
/// <para>
/// It behaves the way the shell's own volume and network flyouts do: no taskbar entry, no frame, dismissed on
/// deactivation and on Escape. That is also why it is never closed, only hidden — it is opened and dismissed
/// many times in a session, and building it once keeps that instant.
/// </para>
/// <para>
/// The one exception to dismiss-on-deactivation is the trigger recorder, which is a modal dialog and takes
/// activation as it opens. The view model says so before opening it, and the panel puts itself away first.
/// </para>
/// </remarks>
internal sealed partial class PanelWindow : Window
{
    /// <summary>Room left between the panel and the edges of the work area.</summary>
    private const double Gap = 8;

    /// <summary>Somewhere off-screen to build in, so the panel is never seen at the wrong position.</summary>
    private const double Offscreen = -32000;

    private readonly PanelViewModel _model;
    private bool _shuttingDown;

    /// <summary>Creates the panel over its view model.</summary>
    /// <param name="model">Everything the panel shows.</param>
    public PanelWindow(PanelViewModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        InitializeComponent();
        DataContext = model;
        _model = model;

        Left = Offscreen;
        Top = Offscreen;

        _model.DismissRequested += (_, _) => Hide();
    }

    /// <summary>Shows the panel above the notification area, or puts it away if it is already there.</summary>
    public void Toggle()
    {
        if (IsVisible)
        {
            Hide();
            return;
        }

        _model.Refresh();
        Show();

        // The size is only known once the content has been laid out, and the position depends on the size.
        UpdateLayout();
        PlaceAboveNotificationArea();

        // Activating is what makes Deactivated fire, which is the whole dismissal mechanism.
        Activate();
    }

    /// <summary>Closes the panel for good, when the application itself is going away.</summary>
    public void CloseForShutdown()
    {
        _shuttingDown = true;
        Close();
    }

    /// <summary>Hides the panel instead of closing it, unless the application is shutting down.</summary>
    /// <param name="e">The event, which is cancelled while SmartZoom is still running.</param>
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        if (!_shuttingDown)
        {
            e.Cancel = true;
            Hide();
        }

        base.OnClosing(e);
    }

    /// <inheritdoc />
    protected override void OnDeactivated(EventArgs e)
    {
        Hide();
        base.OnDeactivated(e);
    }

    /// <inheritdoc />
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        if (e.Key == Key.Escape)
        {
            Hide();
            e.Handled = true;
        }

        base.OnPreviewKeyDown(e);
    }

    /// <summary>
    /// Puts the panel in the bottom-right corner of the work area of whichever monitor the pointer is on,
    /// which is where the notification area it was clicked from lives, and keeps it inside that work area.
    /// </summary>
    /// <remarks>
    /// The work area is in physical pixels and a WPF window is positioned in device-independent ones, so the
    /// window's own composition target does the conversion: on a 150% display the two differ by half again,
    /// and a panel placed in raw pixels would land off the bottom of the screen.
    /// </remarks>
    private void PlaceAboveNotificationArea()
    {
        if (PresentationSource.FromVisual(this) is not HwndSource { CompositionTarget: { } target })
            return;

        var area = Screen.FromPoint(Control.MousePosition).WorkingArea;
        var topLeft = target.TransformFromDevice.Transform(new WpfPoint(area.Left, area.Top));
        var bottomRight = target.TransformFromDevice.Transform(new WpfPoint(area.Right, area.Bottom));

        Left = Math.Max(topLeft.X, bottomRight.X - ActualWidth - Gap);
        Top = Math.Max(topLeft.Y, bottomRight.Y - ActualHeight - Gap);
    }
}
