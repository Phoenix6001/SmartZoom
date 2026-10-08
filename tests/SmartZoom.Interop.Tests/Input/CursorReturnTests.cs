using SmartZoom.Core.Input;
using SmartZoom.Interop.Input;

namespace SmartZoom.Interop.Tests.Input;

/// <summary>
/// Putting the mouse pointer back after a gesture. Windows moves it along with the first touch contact, and
/// for Firefox's synthetic touch device it applies the contact's last position a few milliseconds after the
/// gesture ends, which overwrote an immediate restore: every Firefox zoom left the pointer on the left
/// finger, 236 px to the left of where it was. The same late move can also land just after the restore, about a
/// frame later: after an edge zoom, which ends with a drag, it left the pointer on the drag's end in half the
/// gestures measured.
/// </summary>
public sealed class CursorReturnTests
{
    private static readonly ScreenPoint Original = new(1920, 1080);
    private static readonly ScreenPoint LastTouch = new(1684, 1061);
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan Guard = TimeSpan.FromMilliseconds(100);

    [Fact]
    public void Waits_for_the_gestures_own_last_move_before_putting_the_pointer_back()
    {
        // Three reads still see the pointer where it was, then the late move lands.
        var pointer = new FakePointer(Original, (4, LastTouch));

        var arrived = Restore(pointer);

        Assert.True(arrived);
        Assert.Equal(["move (1920, 1080)"], pointer.Moves);
        Assert.Equal(Original, pointer.Position);
    }

    [Fact]
    public void Puts_it_back_at_once_when_the_last_move_has_already_landed()
    {
        var pointer = new FakePointer(LastTouch);

        Restore(pointer);

        Assert.Equal(1, pointer.ReadsBeforeFirstMove);
        Assert.Equal(Original, pointer.Position);
    }

    [Fact]
    public void A_late_move_that_lands_after_the_pointer_was_put_back_is_undone()
    {
        // The drag ends with the finger held still and lifted where it stopped, so the pointer is already there
        // when the injector looks; the lift's own move arrives a frame later and puts it back on the drag's end.
        var pointer = new FakePointer(LastTouch, (5, LastTouch));

        Restore(pointer);

        Assert.Equal(["move (1920, 1080)", "move (1920, 1080)"], pointer.Moves);
        Assert.Equal(Original, pointer.Position);
    }

    [Fact]
    public void A_pointer_the_user_moves_while_it_is_being_watched_is_left_where_they_put_it()
    {
        var elsewhere = new ScreenPoint(500, 500);
        var pointer = new FakePointer(LastTouch, (4, elsewhere), (6, LastTouch));

        Restore(pointer);

        Assert.Equal(["move (1920, 1080)"], pointer.Moves);
        Assert.Equal(elsewhere, pointer.Position);
    }

    [Fact]
    public void The_watch_ends_after_the_guard()
    {
        var pointer = new FakePointer(LastTouch);

        Restore(pointer);

        Assert.InRange(pointer.Elapsed(), Guard, Guard + FakePointer.Step);
    }

    [Fact]
    public void Gives_up_waiting_at_the_timeout_and_puts_it_back_anyway()
    {
        // A pointer Windows never moves (a setting, or a device that behaves like the other one).
        var pointer = new FakePointer(Original);

        var arrived = Restore(pointer);

        Assert.False(arrived);
        Assert.Equal(["move (1920, 1080)"], pointer.Moves);
        Assert.InRange(pointer.Elapsed(), Timeout + Guard, Timeout + Guard + (2 * FakePointer.Step));
    }

    [Fact]
    public void Without_a_known_last_touch_it_puts_the_pointer_back_without_waiting()
    {
        var pointer = new FakePointer(Original);

        CursorReturn.Restore(Original, lastTouch: null, Timeout, Guard, pointer.Read, pointer.Move, pointer.Elapsed, pointer.Pause);

        Assert.Equal(["move (1920, 1080)"], pointer.Moves);
        Assert.Equal(0, pointer.Reads);
    }

    private static bool Restore(FakePointer pointer) =>
        CursorReturn.Restore(Original, LastTouch, Timeout, Guard, pointer.Read, pointer.Move, pointer.Elapsed, pointer.Pause);

    /// <summary>
    /// A pointer that stays where it was last moved, plus moves the gesture lands late, each on a given read
    /// (counting from 1); and a clock that moves only when paused.
    /// </summary>
    private sealed class FakePointer(ScreenPoint start, params (int OnRead, ScreenPoint To)[] lateMoves)
    {
        public static readonly TimeSpan Step = TimeSpan.FromMilliseconds(5);

        private TimeSpan _clock;

        public ScreenPoint Position { get; private set; } = start;

        public int Reads { get; private set; }

        public int? ReadsBeforeFirstMove { get; private set; }

        public List<string> Moves { get; } = [];

        public ScreenPoint? Read()
        {
            Reads++;
            foreach (var (onRead, to) in lateMoves)
            {
                if (onRead == Reads)
                    Position = to;
            }

            return Position;
        }

        public void Move(ScreenPoint to)
        {
            ReadsBeforeFirstMove ??= Reads;
            Moves.Add($"move ({to.X}, {to.Y})");
            Position = to;
        }

        public TimeSpan Elapsed() => _clock;

        public void Pause() => _clock += Step;
    }
}
