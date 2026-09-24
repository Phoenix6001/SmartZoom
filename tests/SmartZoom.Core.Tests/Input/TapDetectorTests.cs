using SmartZoom.Core.Input;

namespace SmartZoom.Core.Tests.Input;

public sealed class TapDetectorTests
{
    private const uint Window = 300;

    private static readonly HookDecision PassedThrough = new(Swallow: false, Triggered: false, ReplayAction.None);
    private static readonly HookDecision Swallowed = new(Swallow: true, Triggered: false, ReplayAction.None);
    private static readonly HookDecision SwallowedAndTriggered = new(Swallow: true, Triggered: true, ReplayAction.None);
    private static readonly HookDecision PassedAndTriggered = new(Swallow: false, Triggered: true, ReplayAction.None);

    private static TapDetector Create(int tapCount, bool swallow) => new(new TapOptions(tapCount, Window, swallow));

    public sealed class A_new_detector
    {
        [Theory]
        [InlineData(0u)]
        [InlineData(5001u)]
        public void Rejects_out_of_range_window(uint windowMs) =>
            Assert.Throws<ArgumentOutOfRangeException>(() => new TapDetector(new TapOptions(2, windowMs, false)));

        [Theory]
        [InlineData(0)]
        [InlineData(3)]
        public void Rejects_unsupported_tap_count(int tapCount) =>
            Assert.Throws<ArgumentOutOfRangeException>(() => new TapDetector(new TapOptions(tapCount, Window, false)));

        [Fact]
        public void Exposes_options()
        {
            var detector = new TapDetector(new TapOptions(1, 250, true));

            Assert.Equal(1, detector.TapCount);
            Assert.Equal(250u, detector.WindowMs);
            Assert.True(detector.Swallows);
        }
    }

    public sealed class Single_tap
    {
        [Fact]
        public void Pass_through_triggers_on_every_press_and_never_swallows()
        {
            var detector = Create(1, swallow: false);

            Assert.Equal(PassedAndTriggered, detector.OnInput(true, 1000));
            Assert.Equal(PassedThrough, detector.OnInput(false, 1050));
            Assert.Equal(PassedAndTriggered, detector.OnInput(true, 5000));
        }

        [Fact]
        public void Swallow_hides_the_press_and_its_release_and_triggers_on_press()
        {
            var detector = Create(1, swallow: true);

            Assert.Equal(SwallowedAndTriggered, detector.OnInput(true, 1000));
            Assert.Equal(Swallowed, detector.OnInput(false, 1050));
            Assert.Equal(SwallowedAndTriggered, detector.OnInput(true, 9000));
            Assert.Equal(Swallowed, detector.OnInput(false, 9050));
        }

        [Fact]
        public void Swallow_lets_a_stray_release_through() =>
            Assert.Equal(PassedThrough, Create(1, swallow: true).OnInput(false, 1000));

        [Fact]
        public void Swallow_recovers_from_a_lost_release()
        {
            var detector = Create(1, swallow: true);
            detector.OnInput(true, 1000);

            Assert.Equal(SwallowedAndTriggered, detector.OnInput(true, 2000));
            Assert.Equal(Swallowed, detector.OnInput(false, 2050));
        }

        [Fact]
        public void Never_needs_a_timer_or_replay()
        {
            var detector = Create(1, swallow: true);
            detector.OnInput(true, 1000);

            Assert.False(detector.TryGetDeadline(out _));
            Assert.Equal(ReplayAction.None, detector.OnTimeout(9000));
            Assert.Equal(ReplayAction.None, detector.Reset());
        }
    }

    public sealed class Double_tap_in_pass_through_mode
    {
        private readonly TapDetector _detector = Create(2, swallow: false);

        [Fact]
        public void Two_presses_within_window_trigger_on_second_press_without_swallowing()
        {
            Assert.Equal(PassedThrough, _detector.OnInput(true, 1000));
            Assert.Equal(PassedThrough, _detector.OnInput(false, 1050));
            Assert.Equal(PassedAndTriggered, _detector.OnInput(true, 1200));
            Assert.Equal(PassedThrough, _detector.OnInput(false, 1250));
        }

        [Fact]
        public void Press_exactly_at_window_boundary_triggers()
        {
            _detector.OnInput(true, 1000);

            Assert.True(_detector.OnInput(true, 1000 + Window).Triggered);
        }

        [Fact]
        public void Press_after_window_does_not_trigger_but_starts_a_new_sequence()
        {
            _detector.OnInput(true, 1000);

            Assert.False(_detector.OnInput(true, 1000 + Window + 1).Triggered);
            Assert.True(_detector.OnInput(true, 1000 + Window + 100).Triggered);
        }

        [Fact]
        public void Triple_press_triggers_once_and_fourth_press_triggers_again()
        {
            Assert.False(_detector.OnInput(true, 1000).Triggered);
            Assert.True(_detector.OnInput(true, 1100).Triggered);
            Assert.False(_detector.OnInput(true, 1200).Triggered);
            Assert.True(_detector.OnInput(true, 1300).Triggered);
        }

        [Fact]
        public void Handles_tick_count_wraparound()
        {
            _detector.OnInput(true, uint.MaxValue - 50);

            Assert.True(_detector.OnInput(true, 100).Triggered);
        }

        [Fact]
        public void Never_requests_a_timer_or_replay()
        {
            _detector.OnInput(true, 1000);

            Assert.False(_detector.TryGetDeadline(out _));
            Assert.Equal(ReplayAction.None, _detector.OnTimeout(5000));
            Assert.Equal(ReplayAction.None, _detector.Reset());
        }
    }

    public sealed class Double_tap_in_swallow_mode
    {
        private readonly TapDetector _detector = Create(2, swallow: true);

        [Fact]
        public void Double_tap_swallows_all_four_events_and_triggers_on_second_press()
        {
            Assert.Equal(Swallowed, _detector.OnInput(true, 1000));
            Assert.Equal(Swallowed, _detector.OnInput(false, 1060));
            Assert.Equal(SwallowedAndTriggered, _detector.OnInput(true, 1150));
            Assert.Equal(Swallowed, _detector.OnInput(false, 1210));

            Assert.False(_detector.TryGetDeadline(out _));
        }

        [Fact]
        public void Reports_deadline_one_tick_after_window_while_press_is_held()
        {
            _detector.OnInput(true, 1000);

            Assert.True(_detector.TryGetDeadline(out var deadline));
            Assert.Equal(1000 + Window + 1, deadline);
        }

        [Fact]
        public void Single_press_is_replayed_as_full_press_after_window_expires()
        {
            _detector.OnInput(true, 1000);
            _detector.OnInput(false, 1060);

            Assert.Equal(ReplayAction.None, _detector.OnTimeout(1000 + Window));
            Assert.Equal(ReplayAction.DownUp, _detector.OnTimeout(1000 + Window + 1));
            Assert.False(_detector.TryGetDeadline(out _));

            // Back to idle: the next press is held again.
            Assert.Equal(Swallowed, _detector.OnInput(true, 2000));
        }

        [Fact]
        public void Long_press_replays_down_on_timeout_and_lets_physical_release_through()
        {
            _detector.OnInput(true, 1000);

            Assert.Equal(ReplayAction.Down, _detector.OnTimeout(1000 + Window + 1));
            Assert.False(_detector.TryGetDeadline(out _));
            Assert.Equal(PassedThrough, _detector.OnInput(false, 1500));
            Assert.Equal(Swallowed, _detector.OnInput(true, 2000));
        }

        [Fact]
        public void A_press_arriving_while_the_replayed_downs_release_is_still_owed_replays_that_release_first()
        {
            _detector.OnInput(true, 1000);
            Assert.Equal(ReplayAction.Down, _detector.OnTimeout(1000 + Window + 1)); // the app has seen a Down

            var decision = _detector.OnInput(true, 2000); // its Up was lost; a new press arrives instead

            Assert.Equal(new HookDecision(Swallow: true, Triggered: false, ReplayAction.Up), decision);
            Assert.True(_detector.TryGetDeadline(out var deadline));
            Assert.Equal(2000 + Window + 1, deadline);
        }

        [Fact]
        public void Late_second_press_before_timer_fires_replays_first_press_and_holds_the_new_one()
        {
            _detector.OnInput(true, 1000);
            _detector.OnInput(false, 1060);

            var decision = _detector.OnInput(true, 1000 + Window + 50);

            Assert.Equal(new HookDecision(Swallow: true, Triggered: false, ReplayAction.DownUp), decision);
            Assert.True(_detector.TryGetDeadline(out var deadline));
            Assert.Equal(1000 + Window + 50 + Window + 1, deadline);
        }

        [Fact]
        public void Timeout_after_trigger_is_a_no_op()
        {
            _detector.OnInput(true, 1000);
            _detector.OnInput(false, 1060);
            _detector.OnInput(true, 1150);

            Assert.Equal(ReplayAction.None, _detector.OnTimeout(1000 + Window + 1));
            Assert.Equal(Swallowed, _detector.OnInput(false, 1400));
        }

        [Fact]
        public void Stray_release_while_idle_passes_through() =>
            Assert.Equal(PassedThrough, _detector.OnInput(false, 1000));

        [Fact]
        public void Reset_while_down_returns_down_so_input_is_not_stuck()
        {
            _detector.OnInput(true, 1000);

            Assert.Equal(ReplayAction.Down, _detector.Reset());
            Assert.False(_detector.TryGetDeadline(out _));
        }

        [Fact]
        public void Reset_after_press_returns_full_press()
        {
            _detector.OnInput(true, 1000);
            _detector.OnInput(false, 1060);

            Assert.Equal(ReplayAction.DownUp, _detector.Reset());
        }

        [Fact]
        public void Reset_when_idle_returns_nothing() =>
            Assert.Equal(ReplayAction.None, _detector.Reset());
    }

    public sealed class Idleness
    {
        [Theory]
        [InlineData(1, false)]
        [InlineData(1, true)]
        [InlineData(2, false)]
        [InlineData(2, true)]
        public void A_new_detector_is_idle(int tapCount, bool swallow) =>
            Assert.True(Create(tapCount, swallow).IsIdle);

        [Fact]
        public void Single_tap_swallow_is_busy_between_the_press_and_its_release()
        {
            var detector = Create(1, swallow: true);

            detector.OnInput(true, 1000);
            Assert.False(detector.IsIdle);

            detector.OnInput(false, 1050);
            Assert.True(detector.IsIdle);
        }

        [Fact]
        public void Single_tap_pass_through_never_becomes_busy()
        {
            var detector = Create(1, swallow: false);

            detector.OnInput(true, 1000);

            Assert.True(detector.IsIdle);
        }

        [Fact]
        public void Double_tap_swallow_is_busy_while_a_press_is_held_back_and_idle_once_it_is_replayed()
        {
            var detector = Create(2, swallow: true);

            detector.OnInput(true, 1000);
            detector.OnInput(false, 1050);
            Assert.False(detector.IsIdle);

            detector.OnTimeout(1000 + Window + 1);
            Assert.True(detector.IsIdle);
        }

        [Fact]
        public void Double_tap_pass_through_stays_busy_after_a_first_press_until_the_next()
        {
            var detector = Create(2, swallow: false);

            detector.OnInput(true, 1000);
            detector.OnInput(false, 1050);
            Assert.False(detector.IsIdle);

            detector.OnInput(true, 1100);
            Assert.True(detector.IsIdle);
        }

        [Fact]
        public void Reset_makes_it_idle()
        {
            var detector = Create(2, swallow: true);
            detector.OnInput(true, 1000);

            detector.Reset();

            Assert.True(detector.IsIdle);
        }
    }
}
