using SmartZoom.Core.Input;

namespace SmartZoom.Core.Tests.Input;

public sealed class KeyComboTests
{
    [Theory]
    [InlineData("Ctrl+Alt+Z", KeyModifiers.Control | KeyModifiers.Alt, (ushort)'Z')]
    [InlineData("ctrl + alt + z", KeyModifiers.Control | KeyModifiers.Alt, (ushort)'Z')]
    [InlineData("Control+Shift+F9", KeyModifiers.Control | KeyModifiers.Shift, (ushort)0x78)]
    [InlineData("Win+Space", KeyModifiers.Win, (ushort)0x20)]
    [InlineData("F12", KeyModifiers.None, (ushort)0x7B)]
    [InlineData("Ctrl", KeyModifiers.None, VirtualKeys.Control)]
    [InlineData("Alt", KeyModifiers.None, VirtualKeys.Alt)]
    [InlineData("Ctrl+Shift", KeyModifiers.Control, VirtualKeys.Shift)]
    [InlineData("Numpad5", KeyModifiers.None, (ushort)0x65)]
    [InlineData("7", KeyModifiers.None, (ushort)'7')]
    public void Parses_valid_combinations(string text, KeyModifiers modifiers, ushort key)
    {
        var combo = KeyCombo.Parse(text);

        Assert.Equal(modifiers, combo.Modifiers);
        Assert.Equal(key, combo.Key);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("+")]
    [InlineData("Ctrl+")]
    [InlineData("Z+Ctrl")]        // key must be last
    [InlineData("Ctrl+Ctrl")]     // duplicate modifier
    [InlineData("Ctrl+Ctrl+Z")]   // duplicate modifier
    [InlineData("F25")]
    [InlineData("F0")]
    [InlineData("Banana")]
    [InlineData("é")]
    public void Rejects_invalid_combinations(string text)
    {
        Assert.False(KeyCombo.TryParse(text, out _));
        Assert.Throws<FormatException>(() => KeyCombo.Parse(text));
    }

    [Theory]
    [InlineData("ctrl+alt+z", "Ctrl+Alt+Z")]
    [InlineData("shift+control+win+alt+f5", "Ctrl+Alt+Shift+Win+F5")]
    [InlineData("control", "Ctrl")]
    [InlineData("windows", "Win")]
    [InlineData("Return", "Enter")]
    [InlineData("PgUp", "PageUp")]
    public void Formats_canonically(string text, string expected) =>
        Assert.Equal(expected, KeyCombo.Parse(text).ToString());

    [Theory]
    [InlineData(0xA0, VirtualKeys.Shift)]
    [InlineData(0xA1, VirtualKeys.Shift)]
    [InlineData(0xA2, VirtualKeys.Control)]
    [InlineData(0xA3, VirtualKeys.Control)]
    [InlineData(0xA4, VirtualKeys.Alt)]
    [InlineData(0xA5, VirtualKeys.Alt)]
    [InlineData(0x5C, VirtualKeys.Win)]
    [InlineData((ushort)'Q', (ushort)'Q')]
    public void Folds_left_and_right_modifier_variants(ushort raw, ushort expected) =>
        Assert.Equal(expected, VirtualKeys.Fold(raw));

    [Fact]
    public void Unknown_key_codes_format_as_hex() =>
        Assert.Equal("0xFF", VirtualKeys.GetName(0xFF));
}
