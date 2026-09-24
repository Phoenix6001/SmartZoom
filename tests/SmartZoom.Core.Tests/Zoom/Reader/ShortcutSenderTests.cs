using System.Diagnostics;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

using SmartZoom.Core.Input;
using SmartZoom.Core.Zoom.Reader;

namespace SmartZoom.Core.Tests.Zoom.Reader;

/// <summary>
/// The foreground etiquette both reader adapters depend on. Every one of these is a bug that reached a user
/// or a workbook once.
/// </summary>
public sealed class ShortcutSenderTests
{
    private static readonly KeyCombo FitWidth = KeyCombo.Parse("Ctrl+2");

    private readonly FakeInputInjector _injector = new();
    private readonly FakeActivator _activator = new();
    private readonly FakeTimeProvider _time = new();

    private ShortcutSender Create(TimeSpan? modifierTimeout = null) =>
        new(_injector, _activator, _time, NullLogger<ShortcutSender>.Instance, modifierTimeout ?? ShortcutSender.ModifierReleaseTimeout);

    /// <summary>Runs a send to completion, stepping the fake clock through every wait the sender makes.</summary>
    private async Task<bool> SendAsync(Action? prepare = null, Action? finish = null, TimeSpan? modifierTimeout = null)
    {
        var send = Create(modifierTimeout).SendAsync(Reader.Acrobat, FitWidth, prepare, finish, CancellationToken.None);
        var patience = Stopwatch.StartNew();
        while (!send.IsCompleted)
        {
            // The sender's continuations run on the pool; give them real time, bounded by the wall clock rather than
            // by a step count, so a loaded test run cannot starve them into a false failure.
            Assert.True(patience.Elapsed < TimeSpan.FromSeconds(10), "the send did not complete");
            _time.Advance(TimeSpan.FromMilliseconds(10));
            await Task.WhenAny(send, Task.Delay(1));
        }

        return await send;
    }

    [Fact]
    public async Task The_target_is_focused_and_the_shortcut_is_sent()
    {
        Assert.True(await SendAsync());

        Assert.Equal([0x300L], _activator.Activated);
        Assert.Equal("Ctrl+2", _injector.Script);
    }

    [Fact]
    public async Task The_window_that_was_in_front_is_brought_back_afterwards()
    {
        // Otherwise the reader stays on top of an overlapping browser and catches the user's next trigger.
        _activator.ForegroundWindow = 0x999;

        Assert.True(await SendAsync());

        Assert.Equal([0x300L, 0x999L], _activator.Activated);
        Assert.Equal(0x999, _activator.ForegroundWindow);
    }

    [Fact]
    public async Task A_target_already_in_front_is_left_in_front()
    {
        _activator.ForegroundWindow = 0x300;

        await SendAsync();

        Assert.Equal([0x300L], _activator.Activated);
    }

    [Fact]
    public async Task No_foreground_window_means_nothing_to_bring_back()
    {
        _activator.ForegroundWindow = 0;

        await SendAsync();

        Assert.Equal([0x300L], _activator.Activated);
    }

    [Fact]
    public async Task The_previous_window_is_brought_back_even_when_the_shortcut_is_rejected()
    {
        _activator.ForegroundWindow = 0x999;
        _injector.FailKeys = true;

        Assert.False(await SendAsync());

        Assert.Equal([0x300L, 0x999L], _activator.Activated);
    }

    [Fact]
    public async Task Failing_to_bring_the_previous_window_back_does_not_undo_the_press()
    {
        _activator.ForegroundWindow = 0x999;
        _activator.RestoreSucceeds = false;

        Assert.True(await SendAsync());
    }

    [Fact]
    public async Task A_window_that_cannot_be_focused_is_sent_nothing()
    {
        _activator.Succeeds = false;

        Assert.False(await SendAsync());
        Assert.Empty(_injector.Log);
    }

    [Fact]
    public async Task A_window_that_loses_the_foreground_while_we_wait_is_not_sent_the_shortcut()
    {
        // These shortcuts are destructive in the wrong application: Ctrl+0 hides the selected column in Excel.
        _activator.StealForegroundAfterActivating = 0x999;

        Assert.False(await SendAsync());
        Assert.Empty(_injector.Log);
    }

    [Fact]
    public async Task A_window_the_user_moved_to_while_we_waited_is_left_in_front()
    {
        // The foreground is only put back while the reader still holds it; a third window in front is the
        // user's own doing, and taking it away from them would be the rearrangement this exists to avoid.
        _activator.ForegroundWindow = 0x111;
        _activator.StealForegroundAfterActivating = 0x999;

        Assert.False(await SendAsync());

        Assert.Equal([0x300L], _activator.Activated);
        Assert.Equal(0x999, _activator.ForegroundWindow);
    }

    [Fact]
    public async Task Modifiers_the_user_still_holds_from_a_hotkey_trigger_are_neither_pressed_nor_released()
    {
        _injector.HeldModifiers = KeyModifiers.Control;

        Assert.True(await SendAsync());
        Assert.Equal("2", _injector.Script);
    }

    [Fact]
    public async Task A_foreign_modifier_is_waited_out_before_sending()
    {
        _injector.HeldModifiers = KeyModifiers.Control | KeyModifiers.Alt;
        _injector.ReleaseHeldAfterPolls = 3;

        Assert.True(await SendAsync());

        Assert.Equal("Ctrl+2", _injector.Script);
        Assert.True(_injector.ModifierPolls >= 4);
    }

    [Fact]
    public async Task A_foreign_modifier_held_past_the_timeout_means_nothing_is_sent()
    {
        _injector.HeldModifiers = KeyModifiers.Alt;

        Assert.False(await SendAsync(modifierTimeout: TimeSpan.FromMilliseconds(50)));
        Assert.Empty(_injector.Log);
    }

    [Fact]
    public async Task Prepare_runs_after_the_modifier_wait_and_finish_after_the_keys()
    {
        // A wheel turn with the trigger's Control still down is a zoom to a reader, not a scroll, so the
        // preparation cannot happen before the wait.
        var order = new List<string>();
        _injector.HeldModifiers = KeyModifiers.Alt;
        _injector.ReleaseHeldAfterPolls = 2;

        await SendAsync(prepare: () => order.Add("prepare"), finish: () => order.Add("finish"));

        Assert.Equal(["prepare", "Ctrl+2", "finish"], order.Take(1).Concat(_injector.Log).Concat(order.Skip(1)));
    }

    [Fact]
    public async Task Nothing_runs_after_a_rejected_press()
    {
        _injector.FailKeys = true;
        var finished = false;

        Assert.False(await SendAsync(finish: () => finished = true));
        Assert.False(finished);
    }
}
