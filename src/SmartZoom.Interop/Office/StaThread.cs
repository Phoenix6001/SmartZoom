using System.Collections.Concurrent;
using System.Globalization;

namespace SmartZoom.Interop.Office;

/// <summary>
/// A single-threaded apartment thread that runs delegates in order. Office's object model is
/// apartment-threaded; talking to it from one STA thread avoids cross-apartment marshalling cost and
/// the RPC_E_CANTCALLOUT_ININPUTSYNCCALL class of errors.
/// </summary>
/// <remarks>
/// <para>
/// Every wait is bounded. Office answers the object model only between its own message pumps, so a modal
/// dialog — Save As, "do you want to save your changes?", a cell part-way through being edited — parks the
/// call that is running and everything queued behind it for as long as the dialog is up. That is a wait on a
/// person, and no answer is coming: the waits give up instead, so a press costs one timeout and the strategy
/// reports that Word or Excel could not be reached.
/// </para>
/// <para>
/// Giving up does not free the thread — the call is still inside Office — so once one wait has timed out the
/// next call fails immediately rather than queueing behind the same dialog for another timeout. Both counts
/// below only ever rise, so the moment Office answers and the queue drains, calls go through again on their
/// own.
/// </para>
/// </remarks>
internal sealed class StaThread : IDisposable
{
    /// <summary>
    /// How long a call waits for Office to answer. Long enough for an application that is genuinely busy —
    /// a large recalculation, a document still opening — and short enough that a dialog nobody is going to
    /// dismiss costs one press rather than the session.
    /// </summary>
    internal static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(4);

    private readonly BlockingCollection<Action> _queue = [];
    private readonly Thread _thread;
    private readonly TimeSpan _timeout;

    // Work is queued and run one item at a time, so these are all it takes to know whether the thread is
    // still stuck on a call somebody gave up waiting for: the item holding it up is the _abandonedAt'th one,
    // and it is done once that many items have finished. Concurrent callers can take their numbers in a
    // different order than their work reaches the queue, which makes the test conservative by at most one
    // item and never wrong, because both counts still reach the same total.
    private long _queued;
    private long _finished;
    private long _abandonedAt;

    /// <summary>Starts the thread.</summary>
    /// <param name="name">Thread name, as a debugger shows it.</param>
    public StaThread(string name)
        : this(name, DefaultTimeout)
    {
    }

    /// <summary>Lets a test shorten how long a call waits for the thread.</summary>
    /// <param name="name">Thread name, as a debugger shows it.</param>
    /// <param name="timeout">How long a call waits before giving up.</param>
    internal StaThread(string name, TimeSpan timeout)
    {
        _timeout = timeout;
        _thread = new Thread(Pump) { IsBackground = true, Name = name };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    /// <summary>Runs <paramref name="work"/> on the STA thread and waits for its result.</summary>
    /// <remarks>
    /// The wait covers this call and everything queued ahead of it, which for an Office call is as long as
    /// Office takes to answer — up to the timeout. Callers that cannot wait at all use <see cref="Post"/>.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The thread has been disposed.</exception>
    /// <exception cref="TimeoutException">
    /// The application did not answer in time, or has not yet caught up with an earlier call it failed to
    /// answer.
    /// </exception>
    public T Run<T>(Func<T> work)
    {
        ThrowIfDisposed();

        if (Thread.CurrentThread == _thread)
            return work();

        ThrowIfStalled();

        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var ticket = Enqueue(() => Complete(completion, work), CancellationToken.None);

        // The wait handle rather than Task.Wait(timeout), which reports whatever the work threw as an
        // AggregateException; the awaiter below rethrows it as the caller's catch clauses expect it.
        if (!((IAsyncResult)completion.Task).AsyncWaitHandle.WaitOne(_timeout))
        {
            Abandon(ticket, completion.Task);
            throw Stalled();
        }

        return completion.Task.GetAwaiter().GetResult();
    }

    /// <summary>Runs <paramref name="work"/> on the STA thread and waits for it to finish.</summary>
    /// <exception cref="ObjectDisposedException">The thread has been disposed.</exception>
    /// <exception cref="TimeoutException">The application did not answer in time.</exception>
    public void Run(Action work) => Run(() =>
    {
        work();
        return true;
    });

    /// <summary>Runs <paramref name="work"/> on the STA thread without blocking the caller.</summary>
    /// <param name="work">The work to run.</param>
    /// <param name="cancellationToken">Stops waiting, and skips the work if it has not started.</param>
    /// <exception cref="ObjectDisposedException">The thread has been disposed.</exception>
    /// <exception cref="TimeoutException">The application did not answer in time.</exception>
    public async Task<T> RunAsync<T>(Func<T> work, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ThrowIfStalled();

        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var ticket = Enqueue(
            () =>
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    completion.SetCanceled(cancellationToken);
                    return;
                }

                Complete(completion, work);
            },
            cancellationToken);

        try
        {
            return await completion.Task.WaitAsync(_timeout, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            Abandon(ticket, completion.Task);
            throw Stalled();
        }
    }

    /// <summary>Queues <paramref name="work"/> and returns. Nothing waits for it and nothing is reported.</summary>
    /// <remarks>
    /// For work whose result nobody needs and whose failure nobody could act on — releasing a COM reference
    /// at the end of a zoom. Waiting for that would let a disposal throw a timeout over the exception that
    /// sent the caller into its <c>finally</c> block in the first place.
    /// </remarks>
    /// <param name="work">The work to run.</param>
    public void Post(Action work)
    {
        try
        {
            Enqueue(work, CancellationToken.None);
        }
        catch (ObjectDisposedException)
        {
            // The thread is gone, so the reference goes with the process; there is nothing to release it to.
        }
    }

    /// <inheritdoc />
    public void Dispose() => _queue.CompleteAdding();

    private static void Complete<T>(TaskCompletionSource<T> completion, Func<T> work)
    {
        try
        {
            completion.SetResult(work());
        }
        catch (Exception ex)
        {
            completion.SetException(ex);
        }
    }

    /// <summary>Raises <paramref name="target"/> to <paramref name="value"/>, never lowering it.</summary>
    private static void RaiseTo(ref long target, long value)
    {
        var seen = Volatile.Read(ref target);
        while (seen < value)
        {
            var was = Interlocked.CompareExchange(ref target, value, seen);
            if (was == seen)
                return;

            seen = was;
        }
    }

    private static void Observe(Task work) =>
        _ = work.ContinueWith(
            static finished => _ = finished.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_queue.IsAddingCompleted, this);

    private void ThrowIfStalled()
    {
        if (Volatile.Read(ref _finished) < Volatile.Read(ref _abandonedAt))
            throw Stalled();
    }

    private TimeoutException Stalled() => new(string.Create(
        CultureInfo.InvariantCulture,
        $"{_thread.Name} did not answer within {_timeout.TotalSeconds:0.#} s. The application is most likely showing a dialog."));

    private void Abandon(long ticket, Task work)
    {
        // Somebody has to look at what the work eventually does, or a failure nobody is waiting for any more
        // resurfaces as an unobserved task exception.
        Observe(work);

        RaiseTo(ref _abandonedAt, ticket);
    }

    // Dispose can slip in between ThrowIfDisposed and Add; the collection then refuses with an
    // InvalidOperationException, which is the same condition and gets the same exception.
    private long Enqueue(Action work, CancellationToken cancellationToken)
    {
        try
        {
            _queue.Add(
                () =>
                {
                    try
                    {
                        work();
                    }
                    finally
                    {
                        Interlocked.Increment(ref _finished);
                    }
                },
                cancellationToken);
        }
        catch (InvalidOperationException) when (_queue.IsAddingCompleted)
        {
            throw new ObjectDisposedException(GetType().FullName);
        }

        // Counted only once the work is on the queue, so nothing that never got there can leave the thread
        // looking permanently behind.
        return Interlocked.Increment(ref _queued);
    }

    private void Pump()
    {
        foreach (var work in _queue.GetConsumingEnumerable())
        {
            try
            {
                work();
            }
            catch (Exception)
            {
                // Run and RunAsync hand a failure back to their caller, so anything reaching here came from
                // Post and has nobody to tell. Letting it end the thread would strand every call after it.
            }
        }
    }
}
