using Microsoft.Extensions.Logging;

namespace SmartZoom.App.Diagnostics;

/// <summary>The diagnostics record, made before the host so that a failure while building it is recorded too.</summary>
/// <remarks>
/// The container used to build the recorder, which left everything before it (reading the settings, building
/// the services) able to fail with no entry in the record (issue #9). A recorder needs only the paths, a clock
/// and the version, so it is made first and the container is handed this instance, keeping one record.
/// </remarks>
internal static class StartupDiagnostics
{
    /// <summary>Makes the recorder from the paths alone.</summary>
    /// <param name="paths">Where the record lives.</param>
    /// <param name="version">The version the record is kept for.</param>
    /// <param name="loggers">For the store's own failures, which it reports and swallows.</param>
    /// <remarks>
    /// Recording starts on: that is the setting's default, and until the settings file has been read it is all
    /// there is. The container applies the file's switch when it hands the recorder out.
    /// </remarks>
    public static DiagnosticRecorder Create(AppPaths paths, string version, ILoggerFactory loggers)
    {
        ArgumentNullException.ThrowIfNull(loggers);
        return new DiagnosticRecorder(new DiagnosticStore(paths, loggers.CreateLogger<DiagnosticStore>()), TimeProvider.System, version);
    }
}
