using SmartZoom.Core.Input;

namespace SmartZoom.Core.Tests.Input;

public sealed class DoubleTapDetectorTests
{
    private const uint Window = 300;
    private const MouseButton Trigger = MouseButton.XButton2;

    private static readonly HookDecision PassedThrough = new(Swallow: false, Triggered: false, ReplayAction.None);
    private static readonly HookDecision Swallowed = new(Swallow: true, Triggered: false, ReplayAction.None);
    private static readonly HookDecision SwallowedAndTriggered = new(Swallow: true, Triggered: true, ReplayAction.None);

    private static DoubleTapDetector Create(bool swallow) => new(new TriggerOptions(Trigger, Window, swallow));

    public sealed class Construction
    {
        [Fact]
        public void Rejects_missing_button() =>
            Assert.Throws<ArgumentException>(() => new DoubleTapDetector(new TriggerOptions(MouseButton.None, Window, false)));

        [Theory]
        [InlineData(0u)]
        [InlineData(5001u)]
        public void Rejects_out_of_range_window(uint windowMs) =>
            Assert.Throws<ArgumentOutOfRangeException>(() => new DoubleTapDetector(new TriggerOptions(Trigger, windowMs, false)));
    }

    public sealed class PassThroughMode
    {
        private readonly DoubleTapDetector _detector = Create(swallow: false);

        [Fact]
        public void Two_presses_within_window_trigger_on_second_down_without_swallowing()
        {
            Assert.Equal(PassedThrough, _detector.OnButton(Trigger, isDown: true, 1000));
            Assert.Equal(PassedThrough, _detector.OnButton(Trigger, isDown: false, 1050));

            var decision = _detector.OnButton(Trigger, isDown: true, 1200);

            Assert.True(decision.Triggered);
            Assert.False(decision.Swallow);
            Assert.Equal(ReplayAction.None, decision.Replay);
            Assert.Equal(PassedThrough, _detector.OnButton(Trigger, isDown: false, 1250));
        }

        [Fact]
        public void Press_exactly_at_window_boundary_triggers()
        {
            _detector.OnButton(Trigger, isDown: true, 1000);

            Assert.True(_detector.OnButton(Trigger, isDown: true, 1000 + Window).Triggered);
        }

        [Fact]
        public void Press_after_window_does_not_trigger_but_starts_a_new_sequence()
        {
            _detector.OnButton(Trigger, isDown: true, 1000);

            Assert.False(_detector.OnButton(Trigger, isDown: true, 1000 + Window + 1).Triggered);
            Assert.True(_detector.OnButton(Trigger, isDown: true, 1000 + Window + 100).Triggered);
        }

        [Fact]
        public void Triple_press_triggers_once_and_fourth_press_triggers_again()
        {
            Assert.False(_detector.OnButton(Trigger, true, 1000).Triggered);
            Assert.True(_detector.OnButton(Trigger, true, 1100).Triggered);
            Assert.False(_detector.OnButton(Trigger, true, 1200).Triggered);
            Assert.True(_detector.OnButton(Trigger, true, 1300).Triggered);
        }

        [Theory]
        [InlineData(MouseButton.Middle)]
        [InlineData(MouseButton.XButton1)]
        public void Other_buttons_are_ignored_and_do_not_disturb_a_pending_press(MouseButton other)
        {
            _detector.OnButton(Trigger, true, 1000);

            Assert.Equal(PassedThrough, _detector.OnButton(other, true, 1050));
            Assert.Equal(PassedThrough, _detector.OnButton(other, true, 1100));
            Assert.True(_detector.OnButton(Trigger, true, 1150).Triggered);
        }

        [Fact]
        public void Handles_tick_count_wraparound()
        {
            _detector.OnButton(Trigger, true, uint.MaxValue - 50);

            Assert.True(_detector.OnButton(Trigger, true, 100).Triggered);
        }

        [Fact]
        public void Never_requests_a_timer_or_replay()
        {
            _detector.OnButton(Trigger, true, 1000);

            Assert.False(_detector.TryGetDeadline(out _));
            Assert.Equal(ReplayAction.None, _detector.OnTimeout(5000));
            Assert.Equal(ReplayAction.None, _detector.Reset());
        }
    }

    public sealed class SwallowMode
    {
        private readonly DoubleTapDetector _detector = Create(swallow: true);

        [Fact]
        public void Double_tap_swallows_all_four_events_and_triggers_on_second_down()
        {
            Assert.Equal(Swallowed, _detector.OnButton(Trigger, true, 1000));
            Assert.Equal(Swallowed, _detector.OnButton(Trigger, false, 1060));
            Assert.Equal(SwallowedAndTriggered, _detector.OnButton(Trigger, true, 1150));
            Assert.Equal(Swallowed, _detector.OnButton(Trigger, false, 1210));

            Assert.False(_detector.TryGetDeadline(out _));
        }

        [Fact]
        public void Reports_deadline_one_tick_after_window_while_press_is_held()
        {
            _detector.OnButton(Trigger, true, 1000);

            Assert.True(_detector.TryGetDeadline(out var deadline));
            Assert.Equal(1000 + Window + 1, deadline);
        }

        [Fact]
        public void Single_click_is_replayed_as_full_click_after_window_expires()
        {
            _detector.OnButton(Trigger, true, 1000);
            _detector.OnButton(Trigger, false, 1060);

            Assert.Equal(ReplayAction.None, _detector.OnTimeout(1000 + Window));
            Assert.Equal(ReplayAction.DownUp, _detector.OnTimeout(1000 + Window + 1));
            Assert.False(_detector.TryGetDeadline(out _));

            // Back to idle: the next press is held again.
            Assert.Equal(Swallowed, _detector.OnButton(Trigger, true, 2000));
        }

        [Fact]
        public void Long_press_replays_down_on_timeout_and_lets_physical_up_through()
        {
            _detector.OnButton(Trigger, true, 1000);

            Assert.Equal(ReplayAction.Down, _detector.OnTimeout(1000 + Window + 1));
            Assert.False(_detector.TryGetDeadline(out _));
            Assert.Equal(PassedThrough, _detector.OnButton(Trigger, false, 1500));
            Assert.Equal(Swallowed, _detector.OnButton(Trigger, true, 2000));
        }

        [Fact]
        public void Late_second_press_before_timer_fires_replays_first_click_and_holds_the_new_press()
        {
            _detector.OnButton(Trigger, true, 1000);
            _detector.OnButton(Trigger, false, 1060);

            var decision = _detector.OnButton(Trigger, true, 1000 + Window + 50);

            Assert.Equal(new HookDecision(Swallow: true, Triggered: false, ReplayAction.DownUp), decision);
            Assert.True(_detector.TryGetDeadline(out var deadline));
            Assert.Equal(1000 + Window + 50 + Window + 1, deadline);
        }

        [Fact]
        public void Timeout_after_trigger_is_a_no_op()
        {
            _detector.OnButton(Trigger, true, 1000);
            _detector.OnButton(Trigger, false, 1060);
            _detector.OnButton(Trigger, true, 1150);

            Assert.Equal(ReplayAction.None, _detector.OnTimeout(1000 + Window + 1));
            Assert.Equal(Swallowed, _detector.OnButton(Trigger, false, 1400));
        }

        [Fact]
        public void Stray_up_while_idle_passes_through()
        {
            Assert.Equal(PassedThrough, _detector.OnButton(Trigger, false, 1000));
        }

        [Fact]
        public void Other_buttons_are_never_swallowed()
        {
            _detector.OnButton(Trigger, true, 1000);

            Assert.Equal(PassedThrough, _detector.OnButton(MouseButton.XButton1, true, 1010));
            Assert.Equal(PassedThrough, _detector.OnButton(MouseButton.XButton1, false, 1020));
        }

        [Fact]
        public void Reset_while_down_returns_down_so_button_is_not_lost()
        {
            _detector.OnButton(Trigger, true, 1000);

            Assert.Equal(ReplayAction.Down, _detector.Reset());
            Assert.False(_detector.TryGetDeadline(out _));
        }

        [Fact]
        public void Reset_after_click_returns_full_click()
        {
            _detector.OnButton(Trigger, true, 1000);
            _detector.OnButton(Trigger, false, 1060);

            Assert.Equal(ReplayAction.DownUp, _detector.Reset());
        }

        [Fact]
        public void Reset_when_idle_returns_nothing() =>
            Assert.Equal(ReplayAction.None, _detector.Reset());
    }
}
