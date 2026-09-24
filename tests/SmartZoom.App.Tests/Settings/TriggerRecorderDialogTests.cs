using System.Windows.Forms;

using SmartZoom.App.Settings;
using SmartZoom.Core.Input;
using SmartZoom.Core.Settings;

namespace SmartZoom.App.Tests.Settings;

/// <summary>
/// The dialog itself is a window and is not built here; what is tested is how a trigger fills its slots and
/// how the slots become a trigger again, because a slot left over from the previous trigger makes the result
/// name both a button and a key.
/// </summary>
public sealed class TriggerRecorderDialogTests
{
    public sealed class Recorded
    {
        [Fact]
        public void A_mouse_trigger_names_the_button_and_clears_the_combination()
        {
            var (button, modifiers, combo) = TriggerRecorderDialog.Recorded(new TriggerSettings { Mouse = MouseButton.XButton2 });

            Assert.Equal(MouseButton.XButton2, button);
            Assert.Equal(KeyModifiers.None, modifiers);
            Assert.Null(combo);
        }

        [Fact]
        public void A_mouse_trigger_with_modifiers_names_them_too()
        {
            var (button, modifiers, combo) = TriggerRecorderDialog.Recorded(new TriggerSettings { Mouse = MouseButton.Left, Modifiers = "ctrl+alt" });

            Assert.Equal(MouseButton.Left, button);
            Assert.Equal(KeyModifiers.Control | KeyModifiers.Alt, modifiers);
            Assert.Null(combo);
        }

        [Fact]
        public void A_hotkey_trigger_names_the_combination_and_clears_the_button()
        {
            var (button, modifiers, combo) = TriggerRecorderDialog.Recorded(new TriggerSettings { Keys = "Ctrl+Alt+Z" });

            Assert.Null(button);
            Assert.Equal(KeyModifiers.None, modifiers);
            Assert.Equal(KeyCombo.Parse("Ctrl+Alt+Z"), combo);
        }

        [Fact]
        public void A_combination_that_cannot_be_read_is_treated_as_no_combination()
        {
            var (button, _, combo) = TriggerRecorderDialog.Recorded(new TriggerSettings { Keys = "not a key" });

            Assert.Null(button);
            Assert.Null(combo);
        }

        [Fact]
        public void Modifiers_that_cannot_be_read_are_treated_as_none()
        {
            var (button, modifiers, _) = TriggerRecorderDialog.Recorded(new TriggerSettings { Mouse = MouseButton.Middle, Modifiers = "Banana" });

            Assert.Equal(MouseButton.Middle, button);
            Assert.Equal(KeyModifiers.None, modifiers);
        }
    }

    public sealed class Compose
    {
        [Fact]
        public void A_button_with_modifiers_writes_them_in_their_canonical_form()
        {
            var trigger = TriggerRecorderDialog.Compose(MouseButton.Left, KeyModifiers.Shift | KeyModifiers.Control, combo: null, doubleTap: false, windowMs: 500, swallow: true);

            Assert.Equal(MouseButton.Left, trigger.Mouse);
            Assert.Equal("Ctrl+Shift", trigger.Modifiers);
            Assert.Null(trigger.Keys);
            Assert.Equal(1, trigger.TapCount);
            Assert.Null(trigger.DoubleTapWindowMs);
            Assert.True(trigger.SwallowClicks);
        }

        [Fact]
        public void A_button_without_modifiers_writes_none()
        {
            var trigger = TriggerRecorderDialog.Compose(MouseButton.Middle, KeyModifiers.None, combo: null, doubleTap: true, windowMs: 400, swallow: false);

            Assert.Equal(MouseButton.Middle, trigger.Mouse);
            Assert.Null(trigger.Modifiers);
            Assert.Equal(2, trigger.TapCount);
            Assert.Equal(400u, trigger.DoubleTapWindowMs);
        }

        [Fact]
        public void A_hotkey_keeps_its_modifiers_inside_the_combination()
        {
            // Whatever the modifier slot holds belongs to a mouse trigger; a hotkey must not carry it into the file.
            var trigger = TriggerRecorderDialog.Compose(button: null, KeyModifiers.Control, KeyCombo.Parse("Ctrl+Alt+Z"), doubleTap: false, windowMs: 500, swallow: false);

            Assert.Null(trigger.Mouse);
            Assert.Null(trigger.Modifiers);
            Assert.Equal("Ctrl+Alt+Z", trigger.Keys);
        }
    }

    public sealed class Mouse_capture
    {
        [Theory]
        [InlineData(MouseButtons.Left, MouseButton.Left)]
        [InlineData(MouseButtons.Right, MouseButton.Right)]
        [InlineData(MouseButtons.Middle, MouseButton.Middle)]
        [InlineData(MouseButtons.XButton1, MouseButton.XButton1)]
        [InlineData(MouseButtons.XButton2, MouseButton.XButton2)]
        [InlineData(MouseButtons.None, MouseButton.None)]
        public void Every_button_windows_reports_has_a_trigger_button(MouseButtons reported, MouseButton expected) =>
            Assert.Equal(expected, TriggerRecorderDialog.ButtonOf(reported));

        [Fact]
        public void Reads_ctrl_alt_and_shift_from_the_key_state()
        {
            Assert.Equal(KeyModifiers.Control | KeyModifiers.Shift, TriggerRecorderDialog.ModifiersOf(Keys.Control | Keys.Shift));
            Assert.Equal(KeyModifiers.Alt, TriggerRecorderDialog.ModifiersOf(Keys.Alt));
            Assert.Equal(KeyModifiers.None, TriggerRecorderDialog.ModifiersOf(Keys.None));
        }
    }
}
