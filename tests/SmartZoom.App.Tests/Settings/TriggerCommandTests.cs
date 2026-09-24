using Microsoft.Extensions.Logging.Abstractions;

using SmartZoom.App.Settings;
using SmartZoom.App.Tests.Diagnostics;
using SmartZoom.Core.Input;
using SmartZoom.Core.Settings;

namespace SmartZoom.App.Tests.Settings;

/// <summary>
/// The installer's trigger job, minus the dialog. <c>TryRun</c> is only exercised with arguments that do
/// not write, because it writes to the real settings file; the writing half is tested through
/// <c>Persist</c> over a directory of the test's own.
/// </summary>
public sealed class TriggerCommandTests
{
    private const uint SystemDoubleClickMs = 500;

    private static SettingsStore CreateStore(TempDirectory temp) =>
        new(DiagnosticFixtures.Paths(temp.Path), NullLogger<SettingsStore>.Instance);

    public sealed class TryRun
    {
        [Fact]
        public void Leaves_ordinary_arguments_to_the_app()
        {
            Assert.False(TriggerCommand.TryRun([], out _));
            Assert.False(TriggerCommand.TryRun(["--quit"], out _));
        }

        [Fact]
        public void Refuses_a_trigger_switch_with_nothing_after_it()
        {
            Assert.True(TriggerCommand.TryRun(["--trigger"], out var exitCode));
            Assert.Equal(1, exitCode);
        }
    }

    public sealed class Parse
    {
        [Fact]
        public void Reads_a_mouse_button_by_name_in_any_case()
        {
            var trigger = TriggerCommand.Parse("xbutton2");

            Assert.NotNull(trigger);
            Assert.Equal(MouseButton.XButton2, trigger.Mouse);
            Assert.Null(trigger.Keys);
        }

        [Fact]
        public void Reads_anything_else_as_a_key_combination()
        {
            var trigger = TriggerCommand.Parse("ctrl+alt+z");

            Assert.NotNull(trigger);
            Assert.Null(trigger.Mouse);
            Assert.Equal("Ctrl+Alt+Z", trigger.Keys);
        }

        [Theory]
        [InlineData("Ctrl+LeftClick", MouseButton.Left, "Ctrl")]
        [InlineData("alt+rightclick", MouseButton.Right, "Alt")]
        [InlineData("Shift + Ctrl + MiddleClick", MouseButton.Middle, "Ctrl+Shift")]
        [InlineData("Ctrl+Middle", MouseButton.Middle, "Ctrl")]
        [InlineData("Ctrl+XButton2", MouseButton.XButton2, "Ctrl")]
        public void Reads_a_click_with_modifiers(string spec, MouseButton button, string modifiers)
        {
            var trigger = TriggerCommand.Parse(spec);

            Assert.NotNull(trigger);
            Assert.Equal(button, trigger.Mouse);
            Assert.Equal(modifiers, trigger.Modifiers);
            Assert.Null(trigger.Keys);
        }

        [Fact]
        public void Reads_a_click_suffix_alone_as_that_button_with_no_modifiers()
        {
            // Persist refuses it later, because a bare left click can't be a trigger; parsing is not where that is decided.
            var trigger = TriggerCommand.Parse("LeftClick");

            Assert.NotNull(trigger);
            Assert.Equal(MouseButton.Left, trigger.Mouse);
            Assert.Null(trigger.Modifiers);
        }

        [Theory]
        [InlineData("Left")]
        [InlineData("Ctrl+Right")]
        public void Reads_left_and_right_without_the_click_suffix_as_arrow_keys(string spec)
        {
            var trigger = TriggerCommand.Parse(spec);

            Assert.NotNull(trigger);
            Assert.Null(trigger.Mouse);
            Assert.Equal(spec, trigger.Keys);
        }

        [Theory]
        [InlineData("Z+LeftClick")]
        [InlineData("Ctrl+Ctrl+LeftClick")]
        [InlineData("+LeftClick")]
        public void Refuses_a_click_preceded_by_anything_but_modifiers(string spec) =>
            Assert.Null(TriggerCommand.Parse(spec));

        [Fact]
        public void Refuses_the_button_that_means_no_button()
        {
            Assert.Null(TriggerCommand.Parse("None"));
        }

        [Fact]
        public void Refuses_text_that_names_neither()
        {
            Assert.Null(TriggerCommand.Parse("the big red one"));
        }
    }

    public sealed class Taps
    {
        [Fact]
        public void Is_one_press_when_the_switch_is_missing()
        {
            Assert.Equal(1, TriggerCommand.Taps(["--trigger", "XButton2"]));
        }

        [Fact]
        public void Reads_the_number_after_the_switch()
        {
            Assert.Equal(2, TriggerCommand.Taps(["--trigger", "Middle", "--taps", "2"]));
            Assert.Equal(2, TriggerCommand.Taps(["--TAPS", "2"]));
        }

        [Fact]
        public void Is_one_press_when_the_switch_has_no_number_after_it()
        {
            Assert.Equal(1, TriggerCommand.Taps(["--taps"]));
            Assert.Equal(1, TriggerCommand.Taps(["--taps", "two"]));
        }
    }

    public sealed class Persist
    {
        [Fact]
        public void Replaces_the_first_trigger_and_keeps_the_rest()
        {
            using var temp = new TempDirectory();
            var store = CreateStore(temp);
            var settings = new SmartZoomSettings
            {
                Triggers =
                [
                    new TriggerSettings { Mouse = MouseButton.XButton2, TapCount = 1, SwallowClicks = true },
                    new TriggerSettings { Keys = "Ctrl+Alt+Z", TapCount = 1 },
                ],
            };
            settings.Zoom.MaxScale = 2.5;
            store.Save(settings);

            var exitCode = TriggerCommand.Persist(
                new TriggerSettings { Mouse = MouseButton.Middle, TapCount = 2 }, store, SystemDoubleClickMs);

            Assert.Equal(0, exitCode);
            var written = store.Load();
            Assert.Equal(2, written.Triggers.Count);
            Assert.Equal(MouseButton.Middle, written.Triggers[0].Mouse);
            Assert.Equal("Ctrl+Alt+Z", written.Triggers[1].Keys);
            Assert.Equal(2.5, written.Zoom.MaxScale);
        }

        [Fact]
        public void Creates_the_file_around_the_trigger_on_a_fresh_machine()
        {
            using var temp = new TempDirectory();
            var store = CreateStore(temp);

            var exitCode = TriggerCommand.Persist(new TriggerSettings { Mouse = MouseButton.XButton1, TapCount = 1 }, store, SystemDoubleClickMs);

            Assert.Equal(0, exitCode);
            var trigger = Assert.Single(store.Load().Triggers);
            Assert.Equal(MouseButton.XButton1, trigger.Mouse);
        }

        [Fact]
        public void Refuses_a_trigger_the_hook_would_refuse_and_writes_nothing()
        {
            using var temp = new TempDirectory();
            var store = CreateStore(temp);

            var exitCode = TriggerCommand.Persist(new TriggerSettings { Mouse = MouseButton.Middle, TapCount = 3 }, store, SystemDoubleClickMs);

            Assert.Equal(1, exitCode);
            Assert.False(File.Exists(DiagnosticFixtures.Paths(temp.Path).SettingsFile));
        }

        [Fact]
        public void Refuses_a_bare_left_click_and_writes_nothing()
        {
            using var temp = new TempDirectory();
            var store = CreateStore(temp);

            var exitCode = TriggerCommand.Persist(new TriggerSettings { Mouse = MouseButton.Left, TapCount = 1 }, store, SystemDoubleClickMs);

            Assert.Equal(1, exitCode);
            Assert.False(File.Exists(DiagnosticFixtures.Paths(temp.Path).SettingsFile));
        }

        [Fact]
        public void Writes_a_click_with_its_modifiers()
        {
            using var temp = new TempDirectory();
            var store = CreateStore(temp);

            var exitCode = TriggerCommand.Persist(new TriggerSettings { Mouse = MouseButton.Left, Modifiers = "Ctrl", TapCount = 1 }, store, SystemDoubleClickMs);

            Assert.Equal(0, exitCode);
            var trigger = Assert.Single(store.Load().Triggers);
            Assert.Equal(MouseButton.Left, trigger.Mouse);
            Assert.Equal("Ctrl", trigger.Modifiers);
        }
    }

    public sealed class Existing
    {
        [Fact]
        public void Is_nothing_on_a_fresh_machine_and_leaves_no_file_behind()
        {
            // Cancel in the installer's recorder must leave a fresh machine exactly as it found it: reading
            // the starting point is not allowed to create the settings file.
            using var temp = new TempDirectory();
            var store = CreateStore(temp);

            Assert.Null(TriggerCommand.Existing(store));
            Assert.False(File.Exists(DiagnosticFixtures.Paths(temp.Path).SettingsFile));
        }

        [Fact]
        public void Is_the_first_configured_trigger()
        {
            using var temp = new TempDirectory();
            var store = CreateStore(temp);
            store.Save(new SmartZoomSettings
            {
                Triggers = [new TriggerSettings { Keys = "F9", TapCount = 1 }, new TriggerSettings { Mouse = MouseButton.Middle }],
            });

            var existing = TriggerCommand.Existing(store);

            Assert.NotNull(existing);
            Assert.Equal("F9", existing.Keys);
        }
    }
}
