using SmartZoom.App.Settings;
using SmartZoom.Core.Input;
using SmartZoom.Core.Settings;

namespace SmartZoom.App.Tests.Settings;

/// <summary>
/// The dialog itself is a window and is not built here; what is tested is how a trigger fills its two
/// slots, because a slot left over from the previous trigger makes the result name both a button and a key.
/// </summary>
public sealed class TriggerRecorderDialogTests
{
    public sealed class Recorded
    {
        [Fact]
        public void A_mouse_trigger_names_the_button_and_clears_the_combination()
        {
            var (button, combo) = TriggerRecorderDialog.Recorded(new TriggerSettings { Mouse = MouseButton.XButton2 });

            Assert.Equal(MouseButton.XButton2, button);
            Assert.Null(combo);
        }

        [Fact]
        public void A_hotkey_trigger_names_the_combination_and_clears_the_button()
        {
            var (button, combo) = TriggerRecorderDialog.Recorded(new TriggerSettings { Keys = "Ctrl+Alt+Z" });

            Assert.Null(button);
            Assert.Equal(KeyCombo.Parse("Ctrl+Alt+Z"), combo);
        }

        [Fact]
        public void A_combination_that_cannot_be_read_is_treated_as_no_combination()
        {
            var (button, combo) = TriggerRecorderDialog.Recorded(new TriggerSettings { Keys = "not a key" });

            Assert.Null(button);
            Assert.Null(combo);
        }
    }
}
