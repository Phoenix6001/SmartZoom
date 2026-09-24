using Microsoft.Extensions.Logging.Abstractions;

using SmartZoom.App.Diagnostics;

namespace SmartZoom.App.Tests.Diagnostics;

/// <summary>The diagnostics objects every test builds the same way, over a directory of the test's own.</summary>
internal static class DiagnosticFixtures
{
    /// <summary>The version every test record is written under.</summary>
    public const string Version = "0.1.0-test";

    /// <summary>Paths rooted in <paramref name="directory"/>, so no test touches the machine's real files.</summary>
    public static AppPaths Paths(string directory) =>
        new(SettingsDirectory: directory, LogDirectory: Path.Combine(directory, "logs"));

    /// <summary>A store over <paramref name="directory"/>.</summary>
    public static DiagnosticStore CreateStore(string directory) =>
        new(Paths(directory), NullLogger<DiagnosticStore>.Instance);

    /// <summary>A recorder over a store in <paramref name="directory"/>, on the system clock unless another is given.</summary>
    public static DiagnosticRecorder CreateRecorder(string directory, TimeProvider? time = null) =>
        new(CreateStore(directory), time ?? TimeProvider.System, Version);
}
