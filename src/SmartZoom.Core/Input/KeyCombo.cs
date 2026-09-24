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

        var tokens = Split(text);
        if (tokens.Length == 0 || !TryParseModifierTokens(tokens.AsSpan(..^1), out var modifiers))
            return false;

        if (!VirtualKeys.TryParseKey(tokens[^1], out var key))
            return false;

        // "Ctrl+Ctrl" makes no sense; a modifier used as the main key can't also be required.
        if (VirtualKeys.TryGetModifier(key, out var keyAsModifier) && (modifiers & keyAsModifier) != 0)
            return false;

        combo = new KeyCombo(modifiers, key);
        return true;
    }

    /// <summary>Parses a list of modifiers like "Ctrl+Alt" or a single "Shift", with no main key. Case-insensitive.</summary>
    /// <exception cref="FormatException">The text is not a list of modifier keys.</exception>
    public static KeyModifiers ParseModifiers(string text)
    {
        if (!TryParseModifiers(text, out var modifiers))
            throw new FormatException($"'{text}' is not a list of modifier keys. Expected e.g. \"Ctrl\" or \"Ctrl+Alt\".");

        return modifiers;
    }

    /// <summary>Parses a list of modifiers like "Ctrl+Alt", by the same rules as the modifiers of a combination.</summary>
    /// <param name="text">The text to parse.</param>
    /// <param name="modifiers">The modifiers named, each at most once.</param>
    /// <returns>False for empty text, an unknown name, a repeated modifier or a key that is not a modifier.</returns>
    public static bool TryParseModifiers([NotNullWhen(true)] string? text, out KeyModifiers modifiers)
    {
        modifiers = KeyModifiers.None;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var tokens = Split(text);
        return tokens.Length > 0 && TryParseModifierTokens(tokens, out modifiers);
    }

    /// <summary>The modifiers in their canonical order and spelling, e.g. "Ctrl+Alt"; empty for none.</summary>
    /// <param name="modifiers">The modifiers to name.</param>
    public static string FormatModifiers(KeyModifiers modifiers)
    {
        var parts = new List<string>(4);
        if ((modifiers & KeyModifiers.Control) != 0) parts.Add("Ctrl");
        if ((modifiers & KeyModifiers.Alt) != 0) parts.Add("Alt");
        if ((modifiers & KeyModifiers.Shift) != 0) parts.Add("Shift");
        if ((modifiers & KeyModifiers.Win) != 0) parts.Add("Win");
        return string.Join('+', parts);
    }

    /// <summary>Canonical text form, e.g. "Ctrl+Alt+Z".</summary>
    public override string ToString()
    {
        var name = VirtualKeys.GetName(Key);
        return Modifiers == KeyModifiers.None ? name : $"{FormatModifiers(Modifiers)}+{name}";
    }

    /// <summary>
    /// Tokens between the plus signs. Empty tokens are kept so "Ctrl+" and "+Z" are rejected instead of silently
    /// becoming "Ctrl" / "Z"; the empty array stands for text that had one.
    /// </summary>
    private static string[] Split(string text)
    {
        var tokens = text.Split('+', StringSplitOptions.TrimEntries);
        return Array.Exists(tokens, string.IsNullOrEmpty) ? [] : tokens;
    }

    private static bool TryParseModifierTokens(ReadOnlySpan<string> tokens, out KeyModifiers modifiers)
    {
        modifiers = KeyModifiers.None;
        foreach (var token in tokens)
        {
            if (!VirtualKeys.TryParseModifier(token, out var modifier) || (modifiers & modifier) != 0)
                return false;

            modifiers |= modifier;
        }

        return true;
    }
}
