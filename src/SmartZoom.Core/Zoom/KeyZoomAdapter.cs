using Microsoft.Extensions.Logging;
using SmartZoom.Core.Input;
using SmartZoom.Core.Routing;
using SmartZoom.Core.Settings;

namespace SmartZoom.Core.Zoom;

/// <summary>
/// Zoom through an application's own keyboard shortcuts: one combination to zoom in, another to come
/// back. Made for document readers with "fit width" and "fit page" commands (Acrobat, Sumatra), where the
/// first press makes the page fill the window and the second shows the whole page again, the way a reader
/// is usually left. Both states are exact and never drift, unlike wheel ticks.
/// </summary>
/// <remarks>
/// Shortcuts go to the window with keyboard focus, so the target is brought to the foreground first; the
/// trigger itself does not focus it because SmartZoom swallows the button press. Once the shortcut is out,
/// whatever was in front before is put back: like the wheel and pinch adapters, a zoom must not rearrange
/// the desktop, and a reader left on top of an overlapping browser would receive the user's next trigger.
/// When the trigger is a hotkey the user is still holding its modifiers as the shortcut goes out, which
/// would turn Ctrl+2 into Ctrl+Alt+2; the adapter waits for modifiers outside the shortcut to be released
/// and does not press or release the ones the user already holds.
/// </remarks>
public sealed partial class KeyZoomAdapter : IZoomAdapter
{
    /// <summary>How long to wait for the user to let go of a hotkey trigger's modifiers.</summary>
    internal static readonly TimeSpan ModifierReleaseTimeout = TimeSpan.FromMilliseconds(1500);

    private static readonly TimeSpan ModifierPoll = TimeSpan.FromMilliseconds(10);

    /// <summary>
    /// Time for the target to pull the shortcut out of its input queue before the foreground moves on.
    /// Injected keys are routed to the foreground thread asynchronously; switching away too early would hand
    /// the tail of the combination to the previous window.
    /// </summary>
    internal static readonly TimeSpan KeyDeliverySettle = TimeSpan.FromMilliseconds(120);

    private static readonly (KeyModifiers Flag, ModifierKey Key)[] Modifiers =
    [
        (KeyModifiers.Control, ModifierKey.Control),
        (KeyModifiers.Alt, ModifierKey.Alt),
        (KeyModifiers.Shift, ModifierKey.Shift),
        (KeyModifiers.Win, ModifierKey.Win),
    ];

    private readonly IInputInjector _injector;
    private readonly IWindowActivator _activator;
    private readonly KeyCombo _zoomIn;
    private readonly KeyCombo _zoomOut;
    private readonly TimeProvider _time;
    private readonly TimeSpan _modifierReleaseTimeout;
    private readonly ILogger<KeyZoomAdapter> _logger;

    /// <summary>Creates the adapter.</summary>
    /// <param name="injector">Sends the key combinations.</param>
    /// <param name="activator">Gives the target window keyboard focus.</param>
    /// <param name="settings">Which shortcuts to send.</param>
    /// <param name="time">Clock for the modifier-release wait.</param>
    /// <param name="logger">Logger.</param>
    public KeyZoomAdapter(IInputInjector injector, IWindowActivator activator, KeyZoomSettings settings, TimeProvider time, ILogger<KeyZoomAdapter> logger)
        : this(injector, activator, settings, time, logger, ModifierReleaseTimeout)
    {
    }

    internal KeyZoomAdapter(IInputInjector injector, IWindowActivator activator, KeyZoomSettings settings, TimeProvider time, ILogger<KeyZoomAdapter> logger, TimeSpan modifierReleaseTimeout)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _modifierReleaseTimeout = modifierReleaseTimeout;

        _injector = injector ?? throw new ArgumentNullException(nameof(injector));
        _activator = activator ?? throw new ArgumentNullException(nameof(activator));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _zoomIn = KeyCombo.Parse(settings.ZoomInKeys);
        _zoomOut = KeyCombo.Parse(settings.ZoomOutKeys);
    }

    /// <inheritdoc />
    public AdapterKind Kind => AdapterKind.Keys;

    /// <inheritdoc />
    public async Task<ZoomInResult> ZoomInAsync(TargetInfo target, ScreenPoint point, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        var sent = await SendAsync(target, _zoomIn, cancellationToken).ConfigureAwait(false);
        return sent ? ZoomInResult.Applied(RestoreState.Instance) : ZoomInResult.Unhandled;
    }

    /// <inheritdoc />
    public async Task ZoomOutAsync(TargetInfo target, object restoreState, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (restoreState is not RestoreState)
            throw new ArgumentException($"Expected {nameof(RestoreState)} from a previous zoom-in.", nameof(restoreState));

        await SendAsync(target, _zoomOut, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> SendAsync(TargetInfo target, KeyCombo combo, CancellationToken cancellationToken)
    {
        var previous = _activator.ForegroundWindow;
        if (!_activator.TryActivate(target.RootWindow))
        {
            LogNoFocus(target.ProcessName);
            return false;
        }

        try
        {
            return await SendToForegroundAsync(target, combo, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (previous != 0 && previous != target.RootWindow)
                await RestoreForegroundAsync(previous, target.ProcessName, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<bool> SendToForegroundAsync(TargetInfo target, KeyCombo combo, CancellationToken cancellationToken)
    {
        var held = await WaitForForeignModifiersAsync(combo.Modifiers, cancellationToken).ConfigureAwait(false);
        if ((held & ~combo.Modifiers) != 0)
        {
            var stuck = Describe(held & ~combo.Modifiers);
            LogStillHeld(stuck, _modifierReleaseTimeout, target.ProcessName);
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
        return true;
    }

    private async Task RestoreForegroundAsync(nint previous, string? process, CancellationToken cancellationToken)
    {
        await Task.Delay(KeyDeliverySettle, _time, cancellationToken).ConfigureAwait(false);
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

    /// <summary>Marker for the toggle memory; the adapter needs nothing to come back.</summary>
    internal sealed class RestoreState
    {
        private RestoreState()
        {
        }

        public static RestoreState Instance { get; } = new();
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Sent {Keys} to {Process}.")]
    private partial void LogSent(string keys, string? process);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not give {Process} keyboard focus; nothing was zoomed.")]
    private partial void LogNoFocus(string? process);

    [LoggerMessage(Level = LogLevel.Debug, Message = "The window that was in front before zooming {Process} could not be brought back; it stays behind.")]
    private partial void LogNotRestored(string? process);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Key injection was rejected for {Process}; is the window elevated?")]
    private partial void LogRejected(string? process);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Keys} still held after {Timeout}; the shortcut was not sent to {Process}. Let go of the trigger's modifier keys.")]
    private partial void LogStillHeld(string keys, TimeSpan timeout, string? process);
}

/// <summary>Brings a window to the foreground so keyboard input reaches it.</summary>
public interface IWindowActivator
{
    /// <summary>The top-level window currently in the foreground, or 0 when there is none.</summary>
    nint ForegroundWindow { get; }

    /// <summary>Gives the window keyboard focus.</summary>
    /// <param name="rootWindow">The top-level window.</param>
    /// <returns>True when the window is in the foreground afterwards.</returns>
    bool TryActivate(nint rootWindow);
}
