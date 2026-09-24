namespace SmartZoom.Core.Input;

/// <summary>What a raw key event means for one <see cref="KeyCombo"/>.</summary>
public enum KeyMatch : byte
{
    /// <summary>Not this combination's key, or the modifiers didn't match.</summary>
    None,

    /// <summary>The combination was pressed.</summary>
    Press,

    /// <summary>Auto-repeat of a press we already reported. Swallow it if the press was swallowed.</summary>
    Repeat,

    /// <summary>The combination's key was released after a reported press.</summary>
    Release,
}
