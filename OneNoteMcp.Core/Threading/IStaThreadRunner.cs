namespace OneNoteMcp.Core.Threading;

public interface IStaThreadRunner : IDisposable
{
    Task<T> RunAsync<T>(Func<T> work, CancellationToken cancellationToken = default);

    Task RunAsync(Action work, CancellationToken cancellationToken = default);
}
