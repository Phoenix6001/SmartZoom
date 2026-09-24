using SmartZoom.Core.Input;

namespace SmartZoom.Core.Tests.Input;

public sealed class HotkeyMatcherTests
{
    private const ushort LeftControl = 0xA2;
    private const ushort RightControl = 0xA3;
    private const ushort LeftAlt = 0xA4;
    private const ushort LeftShift = 0xA0;
    private const ushort Z = 'Z';

    private readonly ModifierTracker _modifiers = new();

    /// <summary>Feeds one raw key event through the tracker and the matcher, like the hook does.</summary>
    private KeyMatch Key(HotkeyMatcher matcher, ushort vk, bool isDown) =>
        matcher.OnKey(vk, isDown, _modifiers.OnKey(vk, isDown));

    [Fact]
    public void Combo_fires_when_exactly_its_modifiers_are_held()
    {
        var matcher = new HotkeyMatcher(KeyCombo.Parse("Ctrl+Alt+Z"));

        Assert.Equal(KeyMatch.None, Key(matcher, LeftControl, true));
        Assert.Equal(KeyMatch.None, Key(matcher, LeftAlt, true));
        Assert.Equal(KeyMatch.Press, Key(matcher, Z, true));
        Assert.True(matcher.IsPressed);
        Assert.Equal(KeyMatch.Release, Key(matcher, Z, false));
        Assert.False(matcher.IsPressed);
    }

    [Fact]
    public void Right_side_modifiers_count_the_same_as_left()
    {
        var matcher = new HotkeyMatcher(KeyCombo.Parse("Ctrl+Z"));

        Key(matcher, RightControl, true);

        Assert.Equal(KeyMatch.Press, Key(matcher, Z, true));
    }

    [Fact]
    public void Extra_modifier_prevents_a_match()
    {
        var matcher = new HotkeyMatcher(KeyCombo.Parse("Ctrl+Z"));

        Key(matcher, LeftControl, true);
        Key(matcher, LeftShift, true);

        Assert.Equal(KeyMatch.None, Key(matcher, Z, true));
        Assert.Equal(KeyMatch.None, Key(matcher, Z, false)); // never pressed, so no release either
    }

    [Fact]
    public void Missing_modifier_prevents_a_match()
    {
        var matcher = new HotkeyMatcher(KeyCombo.Parse("Ctrl+Z"));

        Assert.Equal(KeyMatch.None, Key(matcher, Z, true));
    }

    [Fact]
    public void Auto_repeat_is_reported_separately_until_release()
    {
        var matcher = new HotkeyMatcher(KeyCombo.Parse("F9"));

        Assert.Equal(KeyMatch.Press, Key(matcher, 0x78, true));
        Assert.Equal(KeyMatch.Repeat, Key(matcher, 0x78, true));
        Assert.Equal(KeyMatch.Repeat, Key(matcher, 0x78, true));
        Assert.Equal(KeyMatch.Release, Key(matcher, 0x78, false));
        Assert.Equal(KeyMatch.Press, Key(matcher, 0x78, true));
    }

    [Fact]
    public void Release_matches_even_after_the_modifier_was_lifted_first()
    {
        var matcher = new HotkeyMatcher(KeyCombo.Parse("Ctrl+Z"));

        Key(matcher, LeftControl, true);
        Key(matcher, Z, true);
        Key(matcher, LeftControl, false);

        Assert.Equal(KeyMatch.Release, Key(matcher, Z, false));
    }

    [Fact]
    public void Bare_modifier_combo_fires_on_the_modifier_itself()
    {
        var matcher = new HotkeyMatcher(KeyCombo.Parse("Ctrl"));

        Assert.Equal(KeyMatch.Press, Key(matcher, LeftControl, true));
        Assert.Equal(KeyMatch.Release, Key(matcher, LeftControl, false));
        Assert.Equal(KeyMatch.Press, Key(matcher, RightControl, true));
    }

    [Fact]
    public void Bare_modifier_does_not_fire_while_another_modifier_is_held()
    {
        var matcher = new HotkeyMatcher(KeyCombo.Parse("Ctrl"));

        Key(matcher, LeftShift, true);

        Assert.Equal(KeyMatch.None, Key(matcher, LeftControl, true));
    }

    [Fact]
    public void Other_keys_are_ignored()
    {
        var matcher = new HotkeyMatcher(KeyCombo.Parse("Ctrl+Z"));
        Key(matcher, LeftControl, true);

        Assert.Equal(KeyMatch.None, Key(matcher, 'Y', true));
        Assert.Equal(KeyMatch.None, Key(matcher, 'Y', false));
    }

    [Fact]
    public void Reset_forgets_an_in_progress_press()
    {
        var matcher = new HotkeyMatcher(KeyCombo.Parse("F9"));
        Key(matcher, 0x78, true);

        matcher.Reset();

        Assert.False(matcher.IsPressed);
        Assert.Equal(KeyMatch.None, Key(matcher, 0x78, false));
    }

    public sealed class The_modifier_tracker
    {
        [Fact]
        public void Tracks_left_and_right_sides_independently()
        {
            var tracker = new ModifierTracker();

            tracker.OnKey(LeftControl, true);
            tracker.OnKey(RightControl, true);
            Assert.Equal(KeyModifiers.Control, tracker.OnKey(LeftControl, false)); // right still held
            Assert.Equal(KeyModifiers.None, tracker.OnKey(RightControl, false));
        }

        [Fact]
        public void Accumulates_multiple_modifiers()
        {
            var tracker = new ModifierTracker();

            tracker.OnKey(LeftControl, true);
            tracker.OnKey(LeftShift, true);
            Assert.Equal(KeyModifiers.Control | KeyModifiers.Shift, tracker.OnKey(0x5B, true) & ~KeyModifiers.Win);
            Assert.Equal(KeyModifiers.Control | KeyModifiers.Shift | KeyModifiers.Win, tracker.Held);
        }

        [Fact]
        public void Accepts_generic_codes_used_by_injected_input()
        {
            var tracker = new ModifierTracker();

            Assert.Equal(KeyModifiers.Alt, tracker.OnKey(VirtualKeys.Alt, true));
            Assert.Equal(KeyModifiers.None, tracker.OnKey(VirtualKeys.Alt, false));
        }

        [Fact]
        public void Ignores_non_modifier_keys()
        {
            var tracker = new ModifierTracker();

            Assert.Equal(KeyModifiers.None, tracker.OnKey('A', true));
        }

        [Fact]
        public void Reset_clears_everything()
        {
            var tracker = new ModifierTracker();
            tracker.OnKey(LeftControl, true);

            tracker.Reset();

            Assert.Equal(KeyModifiers.None, tracker.Held);
        }
    }
}
