using System.Windows;

namespace SmartZoom.App.Ui.Shell;

/// <summary>
/// The settings window: a frameless window with a title bar of its own, a navigation rail and a page host.
/// </summary>
/// <remarks>
/// The three caption buttons are the only code here, because a frameless window has to move the system
/// commands itself. Closing hides rather than exits: SmartZoom lives in the notification area, and a window
/// that took the application down with it would be a trap.
/// </remarks>
internal sealed partial class ShellWindow : Window
{
    private bool _shuttingDown;

    /// <summary>Creates the window over the shell's view model.</summary>
    /// <param name="model">Everything the window shows.</param>
    public ShellWindow(ShellViewModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        InitializeComponent();
        DataContext = model;

        // A page is always read from its beginning: WPF scrolls whatever takes focus into view, so without
        // this a window that is shown again opens part-way down whichever page it was left on.
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true)
                PageScroller.ScrollToTop();
        };

        model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ShellViewModel.CurrentPage))
                PageScroller.ScrollToTop();
        };
    }

    /// <summary>Closes the window for good, when the application itself is going away.</summary>
    public void CloseForShutdown()
    {
        _shuttingDown = true;
        Close();
    }

    /// <summary>Hides the window instead of closing it, unless the application is shutting down.</summary>
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

    private void OnMinimize(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);

    private void OnMaximizeOrRestore(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized)
            SystemCommands.RestoreWindow(this);
        else
            SystemCommands.MaximizeWindow(this);
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
