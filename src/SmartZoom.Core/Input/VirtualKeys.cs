using System.Globalization;

namespace SmartZoom.Core.Input;

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

    /// <summary>VK_RWIN.</summary>
    internal const ushort RightWin = 0x5C;

    /// <summary>VK_LSHIFT.</summary>
    internal const ushort LeftShift = 0xA0;

    /// <summary>VK_RSHIFT.</summary>
    internal const ushort RightShift = 0xA1;

    /// <summary>VK_LCONTROL.</summary>
    internal const ushort LeftControl = 0xA2;

    /// <summary>VK_RCONTROL.</summary>
    internal const ushort RightControl = 0xA3;

    /// <summary>VK_LMENU (left Alt).</summary>
    internal const ushort LeftAlt = 0xA4;

    /// <summary>VK_RMENU (right Alt).</summary>
    internal const ushort RightAlt = 0xA5;

    private static readonly Dictionary<string, ushort> NamedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Ctrl"] = Control,
        ["Control"] = Control,
        ["Alt"] = Alt,
        ["Menu"] = Alt,
        ["Shift"] = Shift,
        ["Win"] = Win,
        ["Windows"] = Win,
        ["Meta"] = Win,
        ["Super"] = Win,
        ["Space"] = 0x20,
        ["Enter"] = 0x0D,
        ["Return"] = 0x0D,
        ["Tab"] = 0x09,
        ["Esc"] = 0x1B,
        ["Escape"] = 0x1B,
        ["Backspace"] = 0x08,
        ["Insert"] = 0x2D,
        ["Delete"] = 0x2E,
        ["Del"] = 0x2E,
        ["Home"] = 0x24,
        ["End"] = 0x23,
        ["PageUp"] = 0x21,
        ["PgUp"] = 0x21,
        ["PageDown"] = 0x22,
        ["PgDn"] = 0x22,
        ["Left"] = 0x25,
        ["Up"] = 0x26,
        ["Right"] = 0x27,
        ["Down"] = 0x28,
        ["CapsLock"] = 0x14,
        ["NumLock"] = 0x90,
        ["ScrollLock"] = 0x91,
        ["Pause"] = 0x13,
        ["PrintScreen"] = 0x2C,
        ["Apps"] = 0x5D,
        ["Plus"] = 0xBB,
        ["Minus"] = 0xBD,
        ["Comma"] = 0xBC,
        ["Period"] = 0xBE,
        ["Semicolon"] = 0xBA,
        ["Slash"] = 0xBF,
        ["Backslash"] = 0xDC,
        ["Tilde"] = 0xC0,
        ["OpenBracket"] = 0xDB,
        ["CloseBracket"] = 0xDD,
        ["Quote"] = 0xDE,
        ["Numpad0"] = 0x60,
        ["Numpad1"] = 0x61,
        ["Numpad2"] = 0x62,
        ["Numpad3"] = 0x63,
        ["Numpad4"] = 0x64,
        ["Numpad5"] = 0x65,
        ["Numpad6"] = 0x66,
        ["Numpad7"] = 0x67,
        ["Numpad8"] = 0x68,
        ["Numpad9"] = 0x69,
        ["NumpadMultiply"] = 0x6A,
        ["NumpadAdd"] = 0x6B,
        ["NumpadSubtract"] = 0x6D,
        ["NumpadDecimal"] = 0x6E,
        ["NumpadDivide"] = 0x6F,
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
