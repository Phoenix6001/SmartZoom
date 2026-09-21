using System.Diagnostics.CodeAnalysis;
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
///
/// The shortcut alone ignores the cursor, so the reader decides what you end up looking at. Given a scroller,
/// the adapter first brings the content under the cursor to the top of the window: readers keep the top of the
/// view when the zoom changes, so what you pointed at is what fills the window. The second press undoes the
/// zoom and then the scroll, by the distance the view was measured to have moved.
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
    private readonly IReaderView? _view;
    private readonly IPinchInjector? _pinch;
    private readonly bool _gesture;
    private readonly double _scale;
    private readonly TimeSpan _animation;
    private readonly bool _followCursor;
    private readonly int _margin;
    private readonly KeyCombo _zoomIn;
    private readonly KeyCombo _zoomOut;
    private readonly TimeProvider _time;
    private readonly TimeSpan _modifierReleaseTimeout;
    private readonly ILogger<KeyZoomAdapter> _logger;

    /// <summary>Creates the adapter.</summary>
    /// <param name="injector">Sends the key combinations.</param>
    /// <param name="activator">Gives the target window keyboard focus.</param>
    /// <param name="view">The reader's content area and scrolling. Null leaves the framing to the reader.</param>
    /// <param name="pinch">Performs the gesture. Null falls back to the shortcuts.</param>
    /// <param name="settings">How to zoom the reader.</param>
    /// <param name="animation">How long the gesture takes.</param>
    /// <param name="time">Clock for the modifier-release wait.</param>
    /// <param name="logger">Logger.</param>
    public KeyZoomAdapter(IInputInjector injector, IWindowActivator activator, IReaderView? view, IPinchInjector? pinch, ReaderZoomSettings settings, TimeSpan animation, TimeProvider time, ILogger<KeyZoomAdapter> logger)
        : this(injector, activator, view, pinch, settings, animation, time, logger, ModifierReleaseTimeout)
    {
    }

    internal KeyZoomAdapter(IInputInjector injector, IWindowActivator activator, IReaderView? view, IPinchInjector? pinch, ReaderZoomSettings settings, TimeSpan animation, TimeProvider time, ILogger<KeyZoomAdapter> logger, TimeSpan modifierReleaseTimeout)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _modifierReleaseTimeout = modifierReleaseTimeout;
        _view = view;
        _pinch = pinch;
        _gesture = settings.Gesture;
        _scale = settings.Scale;
        _animation = animation;
        _followCursor = settings.FollowCursor;
        _margin = settings.MarginPx;

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

        if (Gesturing(out var pinch, out var view))
            return await PinchInAsync(target, point, pinch, view, cancellationToken).ConfigureAwait(false);

        // Scrolling before the shortcut, while the page is still small, keeps the arithmetic out of this: the
        // reader holds the top of the view across a zoom change, so whatever is at the top stays there and
        // simply grows. It waits for the window and the keyboard along with the shortcut, because a wheel turn
        // with the trigger's Control key still down is not a scroll to a reader, it is a zoom.
        var scrolled = 0;
        var sent = await SendAsync(
            target,
            _zoomIn,
            prepare: () => scrolled = BringToTop(target, point, cancellationToken),
            finish: null,
            cancellationToken).ConfigureAwait(false);

        if (sent)
            return ZoomInResult.Applied(RestoreState.Scrolled(scrolled));

        if (scrolled != 0)
            Scroll(target, -scrolled, cancellationToken);

        return ZoomInResult.Unhandled;
    }

    /// <inheritdoc />
    public async Task ZoomOutAsync(TargetInfo target, object restoreState, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (restoreState is not RestoreState state)
            throw new ArgumentException($"Expected {nameof(RestoreState)} from a previous zoom-in.", nameof(restoreState));

        if (state.Anchor is { } anchor && Gesturing(out var pinch, out var view))
        {
            await PinchOutAsync(target, anchor, state, pinch, view, cancellationToken).ConfigureAwait(false);
            return;
        }

        // The zoom first, then the scroll: the reader anchors the top of the view, so undoing them in the other
        // order would scroll at the wrong scale. Both happen while the reader still has the foreground, or the
        // wheel would land on whatever window comes back to the front.
        await SendAsync(
            target,
            _zoomOut,
            prepare: null,
            finish: () =>
            {
                if (state.ScrolledPixels != 0)
                    Scroll(target, -state.ScrolledPixels, cancellationToken);
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Whether this press should be a gesture: configured, possible, and worth doing.</summary>
    private bool Gesturing([NotNullWhen(true)] out IPinchInjector? pinch, [NotNullWhen(true)] out IReaderView? view)
    {
        pinch = _pinch;
        view = _view;
        return _gesture && pinch is not null && view is not null && _scale > 1;
    }

    /// <summary>Magnifies around the cursor with a gesture, the way a browser is zoomed.</summary>
    private async Task<ZoomInResult> PinchInAsync(TargetInfo target, ScreenPoint point, IPinchInjector pinch, IReaderView view, CancellationToken cancellationToken)
    {
        if (view.Bounds(target) is not { } bounds || !bounds.Contains(point))
        {
            LogNoContentArea(target.ProcessName);
            return ZoomInResult.Unhandled;
        }

        // Where the view sits now, so the gesture's own imprecision can be taken out on the way back.
        var mark = view.Snapshot(target);

        LogPinching(target.ProcessName, _scale, point.X, point.Y);
        if (!await pinch.PinchAsync(point, _scale, _animation, bounds, cancellationToken).ConfigureAwait(false))
        {
            LogGestureRejected(target.ProcessName);
            return ZoomInResult.Unhandled;
        }

        return ZoomInResult.Applied(RestoreState.Pinched(point, mark));
    }

    /// <summary>Animates the magnification away, then lands on the zoom the reader itself defines.</summary>
    /// <remarks>
    /// The gesture is for the eye, not for the arithmetic: Windows' recognizer keeps back a share of a closing
    /// pinch, so an inverse gesture alone leaves the reader a couple of percent smaller every time, and it
    /// compounds. The shortcut afterwards is what makes the second press exact, and because it names a state
    /// rather than a change, the reader can never drift however many times it is pressed.
    /// </remarks>
    private async Task PinchOutAsync(TargetInfo target, ScreenPoint anchor, RestoreState state, IPinchInjector pinch, IReaderView view, CancellationToken cancellationToken)
    {
        if (view.Bounds(target) is { } bounds)
        {
            var back = Math.Clamp(anchor.X, bounds.Left, bounds.Right - 1);
            var down = Math.Clamp(anchor.Y, bounds.Top, bounds.Bottom - 1);
            if (!await pinch.PinchAsync(new ScreenPoint(back, down), 1 / _scale, _animation, bounds, cancellationToken).ConfigureAwait(false))
                LogGestureRejected(target.ProcessName);
        }

        var landed = await SendAsync(
            target,
            _zoomOut,
            prepare: null,
            // The reader's zoom command also moves the view; where it came from is what the mark remembers.
            finish: () =>
            {
                if (state.Mark is { } mark && !view.ScrollBackTo(target, mark, cancellationToken))
                    LogNotAligned(target.ProcessName);
            },
            cancellationToken).ConfigureAwait(false);

        // The gesture alone does not quite undo its opposite, so without the shortcut the reader is left a
        // little smaller than it was. Nothing can be done about it here, but it should not pass in silence.
        if (!landed)
            LogNotLanded(target.ProcessName);
    }

    /// <summary>Scrolls the content under the cursor to the top of the reader; returns how far the view moved.</summary>
    private int BringToTop(TargetInfo target, ScreenPoint point, CancellationToken cancellationToken)
    {
        if (!_followCursor || _view is null || _view.Bounds(target) is not { } bounds || !bounds.Contains(point))
            return 0;

        var below = point.Y - bounds.Top;
        if (below <= _margin)
            return 0;

        var moved = Scroll(target, below - _margin, cancellationToken);
        LogFollowed(target.ProcessName, below - _margin, moved);
        return moved;
    }

    private int Scroll(TargetInfo target, int pixels, CancellationToken cancellationToken) =>
        _view is null ? 0 : _view.ScrollBy(target, pixels, cancellationToken);

    /// <summary>Brings the reader to the front, sends a shortcut once the keyboard is free, and hands the front back.</summary>
    /// <param name="target">The reader window.</param>
    /// <param name="combo">The shortcut to send.</param>
    /// <param name="prepare">Runs after the trigger's modifiers are released and before the shortcut goes out.</param>
    /// <param name="finish">Runs after the shortcut, while the reader is still the foreground window.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    private async Task<bool> SendAsync(TargetInfo target, KeyCombo combo, Action? prepare, Action? finish, CancellationToken cancellationToken)
    {
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
            if (previous != 0 && previous != target.RootWindow)
                await RestoreForegroundAsync(previous, target.ProcessName, cancellationToken).ConfigureAwait(false);
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

        // The window was brought to the front before the wait for the user's modifiers, and a second and a
        // half is long enough for something else to take it. These shortcuts are not harmless in the wrong
        // application — Ctrl+0 hides the selected column in Excel — so the aim is checked again here, as late
        // as possible, and the press is abandoned rather than sent somewhere it was never meant for.
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

    /// <summary>What the toggle memory holds, depending on how the zoom was done.</summary>
    /// <param name="ScrolledPixels">Shortcut zoom: measured movement of the scroll that framed the cursor's content.</param>
    /// <param name="Anchor">Gesture zoom: the point the gesture magnified around.</param>
    /// <param name="Mark">Gesture zoom: how the view looked beforehand, for putting it back.</param>
    internal sealed record RestoreState(int ScrolledPixels, ScreenPoint? Anchor, object? Mark)
    {
        public static RestoreState Scrolled(int pixels) => new(pixels, null, null);

        public static RestoreState Pinched(ScreenPoint anchor, object? mark) => new(0, anchor, mark);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Sent {Keys} to {Process}.")]
    private partial void LogSent(string keys, string? process);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not give {Process} keyboard focus; nothing was zoomed.")]
    private partial void LogNoFocus(string? process);

    [LoggerMessage(Level = LogLevel.Debug, Message = "The window that was in front before zooming {Process} could not be brought back; it stays behind.")]
    private partial void LogNotRestored(string? process);

    [LoggerMessage(Level = LogLevel.Information, Message = "Smart zoom ({Process}): pinching x{Scale} around ({X}, {Y}).")]
    private partial void LogPinching(string? process, double scale, int x, int y);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The gesture was rejected in {Process}; nothing was zoomed.")]
    private partial void LogGestureRejected(string? process);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Smart zoom unavailable: {Process} has no content area under the cursor. Nothing was zoomed.")]
    private partial void LogNoContentArea(string? process);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Process} did not take the zoom command after the gesture; it is left slightly smaller than it started.")]
    private partial void LogNotLanded(string? process);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Could not read {Process} well enough to put the view back exactly after the gesture.")]
    private partial void LogNotAligned(string? process);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Scrolled the cursor's content to the top of {Process}: asked for {Requested} px, the view moved {Moved} px.")]
    private partial void LogFollowed(string? process, int requested, int moved);

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Process} is no longer the window in front; the shortcut was not sent, to keep it out of whatever is.")]
    private partial void LogFocusLost(string? process);

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
