using SmartZoom.Core.Input;

using Windows.Win32;
using Windows.Win32.UI.WindowsAndMessaging;

namespace SmartZoom.Interop.Input;

/// <summary>
/// The two translations the low-level hooks make — a keyboard message into a key transition, a mouse message
/// into the <see cref="MouseButton"/> it concerns — and the flag masks that mark injected input.
/// </summary>
/// <remarks>
/// Kept apart from the hook itself so that what is left there is the concurrency design — the hook thread,
/// its callbacks, the channels and the timer — with nothing in between.
/// </remarks>
internal static class HookMessages
{
    /// <summary>Set on a mouse event that was injected, at this integrity level or a lower one.</summary>
    public const uint MouseInjectedMask = PInvoke.LLMHF_INJECTED | PInvoke.LLMHF_LOWER_IL_INJECTED;

    /// <summary>Set on a keyboard event that was injected, at this integrity level or a lower one.</summary>
    public const KBDLLHOOKSTRUCT_FLAGS KeyboardInjectedMask = KBDLLHOOKSTRUCT_FLAGS.LLKHF_INJECTED | KBDLLHOOKSTRUCT_FLAGS.LLKHF_LOWER_IL_INJECTED;

    /// <summary>Whether a message is a key press or release, and which.</summary>
    /// <param name="message">The hook's wParam.</param>
    /// <param name="isDown">True for a press.</param>
    /// <returns>False for anything that is not a key transition.</returns>
    public static bool IsKeyTransition(uint message, out bool isDown)
    {
        isDown = message is PInvoke.WM_KEYDOWN or PInvoke.WM_SYSKEYDOWN;
        return isDown || message is PInvoke.WM_KEYUP or PInvoke.WM_SYSKEYUP;
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
            case PInvoke.WM_MBUTTONDOWN or PInvoke.WM_MBUTTONUP:
                button = MouseButton.Middle;
                isDown = message == PInvoke.WM_MBUTTONDOWN;
                return true;

            case PInvoke.WM_XBUTTONDOWN or PInvoke.WM_XBUTTONUP:
                // For X buttons the high word of mouseData identifies which one.
                button = (ushort)(mouseData >> 16) switch
                {
                    PInvoke.XBUTTON1 => MouseButton.XButton1,
                    PInvoke.XBUTTON2 => MouseButton.XButton2,
                    _ => MouseButton.None,
                };
                isDown = message == PInvoke.WM_XBUTTONDOWN;
                return button != MouseButton.None;

            default:
                button = MouseButton.None;
                isDown = false;
                return false;
        }
    }
}
