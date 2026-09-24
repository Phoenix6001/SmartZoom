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
    public void Modifiers_produce_a_mouse_trigger_that_needs_them_held()
    {
        var settings = new TriggerSettings { Mouse = MouseButton.Left, Modifiers = "ctrl + alt", TapCount = 1 };

        var mouse = Assert.IsType<MouseButtonTrigger>(settings.ToDefinition(SystemDoubleClick));
        Assert.Equal(MouseButton.Left, mouse.Button);
        Assert.Equal(KeyModifiers.Control | KeyModifiers.Alt, mouse.Modifiers);
        Assert.Equal("Ctrl+Alt+Left x1", mouse.DisplayName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Absent_modifiers_mean_none(string? modifiers)
    {
        var mouse = Assert.IsType<MouseButtonTrigger>(new TriggerSettings { Mouse = MouseButton.Middle, Modifiers = modifiers }.ToDefinition(SystemDoubleClick));

        Assert.Equal(KeyModifiers.None, mouse.Modifiers);
    }

    [Theory]
    [InlineData(MouseButton.Left)]
    [InlineData(MouseButton.Right)]
    public void A_left_or_right_click_without_modifiers_is_an_error(MouseButton button) =>
        Assert.Throws<InvalidOperationException>(() => new TriggerSettings { Mouse = button }.ToDefinition(SystemDoubleClick));

    [Fact]
    public void Modifiers_with_keys_is_an_error()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => new TriggerSettings { Keys = "F9", Modifiers = "Ctrl" }.ToDefinition(SystemDoubleClick));

        Assert.Contains("\"Modifiers\" belongs to a \"Mouse\" trigger", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Ctrl+Z")]
    [InlineData("Banana")]
    [InlineData("Ctrl+")]
    [InlineData("Ctrl+Ctrl")]
    public void Modifiers_that_are_not_modifier_keys_are_a_format_error(string modifiers) =>
        Assert.Throws<FormatException>(() => new TriggerSettings { Mouse = MouseButton.Middle, Modifiers = modifiers }.ToDefinition(SystemDoubleClick));

    [Fact]
    public void Modifiers_round_trip_through_json_and_are_left_out_when_there_are_none()
    {
        var triggers = new List<TriggerSettings>
        {
            new() { Mouse = MouseButton.Left, Modifiers = "Ctrl", TapCount = 1 },
            new() { Mouse = MouseButton.XButton2 },
        };

        var json = JsonSerializer.Serialize(triggers, Json);
        var read = JsonSerializer.Deserialize<List<TriggerSettings>>(json, Json)!;

        Assert.Equal(1, json.Split("\"Modifiers\"").Length - 1);
        Assert.Equal("Ctrl", read[0].Modifiers);
        Assert.Null(read[1].Modifiers);
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
    public void The_written_file_carries_no_shapes_that_were_left_behind()
    {
        // There is no migration code any more, so the model must not grow properties nobody writes.
        var settings = new SmartZoomSettings();

        var json = JsonSerializer.Serialize(settings, Json);

        Assert.Contains("\"Triggers\"", json, StringComparison.Ordinal);
        Assert.Contains("\"Mouse\"", json, StringComparison.Ordinal);
        Assert.Contains("\"Apps\"", json, StringComparison.Ordinal);
        Assert.Contains("\"Smart\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Trigger\":", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Button\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Overrides\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Keys\"", JsonSerializer.Serialize(settings.Triggers, Json), StringComparison.Ordinal); // a mouse trigger has no hotkey field
    }

    [Fact]
    public void A_file_from_an_older_version_still_loads_and_takes_the_new_defaults()
    {
        // The keys it carries for shapes that are gone are simply ignored.
        const string Old = """
            {
              "Triggers": [ { "Mouse": "XButton2", "TapCount": 1 } ],
              "Routing": { "BrowserProcesses": ["brave"], "Overrides": {} },
              "Zoom": { "Browser": { "MarginPx": 40, "AnimationMs": 999 }, "Keys": { "Scale": 2.5 } }
            }
            """;

        var settings = JsonSerializer.Deserialize<SmartZoomSettings>(Old, Json)!;

        Assert.Single(settings.Triggers);
        Assert.Equal(MouseButton.XButton2, settings.Triggers[0].Mouse);
        Assert.Empty(settings.Routing.Apps);
        Assert.Equal(16, settings.Zoom.Smart.MarginPx);
        Assert.Equal(2.0, settings.Zoom.Reader.Magnification);
    }
}
