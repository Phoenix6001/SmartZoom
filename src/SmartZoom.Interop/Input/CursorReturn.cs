using SmartZoom.Core.Input;

namespace SmartZoom.Interop.Input;

/// <summary>Puts the mouse pointer back after a gesture, once the gesture has finished moving it.</summary>
/// <remarks>
/// Windows moves the pointer along with the first touch contact. Through <c>InjectTouchInput</c> that move has
/// landed by the time the injector restores the pointer. Through a synthetic pointer device (Firefox's) the
/// contact's last position is applied a few milliseconds after the gesture ends, and it overwrote an immediate
/// restore: every Firefox zoom left the pointer on the left finger, 236 px from where it was. Waiting for the
/// pointer to reach that last position orders the two moves instead of guessing a delay. No Win32 here, so
/// the waiting is tested; the injector passes the real reads and moves.
/// </remarks>
internal static class CursorReturn
{
    /// <summary>Waits for the pointer to reach the gesture's last contact position, then moves it back.</summary>
    /// <param name="original">Where the pointer was before the gesture.</param>
    /// <param name="lastTouch">Where the first contact last was, or null when that is not known.</param>
    /// <param name="timeout">How long to wait for the pointer to get there.</param>
    /// <param name="read">Reads the pointer's position; null when it cannot be read.</param>
    /// <param name="move">Moves the pointer.</param>
    /// <param name="elapsed">Time since the wait started.</param>
    /// <param name="pause">Waits a moment before the next read.</param>
    /// <returns>Whether the pointer reached <paramref name="lastTouch"/> before the timeout.</returns>
    internal static bool Restore(
        ScreenPoint original,
        ScreenPoint? lastTouch,
        TimeSpan timeout,
        Func<ScreenPoint?> read,
        Action<ScreenPoint> move,
        Func<TimeSpan> elapsed,
        Action pause)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(move);
        ArgumentNullException.ThrowIfNull(elapsed);
        ArgumentNullException.ThrowIfNull(pause);

        var arrived = false;
        if (lastTouch is { } target)
        {
            while (!(arrived = read() == target) && elapsed() < timeout)
                pause();
        }

        move(original);
        return arrived;
    }
}
