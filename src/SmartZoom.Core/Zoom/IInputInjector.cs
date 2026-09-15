namespace SmartZoom.Core.Zoom;

/// <summary>Keyboard modifiers an adapter may hold while injecting other input.</summary>
public enum ModifierKey
{
    /// <summary>The Control key.</summary>
    Control,
}

/// <summary>Synthesizes user input. The Windows implementation wraps <c>SendInput</c>.</summary>
/// <remarks>Every method returns false instead of throwing when the OS rejects the input, most commonly
/// because the foreground window is elevated (UIPI). Adapters must treat that as a soft failure.</remarks>
public interface IInputInjector
{
    /// <summary>Presses or releases a modifier key.</summary>
    /// <param name="key">Which modifier.</param>
    /// <param name="isDown">True to press, false to release.</param>
    bool TrySendModifier(ModifierKey key, bool isDown);

    /// <summary>Scrolls the vertical wheel at the current cursor position.</summary>
    /// <param name="ticks">Positive scrolls up (zoom in for Ctrl+wheel), negative scrolls down. One tick is one detent.</param>
    bool TrySendWheel(int ticks);

    /// <summary>Whether the user is physically holding a modifier right now.</summary>
    /// <param name="key">Which modifier.</param>
    bool IsModifierDown(ModifierKey key);
}
