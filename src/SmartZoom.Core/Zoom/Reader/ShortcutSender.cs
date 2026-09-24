using Microsoft.Extensions.Logging;

using SmartZoom.Core.Input;
using SmartZoom.Core.Routing;

namespace SmartZoom.Core.Zoom.Reader;

/// <summary>
/// Sends one keyboard shortcut to a window that does not have the keyboard, and leaves the desktop as it
/// found it. Both reader adapters use it, because both end up pressing something in the reader.
/// </summary>
/// <remarks>
/// Three pieces of etiquette, each of them learned from a bug:
///
/// The target is brought to the foreground first, because shortcuts go to the window with keyboard focus and
/// the trigger itself does not focus anything — SmartZoom swallows the button press. Whatever was in front
/// before is put back afterwards: a zoom must not rearrange the desktop, and a reader left on top of an
/// overlapping browser would catch the user's next trigger. That holds only while the reader still has the
/// foreground it was given; a foreground the user has moved elsewhere in the meantime is theirs, and is left.
///
/// When the trigger is a hotkey, the user is still holding its modifiers as the shortcut goes out, which
/// would turn Ctrl+2 into Ctrl+Alt+2. So it waits for modifiers outside the shortcut to be released, and
/// never presses or releases a modifier the user is already holding.
///
/// That wait lasts up to a second and a half, which is long enough for something else to take the
/// foreground. These shortcuts are not harmless in the wrong application — Ctrl+0 hides the selected column
/// in Excel — so the aim is checked once more immediately before the keys go out, and the press is abandoned
/// rather than sent somewhere it was never meant for.
/// </remarks>
public sealed partial class ShortcutSender
{
    /// <summary>How long to wait for the user to let go of a hotkey trigger's modifiers.</summary>
    internal static readonly TimeSpan ModifierReleaseTimeout = TimeSpan.FromMilliseconds(1500);

    /// <summary>
    /// Time for the target to pull the shortcut out of its input queue before the foreground moves on.
    /// Injected keys are routed to the foreground thread asynchronously; switching away too early would hand
    /// the tail of the combination to the previous window.
    /// </summary>
    internal static readonly TimeSpan KeyDeliverySettle = TimeSpan.FromMilliseconds(120);

    private static readonly TimeSpan ModifierPoll = TimeSpan.FromMilliseconds(10);

    private static readonly (KeyModifiers Flag, ModifierKey Key)[] Modifiers =
    [
        (KeyModifiers.Control, ModifierKey.Control),
        (KeyModifiers.Alt, ModifierKey.Alt),
        (KeyModifiers.Shift, ModifierKey.Shift),
        (KeyModifiers.Win, ModifierKey.Win),
    ];

    private readonly IInputInjector _injector;
    private readonly IWindowActivator _activator;
    private readonly TimeProvider _time;
    private readonly TimeSpan _modifierReleaseTimeout;
    private readonly ILogger<ShortcutSender> _logger;

    /// <summary>Creates the sender.</summary>
    /// <param name="injector">Sends the key combinations.</param>
    /// <param name="activator">Gives the target window keyboard focus.</param>
    /// <param name="time">Clock for the modifier-release wait.</param>
    /// <param name="logger">Logger.</param>
    public ShortcutSender(IInputInjector injector, IWindowActivator activator, TimeProvider time, ILogger<ShortcutSender> logger)
        : this(injector, activator, time, logger, ModifierReleaseTimeout)
    {
    }

    internal ShortcutSender(IInputInjector injector, IWindowActivator activator, TimeProvider time, ILogger<ShortcutSender> logger, TimeSpan modifierReleaseTimeout)
    {
        _injector = injector ?? throw new ArgumentNullException(nameof(injector));
        _activator = activator ?? throw new ArgumentNullException(nameof(activator));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _modifierReleaseTimeout = modifierReleaseTimeout;
    }

    /// <summary>
    /// Brings the reader to the front, sends a shortcut once the keyboard is free, and hands the front back
    /// to the window that had it, unless the user has moved it somewhere else in the meantime.
    /// </summary>
    /// <param name="target">The reader window.</param>
    /// <param name="combo">The shortcut to send.</param>
    /// <param name="prepare">Runs after the trigger's modifiers are released and before the shortcut goes out.</param>
    /// <param name="finish">Runs after the shortcut, while the reader is still the foreground window.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>True when the shortcut reached the target.</returns>
    public async Task<bool> SendAsync(TargetInfo target, KeyCombo combo, Action? prepare, Action? finish, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);

        var previous = _activator.ForegroundWindow;
        if (!_activator.TryActivate(target.RootWindow))
        {
            LogNoFocus(target.ProcessName);
            return false;
        }

        try
        {
            return await SendToForegroundAsync(target, combo, prepare, finish, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            // Only a foreground this sender gave the reader is its to take back. If a third window holds it,
            // the user put it there, and pulling it away would be exactly the rearrangement this avoids.
            if (previous != 0 && previous != target.RootWindow && _activator.ForegroundWindow == target.RootWindow)
                await RestoreForegroundAsync(previous, target.ProcessName).ConfigureAwait(false);
        }
    }

    private async Task<bool> SendToForegroundAsync(TargetInfo target, KeyCombo combo, Action? prepare, Action? finish, CancellationToken cancellationToken)
    {
        var held = await WaitForForeignModifiersAsync(combo.Modifiers, cancellationToken).ConfigureAwait(false);
        if ((held & ~combo.Modifiers) != 0)
        {
            var stuck = Describe(held & ~combo.Modifiers);
            LogStillHeld(stuck, _modifierReleaseTimeout, target.ProcessName);
            return false;
        }

        prepare?.Invoke();

        if (_activator.ForegroundWindow != target.RootWindow)
        {
            LogFocusLost(target.ProcessName);
            return false;
        }

        // Modifiers the user is holding are already in place; pressing them again is harmless but releasing
        // them would take the user's own key away from them.
        var toPress = combo with { Modifiers = combo.Modifiers & ~held };
        if (!_injector.TrySendKeyCombo(toPress))
        {
            LogRejected(target.ProcessName);
            return false;
        }

        var keys = combo.ToString();
        LogSent(keys, target.ProcessName);
        finish?.Invoke();
        return true;
    }

    /// <summary>
    /// Puts the window that was in front back. Deliberately not cancellable: this runs in a `finally`, and a
    /// cancelled zoom that skipped it would leave the reader on top of everything, catching the user's next
    /// trigger — the exact thing bringing the foreground back exists to prevent.
    /// </summary>
    private async Task RestoreForegroundAsync(nint previous, string? process)
    {
        await Task.Delay(KeyDeliverySettle, _time, CancellationToken.None).ConfigureAwait(false);
        if (!_activator.TryActivate(previous))
            LogNotRestored(process);
    }

    /// <summary>Waits until no modifier outside the shortcut is held, then reports which modifiers still are.</summary>
    private async Task<KeyModifiers> WaitForForeignModifiersAsync(KeyModifiers wanted, CancellationToken cancellationToken)
    {
        var deadline = _time.GetUtcNow() + _modifierReleaseTimeout;
        while (true)
        {
            var held = HeldModifiers();
            if ((held & ~wanted) == 0 || _time.GetUtcNow() >= deadline)
                return held;

            await Task.Delay(ModifierPoll, _time, cancellationToken).ConfigureAwait(false);
        }
    }

    private static string Describe(KeyModifiers modifiers)
    {
        var names = new List<string>(4);
        foreach (var (flag, key) in Modifiers)
        {
            if ((modifiers & flag) != 0)
                names.Add(key.ToString());
        }

        return string.Join('+', names);
    }

    private KeyModifiers HeldModifiers()
    {
        var held = KeyModifiers.None;
        foreach (var (flag, key) in Modifiers)
        {
            if (_injector.IsModifierDown(key))
                held |= flag;
        }

        return held;
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Sent {Keys} to {Process}.")]
    private partial void LogSent(string keys, string? process);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not give {Process} keyboard focus; nothing was zoomed.")]
    private partial void LogNoFocus(string? process);

    [LoggerMessage(Level = LogLevel.Debug, Message = "The window that was in front before zooming {Process} could not be brought back; it stays behind.")]
    private partial void LogNotRestored(string? process);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Process} is no longer the window in front; the shortcut was not sent, to keep it out of whatever is.")]
    private partial void LogFocusLost(string? process);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Key injection was rejected for {Process}; is the window elevated?")]
    private partial void LogRejected(string? process);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Keys} still held after {Timeout}; the shortcut was not sent to {Process}. Let go of the trigger's modifier keys.")]
    private partial void LogStillHeld(string keys, TimeSpan timeout, string? process);
}
