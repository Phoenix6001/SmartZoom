using SmartZoom.Core.Zoom;
using SmartZoom.Interop.Input;

using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace SmartZoom.Interop.Windows;

/// <summary>Brings windows to the foreground with <c>SetForegroundWindow</c>.</summary>
/// <remarks>
/// <para>Windows only lets the process that produced or received the last input event activate other
/// windows. The trigger that got us here was swallowed by our hook or, for a hotkey, its modifiers went to
/// whichever window had focus, so the request would be refused; injecting a mouse move of zero pixels first
/// makes the last input event ours (no window reacts to it, and our own hook skips the tag it carries).
/// Should that still be refused, the thread briefly attaches its input queue to the foreground window's
/// thread and asks again, which older Windows builds honour.</para>
/// <para>Activation is asynchronous: the call returns before the target has processed it, so a window that
/// was not already in front is given a moment to take keyboard focus before input is sent its way.</para>
/// </remarks>
public sealed class WindowActivator : IWindowActivator
{
    /// <summary>Longest wait for the target to report itself as the foreground window.</summary>
    private static readonly TimeSpan ActivationTimeout = TimeSpan.FromMilliseconds(300);

    /// <summary>Extra time after the foreground switch for the target's own focus handling.</summary>
    private static readonly TimeSpan FocusSettle = TimeSpan.FromMilliseconds(60);

    /// <summary>How often the foreground window is re-read while waiting for the switch. Chosen, not measured.</summary>
    private const int ForegroundPollMs = 10;

    /// <inheritdoc />
    /// <remarks>Reports 0 when the foreground belongs to this process (the tray's hidden menu window after the
    /// tray menu was used): there is nothing worth bringing back in front of the target then.</remarks>
    public nint ForegroundWindow
    {
        get
        {
            var window = PInvoke.GetForegroundWindow();
            if (window.IsNull)
                return 0;

            PInvoke.GetWindowThreadProcessId(window, out var processId);
            return processId == (uint)Environment.ProcessId ? 0 : (nint)window;
        }
    }

    /// <inheritdoc />
    public bool TryActivate(nint rootWindow)
    {
        var window = new HWND(rootWindow);
        if (!PInvoke.IsWindow(window))
            return false;

        if (PInvoke.GetForegroundWindow() == window)
            return true;

        ClaimLastInput();
        if (!PInvoke.SetForegroundWindow(window) || !WaitForForeground(window))
        {
            if (!ActivateThroughForegroundThread(window) || !WaitForForeground(window))
                return false;
        }

        Thread.Sleep(FocusSettle);
        return true;
    }

    // A relative mouse move by (0, 0): the cheapest input event there is, and the one nothing responds to.
    private static unsafe void ClaimLastInput()
    {
        var input = new INPUT { type = INPUT_TYPE.INPUT_MOUSE };
        input.mi.dwFlags = MOUSE_EVENT_FLAGS.MOUSEEVENTF_MOVE;
        input.mi.dwExtraInfo = InputInjection.Tag;
        _ = PInvoke.SendInput(new ReadOnlySpan<INPUT>(in input), sizeof(INPUT));
    }

    private static bool ActivateThroughForegroundThread(HWND window)
    {
        var foreground = PInvoke.GetForegroundWindow();
        if (foreground.IsNull)
            return false;

        var foregroundThread = PInvoke.GetWindowThreadProcessId(foreground, out _);
        var ourThread = PInvoke.GetCurrentThreadId();
        if (foregroundThread == 0 || foregroundThread == ourThread || !PInvoke.AttachThreadInput(ourThread, foregroundThread, true))
            return false;

        try
        {
            return PInvoke.SetForegroundWindow(window);
        }
        finally
        {
            PInvoke.AttachThreadInput(ourThread, foregroundThread, false);
        }
    }

    private static bool WaitForForeground(HWND window)
    {
        var deadline = Environment.TickCount64 + (long)ActivationTimeout.TotalMilliseconds;
        while (PInvoke.GetForegroundWindow() != window)
        {
            if (Environment.TickCount64 >= deadline)
                return false;
            Thread.Sleep(ForegroundPollMs);
        }

        return true;
    }
}
