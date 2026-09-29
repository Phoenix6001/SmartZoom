namespace SmartZoom.App.Ui.Shell;

/// <summary>The pages the settings window's navigation rail offers.</summary>
/// <remarks>
/// Six. <see cref="Overview"/> is the landing page and says what SmartZoom is doing; the tray panel shows
/// the same few facts for someone who only wants to flip a switch. Zoom tuning and the logging level are
/// things a user goes looking for rather than meets on the way, so they are sections of <see cref="Advanced"/>
/// rather than rows of their own. <see cref="Diagnostics"/> is the exception, and has a row because the app
/// sends people there: the status card counts issues and the tray offers the report, and a page two other
/// surfaces point at is not one anybody goes looking for.
/// </remarks>
internal enum NavigationSection
{
    /// <summary>What SmartZoom is doing, and the settings people change most.</summary>
    Overview,

    /// <summary>The gestures that start a zoom.</summary>
    Triggers,

    /// <summary>Which application is handled by which strategy.</summary>
    Applications,

    /// <summary>Everything a user has to go looking for: zoom tuning, PDF readers, logging.</summary>
    Advanced,

    /// <summary>The record of what did not work, and the report built from it.</summary>
    Diagnostics,

    /// <summary>Which build this is, where the project lives, and what it records.</summary>
    About,
}
