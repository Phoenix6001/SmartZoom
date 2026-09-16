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
    public T Run<T>(Func<T> work)
    {
        if (Thread.CurrentThread == _thread)
            return work();

        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _queue.Add(() =>
        {
            try
            {
                completion.SetResult(work());
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        });

        return completion.Task.GetAwaiter().GetResult();
    }

    /// <summary>Runs <paramref name="work"/> on the STA thread and waits for it to finish.</summary>
    public void Run(Action work) => Run(() =>
    {
        work();
        return true;
    });

    /// <summary>Runs <paramref name="work"/> on the STA thread without blocking the caller.</summary>
    public Task<T> RunAsync<T>(Func<T> work, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _queue.Add(() =>
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

    private void Pump()
    {
        foreach (var work in _queue.GetConsumingEnumerable())
            work();
    }
}
