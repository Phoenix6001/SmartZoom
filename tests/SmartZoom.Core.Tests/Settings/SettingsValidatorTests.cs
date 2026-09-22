using SmartZoom.Core.Input;
using SmartZoom.Core.Routing;
using SmartZoom.Core.Settings;
using SmartZoom.Core.Zoom;
using SmartZoom.Core.Zoom.Office;
using SmartZoom.Core.Zoom.Reader;

namespace SmartZoom.Core.Tests.Settings;

public sealed class SettingsValidatorTests
{
    private const uint SystemDoubleClick = 500;

    private static readonly AdapterDescriptor[] Adapters =
    [
        CtrlWheelAdapter.Descriptor,
        BrowserAdapter.Descriptor,
        ReaderAdapter.Descriptor,
        WordComAdapter.Descriptor,
        ExcelComAdapter.Descriptor,
    ];

    [Fact]
    public void The_shipped_defaults_are_valid() =>
        Assert.Empty(Validate(new SmartZoomSettings()));

    public sealed class Triggers
    {
        [Fact]
        public void A_file_with_no_triggers_is_an_error()
        {
            // The hook's constructor refuses an empty set, so this would otherwise be a failure to start.
            var problem = Assert.Single(Validate(With(s => s.Triggers = [])));

            Assert.Equal(SettingsProblemSeverity.Error, problem.Severity);
            Assert.Equal("Triggers", problem.Section);
        }

        [Fact]
        public void A_trigger_with_neither_a_button_nor_keys_is_an_error()
        {
            var problem = Assert.Single(Validate(With(s => s.Triggers = [new TriggerSettings()])));

            Assert.Equal("Triggers[0]", problem.Section);
            Assert.Contains("needs either", problem.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void A_trigger_with_both_a_button_and_keys_is_an_error()
        {
            var both = new TriggerSettings { Mouse = MouseButton.Middle, Keys = "F9" };

            var problem = Assert.Single(Validate(With(s => s.Triggers = [both])));

            Assert.Contains("can't have both", problem.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void Keys_that_are_not_a_combination_are_an_error()
        {
            var problem = Assert.Single(Validate(With(s => s.Triggers = [new TriggerSettings { Keys = "Ctrl+" }])));

            Assert.Equal("Triggers[0]", problem.Section);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(3)]
        public void A_tap_count_the_detector_refuses_is_an_error(int taps)
        {
            // TapDetector owns this rule, and it is not constructed until the hook is - which is after the
            // point where a bad value could still be reported gently.
            var trigger = new TriggerSettings { Mouse = MouseButton.XButton2, TapCount = taps };

            var problem = Assert.Single(Validate(With(s => s.Triggers = [trigger])));

            Assert.Contains("Tap count", problem.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void The_parameter_name_is_not_shown_to_the_user()
        {
            var trigger = new TriggerSettings { Mouse = MouseButton.XButton2, TapCount = 9 };

            var problem = Assert.Single(Validate(With(s => s.Triggers = [trigger])));

            Assert.DoesNotContain("Parameter", problem.Message, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData(0u)]
        [InlineData(6000u)]
        public void A_double_tap_window_the_detector_refuses_is_an_error(uint window)
        {
            var trigger = new TriggerSettings { Mouse = MouseButton.XButton2, DoubleTapWindowMs = window };

            var problem = Assert.Single(Validate(With(s => s.Triggers = [trigger])));

            Assert.Contains("Double-tap window", problem.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void Two_triggers_on_the_same_button_is_an_error_naming_the_other_one()
        {
            var settings = With(s => s.Triggers =
            [
                new TriggerSettings { Mouse = MouseButton.XButton2 },
                new TriggerSettings { Mouse = MouseButton.XButton2, TapCount = 1 },
            ]);

            var problem = Assert.Single(Validate(settings));

            Assert.Equal("Triggers[1]", problem.Section);
            Assert.Contains("trigger 1", problem.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void Two_triggers_on_the_same_combination_is_an_error_however_it_was_spelled()
        {
            var settings = With(s => s.Triggers =
            [
                new TriggerSettings { Keys = "Ctrl+Alt+Z" },
                new TriggerSettings { Keys = "ctrl+alt+z" },
            ]);

            var problem = Assert.Single(Validate(settings));

            Assert.Equal("Triggers[1]", problem.Section);
        }

        [Fact]
        public void A_button_and_a_hotkey_never_collide()
        {
            var settings = With(s => s.Triggers =
            [
                new TriggerSettings { Mouse = MouseButton.XButton2 },
                new TriggerSettings { Keys = "Ctrl+Alt+Z" },
            ]);

            Assert.Empty(Validate(settings));
        }
    }

    public sealed class Routing
    {
        [Fact]
        public void An_application_routed_to_a_strategy_this_build_lacks_is_a_warning_not_an_error()
        {
            // It still works: the coordinator says so once and falls back to Ctrl+wheel.
            var settings = With(s => s.Routing.Apps["POWERPNT"] = new AdapterId("PowerPointCom"));

            var problem = Assert.Single(Validate(settings));

            Assert.Equal(SettingsProblemSeverity.Warning, problem.Severity);
            Assert.Contains("PowerPointCom", problem.Message, StringComparison.Ordinal);
            Assert.Contains("CtrlWheel", problem.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void Routing_an_application_to_None_is_fine() =>
            Assert.Empty(Validate(With(s => s.Routing.Apps["EXCEL"] = AdapterId.None)));

        [Fact]
        public void Two_adapters_claiming_one_application_is_an_error()
        {
            var rival = new AdapterDescriptor("Rival", ["EXCEL"], "Rival", "Claims Excel too.");

            var problem = Assert.Single(Validate(new SmartZoomSettings(), [.. Adapters, rival]));

            Assert.Equal(SettingsProblemSeverity.Error, problem.Severity);
            Assert.Equal("Routing", problem.Section);
        }
    }

    public sealed class Zoom
    {
        [Theory]
        [InlineData(1.0)]
        [InlineData(0.5)]
        public void A_smallest_zoom_that_does_not_magnify_is_an_error(double min)
        {
            var problems = Validate(With(s => s.Zoom.MinScale = min));

            Assert.Contains(problems, p => p.Section == "Zoom.MinScale");
        }

        [Fact]
        public void A_largest_zoom_below_the_smallest_is_an_error()
        {
            var settings = With(s =>
            {
                s.Zoom.MinScale = 2.0;
                s.Zoom.MaxScale = 1.5;
            });

            var problem = Assert.Single(Validate(settings));

            Assert.Equal("Zoom.MaxScale", problem.Section);
        }

        [Fact]
        public void A_pinch_that_does_not_magnify_is_an_error()
        {
            // ReaderPinchAdapter's constructor throws on this, which would stop the app starting.
            var problem = Assert.Single(Validate(With(s => s.Zoom.Reader.Magnification = 1.0)));

            Assert.Equal("Zoom.Reader.Magnification", problem.Section);
        }

        [Fact]
        public void The_same_magnification_is_fine_in_shortcuts_mode_where_nothing_reads_it()
        {
            var settings = With(s =>
            {
                s.Zoom.Reader.Mode = ReaderZoomMode.Shortcuts;
                s.Zoom.Reader.Magnification = 1.0;
            });

            Assert.Empty(Validate(settings));
        }

        [Fact]
        public void Reader_keys_that_are_not_a_combination_are_an_error()
        {
            var problem = Assert.Single(Validate(With(s => s.Zoom.Reader.ZoomOutKeys = "Ctrl+")));

            Assert.Equal("Zoom.Reader.ZoomOutKeys", problem.Section);
        }

        [Fact]
        public void No_wheel_ticks_is_a_warning_because_the_strategy_then_does_nothing()
        {
            var problem = Assert.Single(Validate(With(s => s.Zoom.CtrlWheel.Ticks = 0)));

            Assert.Equal(SettingsProblemSeverity.Warning, problem.Severity);
        }

        [Fact]
        public void A_negative_animation_is_an_error()
        {
            var problem = Assert.Single(Validate(With(s => s.Zoom.Smart.AnimationMs = -1)));

            Assert.Equal("Zoom.Smart.AnimationMs", problem.Section);
        }
    }

    [Fact]
    public void Errors_are_listed_before_warnings()
    {
        var settings = With(s =>
        {
            s.Zoom.CtrlWheel.Ticks = 0;                 // warning
            s.Zoom.Reader.ZoomInKeys = "Ctrl+";         // error
        });

        var problems = Validate(settings);

        Assert.Equal(2, problems.Count);
        Assert.Equal(SettingsProblemSeverity.Error, problems[0].Severity);
        Assert.Equal(SettingsProblemSeverity.Warning, problems[1].Severity);
    }

    private static SmartZoomSettings With(Action<SmartZoomSettings> change)
    {
        var settings = new SmartZoomSettings();
        change(settings);
        return settings;
    }

    private static IReadOnlyList<SettingsProblem> Validate(SmartZoomSettings settings, AdapterDescriptor[]? adapters = null) =>
        SettingsValidator.Validate(settings, adapters ?? Adapters, SystemDoubleClick);
}
