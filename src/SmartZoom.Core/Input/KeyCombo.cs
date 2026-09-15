using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace SmartZoom.Core.Input;

/// <summary>Modifier keys, left and right variants folded together.</summary>
[Flags]
public enum KeyModifiers : byte
{
    /// <summary>No modifier.</summary>
    None = 0,

    /// <summary>Either Control key.</summary>
    Control = 1,

    /// <summary>Either Alt key.</summary>
    Alt = 2,

    /// <summary>Either Shift key.</summary>
    Shift = 4,

    /// <summary>Either Windows key.</summary>
    Win = 8,
}

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

/// <summary>Virtual-key code helpers. Codes follow the Win32 <c>VK_*</c> constants.</summary>
public static class VirtualKeys
{
    /// <summary>VK_SHIFT.</summary>
    public const ushort Shift = 0x10;

    /// <summary>VK_CONTROL.</summary>
    public const ushort Control = 0x11;

    /// <summary>VK_MENU (Alt).</summary>
    public const ushort Alt = 0x12;

    /// <summary>VK_LWIN; used as the generic Windows key.</summary>
    public const ushort Win = 0x5B;

    private const ushort RightWin = 0x5C;
    private const ushort LeftShift = 0xA0;
    private const ushort RightShift = 0xA1;
    private const ushort LeftControl = 0xA2;
    private const ushort RightControl = 0xA3;
    private const ushort LeftAlt = 0xA4;
    private const ushort RightAlt = 0xA5;

    private static readonly Dictionary<string, ushort> NamedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Ctrl"] = Control, ["Control"] = Control,
        ["Alt"] = Alt, ["Menu"] = Alt,
        ["Shift"] = Shift,
        ["Win"] = Win, ["Windows"] = Win, ["Meta"] = Win, ["Super"] = Win,
        ["Space"] = 0x20, ["Enter"] = 0x0D, ["Return"] = 0x0D, ["Tab"] = 0x09,
        ["Esc"] = 0x1B, ["Escape"] = 0x1B, ["Backspace"] = 0x08,
        ["Insert"] = 0x2D, ["Delete"] = 0x2E, ["Del"] = 0x2E,
        ["Home"] = 0x24, ["End"] = 0x23, ["PageUp"] = 0x21, ["PgUp"] = 0x21, ["PageDown"] = 0x22, ["PgDn"] = 0x22,
        ["Left"] = 0x25, ["Up"] = 0x26, ["Right"] = 0x27, ["Down"] = 0x28,
        ["CapsLock"] = 0x14, ["NumLock"] = 0x90, ["ScrollLock"] = 0x91,
        ["Pause"] = 0x13, ["PrintScreen"] = 0x2C, ["Apps"] = 0x5D,
        ["Plus"] = 0xBB, ["Minus"] = 0xBD, ["Comma"] = 0xBC, ["Period"] = 0xBE,
        ["Semicolon"] = 0xBA, ["Slash"] = 0xBF, ["Backslash"] = 0xDC, ["Tilde"] = 0xC0,
        ["OpenBracket"] = 0xDB, ["CloseBracket"] = 0xDD, ["Quote"] = 0xDE,
        ["Numpad0"] = 0x60, ["Numpad1"] = 0x61, ["Numpad2"] = 0x62, ["Numpad3"] = 0x63, ["Numpad4"] = 0x64,
        ["Numpad5"] = 0x65, ["Numpad6"] = 0x66, ["Numpad7"] = 0x67, ["Numpad8"] = 0x68, ["Numpad9"] = 0x69,
        ["NumpadMultiply"] = 0x6A, ["NumpadAdd"] = 0x6B, ["NumpadSubtract"] = 0x6D, ["NumpadDecimal"] = 0x6E, ["NumpadDivide"] = 0x6F,
    };

    private static readonly Dictionary<ushort, string> KeyNames = BuildKeyNames();

    /// <summary>Folds left/right modifier variants into their generic code so combos compare by role, not side.</summary>
    public static ushort Fold(ushort virtualKey) => virtualKey switch
    {
        LeftShift or RightShift => Shift,
        LeftControl or RightControl => Control,
        LeftAlt or RightAlt => Alt,
        RightWin => Win,
        _ => virtualKey,
    };

    /// <summary>Maps a (folded) modifier virtual key to its <see cref="KeyModifiers"/> flag.</summary>
    public static bool TryGetModifier(ushort virtualKey, out KeyModifiers modifier)
    {
        modifier = Fold(virtualKey) switch
        {
            Shift => KeyModifiers.Shift,
            Control => KeyModifiers.Control,
            Alt => KeyModifiers.Alt,
            Win => KeyModifiers.Win,
            _ => KeyModifiers.None,
        };
        return modifier != KeyModifiers.None;
    }

    /// <summary>Parses a single key name: a letter, digit, F1–F24, or one of the named keys.</summary>
    public static bool TryParseKey(string token, out ushort virtualKey)
    {
        ArgumentNullException.ThrowIfNull(token);
        virtualKey = 0;

        if (token.Length == 1)
        {
            var c = char.ToUpperInvariant(token[0]);
            if (c is (>= 'A' and <= 'Z') or (>= '0' and <= '9'))
            {
                virtualKey = c;
                return true;
            }

            return false;
        }

        if ((token[0] is 'F' or 'f')
            && int.TryParse(token.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var f)
            && f is >= 1 and <= 24)
        {
            virtualKey = (ushort)(0x70 + f - 1);
            return true;
        }

        return NamedKeys.TryGetValue(token, out virtualKey);
    }

    /// <summary>Parses a modifier name (Ctrl, Alt, Shift, Win and their aliases).</summary>
    public static bool TryParseModifier(string token, out KeyModifiers modifier)
    {
        modifier = KeyModifiers.None;
        return NamedKeys.TryGetValue(token, out var vk) && TryGetModifier(vk, out modifier);
    }

    /// <summary>Canonical name for a virtual key, or "0x.." for unknown codes.</summary>
    public static string GetName(ushort virtualKey) =>
        KeyNames.TryGetValue(Fold(virtualKey), out var name) ? name : $"0x{virtualKey:X2}";

    private static Dictionary<ushort, string> BuildKeyNames()
    {
        var names = new Dictionary<ushort, string>();

        // First alias registered wins, so the dictionary above lists the canonical name first.
        foreach (var (name, vk) in NamedKeys)
            names.TryAdd(vk, name);

        for (var c = 'A'; c <= 'Z'; c++) names[c] = c.ToString();
        for (var c = '0'; c <= '9'; c++) names[c] = c.ToString();
        for (var f = 1; f <= 24; f++) names[(ushort)(0x70 + f - 1)] = $"F{f}";

        return names;
    }
}
