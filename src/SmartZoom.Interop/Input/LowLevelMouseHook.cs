using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using SmartZoom.Core.Input;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace SmartZoom.Interop.Input;

/// <summary>
/// Captures the trigger gesture with a global <c>WH_MOUSE_LL</c> hook.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a hook and not Raw Input:</b> Raw Input can only observe. A low-level hook can also swallow
/// events, which is required when <see cref="TriggerOptions.SwallowClicks"/> is enabled.
/// </para>
/// <para>
/// <b>Threading:</b> Windows calls a low-level hook on the thread that installed it, from inside that
/// thread's message wait. If the callback exceeds <c>LowLevelHooksTimeout</c> (a few hundred ms) the
/// system silently uninstalls the hook, so the hook owns a dedicated thread that does nothing but pump
/// messages. The callback runs the allocation-free <see cref="DoubleTapDetector"/> under a short lock and
/// hands everything else to channels consumed by other threads.
/// </para>
/// <para>
/// <b>Re-entrancy:</b> input we inject ourselves (replayed clicks) passes back through this hook. It is
/// recognized by <see cref="InjectedInputTag"/> in <c>dwExtraInfo</c> and ignored. Input injected by other
/// software (e.g. vendor mouse utilities remapping buttons) is deliberately still processed.
/// </para>
/// <para>This type is single-use: it can be started once and stopped once.</para>
/// </remarks>
public sealed partial class LowLevelMouseHook : ITriggerSource, IDisposable
{
    /// <summary>Marker written to <c>dwExtraInfo</c> of input we synthesize ("SZMH").</summary>
    internal const nuint InjectedInputTag = 0x535A_4D48;

    private const int HcAction = 0;
    private const uint WmMButtonDown = 0x0207;
    private const uint WmMButtonUp = 0x0208;
    private const uint WmXButtonDown = 0x020B;
    private const uint WmXButtonUp = 0x020C;
    private const uint WmQuit = 0x0012;
    private const ushort XButton1 = 0x0001;
    private const ushort XButton2 = 0x0002;

    private const int StateCreated = 0;
    private const int StateRunning = 1;
    private const int StateStopped = 2;

    private readonly DoubleTapDetector _detector;
    private readonly ILogger<LowLevelMouseHook> _logger;
    private readonly Lock _gate = new();

    // Bounded + DropWrite: if dispatching stalls, excess triggers are discarded rather than queued up and
    // replayed as a burst of zooms later. Only the hook thread writes.
    private readonly Channel<TriggerEvent> _triggers = Channel.CreateBounded<TriggerEvent>(
        new BoundedChannelOptions(8) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true, SingleWriter = true });

    // Unbounded: a dropped replay is a click the user made that the target app never receives.
    private readonly Channel<ReplayAction> _replays = Channel.CreateUnbounded<ReplayAction>(
        new UnboundedChannelOptions { SingleReader = true });

    // Must stay rooted for as long as the hook is installed; if the GC collected the delegate, the
    // native thunk Windows calls would be freed and the process would crash on the next mouse event.
    private readonly HOOKPROC _hookProc;
    private readonly Timer _timeoutTimer;
    private readonly ManualResetEventSlim _installed = new();

    private Thread? _hookThread;
    private uint _hookThreadId;
    private int _installError;
    private Task? _replayWorker;
    private bool _enabled = true;
    private int _state = StateCreated;

    /// <summary>Creates the hook. No input is captured until <see cref="StartCapture"/>.</summary>
    /// <param name="options">Trigger configuration.</param>
    /// <param name="logger">Logger. Never called from the hook callback itself.</param>
    public LowLevelMouseHook(TriggerOptions options, ILogger<LowLevelMouseHook> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        _detector = new DoubleTapDetector(options);
        _logger = logger;
        _hookProc = HookCallback;
        _timeoutTimer = new Timer(static s => ((LowLevelMouseHook)s!).OnTimeout(), this, Timeout.Infinite, Timeout.Infinite);
    }

    /// <inheritdoc />
    public ChannelReader<TriggerEvent> Triggers => _triggers.Reader;

    /// <inheritdoc />
    public bool Enabled
    {
        get
        {
            lock (_gate)
            {
                return _enabled;
            }
        }

        set
        {
            lock (_gate)
            {
                if (_enabled == value)
                {
                    return;
                }

                _enabled = value;
                if (!value)
                {
                    EnqueueReplay(_detector.Reset());
                }
            }

            LogEnabledChanged(value);
        }
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">The hook was already started.</exception>
    /// <exception cref="Win32Exception">Windows refused to install the hook.</exception>
    public void StartCapture()
    {
        if (Interlocked.CompareExchange(ref _state, StateRunning, StateCreated) != StateCreated)
        {
            throw new InvalidOperationException("The mouse hook can only be started once.");
        }

        _replayWorker = Task.Run(ReplayLoopAsync);

        _hookThread = new Thread(HookThreadMain)
        {
            IsBackground = true,
            Name = "SmartZoom mouse hook",
            Priority = ThreadPriority.AboveNormal,
        };
        _hookThread.Start();
        _installed.Wait();

        if (_installError != 0)
        {
            _replays.Writer.TryComplete();
            _triggers.Writer.TryComplete();
            Volatile.Write(ref _state, StateStopped);
            throw new Win32Exception(_installError, "SetWindowsHookEx(WH_MOUSE_LL) failed.");
        }

        LogStarted(_detector.Button, _detector.WindowMs);
    }

    /// <inheritdoc />
    public void StopCapture()
    {
        if (Interlocked.CompareExchange(ref _state, StateStopped, StateRunning) != StateRunning)
        {
            return;
        }

        if (!PInvoke.PostThreadMessage(_hookThreadId, WmQuit, default, default))
        {
            LogStopFailed(Marshal.GetLastPInvokeError());
        }

        _hookThread?.Join(TimeSpan.FromSeconds(2));
        _timeoutTimer.Change(Timeout.Infinite, Timeout.Infinite);

        // Anything still held must reach the target app, or the user loses a click (or keeps a stuck button).
        lock (_gate)
        {
            EnqueueReplay(_detector.Reset());
        }

        _replays.Writer.TryComplete();
        _replayWorker?.Wait(TimeSpan.FromSeconds(2));
        _triggers.Writer.TryComplete();

        LogStopped();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        StopCapture();
        _timeoutTimer.Dispose();
        _installed.Dispose();
    }

    private unsafe void HookThreadMain()
    {
        _hookThreadId = PInvoke.GetCurrentThreadId();

        // Low-level hooks are never injected into other processes, so the module handle is only validated,
        // not loaded elsewhere. The callback always runs on this thread.
        var module = PInvoke.GetModuleHandle(default(PCWSTR));
        var hook = PInvoke.SetWindowsHookEx(WINDOWS_HOOK_ID.WH_MOUSE_LL, _hookProc, new HINSTANCE(module.Value), 0);
        if (hook.IsNull)
        {
            _installError = Marshal.GetLastPInvokeError();
            _installed.Set();
            return;
        }

        _installed.Set();
        try
        {
            // Hook callbacks are delivered while this thread waits inside GetMessage; nothing to dispatch.
            // GetMessage returns 0 for WM_QUIT and -1 on failure.
            while (PInvoke.GetMessage(out _, HWND.Null, 0, 0).Value > 0)
            {
            }
        }
        finally
        {
            PInvoke.UnhookWindowsHookEx(hook);
        }
    }

    private unsafe LRESULT HookCallback(int code, WPARAM wParam, LPARAM lParam)
    {
        if (code == HcAction)
        {
            try
            {
                var info = (MSLLHOOKSTRUCT*)lParam.Value;
                if (info->dwExtraInfo != InjectedInputTag
                    && TryMapButton((uint)wParam.Value, info->mouseData, out var button, out var isDown)
                    && ProcessButton(button, isDown, info))
                {
                    return new LRESULT(1);
                }
            }
            catch (Exception ex) when (ReportCallbackFault(ex))
            {
                // Never let an exception unwind into user32: fall through and pass the event on.
            }
        }

        return PInvoke.CallNextHookEx(HHOOK.Null, code, wParam, lParam);
    }

    private unsafe bool ProcessButton(MouseButton button, bool isDown, MSLLHOOKSTRUCT* info)
    {
        HookDecision decision;
        bool hasDeadline;
        uint deadline;

        lock (_gate)
        {
            if (!_enabled)
            {
                return false;
            }

            decision = _detector.OnButton(button, isDown, info->time);
            EnqueueReplay(decision.Replay);
            hasDeadline = _detector.TryGetDeadline(out deadline);
        }

        if (decision.Triggered)
        {
            _triggers.Writer.TryWrite(new TriggerEvent(new ScreenPoint(info->pt.X, info->pt.Y), info->time));
        }

        if (hasDeadline)
        {
            ArmTimeout(deadline);
        }

        return decision.Swallow;
    }

    private void OnTimeout()
    {
        bool hasDeadline;
        uint deadline;

        lock (_gate)
        {
            EnqueueReplay(_detector.OnTimeout(unchecked((uint)Environment.TickCount)));
            hasDeadline = _detector.TryGetDeadline(out deadline);
        }

        // The timer can fire a tick early relative to the coarse GetTickCount clock; re-arm until expired.
        if (hasDeadline)
        {
            ArmTimeout(deadline);
        }
    }

    private void ArmTimeout(uint deadline)
    {
        var dueMs = unchecked((int)(deadline - (uint)Environment.TickCount));
        _timeoutTimer.Change(Math.Max(dueMs, 1), Timeout.Infinite);
    }

    // Callers hold _gate so replays are queued in the same order the detector produced them.
    private void EnqueueReplay(ReplayAction action)
    {
        if (action != ReplayAction.None)
        {
            _replays.Writer.TryWrite(action);
        }
    }

    private async Task ReplayLoopAsync()
    {
        await foreach (var action in _replays.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            if (MouseInputInjector.TrySend(_detector.Button, action, InjectedInputTag, out var error))
            {
                LogReplayed(action);
            }
            else
            {
                // Typically UIPI: the foreground window belongs to an elevated process.
                LogReplayFailed(action, error);
            }
        }
    }

    private bool ReportCallbackFault(Exception exception)
    {
        ThreadPool.UnsafeQueueUserWorkItem(static state => state.Hook.LogCallbackFault(state.Exception), (Hook: this, Exception: exception), preferLocal: false);
        return true;
    }

    private static bool TryMapButton(uint message, uint mouseData, out MouseButton button, out bool isDown)
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

    [LoggerMessage(Level = LogLevel.Information, Message = "Mouse hook installed. Trigger: double-press {Button} within {WindowMs} ms.")]
    private partial void LogStarted(MouseButton button, uint windowMs);

    [LoggerMessage(Level = LogLevel.Information, Message = "Mouse hook removed.")]
    private partial void LogStopped();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not signal the hook thread to exit (Win32 error {Error}).")]
    private partial void LogStopFailed(int error);

    [LoggerMessage(Level = LogLevel.Information, Message = "Trigger capture enabled: {Enabled}.")]
    private partial void LogEnabledChanged(bool enabled);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Replayed held input: {Action}.")]
    private partial void LogReplayed(ReplayAction action);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to replay held input {Action} (Win32 error {Error}).")]
    private partial void LogReplayFailed(ReplayAction action, int error);

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception in the mouse hook callback; the event was passed through.")]
    private partial void LogCallbackFault(Exception exception);
}
