using Microsoft.Extensions.Logging.Abstractions;

using SmartZoom.App.Settings;
using SmartZoom.App.Tests.Diagnostics;
using SmartZoom.Core.Settings;

namespace SmartZoom.App.Tests.Settings;

/// <summary>
/// The same guarantee as <see cref="DiagnosticsSettingsTests"/>, for the palette the settings window paints
/// itself in: chosen once, still chosen at the next launch, and written through the real store rather than a
/// stand-in for it.
/// </summary>
public sealed class AppearanceSettingsStoreTests
{
    [Fact]
    public void Choosing_dark_survives_a_save_and_a_load()
    {
        using var temp = new TempDirectory();
        var store = Store(temp);

        store.Save(new SmartZoomSettings { Appearance = AppearanceMode.Dark });

        Assert.Equal(AppearanceMode.Dark, store.Load().Appearance);
    }

    [Fact]
    public void The_file_spells_the_choice_out()
    {
        using var temp = new TempDirectory();
        var paths = Paths(temp);
        new SettingsStore(paths, NullLogger<SettingsStore>.Instance).Save(new SmartZoomSettings { Appearance = AppearanceMode.Light });

        Assert.Contains("\"Appearance\": \"Light\"", File.ReadAllText(paths.SettingsFile), StringComparison.Ordinal);
    }

    [Fact]
    public void A_file_written_before_the_setting_existed_follows_windows()
    {
        using var temp = new TempDirectory();
        var paths = Paths(temp);
        Directory.CreateDirectory(paths.SettingsDirectory);
        File.WriteAllText(paths.SettingsFile, """{ "Enabled": true }""");

        Assert.Equal(AppearanceMode.System, new SettingsStore(paths, NullLogger<SettingsStore>.Instance).Load().Appearance);
    }

    [Fact]
    public void Clone_carries_the_choice_and_is_independent_of_it()
    {
        var inForce = new SmartZoomSettings { Appearance = AppearanceMode.Dark };

        var working = SettingsStore.Clone(inForce);
        Assert.Equal(AppearanceMode.Dark, working.Appearance);

        working.Appearance = AppearanceMode.Light;
        Assert.Equal(AppearanceMode.Dark, inForce.Appearance);
    }

    private static AppPaths Paths(TempDirectory temp) =>
        new(SettingsDirectory: temp.Path, LogDirectory: Path.Combine(temp.Path, "logs"));

    private static SettingsStore Store(TempDirectory temp) => new(Paths(temp), NullLogger<SettingsStore>.Instance);
}
