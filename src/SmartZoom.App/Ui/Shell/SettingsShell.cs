using System.Windows;

using Microsoft.Extensions.Logging;

using SmartZoom.App.Settings;
using SmartZoom.App.Ui.Panel;
using SmartZoom.App.Ui.Theming;

using WpfApplication = System.Windows.Application;

namespace SmartZoom.App.Ui.Shell;

/// <summary>
/// Owns the tray panel and the settings window, and the WPF application object both of them need to exist.
/// </summary>
/// <remarks>
/// <para>
/// SmartZoom is a WinForms tray application, so there is no WPF <see cref="WpfApplication"/> until something
/// asks for one of these. It is created here, on the UI thread, the first time — lazily, so a user who never
/// opens either never pays for WPF, and with <see cref="ShutdownMode.OnExplicitShutdown"/>, because the
/// default mode ends the process when the last window closes and the owner of these lives in the tray.
/// </para>
/// <para>
/// Both windows are created once and kept. Closing hides them; opening again shows the same instance, so the
/// panel appears instantly and the settings window comes back on the page the user left it on.
/// </para>
/// </remarks>
internal sealed partial class SettingsShell(
    ThemeManager theme,
    SettingsHolder holder,
    Func<PanelViewModel> panelModel,
    Func<ShellViewModel> shellModel,
    ILogger<SettingsShell> logger) : IDisposable
{
    private PanelWindow? _panel;
    private PanelViewModel? _panelView;
    private ShellWindow? _window;
    private ShellViewModel? _shell;

    /// <summary>
    /// Shows the tray panel above the notification area, or puts it away when it is already there.
    /// </summary>
    /// <remarks>Must be called on the UI thread; the window and everything under it belong to it.</remarks>
    public void TogglePanel()
    {
        if (_panel is not null)
        {
            _panel.Toggle();
            return;
        }

        EnsureApplication();
        _panelView = panelModel();
        _panelView.SettingsRequested += (_, _) => ShowSettings();
        _panel = new PanelWindow(_panelView);
        _panel.Toggle();

        // Only now does it have a handle, which is what the colour broadcast is listened for on. The panel is
        // followed as well as the window because a user may open nothing but the panel for weeks.
        theme.Follow(_panel);
        LogPanelCreated();
    }

    /// <summary>
    /// Shows the tray panel. The name the tray's menu item already calls; <see cref="TogglePanel"/> says
    /// what it does, and is what a left click on the tray icon should be wired to.
    /// </summary>
    public void Show() => TogglePanel();

    /// <summary>Shows the settings window on the About page.</summary>
    /// <remarks>Must be called on the UI thread.</remarks>
    public void ShowAbout()
    {
        ShowSettings();
        _shell?.GoTo(NavigationSection.About);
    }

    /// <summary>Shows the settings window, creating it the first time, and brings it to the front.</summary>
    /// <remarks>Must be called on the UI thread.</remarks>
    public void ShowSettings()
    {
        if (_window is null)
        {
            EnsureApplication();
            _shell = shellModel();
            _window = new ShellWindow(_shell);
            _window.Show();

            // Only now does the window have a handle for the frame's theme and the colour broadcast.
            theme.Follow(_window);
            LogWindowOpened();
        }
        else
        {
            _shell?.Refresh();
            _window.Show();
        }

        if (_window.WindowState == WindowState.Minimized)
            _window.WindowState = WindowState.Normal;

        _window.Activate();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _panel?.CloseForShutdown();
        _window?.CloseForShutdown();
        _panel = null;
        _panelView = null;
        _window = null;
        _shell = null;
    }

    /// <summary>
    /// The WPF application has to exist, and carry the palette, before the first control is constructed:
    /// every style and brush in either window resolves against its resources.
    /// </summary>
    private void EnsureApplication() => theme.Install(
        WpfApplication.Current ?? new WpfApplication { ShutdownMode = ShutdownMode.OnExplicitShutdown },
        holder.Current.Appearance);

    [LoggerMessage(Level = LogLevel.Debug, Message = "The SmartZoom panel was created.")]
    private partial void LogPanelCreated();

    [LoggerMessage(Level = LogLevel.Debug, Message = "The settings window was opened.")]
    private partial void LogWindowOpened();
}
