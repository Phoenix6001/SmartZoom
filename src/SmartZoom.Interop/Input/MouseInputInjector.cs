using System.Runtime.InteropServices;
using SmartZoom.Core.Input;
using Windows.Win32;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace SmartZoom.Interop.Input;

/// <summary>Synthesizes mouse button input with <c>SendInput</c>.</summary>
internal static class MouseInputInjector
{
    private const uint XButton1 = 0x0001;
    private const uint XButton2 = 0x0002;

    /// <summary>Injects the requested button transitions at the current cursor position.</summary>
    /// <param name="button">Button to press and/or release.</param>
    /// <param name="action">Transitions to inject, down before up.</param>
    /// <param name="tag">Value for <c>dwExtraInfo</c> so our own hook can recognize the input.</param>
    /// <param name="error">Win32 error code when injection fails; otherwise 0.</param>
    /// <returns>True if every event was inserted into the input stream.</returns>
    public static unsafe bool TrySend(MouseButton button, ReplayAction action, nuint tag, out int error)
    {
        Span<INPUT> inputs = stackalloc INPUT[2];
        var count = 0;

        if ((action & ReplayAction.Down) != 0)
        {
            inputs[count++] = Create(button, isDown: true, tag);
        }

        if ((action & ReplayAction.Up) != 0)
        {
            inputs[count++] = Create(button, isDown: false, tag);
        }

        if (count == 0)
        {
            error = 0;
            return true;
        }

        // Both transitions go in one SendInput call so no other input can be interleaved between them.
        var sent = PInvoke.SendInput(inputs[..count], sizeof(INPUT));
        error = sent == count ? 0 : Marshal.GetLastPInvokeError();
        return sent == count;
    }

    private static INPUT Create(MouseButton button, bool isDown, nuint tag)
    {
        var (flags, data) = button switch
        {
            MouseButton.Middle => (isDown ? MOUSE_EVENT_FLAGS.MOUSEEVENTF_MIDDLEDOWN : MOUSE_EVENT_FLAGS.MOUSEEVENTF_MIDDLEUP, 0u),
            MouseButton.XButton1 => (isDown ? MOUSE_EVENT_FLAGS.MOUSEEVENTF_XDOWN : MOUSE_EVENT_FLAGS.MOUSEEVENTF_XUP, XButton1),
            MouseButton.XButton2 => (isDown ? MOUSE_EVENT_FLAGS.MOUSEEVENTF_XDOWN : MOUSE_EVENT_FLAGS.MOUSEEVENTF_XUP, XButton2),
            _ => throw new ArgumentOutOfRangeException(nameof(button), button, "Not an injectable trigger button."),
        };

        // No MOUSEEVENTF_MOVE / ABSOLUTE: the click lands wherever the cursor currently is.
        var input = new INPUT { type = INPUT_TYPE.INPUT_MOUSE };
        input.mi.dwFlags = flags;
        input.mi.mouseData = data;
        input.mi.dwExtraInfo = tag;
        return input;
    }
}
