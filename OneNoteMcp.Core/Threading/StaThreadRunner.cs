using System.Collections.Concurrent;

namespace OneNoteMcp.Core.Threading;

/// <summary>
/// A single dedicated STA thread with a work queue.
/// </summary>
/// <remarks>
/// <para>
/// Exactly one thread, never a pool: the OneNote RCW is apartment-affine, so all calls must land
/// on the thread that created it.
/// </para>
/// <para>
/// The idle thread deliberately does not run a Windows message pump. That is safe here because we
/// register no COM event sinks, so OneNote never calls into us; COM pumps messages itself for the
/// duration of each outgoing synchronous call.
/// </para>
/// </remarks>
public sealed class StaThreadRunner : IStaThreadRunner
{
    private readonly BlockingCollection<Action> _queue = new(new ConcurrentQueue<Action>());
    private readonly Thread _thread;
    private volatile bool _disposed;

    public StaThreadRunner()
    {
        _thread = new Thread(Loop)
        {
            IsBackground = true,
            Name = "OneNote-STA",
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    /// <summary>Exposed for tests: the managed id of the worker thread.</summary>
    public int ThreadId => _thread.ManagedThreadId;

    private void Loop()
    {
        try
        {
            // A faulted work item must never take the loop down - each item captures its own
            // exception into its TaskCompletionSource.
            foreach (Action item in _queue.GetConsumingEnumerable())
            {
                item();
            }
        }
        catch (ObjectDisposedException)
        {
            // Queue disposed during shutdown.
        }
        catch (OperationCanceledException)
        {
            // CompleteAdding raced with a pending take.
        }
    }

    public Task<T> RunAsync<T>(Func<T> work, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(work);
        ObjectDisposedException.ThrowIf(_disposed, this);

        // RunContinuationsAsynchronously is essential: without it the awaiting continuation
        // resumes ON the STA thread, starving the queue and deadlocking nested calls.
        TaskCompletionSource<T> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

        if (cancellationToken.IsCancellationRequested)
        {
            tcs.SetCanceled(cancellationToken);
            return tcs.Task;
        }

        try
        {
            _queue.Add(() =>
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    tcs.TrySetCanceled(cancellationToken);
                    return;
                }

                try
                {
                    tcs.TrySetResult(work());
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            });
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException)
        {
            throw new ObjectDisposedException(nameof(StaThreadRunner), ex);
        }

        return tcs.Task;
    }

    public Task RunAsync(Action work, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(work);
        return RunAsync(
            () =>
            {
                work();
                return true;
            },
            cancellationToken);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            _queue.CompleteAdding();
        }
        catch (ObjectDisposedException)
        {
            // Already torn down.
        }

        // Give in-flight COM calls a chance to finish; the thread is a background thread, so a
        // hung OneNote call cannot block process exit.
        _thread.Join(TimeSpan.FromSeconds(10));
        _queue.Dispose();
    }
}
