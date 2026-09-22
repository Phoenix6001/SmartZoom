using Microsoft.Extensions.Logging.Abstractions;

using SmartZoom.App.Settings;
using SmartZoom.App.Tests.Diagnostics;
using SmartZoom.Core.Settings;

namespace SmartZoom.App.Tests.Settings;

/// <summary>
/// The record switch is the only control the user has over a privacy feature, and both README.md and
/// SECURITY.md advertise it. It used to be an in-memory property on the recorder, so a user who turned
/// recording off had it back on at the next launch.
/// </summary>
public sealed class DiagnosticsSettingsTests
{
    [Fact]
    public void Recording_is_on_by_default()
    {
        Assert.True(new SmartZoomSettings().Diagnostics.Enabled);
    }

    [Fact]
    public void Turning_recording_off_survives_a_save_and_a_load()
    {
        using var temp = new TempDirectory();
        var paths = new AppPaths(SettingsDirectory: temp.Path, LogDirectory: Path.Combine(temp.Path, "logs"));
        var store = new SettingsStore(paths, NullLogger<SettingsStore>.Instance);

        var settings = new SmartZoomSettings();
        settings.Diagnostics.Enabled = false;
        store.Save(settings);

        Assert.False(store.Load().Diagnostics.Enabled);
    }

    [Fact]
    public void A_file_written_before_the_switch_existed_still_records()
    {
        using var temp = new TempDirectory();
        var paths = new AppPaths(SettingsDirectory: temp.Path, LogDirectory: Path.Combine(temp.Path, "logs"));
        Directory.CreateDirectory(paths.SettingsDirectory);
        File.WriteAllText(paths.SettingsFile, """{ "Enabled": true }""");

        Assert.True(new SettingsStore(paths, NullLogger<SettingsStore>.Instance).Load().Diagnostics.Enabled);
    }

    [Fact]
    public void The_settings_window_edits_a_copy_that_carries_the_switch()
    {
        var inForce = new SmartZoomSettings();
        inForce.Diagnostics.Enabled = false;

        // What SettingsForm hands the Diagnostics page. Editing it must not touch what the app is running,
        // and saving it must carry the value - that is the Save/Cancel model the other pages live in.
        var working = SettingsStore.Clone(inForce);
        Assert.False(working.Diagnostics.Enabled);

        working.Diagnostics.Enabled = true;
        Assert.False(inForce.Diagnostics.Enabled);
    }
}
