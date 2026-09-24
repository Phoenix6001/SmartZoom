using SmartZoom.Core.Input;

namespace SmartZoom.Core.Tests.Input;

public sealed class MouseButtonTriggerTests
{
    private static readonly TapOptions Once = new(1, 500, true);
    private static readonly TapOptions Twice = new(2, 500, false);

    public sealed class Creating_one
    {
        [Theory]
        [InlineData(MouseButton.Left)]
        [InlineData(MouseButton.Right)]
        public void Refuses_a_left_or_right_click_with_no_modifiers(MouseButton button)
        {
            var ex = Assert.Throws<InvalidOperationException>(() => new MouseButtonTrigger(button, KeyModifiers.None, Once));

            Assert.Equal("A left or right click needs a modifier key such as Ctrl or Alt to be a trigger.", ex.Message);
        }

        [Theory]
        [InlineData(MouseButton.Left)]
        [InlineData(MouseButton.Right)]
        public void The_two_argument_form_refuses_them_too(MouseButton button) =>
            Assert.Throws<InvalidOperationException>(() => new MouseButtonTrigger(button, Once));

        [Theory]
        [InlineData(MouseButton.Left, KeyModifiers.Control)]
        [InlineData(MouseButton.Right, KeyModifiers.Alt)]
        [InlineData(MouseButton.Left, KeyModifiers.Shift | KeyModifiers.Win)]
        public void Accepts_a_left_or_right_click_with_any_modifier(MouseButton button, KeyModifiers modifiers)
        {
            var trigger = new MouseButtonTrigger(button, modifiers, Once);

            Assert.Equal(button, trigger.Button);
            Assert.Equal(modifiers, trigger.Modifiers);
        }

        [Theory]
        [InlineData(MouseButton.Middle)]
        [InlineData(MouseButton.XButton1)]
        [InlineData(MouseButton.XButton2)]
        public void Other_buttons_need_no_modifiers(MouseButton button)
        {
            var trigger = new MouseButtonTrigger(button, Once);

            Assert.Equal(button, trigger.Button);
            Assert.Equal(KeyModifiers.None, trigger.Modifiers);
            Assert.Equal(Once, trigger.Tap);
        }

        [Fact]
        public void Two_triggers_on_the_same_input_are_equal_and_different_modifiers_make_them_differ()
        {
            Assert.Equal(new MouseButtonTrigger(MouseButton.Middle, KeyModifiers.Control, Once), new MouseButtonTrigger(MouseButton.Middle, KeyModifiers.Control, Once));
            Assert.NotEqual(new MouseButtonTrigger(MouseButton.Middle, Once), new MouseButtonTrigger(MouseButton.Middle, KeyModifiers.Control, Once));
        }
    }

    public sealed class Display_name
    {
        [Fact]
        public void A_button_alone_is_the_button_and_the_tap_count() =>
            Assert.Equal("XButton2 x1", new MouseButtonTrigger(MouseButton.XButton2, Once).DisplayName);

        [Fact]
        public void Modifiers_come_first_in_the_order_a_hotkey_uses()
        {
            Assert.Equal("Ctrl+Left x1", new MouseButtonTrigger(MouseButton.Left, KeyModifiers.Control, Once).DisplayName);
            Assert.Equal("Ctrl+Alt+Middle x2", new MouseButtonTrigger(MouseButton.Middle, KeyModifiers.Alt | KeyModifiers.Control, Twice).DisplayName);
        }

        [Fact]
        public void Describe_names_the_input_without_the_tap_count()
        {
            Assert.Equal("Middle", MouseButtonTrigger.Describe(MouseButton.Middle, KeyModifiers.None));
            Assert.Equal("Ctrl+Shift+Right", MouseButtonTrigger.Describe(MouseButton.Right, KeyModifiers.Shift | KeyModifiers.Control));
        }
    }

    public sealed class Caution
    {
        [Theory]
        [InlineData(KeyModifiers.Control)]
        [InlineData(KeyModifiers.Shift)]
        [InlineData(KeyModifiers.Control | KeyModifiers.Alt)]
        public void A_left_click_with_ctrl_or_shift_has_one(KeyModifiers modifiers) =>
            Assert.Contains("Alt+click", MouseButtonTrigger.CautionFor(MouseButton.Left, modifiers), StringComparison.Ordinal);

        [Theory]
        [InlineData(MouseButton.Left, KeyModifiers.Alt)]
        [InlineData(MouseButton.Right, KeyModifiers.Control)]
        [InlineData(MouseButton.Middle, KeyModifiers.Shift)]
        [InlineData(MouseButton.XButton2, KeyModifiers.None)]
        public void Anything_else_has_none(MouseButton button, KeyModifiers modifiers) =>
            Assert.Null(MouseButtonTrigger.CautionFor(button, modifiers));
    }
}
