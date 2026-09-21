using SmartZoom.Core.Input;

namespace SmartZoom.Interop.Input;

/// <summary>
/// The Win32 vocabulary of the low-level hooks: the messages they receive, the flags that mark injected
/// input, and the one translation between a message and a <see cref="MouseButton"/>.
/// </summary>
/// <remarks>
/// Kept apart from the hook itself so that what is left there is the concurrency design — the hook thread,
/// its callbacks, the channels and the timer — with nothing in between.
/// </remarks>
internal static class MouseMessages
{
    /// <summary>The hook may only act when the code is this; anything else must be passed straight on.</summary>
    public const int HcAction = 0;

    public const uint WmQuit = 0x0012;
    public const uint WmKeyDown = 0x0100;
    public const uint WmKeyUp = 0x0101;
    public const uint WmSysKeyDown = 0x0104;
    public const uint WmSysKeyUp = 0x0105;
    public const uint WmMButtonDown = 0x0207;
    public const uint WmMButtonUp = 0x0208;
    public const uint WmXButtonDown = 0x020B;
    public const uint WmXButtonUp = 0x020C;

    /// <summary>LLMHF_INJECTED | LLMHF_LOWER_IL_INJECTED.</summary>
    public const uint MouseInjectedMask = 0x3;

    /// <summary>LLKHF_INJECTED | LLKHF_LOWER_IL_INJECTED.</summary>
    public const uint KeyboardInjectedMask = 0x12;

    private const ushort XButton1 = 0x0001;
    private const ushort XButton2 = 0x0002;

    /// <summary>Whether a message is a key press or release, and which.</summary>
    /// <param name="message">The hook's wParam.</param>
    /// <param name="isDown">True for a press.</param>
    /// <returns>False for anything that is not a key transition.</returns>
    public static bool IsKeyTransition(uint message, out bool isDown)
    {
        isDown = message is WmKeyDown or WmSysKeyDown;
        return isDown || message is WmKeyUp or WmSysKeyUp;
    }

    /// <summary>Translates a mouse message into the button it concerns.</summary>
    /// <param name="message">The hook's wParam.</param>
    /// <param name="mouseData">The event's mouseData; for X buttons its high word says which one.</param>
    /// <param name="button">The button, or <see cref="MouseButton.None"/>.</param>
    /// <param name="isDown">True for a press.</param>
    /// <returns>False for messages that are not a button transition SmartZoom can trigger on.</returns>
    public static bool TryMapButton(uint message, uint mouseData, out MouseButton button, out bool isDown)
    {
        switch (message)
        {
            case WmMButtonDown or WmMButtonUp:
                button = MouseButton.Middle;
                isDown = message == WmMButtonDown;
                return true;

            case WmXButtonDown or WmXButtonUp:
                // For X buttons the high word of mouseData identifies which one.
                button = (ushort)(mouseData >> 16) switch
                {
                    XButton1 => MouseButton.XButton1,
                    XButton2 => MouseButton.XButton2,
                    _ => MouseButton.None,
                };
                isDown = message == WmXButtonDown;
                return button != MouseButton.None;

            default:
                button = MouseButton.None;
                isDown = false;
                return false;
        }
    }
}
