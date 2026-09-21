using System.Runtime.InteropServices;
using SmartZoom.Core.Input;
using Windows.Win32;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace SmartZoom.Interop.Input;

/// <summary>Low-level <c>SendInput</c> helpers shared by the hook (replays) and the zoom adapters.</summary>
internal static class InputInjection
{
    /// <summary>
    /// Marker written to <c>dwExtraInfo</c> of every event we synthesize ("SZMH"), so our own hooks can
    /// recognize and ignore it. Input injected by <em>other</em> software carries a different value (usually 0)
    /// and is deliberately processed like hardware input.
    /// </summary>
    public const nuint Tag = 0x535A_4D48;

    private const uint XButton1 = 0x0001;
    private const uint XButton2 = 0x0002;

    /// <summary>Injects mouse button transitions at the current cursor position.</summary>
    /// <param name="button">Button to press and/or release.</param>
    /// <param name="action">Transitions to inject, down before up.</param>
    /// <param name="error">Win32 error code when injection fails; otherwise 0.</param>
    public static unsafe bool TrySendMouseButton(MouseButton button, ReplayAction action, out int error)
    {
        Span<INPUT> inputs = stackalloc INPUT[2];
        var count = 0;

        if ((action & ReplayAction.Down) != 0)
            inputs[count++] = MouseButtonInput(button, isDown: true);
        if ((action & ReplayAction.Up) != 0)
            inputs[count++] = MouseButtonInput(button, isDown: false);

        return Send(inputs[..count], out error);
    }

    /// <summary>Injects key transitions for a virtual key. Modifiers are not touched; the user is still holding them.</summary>
    /// <param name="virtualKey">Virtual key to press and/or release.</param>
    /// <param name="action">Transitions to inject, down before up.</param>
    /// <param name="error">Win32 error code when injection fails; otherwise 0.</param>
    public static unsafe bool TrySendKey(ushort virtualKey, ReplayAction action, out int error)
    {
        Span<INPUT> inputs = stackalloc INPUT[2];
        var count = 0;

        if ((action & ReplayAction.Down) != 0)
            inputs[count++] = KeyInput(virtualKey, isDown: true);
        if ((action & ReplayAction.Up) != 0)
            inputs[count++] = KeyInput(virtualKey, isDown: false);

        return Send(inputs[..count], out error);
    }

    /// <summary>Builds a keyboard event. Public for <see cref="SendInputInjector"/>.</summary>
    public static INPUT KeyInput(ushort virtualKey, bool isDown)
    {
        var vk = (VIRTUAL_KEY)virtualKey;

        // Some apps (notably ones built on Qt or Chromium) look at the scan code, not just the VK.
        var scan = (ushort)PInvoke.MapVirtualKey((uint)vk, MAP_VIRTUAL_KEY_TYPE.MAPVK_VK_TO_VSC);

        var input = new INPUT { type = INPUT_TYPE.INPUT_KEYBOARD };
        input.ki.wVk = vk;
        input.ki.wScan = scan;
        input.ki.dwFlags = isDown ? 0 : KEYBD_EVENT_FLAGS.KEYEVENTF_KEYUP;
        input.ki.dwExtraInfo = Tag;
        return input;
    }

    /// <summary>Injects wheel notches at the current cursor position.</summary>
    /// <param name="ticks">Notches; positive scrolls up (away from the user), as Windows defines it.</param>
    /// <param name="error">Win32 error code when injection fails; otherwise 0.</param>
    public static bool TrySendWheel(int ticks, out int error)
    {
        var input = new INPUT { type = INPUT_TYPE.INPUT_MOUSE };
        input.mi.dwFlags = MOUSE_EVENT_FLAGS.MOUSEEVENTF_WHEEL;

        // mouseData is documented as a signed delta stored in a DWORD; a negative value scrolls down.
        input.mi.mouseData = unchecked((uint)(ticks * (int)PInvoke.WHEEL_DELTA));
        input.mi.dwExtraInfo = Tag;

        return Send(new ReadOnlySpan<INPUT>(in input), out error);
    }

    private static INPUT MouseButtonInput(MouseButton button, bool isDown)
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
        input.mi.dwExtraInfo = Tag;
        return input;
    }

    private static unsafe bool Send(ReadOnlySpan<INPUT> inputs, out int error)
    {
        if (inputs.IsEmpty)
        {
            error = 0;
            return true;
        }

        // All transitions go in one SendInput call so no other input can be interleaved between them.
        var sent = PInvoke.SendInput(inputs, sizeof(INPUT));
        error = sent == inputs.Length ? 0 : Marshal.GetLastPInvokeError();
        return sent == inputs.Length;
    }
}
