using System.Diagnostics.CodeAnalysis;

namespace SmartZoom.Core.Input;

/// <summary>
/// A key with required modifiers, e.g. <c>Ctrl+Alt+Z</c>, or a bare modifier such as <c>Ctrl</c>
/// (useful as a double-tap trigger). Keys are Windows virtual-key codes.
/// </summary>
/// <param name="Modifiers">Modifiers that must be held, and no others, when <paramref name="Key"/> is pressed.</param>
/// <param name="Key">The main key as a generic virtual-key code (left/right modifier variants folded, see <see cref="VirtualKeys.Fold"/>).</param>
public readonly record struct KeyCombo(KeyModifiers Modifiers, ushort Key)
{
    /// <summary>Parses a combination like "Ctrl+Alt+Z", "F9", "Win+Space" or a bare "Ctrl". Case-insensitive.</summary>
    /// <exception cref="FormatException">The text is not a valid combination.</exception>
    public static KeyCombo Parse(string text)
    {
        if (!TryParse(text, out var combo))
            throw new FormatException($"'{text}' is not a valid key combination. Expected e.g. \"Ctrl+Alt+Z\", \"F9\" or \"Ctrl\".");

        return combo;
    }

    /// <summary>Parses a combination like "Ctrl+Alt+Z". The last token is the key; the others must be modifiers.</summary>
    public static bool TryParse([NotNullWhen(true)] string? text, out KeyCombo combo)
    {
        combo = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        // Empty tokens are kept so "Ctrl+" and "+Z" are rejected instead of silently becoming "Ctrl" / "Z".
        var tokens = text.Split('+', StringSplitOptions.TrimEntries);
        if (tokens.Length == 0 || Array.Exists(tokens, string.IsNullOrEmpty))
            return false;

        var modifiers = KeyModifiers.None;
        for (var i = 0; i < tokens.Length - 1; i++)
        {
            if (!VirtualKeys.TryParseModifier(tokens[i], out var modifier) || (modifiers & modifier) != 0)
                return false;

            modifiers |= modifier;
        }

        if (!VirtualKeys.TryParseKey(tokens[^1], out var key))
            return false;

        // "Ctrl+Ctrl" makes no sense; a modifier used as the main key can't also be required.
        if (VirtualKeys.TryGetModifier(key, out var keyAsModifier) && (modifiers & keyAsModifier) != 0)
            return false;

        combo = new KeyCombo(modifiers, key);
        return true;
    }

    /// <summary>Canonical text form, e.g. "Ctrl+Alt+Z".</summary>
    public override string ToString()
    {
        var parts = new List<string>(5);
        if ((Modifiers & KeyModifiers.Control) != 0) parts.Add("Ctrl");
        if ((Modifiers & KeyModifiers.Alt) != 0) parts.Add("Alt");
        if ((Modifiers & KeyModifiers.Shift) != 0) parts.Add("Shift");
        if ((Modifiers & KeyModifiers.Win) != 0) parts.Add("Win");
        parts.Add(VirtualKeys.GetName(Key));
        return string.Join('+', parts);
    }
}
