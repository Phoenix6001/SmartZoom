using Microsoft.Extensions.Logging;

namespace SmartZoom.Core.Settings;

/// <summary>How much SmartZoom writes to its log file.</summary>
public sealed class LoggingSettings
{
    /// <summary>
    /// The quietest level that is still written. <see cref="LogLevel.Debug"/> is the default because the log
    /// is the only way to see what a press did, and it is what a bug report needs; <see cref="LogLevel.Information"/>
    /// keeps one line per zoom, and <see cref="LogLevel.Warning"/> keeps only the things that went wrong.
    /// </summary>
    public LogLevel Level { get; set; } = LogLevel.Debug;
}
