using SmartZoom.Core.Zoom;
using Windows.Win32;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace SmartZoom.Interop.Input;

/// <summary>Injects keyboard and wheel input with <c>SendInput</c> for the zoom adapters.</summary>
/// <remarks>
/// Events carry <see cref="InputInjection.Tag"/> so our own hooks skip them. Wheel input goes to the
/// window under the cursor (Windows 10+ default "scroll inactive windows"), which is exactly where the
/// trigger happened.
/// </remarks>
public sealed class SendInputInjector : IInputInjector
{
    /// <inheritdoc />
    public unsafe bool TrySendModifier(ModifierKey key, bool isDown)
    {
        var input = InputInjection.KeyInput((ushort)ToVirtualKey(key), isDown);
        return PInvoke.SendInput(new ReadOnlySpan<INPUT>(in input), sizeof(INPUT)) == 1;
    }

    /// <inheritdoc />
    public unsafe bool TrySendWheel(int ticks)
    {
        var input = new INPUT { type = INPUT_TYPE.INPUT_MOUSE };
        input.mi.dwFlags = MOUSE_EVENT_FLAGS.MOUSEEVENTF_WHEEL;

        // mouseData is documented as a signed delta stored in a DWORD; a negative value scrolls down.
        input.mi.mouseData = unchecked((uint)(ticks * (int)PInvoke.WHEEL_DELTA));
        input.mi.dwExtraInfo = InputInjection.Tag;

        return PInvoke.SendInput(new ReadOnlySpan<INPUT>(in input), sizeof(INPUT)) == 1;
    }

    /// <inheritdoc />
    public bool IsModifierDown(ModifierKey key) =>
        // High bit set = currently down. Reads the physical/asynchronous state, not the thread's message-queue state.
        (PInvoke.GetAsyncKeyState((int)ToVirtualKey(key)) & 0x8000) != 0;

    private static VIRTUAL_KEY ToVirtualKey(ModifierKey key) => key switch
    {
        ModifierKey.Control => VIRTUAL_KEY.VK_CONTROL,
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown modifier."),
    };
}
