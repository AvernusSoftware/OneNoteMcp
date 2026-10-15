using OneNoteMcp.Core.Exceptions;
using OneNoteMcp.Core.Threading;
using System.Runtime.InteropServices;

namespace OneNoteMcp.Core.Interop;

/// <summary>
/// Owns the OneNote COM object. All members must be touched only from the STA thread.
/// </summary>
public sealed class OneNoteApplicationHandle : IDisposable
{
    // Classic Office automation HRESULTs that mean "busy, try again".
    private const int RpcECallRejected = unchecked((int)0x80010001);

    private const int RpcEServerCallRetryLater = unchecked((int)0x8001010A);
    private const int CoEServerExecFailure = unchecked((int)0x80080005);

    // OneNote went away (user quit it) - the RCW is dead and must be replaced.
    private const int RpcSServerUnavailable = unchecked((int)0x800706BA);

    private const int RpcEDisconnected = unchecked((int)0x80010108);

    private readonly IComObjects _comObjects;
    private readonly IStaThreadRunner _sta;
    private readonly TimeProvider _time;
    private readonly TimeSpan _idleRelease;
    private IApplication? _app;
    private ITimer? _idleTimer;
    private long _completedOperations;

    public OneNoteApplicationHandle(IComObjects comObjects, IStaThreadRunner sta, TimeProvider time, TimeSpan idleRelease)
    {
        _comObjects = comObjects;
        _sta = sta;
        _time = time;
        _idleRelease = idleRelease;
    }

    /// <summary>Gets the application, activating OneNote on first use.</summary>
    public IApplication Application => _app ??= Activate();

    private IApplication Activate()
    {
        Exception? last = null;
        for (int attempt = 1; attempt <= 5; attempt++)
        {
            try
            {
                return _comObjects.CreateApplication();
            }
            catch (COMException ex) when (IsTransient(ex.HResult))
            {
                last = ex;
                Thread.Sleep(TimeSpan.FromMilliseconds(500 * attempt));
            }
        }

        throw new OneNoteUnavailableException(
            "Could not start the OneNote COM server after several attempts. Make sure OneNote " +
            "desktop can be launched manually, and that it is not running elevated while this " +
            "server is not (or vice versa).",
            last!);
    }

    private static bool IsTransient(int hr) =>
        hr is RpcECallRejected or RpcEServerCallRetryLater or CoEServerExecFailure;

    private static bool IsDisconnected(int hr) =>
        hr is RpcSServerUnavailable or RpcEDisconnected;

    // Retries while OneNote reports itself busy, and re-activates once if OneNote was shut down
    // underneath us.
    public T Invoke<T>(Func<IApplication, T> call)
    {
        ArgumentNullException.ThrowIfNull(call);

        Exception? last = null;
        for (int attempt = 1; attempt <= 5; attempt++)
        {
            try
            {
                return call(Application);
            }
            catch (COMException ex) when (IsTransient(ex.HResult))
            {
                last = ex;
                Thread.Sleep(TimeSpan.FromMilliseconds(400 * attempt));
            }
            catch (COMException ex) when (IsDisconnected(ex.HResult) && attempt == 1)
            {
                last = ex;
                ReleaseApplication();
            }
        }

        throw new OneNoteException(
            $"OneNote stayed busy or unreachable across {5} attempts. Last error: {last?.Message}",
            last!);
    }

    public void Invoke(Action<IApplication> call)
    {
        ArgumentNullException.ThrowIfNull(call);
        Invoke<bool>(app =>
        {
            call(app);
            return true;
        });
    }

    public void Release(object comObject) => _comObjects.Release(comObject);

    public void OperationCompleted()
    {
        StopIdleTimer();

        if (_app is null)
        {
            return;
        }

        if (_idleRelease == TimeSpan.Zero)
        {
            ReleaseApplication();
            return;
        }

        long operation = ++_completedOperations;
        _idleTimer = _time.CreateTimer(_ => QueueIdleRelease(operation), null, _idleRelease, Timeout.InfiniteTimeSpan);
    }

    private void QueueIdleRelease(long operation)
    {
        try
        {
            _sta.RunAsync(() => ReleaseIfIdle(operation));
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private void ReleaseIfIdle(long operation)
    {
        if (operation == _completedOperations)
        {
            StopIdleTimer();
            ReleaseApplication();
        }
    }

    private void StopIdleTimer()
    {
        _idleTimer?.Dispose();
        _idleTimer = null;
    }

    private void ReleaseApplication()
    {
        if (_app is not null)
        {
            _comObjects.Release(_app);
        }

        _app = null;
    }

    public void Dispose()
    {
        StopIdleTimer();
        ReleaseApplication();
    }
}
