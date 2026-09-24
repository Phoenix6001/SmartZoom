namespace SmartZoom.App.Ui.Pages;

/// <summary>A page of the settings window that shows live state rather than a copy of it.</summary>
/// <remarks>
/// Nothing in this window holds its own copy of the settings. Every page reads
/// <see cref="Settings.SettingsHolder.Current"/> when it is asked to, so a change made in the tray panel, in
/// the tray menu, on another page or by hand in the file is visible the next time the window is looked at.
/// The shell calls <see cref="Refresh"/> on every page whenever the window is shown.
/// </remarks>
internal interface IPageModel
{
    /// <summary>Re-reads everything the page shows from the state the application is running on.</summary>
    void Refresh();
}
