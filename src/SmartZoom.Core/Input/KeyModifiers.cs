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
