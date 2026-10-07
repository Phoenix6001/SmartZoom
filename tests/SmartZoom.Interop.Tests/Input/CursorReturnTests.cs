using SmartZoom.Core.Input;
using SmartZoom.Interop.Input;

namespace SmartZoom.Interop.Tests.Input;

/// <summary>
/// Putting the mouse pointer back after a gesture. Windows moves it along with the first touch contact, and
/// for Firefox's synthetic touch device it applies the contact's last position a few milliseconds after the
/// gesture ends, which overwrote an immediate restore: every Firefox zoom left the pointer on the left
/// finger, 236 px to the left of where it was.
/// </summary>
public sealed class CursorReturnTests
{
    private static readonly ScreenPoint Original = new(1920, 1080);
    private static readonly ScreenPoint LastTouch = new(1684, 1061);
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(250);

    [Fact]
    public void Waits_for_the_gestures_own_last_move_before_putting_the_pointer_back()
    {
        // Three reads still see the pointer where it was, then the late move lands.
        var pointer = new FakePointer([Original, Original, Original, LastTouch]);

        var arrived = CursorReturn.Restore(Original, LastTouch, Timeout, pointer.Read, pointer.Move, pointer.Elapsed, pointer.Pause);

        Assert.True(arrived);
        Assert.Equal(["read", "read", "read", "read", "move (1920, 1080)"], pointer.Log);
    }

    [Fact]
    public void Puts_it_back_at_once_when_the_last_move_has_already_landed()
    {
        var pointer = new FakePointer([LastTouch]);

        CursorReturn.Restore(Original, LastTouch, Timeout, pointer.Read, pointer.Move, pointer.Elapsed, pointer.Pause);

        Assert.Equal(["read", "move (1920, 1080)"], pointer.Log);
    }

    [Fact]
    public void Gives_up_waiting_at_the_timeout_and_puts_it_back_anyway()
    {
        // A pointer Windows never moves (a setting, or a device that behaves like the other one).
        var pointer = new FakePointer([Original]);

        var arrived = CursorReturn.Restore(Original, LastTouch, Timeout, pointer.Read, pointer.Move, pointer.Elapsed, pointer.Pause);

        Assert.False(arrived);
        Assert.Equal("move (1920, 1080)", pointer.Log[^1]);
        Assert.InRange(pointer.Elapsed(), Timeout, Timeout + FakePointer.Step);
    }

    [Fact]
    public void Without_a_known_last_touch_it_puts_the_pointer_back_without_waiting()
    {
        var pointer = new FakePointer([Original]);

        CursorReturn.Restore(Original, lastTouch: null, Timeout, pointer.Read, pointer.Move, pointer.Elapsed, pointer.Pause);

        Assert.Equal(["move (1920, 1080)"], pointer.Log);
    }

    /// <summary>A pointer whose position is scripted read by read, and a clock that moves only when paused.</summary>
    private sealed class FakePointer(ScreenPoint[] positions)
    {
        public static readonly TimeSpan Step = TimeSpan.FromMilliseconds(5);

        private int _reads;
        private TimeSpan _clock;

        public List<string> Log { get; } = [];

        public ScreenPoint? Read()
        {
            Log.Add("read");
            return positions[Math.Min(_reads++, positions.Length - 1)];
        }

        public void Move(ScreenPoint to) => Log.Add($"move ({to.X}, {to.Y})");

        public TimeSpan Elapsed() => _clock;

        public void Pause() => _clock += Step;
    }
}
