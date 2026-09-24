using System.Text.Json;
using System.Text.Json.Serialization;

using SmartZoom.Core.Settings;

namespace SmartZoom.Core.Tests.Settings;

/// <summary>
/// The settings window's palette is a preference, so it lives in the settings file and has to survive the
/// trip through it. The options here are the ones the app's SettingsStore writes with: the enum is spelled
/// out as a string, the way ReaderZoomMode is, because the file is meant to be read and edited by hand.
/// </summary>
public sealed class AppearanceSettingsTests
{
    private static readonly JsonSerializerOptions FileOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    [Fact]
    public void Windows_decides_by_default()
    {
        Assert.Equal(AppearanceMode.System, new SmartZoomSettings().Appearance);
    }

    [Fact]
    public void A_file_written_before_the_setting_existed_still_follows_windows()
    {
        var loaded = JsonSerializer.Deserialize<SmartZoomSettings>("""{ "Enabled": true }""", FileOptions)!;

        Assert.Equal(AppearanceMode.System, loaded.Appearance);
    }

    [Theory]
    [InlineData(AppearanceMode.System)]
    [InlineData(AppearanceMode.Light)]
    [InlineData(AppearanceMode.Dark)]
    public void A_chosen_appearance_survives_the_file(AppearanceMode mode)
    {
        var settings = new SmartZoomSettings { Appearance = mode };

        var loaded = JsonSerializer.Deserialize<SmartZoomSettings>(JsonSerializer.Serialize(settings, FileOptions), FileOptions)!;

        Assert.Equal(mode, loaded.Appearance);
    }

    [Fact]
    public void Is_written_as_a_word_rather_than_a_number()
    {
        var json = JsonSerializer.Serialize(new SmartZoomSettings { Appearance = AppearanceMode.Dark }, FileOptions);

        Assert.Contains("\"Appearance\": \"Dark\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Is_read_back_without_regard_to_case()
    {
        var loaded = JsonSerializer.Deserialize<SmartZoomSettings>("""{ "appearance": "light" }""", FileOptions)!;

        Assert.Equal(AppearanceMode.Light, loaded.Appearance);
    }
}
