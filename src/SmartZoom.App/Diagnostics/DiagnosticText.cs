using SmartZoom.Core.Diagnostics;

namespace SmartZoom.App.Diagnostics;

/// <summary>
/// The one place free text is cleaned before it is recorded or rendered.
/// </summary>
/// <remarks>
/// Redaction has to happen when a value is <em>recorded</em>, not only when it is rendered. Rendering-time
/// redaction keeps the report clean but leaves <c>%LOCALAPPDATA%\SmartZoom\diagnostics.json</c> itself
/// carrying whatever the exception said — and an <see cref="IOException"/> on the settings file says
/// <c>C:\Users\&lt;name&gt;\AppData\Roaming\SmartZoom\settings.json</c>. SECURITY.md promises that file holds
/// no username, and users are invited to attach it to an issue directly, so the promise has to hold on disk
/// and not merely on screen.
/// </remarks>
internal static class DiagnosticText
{
    /// <summary>The most characters of exception text kept in one sample.</summary>
    public const int MaxExceptionCharacters = 4000;

    private static readonly string Home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private static readonly string LocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    private static readonly string RoamingAppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

    /// <summary>Rewrites the user's own folders out of <paramref name="text"/>.</summary>
    /// <param name="text">The text to clean.</param>
    /// <returns>The text with profile paths replaced by the variables that name them.</returns>
    public static string Redact(string text) => Redaction.Paths(text, Home, LocalAppData, RoamingAppData);

    /// <summary>
    /// Builds the exception text stored in a <see cref="DiagnosticSample"/>: type, message and stack,
    /// redacted first and only then cut to length.
    /// </summary>
    /// <param name="exception">The exception to describe.</param>
    /// <returns>Text that is safe to write to disk.</returns>
    /// <remarks>
    /// Redacted before truncation deliberately. Truncating first would bound the length of text that still
    /// contained a username; redacting first means nothing identifying ever reaches the file, and the
    /// replacement (<c>%USERPROFILE%</c>) is shorter than what it replaces, so the cut stays honest.
    /// </remarks>
    public static string ForException(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return Redaction.Truncate(
            Redact($"{exception.GetType().FullName}: {exception.Message}{Environment.NewLine}{exception.StackTrace}"),
            MaxExceptionCharacters);
    }
}
