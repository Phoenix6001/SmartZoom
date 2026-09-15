using System.Text.Json;
using System.Text.Json.Serialization;
using SmartZoom.Core.Input;
using SmartZoom.Core.Settings;

namespace SmartZoom.Core.Tests.Settings;

public sealed class TriggerSettingsTests
{
    private const uint SystemDoubleClick = 500;

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    [Fact]
    public void Default_is_a_double_tap_of_xbutton2_using_the_system_double_click_time()
    {
        var definition = TriggerSettings.CreateDefault().ToDefinition(SystemDoubleClick);

        var mouse = Assert.IsType<MouseButtonTrigger>(definition);
        Assert.Equal(MouseButton.XButton2, mouse.Button);
        Assert.Equal(new TapOptions(2, SystemDoubleClick, false), mouse.Tap);
        Assert.Equal("XButton2 x2", mouse.DisplayName);
    }

    [Fact]
    public void Keys_produce_a_hotkey_trigger()
    {
        var settings = new TriggerSettings { Keys = "Ctrl+Alt+Z", TapCount = 1, SwallowClicks = true, DoubleTapWindowMs = 250 };

        var hotkey = Assert.IsType<HotkeyTrigger>(settings.ToDefinition(SystemDoubleClick));
        Assert.Equal(KeyCombo.Parse("Ctrl+Alt+Z"), hotkey.Keys);
        Assert.Equal(new TapOptions(1, 250, true), hotkey.Tap);
        Assert.Equal("Ctrl+Alt+Z x1", hotkey.DisplayName);
    }

    [Fact]
    public void Both_mouse_and_keys_is_an_error() =>
        Assert.Throws<InvalidOperationException>(() => new TriggerSettings { Mouse = MouseButton.Middle, Keys = "F9" }.ToDefinition(SystemDoubleClick));

    [Fact]
    public void Neither_mouse_nor_keys_is_an_error() =>
        Assert.Throws<InvalidOperationException>(() => new TriggerSettings().ToDefinition(SystemDoubleClick));

    [Fact]
    public void Invalid_keys_are_a_format_error() =>
        Assert.Throws<FormatException>(() => new TriggerSettings { Keys = "Ctrl+" }.ToDefinition(SystemDoubleClick));

    [Fact]
    public void Legacy_single_trigger_is_migrated_into_the_list()
    {
        const string legacy = """
            { "Trigger": { "Button": "Middle", "TapCount": 1, "SwallowClicks": true, "DoubleTapWindowMs": 700 } }
            """;

        var settings = JsonSerializer.Deserialize<SmartZoomSettings>(legacy, Json)!;

        Assert.True(settings.Migrate());
        Assert.Null(settings.Trigger);
        var trigger = Assert.Single(settings.Triggers);
        Assert.Equal(MouseButton.Middle, trigger.Mouse);
        Assert.Equal(1, trigger.TapCount);
        Assert.True(trigger.SwallowClicks);
        Assert.Equal(700u, trigger.DoubleTapWindowMs);
        Assert.False(settings.Migrate(), "second migration must be a no-op");
    }

    [Fact]
    public void Legacy_trigger_does_not_override_an_explicit_list()
    {
        const string json = """
            { "Trigger": { "Button": "Middle" }, "Triggers": [ { "Keys": "F9" } ] }
            """;

        var settings = JsonSerializer.Deserialize<SmartZoomSettings>(json, Json)!;

        Assert.True(settings.Migrate());
        var trigger = Assert.Single(settings.Triggers);
        Assert.Equal("F9", trigger.Keys);
    }

    [Fact]
    public void Serialized_form_uses_the_new_names_only()
    {
        var settings = new SmartZoomSettings();

        var json = JsonSerializer.Serialize(settings, Json);

        Assert.Contains("\"Triggers\"", json, StringComparison.Ordinal);
        Assert.Contains("\"Mouse\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Trigger\":", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Button\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Keys\"", json, StringComparison.Ordinal);
    }
}
