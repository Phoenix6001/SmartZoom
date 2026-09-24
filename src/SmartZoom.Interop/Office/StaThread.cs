using System.Collections.Concurrent;

namespace SmartZoom.Interop.Office;

/// <summary>
/// A single-threaded apartment thread that runs delegates in order. Office's object model is
/// apartment-threaded; talking to it from one STA thread avoids cross-apartment marshalling cost and
/// the RPC_E_CANTCALLOUT_ININPUTSYNCCALL class of errors.
/// </summary>
internal sealed class StaThread : IDisposable
{
    private readonly BlockingCollection<Action> _queue = [];
    private readonly Thread _thread;

    public StaThread(string name)
    {
        _thread = new Thread(Pump) { IsBackground = true, Name = name };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    /// <summary>Runs <paramref name="work"/> on the STA thread and waits for its result.</summary>
    /// <remarks>
    /// There is no timeout: the wait lasts as long as the work and everything queued ahead of it, which for
    /// an Office call is as long as Office takes to answer. Callers that cannot wait use <see cref="RunAsync{T}"/>.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The thread has been disposed.</exception>
    public T Run<T>(Func<T> work)
    {
        ThrowIfDisposed();

        if (Thread.CurrentThread == _thread)
            return work();

        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Enqueue(() =>
        {
            try
            {
                completion.SetResult(work());
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        }, CancellationToken.None);

        return completion.Task.GetAwaiter().GetResult();
    }

    /// <summary>Runs <paramref name="work"/> on the STA thread and waits for it to finish.</summary>
    /// <exception cref="ObjectDisposedException">The thread has been disposed.</exception>
    public void Run(Action work) => Run(() =>
    {
        work();
        return true;
    });

    /// <summary>Runs <paramref name="work"/> on the STA thread without blocking the caller.</summary>
    /// <exception cref="ObjectDisposedException">The thread has been disposed.</exception>
    public Task<T> RunAsync<T>(Func<T> work, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Enqueue(() =>
        {
            if (cancellationToken.IsCancellationRequested)
            {
                completion.SetCanceled(cancellationToken);
                return;
            }

            try
            {
                completion.SetResult(work());
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        }, cancellationToken);

        return completion.Task;
    }

    public void Dispose() => _queue.CompleteAdding();

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_queue.IsAddingCompleted, this);

    // Dispose can slip in between ThrowIfDisposed and Add; the collection then refuses with an
    // InvalidOperationException, which is the same condition and gets the same exception.
    private void Enqueue(Action work, CancellationToken cancellationToken)
    {
        try
        {
            _queue.Add(work, cancellationToken);
        }
        catch (InvalidOperationException) when (_queue.IsAddingCompleted)
        {
            throw new ObjectDisposedException(GetType().FullName);
        }
    }

    private void Pump()
    {
        foreach (var work in _queue.GetConsumingEnumerable())
            work();
    }
}
