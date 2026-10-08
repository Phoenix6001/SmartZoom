using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Serilog.Core;

using SmartZoom.App.Diagnostics;
using SmartZoom.App.Tests.Diagnostics;
using SmartZoom.Core.Diagnostics;
using SmartZoom.Core.Zoom.Gesture;

namespace SmartZoom.App.Tests.Hosting;

/// <summary>
/// Issue #9: a crash during startup, before the host existed, reached the log and a message box but never the
/// diagnostics record, because the recorder was only built by the container. It is now built first, from the
/// paths alone, and the container hands out that same instance.
/// </summary>
public sealed class StartupDiagnosticsTests
{
    [Fact]
    public void A_failure_before_the_host_exists_is_recorded()
    {
        using var temp = new TempDirectory();
        var recorder = StartupDiagnostics.Create(DiagnosticFixtures.Paths(temp.Path), DiagnosticFixtures.Version, NullLoggerFactory.Instance);

        // What Program.Main's handlers do with an exception thrown while the host is still being built.
        Crash.Record(recorder, new InvalidOperationException("settings.json is locked"));

        var record = DiagnosticFixtures.CreateStore(temp.Path).Load(DiagnosticFixtures.Version);
        var counter = Assert.Single(record.Counters);
        Assert.Equal(new DiagnosticKey(DiagnosticKind.Crashed, null, null, nameof(InvalidOperationException)), counter.Key);
    }

    [Fact]
    public void The_container_hands_out_the_recorder_made_before_it()
    {
        // One record: a second, separately built recorder would split the counts between two instances and
        // let one flush overwrite the other.
        using var temp = new TempDirectory();
        var paths = DiagnosticFixtures.Paths(temp.Path);
        var recorder = StartupDiagnostics.Create(paths, DiagnosticFixtures.Version, NullLoggerFactory.Instance);

        using var host = Program.BuildHost(paths, new LoggingLevelSwitch(), recorder);

        Assert.Same(recorder, host.Services.GetRequiredService<DiagnosticRecorder>());
        Assert.Same(recorder, host.Services.GetRequiredService<IGesturePacingSink>());
    }

    [Fact]
    public void Once_the_settings_are_read_the_recorder_follows_their_switch()
    {
        // Until the settings file has been read, the setting's default (recording on) is all there is.
        using var temp = new TempDirectory();
        var paths = DiagnosticFixtures.Paths(temp.Path);
        File.WriteAllText(paths.SettingsFile, """{ "Diagnostics": { "Enabled": false } }""");
        var recorder = StartupDiagnostics.Create(paths, DiagnosticFixtures.Version, NullLoggerFactory.Instance);
        Assert.True(recorder.Enabled);

        using var host = Program.BuildHost(paths, new LoggingLevelSwitch(), recorder);
        host.Services.GetRequiredService<DiagnosticRecorder>();

        Assert.False(recorder.Enabled);
    }
}
