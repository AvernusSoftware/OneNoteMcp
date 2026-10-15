using OneNoteMcp.Core.Threading;

namespace OneNoteMcp.Tests;

internal sealed class InlineStaThreadRunner : IStaThreadRunner
{
    private bool _disposed;

    public bool IsRunningWork { get; private set; }

    public Task<T> RunAsync<T>(Func<T> work, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled<T>(cancellationToken);
        }

        bool outer = !IsRunningWork;
        IsRunningWork = true;
        try
        {
            return Task.FromResult(work());
        }
        catch (Exception ex)
        {
            return Task.FromException<T>(ex);
        }
        finally
        {
            if (outer)
            {
                IsRunningWork = false;
            }
        }
    }

    public Task RunAsync(Action work, CancellationToken cancellationToken = default) =>
        RunAsync(
            () =>
            {
                work();
                return true;
            },
            cancellationToken);

    public void Dispose() => _disposed = true;
}
