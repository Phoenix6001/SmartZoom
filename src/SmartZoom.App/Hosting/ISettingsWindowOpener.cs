namespace SmartZoom.App.Hosting;

/// <summary>Whatever owns the settings window, seen from code that only needs to ask for it.</summary>
internal interface ISettingsWindowOpener
{
    /// <summary>Asks for the settings window from any thread; the window itself belongs to the UI thread.</summary>
    void RequestSettings();
}
