using SmartZoom.Core.Input;
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
    public bool TrySendWheel(int ticks) => InputInjection.TrySendWheel(ticks, out _);

    /// <summary>How long the key is held. Some apps (Acrobat) ignore a press and release that arrive in the same batch.</summary>
    private static readonly TimeSpan KeyHold = TimeSpan.FromMilliseconds(30);

    /// <inheritdoc />
    public bool TrySendKeyCombo(KeyCombo combo)
    {
        // One SendInput call per transition, like a person typing: the left-hand modifiers go down one by one,
        // the key is held briefly, then everything is released in reverse. A single batched call with the
        // generic VK_CONTROL was accepted by Windows but ignored by Acrobat.
        var modifiers = new List<ushort>(4);
        if (combo.Modifiers.HasFlag(KeyModifiers.Control))
            modifiers.Add((ushort)VIRTUAL_KEY.VK_LCONTROL);
        if (combo.Modifiers.HasFlag(KeyModifiers.Alt))
            modifiers.Add((ushort)VIRTUAL_KEY.VK_LMENU);
        if (combo.Modifiers.HasFlag(KeyModifiers.Shift))
            modifiers.Add((ushort)VIRTUAL_KEY.VK_LSHIFT);
        if (combo.Modifiers.HasFlag(KeyModifiers.Win))
            modifiers.Add((ushort)VIRTUAL_KEY.VK_LWIN);

        var ok = true;
        foreach (var modifier in modifiers)
            ok &= SendKey(modifier, isDown: true);
        ok &= SendKey(combo.Key, isDown: true);
        Thread.Sleep(KeyHold);
        ok &= SendKey(combo.Key, isDown: false);
        for (var m = modifiers.Count - 1; m >= 0; m--)
            ok &= SendKey(modifiers[m], isDown: false);
        return ok;
    }

    /// <inheritdoc />
    public bool IsModifierDown(ModifierKey key) =>
        // High bit set = currently down. Reads the physical/asynchronous state, not the thread's message-queue state.
        (PInvoke.GetAsyncKeyState((int)ToVirtualKey(key)) & 0x8000) != 0;

    private static unsafe bool SendKey(ushort virtualKey, bool isDown)
    {
        var input = InputInjection.KeyInput(virtualKey, isDown);
        return PInvoke.SendInput(new ReadOnlySpan<INPUT>(in input), sizeof(INPUT)) == 1;
    }

    private static VIRTUAL_KEY ToVirtualKey(ModifierKey key) => key switch
    {
        ModifierKey.Control => VIRTUAL_KEY.VK_CONTROL,
        ModifierKey.Alt => VIRTUAL_KEY.VK_MENU,
        ModifierKey.Shift => VIRTUAL_KEY.VK_SHIFT,
        ModifierKey.Win => VIRTUAL_KEY.VK_LWIN,
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, "Unknown modifier."),
    };
}
