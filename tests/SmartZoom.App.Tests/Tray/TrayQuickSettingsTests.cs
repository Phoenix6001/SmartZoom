using SmartZoom.App.Tray;
using SmartZoom.Core.Input;
using SmartZoom.Core.Routing;
using SmartZoom.Core.Settings;

namespace SmartZoom.App.Tests.Tray;

/// <summary>
/// The settings the tray menu changes. They are functions over a settings object rather than click handlers
/// so that the rules a user relies on — a second trigger survives, switching an application off is reversible
/// — can be checked without a message loop.
/// </summary>
public sealed class TrayQuickSettingsTests
{
    public sealed class The_zoom_amount
    {
        [Fact]
        public void Matches_the_amount_in_force()
        {
            var settings = new SmartZoomSettings();
            settings.Zoom.MaxScale = 2.0;

            Assert.True(TrayQuickSettings.IsZoomAmount(settings, 2.0));
            Assert.False(TrayQuickSettings.IsZoomAmount(settings, 3.0));
        }

        [Fact]
        public void Matches_nothing_when_the_file_names_a_value_the_menu_does_not_offer()
        {
            var settings = new SmartZoomSettings();
            settings.Zoom.MaxScale = 2.4;

            Assert.All(TrayQuickSettings.ZoomAmounts, amount => Assert.False(TrayQuickSettings.IsZoomAmount(settings, amount)));
        }

        [Fact]
        public void Is_changed_on_a_copy_leaving_the_settings_in_force_alone()
        {
            var settings = new SmartZoomSettings();
            settings.Zoom.MaxScale = 3.0;

            var changed = TrayQuickSettings.WithZoomAmount(settings, 1.5);

            Assert.Equal(1.5, changed.Zoom.MaxScale);
            Assert.Equal(3.0, settings.Zoom.MaxScale);
        }

        [Fact]
        public void Leaves_every_other_setting_as_it_was()
        {
            var settings = new SmartZoomSettings();
            settings.Zoom.MinScale = 1.4;
            settings.Diagnostics.Enabled = false;

            var changed = TrayQuickSettings.WithZoomAmount(settings, 2.0);

            Assert.Equal(1.4, changed.Zoom.MinScale);
            Assert.False(changed.Diagnostics.Enabled);
        }
    }

    public sealed class Switching_an_application_off
    {
        [Fact]
        public void Routes_it_to_None()
        {
            var changed = TrayQuickSettings.WithIgnored(new SmartZoomSettings(), "brave", ignored: true);

            Assert.Equal(AdapterId.None, changed.Routing.Apps["brave"]);
            Assert.True(TrayQuickSettings.IsIgnored(changed, "brave"));
        }

        [Fact]
        public void Is_undone_by_removing_the_entry_rather_than_naming_a_strategy()
        {
            // Naming one would freeze today's answer: an application handed back to its default picks up a
            // better strategy in a later version, and the entry is what would stop it.
            var off = TrayQuickSettings.WithIgnored(new SmartZoomSettings(), "brave", ignored: true);

            var on = TrayQuickSettings.WithIgnored(off, "brave", ignored: false);

            Assert.DoesNotContain("brave", on.Routing.Apps);
            Assert.False(TrayQuickSettings.IsIgnored(on, "brave"));
        }

        [Fact]
        public void Matches_the_process_whatever_case_the_file_used()
        {
            var settings = new SmartZoomSettings();
            settings.Routing.Apps["Brave"] = AdapterId.None;

            Assert.True(TrayQuickSettings.IsIgnored(settings, "brave"));
        }

        [Fact]
        public void Leaves_an_application_routed_somewhere_else_reported_as_zoomed()
        {
            var settings = new SmartZoomSettings();
            settings.Routing.Apps["notepad"] = new AdapterId("CtrlWheel");

            Assert.False(TrayQuickSettings.IsIgnored(settings, "notepad"));
        }

        [Fact]
        public void Keeps_the_other_applications()
        {
            var settings = new SmartZoomSettings();
            settings.Routing.Apps["notepad"] = new AdapterId("CtrlWheel");

            var changed = TrayQuickSettings.WithIgnored(settings, "brave", ignored: true);

            Assert.Equal(new AdapterId("CtrlWheel"), changed.Routing.Apps["notepad"]);
        }
    }

    public sealed class Recording_the_trigger_again
    {
        [Fact]
        public void Replaces_the_one_the_menu_showed_and_keeps_the_rest()
        {
            // The regression this exists for: replacing the whole list deletes a hotkey the user never saw.
            var settings = new SmartZoomSettings
            {
                Triggers =
                [
                    new TriggerSettings { Mouse = MouseButton.XButton2, TapCount = 1 },
                    new TriggerSettings { Keys = "Ctrl+Alt+Z", TapCount = 1 },
                ],
            };

            var changed = TrayQuickSettings.WithFirstTrigger(settings, new TriggerSettings { Mouse = MouseButton.Middle, TapCount = 2 });

            Assert.Equal(2, changed.Triggers.Count);
            Assert.Equal(MouseButton.Middle, changed.Triggers[0].Mouse);
            Assert.Equal("Ctrl+Alt+Z", changed.Triggers[1].Keys);
        }

        [Fact]
        public void Adds_one_when_the_file_has_none()
        {
            var settings = new SmartZoomSettings { Triggers = [] };

            var changed = TrayQuickSettings.WithFirstTrigger(settings, new TriggerSettings { Mouse = MouseButton.Middle });

            Assert.Equal(MouseButton.Middle, Assert.Single(changed.Triggers).Mouse);
        }

        [Fact]
        public void Leaves_the_settings_in_force_alone()
        {
            var settings = new SmartZoomSettings
            {
                Triggers = [new TriggerSettings { Mouse = MouseButton.XButton2, TapCount = 1 }],
            };

            TrayQuickSettings.WithFirstTrigger(settings, new TriggerSettings { Mouse = MouseButton.Middle });

            Assert.Equal(MouseButton.XButton2, settings.Triggers[0].Mouse);
        }

        [Fact]
        public void Opens_the_recorder_on_the_first_trigger()
        {
            var settings = new SmartZoomSettings
            {
                Triggers = [new TriggerSettings { Keys = "Ctrl+Alt+Z" }, new TriggerSettings { Mouse = MouseButton.Middle }],
            };

            Assert.Equal("Ctrl+Alt+Z", TrayQuickSettings.FirstTrigger(settings)?.Keys);
        }

        [Fact]
        public void Opens_the_recorder_empty_when_the_file_has_no_trigger()
        {
            Assert.Null(TrayQuickSettings.FirstTrigger(new SmartZoomSettings { Triggers = [] }));
        }
    }
}
