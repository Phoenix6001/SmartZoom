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
/// Captures trigger gestures with global <c>WH_MOUSE_LL</c> and <c>WH_KEYBOARD_LL</c> hooks.
/// Any number of mouse-button and hotkey triggers can be active at once; each has its own
/// <see cref="TapDetector"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why hooks and not Raw Input:</b> Raw Input can only observe. A low-level hook can also swallow
/// events, which is required when <see cref="TapOptions.SwallowInput"/> is enabled.
/// </para>
/// <para>
/// <b>Threading:</b> Windows calls a low-level hook on the thread that installed it, from inside that
/// thread's message wait. If the callback exceeds <c>LowLevelHooksTimeout</c> (a few hundred ms) the
/// system silently uninstalls the hook, so both hooks live on a dedicated thread that does nothing but pump
/// messages. The callbacks run the allocation-free detectors under a short lock and hand everything else
/// to channels consumed by other threads.
/// </para>
/// <para>
/// <b>Re-entrancy:</b> input we inject ourselves (replays, Ctrl+wheel bursts) passes back through these
/// hooks. It is recognized by <see cref="InputInjection.Tag"/> in <c>dwExtraInfo</c> and ignored. Input
/// injected by other software (e.g. vendor mouse utilities remapping buttons) is deliberately still processed.
/// </para>
/// <para>
/// <b>Privacy:</b> the keyboard hook never records or logs keys other than modifiers and the configured
/// hotkeys' own keys.
/// </para>
/// <para>This type is single-use: it can be started once and stopped once.</para>
/// </remarks>
public sealed partial class LowLevelInputHook : ITriggerSource, IDisposable
{
    private const int HcAction = 0;
    private const uint WmKeyDown = 0x0100;
    private const uint WmKeyUp = 0x0101;
    private const uint WmSysKeyDown = 0x0104;
    private const uint WmSysKeyUp = 0x0105;
    private const uint WmMButtonDown = 0x0207;
    private const uint WmMButtonUp = 0x0208;
    private const uint WmXButtonDown = 0x020B;
    private const uint WmXButtonUp = 0x020C;
    private const uint WmQuit = 0x0012;
    private const ushort XButton1 = 0x0001;
    private const ushort XButton2 = 0x0002;
    private const uint LlmhfInjectedMask = 0x3;   // LLMHF_INJECTED | LLMHF_LOWER_IL_INJECTED
    private const uint LlkhfInjectedMask = 0x12;  // LLKHF_INJECTED | LLKHF_LOWER_IL_INJECTED

    private const int StateCreated = 0;
    private const int StateRunning = 1;
    private const int StateStopped = 2;

    private readonly TriggerState[] _triggers;
    private readonly MouseTriggerState[] _mouseTriggers;
    private readonly HotkeyTriggerState[] _hotkeyTriggers;
    private readonly ModifierTracker _modifiers = new();
    private readonly ILogger<LowLevelInputHook> _logger;
    private readonly Lock _gate = new();

    // Bounded + DropWrite: if dispatching stalls, excess triggers are discarded rather than queued up and
    // replayed as a burst of zooms later. Only the hook thread writes.
    private readonly Channel<TriggerEvent> _triggerEvents = Channel.CreateBounded<TriggerEvent>(
        new BoundedChannelOptions(8) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true, SingleWriter = true });

    // Unbounded: a dropped replay is a press the user made that the target app never receives.
    private readonly Channel<ReplayRequest> _replays = Channel.CreateUnbounded<ReplayRequest>(
        new UnboundedChannelOptions { SingleReader = true });

    // Diagnostics only: every transition of a *configured* trigger input, logged at Debug so users can tell
    // "the hook never saw my button" from "the two presses were too far apart". Dropping is fine here.
    private readonly Channel<InputObservation> _observations = Channel.CreateBounded<InputObservation>(
        new BoundedChannelOptions(64) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true, SingleWriter = true });

    // Must stay rooted while the hooks are installed; if the GC collected a delegate, the native thunk
    // Windows calls would be freed and the process would crash on the next input event.
    private readonly HOOKPROC _mouseProc;
    private readonly HOOKPROC _keyboardProc;
    private readonly Timer _timeoutTimer;
    private readonly ManualResetEventSlim _installed = new();

    private Thread? _hookThread;
    private uint _hookThreadId;
    private int _installError;
    private Task? _replayWorker;
    private Task? _observationWorker;
    private bool _observe;
    private bool _enabled = true;
    private int _state = StateCreated;

    /// <summary>Creates the hook. No input is captured until <see cref="StartCapture"/>.</summary>
    /// <param name="triggers">Gestures to detect. Buttons and key combinations must be distinct.</param>
    /// <param name="logger">Logger. Never called from the hook callbacks themselves.</param>
    /// <exception cref="ArgumentException">No triggers, or two triggers share an input.</exception>
    public LowLevelInputHook(IEnumerable<TriggerDefinition> triggers, ILogger<LowLevelInputHook> logger)
    {
        ArgumentNullException.ThrowIfNull(triggers);
        ArgumentNullException.ThrowIfNull(logger);

        _triggers = triggers.Select(TriggerState.Create).ToArray();
        if (_triggers.Length == 0)
            throw new ArgumentException("At least one trigger is required.", nameof(triggers));

        _mouseTriggers = _triggers.OfType<MouseTriggerState>().ToArray();
        _hotkeyTriggers = _triggers.OfType<HotkeyTriggerState>().ToArray();

        if (_mouseTriggers.Select(t => t.Button).Distinct().Count() != _mouseTriggers.Length)
            throw new ArgumentException("Each mouse button can only be used by one trigger.", nameof(triggers));
        if (_hotkeyTriggers.Select(t => t.Matcher.Combo).Distinct().Count() != _hotkeyTriggers.Length)
            throw new ArgumentException("Each key combination can only be used by one trigger.", nameof(triggers));

        _logger = logger;
        _mouseProc = MouseCallback;
        _keyboardProc = KeyboardCallback;
        _timeoutTimer = new Timer(static s => ((LowLevelInputHook)s!).OnTimeout(), this, Timeout.Infinite, Timeout.Infinite);
    }

    /// <inheritdoc />
    public ChannelReader<TriggerEvent> Triggers => _triggerEvents.Reader;

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
                    return;

                _enabled = value;
                if (!value)
                    ResetAllLocked();
            }

            LogEnabledChanged(value);
        }
    }

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">The hook was already started.</exception>
    /// <exception cref="Win32Exception">Windows refused to install a hook.</exception>
    public void StartCapture()
    {
        if (Interlocked.CompareExchange(ref _state, StateRunning, StateCreated) != StateCreated)
            throw new InvalidOperationException("The input hook can only be started once.");

        _replayWorker = Task.Run(ReplayLoopAsync);

        // Sampled once: toggling log levels at runtime isn't supported, and this keeps the callbacks branch-cheap.
        _observe = _logger.IsEnabled(LogLevel.Debug);
        _observationWorker = _observe ? Task.Run(ObservationLoopAsync) : null;

        _hookThread = new Thread(HookThreadMain)
        {
            IsBackground = true,
            Name = "SmartZoom input hooks",
            Priority = ThreadPriority.AboveNormal,
        };
        _hookThread.Start();
        _installed.Wait();

        if (_installError != 0)
        {
            _replays.Writer.TryComplete();
            _observations.Writer.TryComplete();
            _triggerEvents.Writer.TryComplete();
            Volatile.Write(ref _state, StateStopped);
            throw new Win32Exception(_installError, "SetWindowsHookEx failed.");
        }

        var names = string.Join(", ", _triggers.Select(t => t.Definition.DisplayName));
        LogStarted(names);
    }

    /// <inheritdoc />
    public void StopCapture()
    {
        if (Interlocked.CompareExchange(ref _state, StateStopped, StateRunning) != StateRunning)
            return;

        if (!PInvoke.PostThreadMessage(_hookThreadId, WmQuit, default, default))
            LogStopFailed(Marshal.GetLastPInvokeError());

        _hookThread?.Join(TimeSpan.FromSeconds(2));
        _timeoutTimer.Change(Timeout.Infinite, Timeout.Infinite);

        // Anything still held must reach the target app, or the user loses a press (or keeps a stuck input).
        lock (_gate)
        {
            ResetAllLocked();
        }

        _replays.Writer.TryComplete();
        _replayWorker?.Wait(TimeSpan.FromSeconds(2));
        _observations.Writer.TryComplete();
        _observationWorker?.Wait(TimeSpan.FromSeconds(2));
        _triggerEvents.Writer.TryComplete();

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
        // not loaded elsewhere. The callbacks always run on this thread.
        var module = new HINSTANCE(PInvoke.GetModuleHandle(default(PCWSTR)).Value);
        var mouseHook = PInvoke.SetWindowsHookEx(WINDOWS_HOOK_ID.WH_MOUSE_LL, _mouseProc, module, 0);
        if (mouseHook.IsNull)
        {
            _installError = Marshal.GetLastPInvokeError();
            _installed.Set();
            return;
        }

        var keyboardHook = _hotkeyTriggers.Length == 0
            ? HHOOK.Null
            : PInvoke.SetWindowsHookEx(WINDOWS_HOOK_ID.WH_KEYBOARD_LL, _keyboardProc, module, 0);
        if (_hotkeyTriggers.Length > 0 && keyboardHook.IsNull)
        {
            _installError = Marshal.GetLastPInvokeError();
            PInvoke.UnhookWindowsHookEx(mouseHook);
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
            if (!keyboardHook.IsNull)
                PInvoke.UnhookWindowsHookEx(keyboardHook);
            PInvoke.UnhookWindowsHookEx(mouseHook);
        }
    }

    private unsafe LRESULT MouseCallback(int code, WPARAM wParam, LPARAM lParam)
    {
        if (code == HcAction)
        {
            try
            {
                var info = (MSLLHOOKSTRUCT*)lParam.Value;
                if (TryMapButton((uint)wParam.Value, info->mouseData, out var button, out var isDown)
                    && TryFindMouseTrigger(button, out var trigger))
                {
                    if (_observe)
                        _observations.Writer.TryWrite(new InputObservation(trigger.Definition.DisplayName, isDown, info->time, (info->flags & LlmhfInjectedMask) != 0, Ours: info->dwExtraInfo == InputInjection.Tag));

                    if (info->dwExtraInfo != InputInjection.Tag
                        && Process(trigger, isDown, info->time, new ScreenPoint(info->pt.X, info->pt.Y)))
                    {
                        return new LRESULT(1);
                    }
                }
            }
            catch (Exception ex) when (ReportCallbackFault(ex))
            {
                // Never let an exception unwind into user32: fall through and pass the event on.
            }
        }

        return PInvoke.CallNextHookEx(HHOOK.Null, code, wParam, lParam);
    }

    private unsafe LRESULT KeyboardCallback(int code, WPARAM wParam, LPARAM lParam)
    {
        if (code == HcAction)
        {
            try
            {
                var info = (KBDLLHOOKSTRUCT*)lParam.Value;
                var message = (uint)wParam.Value;
                var isDown = message is WmKeyDown or WmSysKeyDown;
                if ((isDown || message is WmKeyUp or WmSysKeyUp) && ProcessKey((ushort)info->vkCode, isDown, info))
                    return new LRESULT(1);
            }
            catch (Exception ex) when (ReportCallbackFault(ex))
            {
            }
        }

        return PInvoke.CallNextHookEx(HHOOK.Null, code, wParam, lParam);
    }

    private unsafe bool ProcessKey(ushort virtualKey, bool isDown, KBDLLHOOKSTRUCT* info)
    {
        var swallow = false;
        var ours = info->dwExtraInfo == InputInjection.Tag;

        lock (_gate)
        {
            // Modifier state must track even our own and disabled-time input, or a later combo mismatches.
            var held = _modifiers.OnKey(virtualKey, isDown);
            if (ours)
                return false;

            foreach (var trigger in _hotkeyTriggers)
            {
                var match = trigger.Matcher.OnKey(virtualKey, isDown, held);
                if (match == KeyMatch.None)
                    continue;

                if (_observe)
                    _observations.Writer.TryWrite(new InputObservation(trigger.Definition.DisplayName, isDown, info->time, ((uint)info->flags & LlkhfInjectedMask) != 0, Ours: false));

                if (!_enabled)
                    continue;

                switch (match)
                {
                    case KeyMatch.Repeat:
                        swallow |= trigger.Detector.Swallows;
                        break;

                    case KeyMatch.Press or KeyMatch.Release:
                        var decision = trigger.Detector.OnInput(match == KeyMatch.Press, info->time);
                        EnqueueReplayLocked(trigger, decision.Replay);
                        if (decision.Triggered)
                            RaiseTrigger(info->time, position: null);
                        swallow |= decision.Swallow;
                        break;
                }
            }

            ArmTimeoutLocked();
        }

        return swallow;
    }

    private bool Process(TriggerState trigger, bool isDown, uint timeMs, ScreenPoint position)
    {
        HookDecision decision;
        lock (_gate)
        {
            if (!_enabled)
                return false;

            decision = trigger.Detector.OnInput(isDown, timeMs);
            EnqueueReplayLocked(trigger, decision.Replay);
            ArmTimeoutLocked();
        }

        if (decision.Triggered)
            RaiseTrigger(timeMs, position);

        return decision.Swallow;
    }

    private void RaiseTrigger(uint timeMs, ScreenPoint? position)
    {
        if (position is null)
        {
            // Keyboard triggers carry no position; ask where the cursor is right now.
            if (!PInvoke.GetCursorPos(out var cursor))
                return;
            position = new ScreenPoint(cursor.X, cursor.Y);
        }

        _triggerEvents.Writer.TryWrite(new TriggerEvent(position.Value, timeMs));
    }

    private void OnTimeout()
    {
        lock (_gate)
        {
            var now = unchecked((uint)Environment.TickCount);
            foreach (var trigger in _triggers)
                EnqueueReplayLocked(trigger, trigger.Detector.OnTimeout(now));

            // The timer can fire a tick early relative to the coarse GetTickCount clock; re-arm until expired.
            ArmTimeoutLocked();
        }
    }

    // Caller holds _gate. Arms the single timer for the earliest pending deadline across all triggers.
    private void ArmTimeoutLocked()
    {
        var now = unchecked((uint)Environment.TickCount);
        var earliest = int.MaxValue;
        foreach (var trigger in _triggers)
        {
            if (trigger.Detector.TryGetDeadline(out var deadline))
                earliest = Math.Min(earliest, unchecked((int)(deadline - now)));
        }

        if (earliest != int.MaxValue)
            _timeoutTimer.Change(Math.Max(earliest, 1), Timeout.Infinite);
    }

    // Caller holds _gate, so replays are queued in the order the detectors produced them.
    private void EnqueueReplayLocked(TriggerState trigger, ReplayAction action)
    {
        if (action != ReplayAction.None)
            _replays.Writer.TryWrite(new ReplayRequest(trigger, action));
    }

    // Caller holds _gate.
    private void ResetAllLocked()
    {
        foreach (var trigger in _triggers)
        {
            EnqueueReplayLocked(trigger, trigger.Detector.Reset());
            trigger.ResetMatcher();
        }

        _modifiers.Reset();
    }

    private bool TryFindMouseTrigger(MouseButton button, out MouseTriggerState trigger)
    {
        foreach (var candidate in _mouseTriggers)
        {
            if (candidate.Button == button)
            {
                trigger = candidate;
                return true;
            }
        }

        trigger = null!;
        return false;
    }

    private async Task ReplayLoopAsync()
    {
        await foreach (var (trigger, action) in _replays.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            if (trigger.TryReplay(action, out var error))
                LogReplayed(trigger.Definition.DisplayName, action);
            else
                LogReplayFailed(trigger.Definition.DisplayName, action, error); // Typically UIPI: the foreground window is elevated.
        }
    }

    private async Task ObservationLoopAsync()
    {
        await foreach (var o in _observations.Reader.ReadAllAsync().ConfigureAwait(false))
            LogInput(o.Trigger, o.IsDown ? "down" : "up", o.TimeMs, o.Injected, o.Ours);
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

    [LoggerMessage(Level = LogLevel.Information, Message = "Input hooks installed. Triggers: {Triggers}.")]
    private partial void LogStarted(string triggers);

    [LoggerMessage(Level = LogLevel.Information, Message = "Input hooks removed.")]
    private partial void LogStopped();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not signal the hook thread to exit (Win32 error {Error}).")]
    private partial void LogStopFailed(int error);

    [LoggerMessage(Level = LogLevel.Information, Message = "Trigger capture enabled: {Enabled}.")]
    private partial void LogEnabledChanged(bool enabled);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Replayed held input for {Trigger}: {Action}.")]
    private partial void LogReplayed(string trigger, ReplayAction action);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to replay held input for {Trigger} ({Action}, Win32 error {Error}).")]
    private partial void LogReplayFailed(string trigger, ReplayAction action, int error);

    [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception in a hook callback; the event was passed through.")]
    private partial void LogCallbackFault(Exception exception);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Input {Trigger} {Transition} at t={TimeMs} (injected: {Injected}, ours: {Ours}).")]
    private partial void LogInput(string trigger, string transition, uint timeMs, bool injected, bool ours);

    private readonly record struct ReplayRequest(TriggerState Trigger, ReplayAction Action);

    private readonly record struct InputObservation(string Trigger, bool IsDown, uint TimeMs, bool Injected, bool Ours);

    private abstract class TriggerState(TriggerDefinition definition)
    {
        public TriggerDefinition Definition { get; } = definition;

        public TapDetector Detector { get; } = new(definition.Tap);

        public static TriggerState Create(TriggerDefinition definition) => definition switch
        {
            MouseButtonTrigger m => new MouseTriggerState(m),
            HotkeyTrigger h => new HotkeyTriggerState(h),
            _ => throw new ArgumentOutOfRangeException(nameof(definition), definition, "Unknown trigger kind."),
        };

        public abstract bool TryReplay(ReplayAction action, out int error);

        public virtual void ResetMatcher()
        {
        }
    }

    private sealed class MouseTriggerState(MouseButtonTrigger definition) : TriggerState(definition)
    {
        public MouseButton Button { get; } = definition.Button;

        public override bool TryReplay(ReplayAction action, out int error) =>
            InputInjection.TrySendMouseButton(Button, action, out error);
    }

    private sealed class HotkeyTriggerState(HotkeyTrigger definition) : TriggerState(definition)
    {
        public HotkeyMatcher Matcher { get; } = new(definition.Keys);

        public override bool TryReplay(ReplayAction action, out int error) =>
            InputInjection.TrySendKey(Matcher.Combo.Key, action, out error);

        public override void ResetMatcher() => Matcher.Reset();
    }
}
