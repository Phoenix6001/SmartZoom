using System.Diagnostics;

using SmartZoom.Interop.Office;

namespace SmartZoom.Interop.Tests.Office;

/// <summary>
/// What happens when Office stops answering. A modal dialog parks the object-model call that is running and
/// everything queued behind it, so the property that matters is that a press costs one timeout rather than
/// the session: the wait ends, later calls do not queue up behind the same dialog, and the thread comes back
/// on its own once the dialog is dismissed.
/// </summary>
public sealed class StaThreadTests
{
    /// <summary>Short enough to keep the tests quick, long enough not to trip over a loaded build agent.</summary>
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(250);

    /// <summary>How long a test waits for something that should happen promptly.</summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    [Fact]
    public void Work_runs_on_a_single_threaded_apartment_thread()
    {
        using var sta = new StaThread("test", Timeout);

        Assert.Equal(ApartmentState.STA, sta.Run(() => Thread.CurrentThread.GetApartmentState()));
    }

    [Fact]
    public void A_result_comes_back_and_a_failure_comes_back_as_itself()
    {
        using var sta = new StaThread("test", Timeout);

        Assert.Equal(42, sta.Run(() => 42));

        // Not wrapped in an AggregateException: the Office adapters catch COMException by type.
        var thrown = Assert.Throws<InvalidOperationException>(() => sta.Run<int>(() => throw new InvalidOperationException("boom")));
        Assert.Equal("boom", thrown.Message);
    }

    [Fact]
    public void A_call_the_application_never_answers_gives_up_instead_of_waiting_for_ever()
    {
        using var sta = new StaThread("test", Timeout);
        var dialog = new Dialog();

        var waited = Stopwatch.StartNew();
        Assert.Throws<TimeoutException>(() => sta.Run(dialog.Wait));
        waited.Stop();

        Assert.True(waited.Elapsed < Patience, $"gave up after {waited.Elapsed}, which is not a bounded wait");

        dialog.Dismiss();
    }

    [Fact]
    public async Task An_attach_the_application_never_answers_gives_up_too()
    {
        // The asynchronous entry point is how a zoom attaches to Word or Excel in the first place, and the
        // task it hands back is not completed by anything at all while the thread is parked.
        using var sta = new StaThread("test", Timeout);
        var dialog = new Dialog();

        await Assert.ThrowsAsync<TimeoutException>(() => sta.RunAsync(dialog.Wait, CancellationToken.None));

        dialog.Dismiss();
    }

    [Fact]
    public void A_call_behind_one_that_was_given_up_on_fails_at_once_rather_than_waiting_again()
    {
        // Giving up does not free the thread — the call is still inside the application — so waiting the
        // whole timeout again would only make the next press slower for no chance of an answer.
        using var sta = new StaThread("test", Timeout);
        var dialog = new Dialog();

        Assert.Throws<TimeoutException>(() => sta.Run(dialog.Wait));

        var waited = Stopwatch.StartNew();
        Assert.Throws<TimeoutException>(() => sta.Run(() => 1));
        waited.Stop();

        Assert.True(waited.Elapsed < Timeout, $"waited {waited.Elapsed} for a thread already known to be stuck");

        dialog.Dismiss();
    }

    [Fact]
    public void Calls_go_through_again_once_the_application_comes_back()
    {
        // The one that matters most: refusing for ever would be worse than the hang this replaces. Nothing
        // resets the thread, so it has to recover on its own the moment the dialog is dismissed.
        using var sta = new StaThread("test", Timeout);
        var dialog = new Dialog();

        Assert.Throws<TimeoutException>(() => sta.Run(dialog.Wait));
        dialog.Dismiss();

        Assert.Equal(7, Eventually(() => sta.Run(() => 7)));
    }

    [Fact]
    public async Task Posted_work_neither_waits_for_the_thread_nor_reports_its_failure()
    {
        // This is what a zoom's disposal uses: releasing a COM reference must not throw a timeout over the
        // exception that sent the caller into its finally block.
        using var sta = new StaThread("test", Timeout);
        var dialog = new Dialog();

        var parked = Task.Run(() => Assert.Throws<TimeoutException>(() => sta.Run(dialog.Wait)));
        await dialog.Opened.WaitAsync(Patience);

        var waited = Stopwatch.StartNew();
        sta.Post(() => { });
        sta.Post(() => throw new InvalidOperationException("nobody is listening"));
        waited.Stop();

        Assert.True(waited.Elapsed < Timeout, $"posting waited {waited.Elapsed}");

        // Only once the parked call has given up, so that dismissing cannot be what ended it.
        await parked.WaitAsync(Patience);
        dialog.Dismiss();

        // The posted failure did not end the thread, which would have stranded every call after it.
        Assert.Equal(7, Eventually(() => sta.Run(() => 7)));
    }

    [Fact]
    public void A_disposed_thread_refuses_work_rather_than_swallowing_it()
    {
        var sta = new StaThread("test", Timeout);
        sta.Dispose();

        Assert.Throws<ObjectDisposedException>(() => sta.Run(() => 1));

        // Posting is the exception: it is called from a Dispose, which must not throw.
        sta.Post(() => { });
    }

    /// <summary>
    /// Retries until the thread has caught up. Nothing signals that, because the thread being behind is not
    /// a state the thread itself knows about — it is a call that has not returned yet.
    /// </summary>
    private static T Eventually<T>(Func<T> work)
    {
        var waited = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                return work();
            }
            catch (TimeoutException) when (waited.Elapsed < Patience)
            {
                Thread.Sleep(10);
            }
        }
    }

    /// <summary>
    /// Stands in for the modal dialog: work that does not return until somebody dismisses it. Built on a
    /// task rather than a wait handle so that nothing here is disposed while the parked thread is still
    /// inside it.
    /// </summary>
    private sealed class Dialog
    {
        private readonly TaskCompletionSource _opened = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _dismissed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Completes once the thread is actually inside the call, not merely on its way there.</summary>
        public Task Opened => _opened.Task;

        public int Wait()
        {
            _opened.TrySetResult();
            _dismissed.Task.GetAwaiter().GetResult();
            return 0;
        }

        public void Dismiss() => _dismissed.TrySetResult();
    }
}
