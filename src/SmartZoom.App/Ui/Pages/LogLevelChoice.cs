using Microsoft.Extensions.Logging;

namespace SmartZoom.App.Ui.Pages;

/// <summary>One entry of the logging combo box: a level, and what choosing it means for the log file.</summary>
/// <param name="Level">The level written into <c>Logging.Level</c>.</param>
/// <param name="DisplayName">What the combo box shows.</param>
/// <param name="Description">One sentence on what lands in the file at this level.</param>
internal sealed record LogLevelChoice(LogLevel Level, string DisplayName, string Description)
{
    /// <summary>
    /// Every level, in the order they get quieter. All of them, including
    /// <see cref="LogLevel.None"/>: the log is the only record of what a press did, so switching it off is a
    /// real choice somebody may want and must not be hidden behind the settings file.
    /// </summary>
    public static IReadOnlyList<LogLevelChoice> All { get; } =
    [
        .. Enum.GetValues<LogLevel>().Select(level => new LogLevelChoice(level, Name(level), Note(level))),
    ];

    private static string Name(LogLevel level) => level switch
    {
        LogLevel.None => "Nothing",
        LogLevel.Information => "Information",
        _ => level.ToString(),
    };

    private static string Note(LogLevel level) => level switch
    {
        LogLevel.Trace => "Every step of every press. Very large, and only useful while chasing something.",
        LogLevel.Debug => "What each press found and did. This is what a bug report needs.",
        LogLevel.Information => "One line per zoom.",
        LogLevel.Warning => "Only the things that did not work.",
        LogLevel.Error => "Only failures.",
        LogLevel.Critical => "Only what stopped SmartZoom.",
        _ => "Nothing is written to the log file at all.",
    };
}
