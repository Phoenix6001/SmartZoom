namespace SmartZoom.Core.Input;

/// <summary>
/// Pure, allocation-free trigger state machine for single- or double-tap of one input (a mouse
/// button or a key combination). The caller filters events for that input; the detector only sees
/// press/release transitions. It runs *inside* the low-level hook callback, so every method is O(1)
/// and touches only fields. Not thread-safe: callers serialize access.
/// </summary>
/// <remarks>
/// <para>Timestamps are GetTickCount-style milliseconds (uint, wraps every ~49.7 days); all
/// arithmetic is unchecked so wrap-around is handled naturally.</para>
/// <para><b>Single tap</b>: every press triggers. In swallow mode the press and its release are
/// hidden from the target app; nothing is ever held back, so no timer or replay is involved.</para>
/// <para><b>Double tap, pass-through mode</b>: every event reaches the target app; we only observe
/// presses.</para>
/// <para><b>Double tap, swallow mode</b>: at the first press we cannot know whether a second is
/// coming, so we hold the press back. If the window expires (<see cref="OnTimeout"/>) or a late
/// press arrives, the held press is handed back as a <see cref="ReplayAction"/> for the host to
/// re-inject.</para>
/// </remarks>
public sealed class TapDetector
{
    private enum State : byte
    {
        Idle,
        /// <summary>First press is down (swallowed in swallow mode; merely recorded otherwise).</summary>
        FirstDown,
        /// <summary>Swallow mode: first press fully swallowed, waiting for a second press.</summary>
        FirstUp,
        /// <summary>Swallow mode: trigger fired, swallowing the matching release.</summary>
        TriggerDown,
        /// <summary>Swallow mode: a held first press was replayed as Down; let the physical release through.</summary>
        PassUp,
    }

    private static readonly HookDecision Swallowed = new(Swallow: true, Triggered: false, ReplayAction.None);
    private static readonly HookDecision SwallowedAndTriggered = new(Swallow: true, Triggered: true, ReplayAction.None);
    private static readonly HookDecision PassedAndTriggered = new(Swallow: false, Triggered: true, ReplayAction.None);

    private readonly int _tapCount;
    private readonly uint _windowMs;
    private readonly bool _swallow;
    private State _state;
    private uint _firstDownTime;

    /// <summary>Creates a detector in the idle state.</summary>
    /// <param name="options">Tap configuration.</param>
    /// <exception cref="ArgumentOutOfRangeException">The tap count is not 1 or 2, or the double-tap window is outside 1..5000 ms.</exception>
    public TapDetector(TapOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.TapCount is not (1 or 2))
            throw new ArgumentOutOfRangeException(nameof(options), options.TapCount, "Tap count must be 1 or 2.");
        if (options.DoubleTapWindowMs is 0 or > 5000)
            throw new ArgumentOutOfRangeException(nameof(options), options.DoubleTapWindowMs, "Double-tap window must be 1..5000 ms.");

        _tapCount = options.TapCount;
        _windowMs = options.DoubleTapWindowMs;
        _swallow = options.SwallowInput;
    }

    /// <summary>Number of presses that make a trigger: 1 or 2.</summary>
    public int TapCount => _tapCount;

    /// <summary>Maximum milliseconds between the two presses of a double tap.</summary>
    public uint WindowMs => _windowMs;

    /// <summary>Whether this detector hides its input from the target application.</summary>
    public bool Swallows => _swallow;

    /// <summary>
    /// True when nothing is in progress: no press is held back, swallowed or awaiting a second tap. In pass-through
    /// double-tap mode the first press is remembered until the next press, so this stays false after its release.
    /// </summary>
    public bool IsIdle => _state == State.Idle;

    /// <summary>
    /// When a swallowed press is pending, the tick at which <see cref="OnTimeout"/> should be called.
    /// Always false for single tap and in pass-through mode (nothing to replay, so no timer needed).
    /// </summary>
    public bool TryGetDeadline(out uint deadlineMs)
    {
        if (_swallow && _state is State.FirstDown or State.FirstUp)
        {
            deadlineMs = unchecked(_firstDownTime + _windowMs + 1);
            return true;
        }

        deadlineMs = 0;
        return false;
    }

    /// <summary>Processes one press or release of the detector's input.</summary>
    /// <param name="isDown">True for a press, false for a release.</param>
    /// <param name="timeMs">Event time on the GetTickCount clock.</param>
    /// <returns>What the hook should do with this event.</returns>
    public HookDecision OnInput(bool isDown, uint timeMs)
    {
        if (_tapCount == 1)
            return OnSingleTap(isDown);

        return _swallow ? OnSwallowing(isDown, timeMs) : OnPassThrough(isDown, timeMs);
    }

    /// <summary>Called by the host's timer once the deadline passes. Returns the input to replay, if any.</summary>
    public ReplayAction OnTimeout(uint nowMs)
    {
        if (!_swallow || !IsExpired(nowMs))
            return ReplayAction.None;

        switch (_state)
        {
            case State.FirstDown:
                // Still physically held (long press): release the Down now, let the real Up through later.
                _state = State.PassUp;
                return ReplayAction.Down;
            case State.FirstUp:
                _state = State.Idle;
                return ReplayAction.DownUp;
            default:
                return ReplayAction.None;
        }
    }

    /// <summary>Abandons any in-progress detection (e.g. when the user disables SmartZoom). Returns held input to replay.</summary>
    public ReplayAction Reset()
    {
        var replay = !_swallow ? ReplayAction.None : _state switch
        {
            State.FirstDown => ReplayAction.Down,
            State.FirstUp => ReplayAction.DownUp,
            _ => ReplayAction.None,
        };
        _state = State.Idle;
        return replay;
    }

    private HookDecision OnSingleTap(bool isDown)
    {
        if (!_swallow)
            return isDown ? PassedAndTriggered : default;

        if (isDown)
        {
            // Also covers a lost Up (already in TriggerDown): the new press simply triggers again.
            _state = State.TriggerDown;
            return SwallowedAndTriggered;
        }

        if (_state != State.TriggerDown)
            return default; // Stray release (input was held when we started) — not ours.

        _state = State.Idle;
        return Swallowed;
    }

    private HookDecision OnPassThrough(bool isDown, uint timeMs)
    {
        if (!isDown)
            return default;

        if (_state == State.FirstDown && !IsExpired(timeMs))
        {
            _state = State.Idle;
            return PassedAndTriggered;
        }

        Begin(timeMs);
        return default;
    }

    private HookDecision OnSwallowing(bool isDown, uint timeMs)
    {
        switch (_state)
        {
            case State.Idle:
                if (!isDown)
                    return default; // Stray release — not ours.
                Begin(timeMs);
                return Swallowed;

            case State.FirstDown or State.FirstUp when isDown:
                if (!IsExpired(timeMs))
                {
                    _state = State.TriggerDown;
                    return SwallowedAndTriggered;
                }

                // Late second press: our timer hasn't fired yet. Hand back the first press (DownUp, even if its
                // Up was lost, so the app never sees a stuck input) and hold this press as a new first press.
                Begin(timeMs);
                return new HookDecision(Swallow: true, Triggered: false, ReplayAction.DownUp);

            case State.FirstDown: // release
                _state = State.FirstUp;
                return Swallowed;

            case State.FirstUp: // stray release; nothing held corresponds to it
                return default;

            case State.TriggerDown:
                if (isDown)
                {
                    // Lost the Up of the triggering press; treat this as a fresh first press.
                    Begin(timeMs);
                    return Swallowed;
                }
                _state = State.Idle;
                return Swallowed;

            case State.PassUp:
                if (isDown)
                {
                    // The application has seen the replayed Down and is still owed its Up, which was lost.
                    // Hand the Up back before holding this press, so the app never sees a stuck input.
                    Begin(timeMs);
                    return new HookDecision(Swallow: true, Triggered: false, ReplayAction.Up);
                }
                _state = State.Idle;
                return default;

            default:
                return default;
        }
    }

    private void Begin(uint timeMs)
    {
        _state = State.FirstDown;
        _firstDownTime = timeMs;
    }

    private bool IsExpired(uint timeMs) => unchecked(timeMs - _firstDownTime) > _windowMs;
}
