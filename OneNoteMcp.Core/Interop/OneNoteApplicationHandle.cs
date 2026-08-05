using OneNoteMcp.Core.Exceptions;
using System.Runtime.InteropServices;

namespace OneNoteMcp.Core.Interop;

/// <summary>
/// Owns the OneNote COM object. All members must be touched only from the STA thread.
/// </summary>
public sealed class OneNoteApplicationHandle : IDisposable
{
    private const string ProgId = "OneNote.Application";

    // Classic Office automation HRESULTs that mean "busy, try again".
    private const int RpcECallRejected = unchecked((int)0x80010001);

    private const int RpcEServerCallRetryLater = unchecked((int)0x8001010A);
    private const int CoEServerExecFailure = unchecked((int)0x80080005);

    // OneNote went away (user quit it) - the RCW is dead and must be replaced.
    private const int RpcSServerUnavailable = unchecked((int)0x800706BA);

    private const int RpcEDisconnected = unchecked((int)0x80010108);

    private object? _raw;
    private IApplication? _app;

    /// <summary>Gets the application, activating OneNote on first use.</summary>
    public IApplication Application => _app ??= Activate();

    private IApplication Activate()
    {
        Type type = Type.GetTypeFromProgID(ProgId, throwOnError: false)
            ?? throw new OneNoteUnavailableException(
                "OneNote desktop is not installed (the 'OneNote.Application' COM class is not " +
                "registered). This server requires the desktop OneNote that ships with Microsoft " +
                "365 or Office; the OneNote app from the Microsoft Store has no COM API.");

        Exception? last = null;
        for (int attempt = 1; attempt <= 5; attempt++)
        {
            try
            {
                _raw = Activator.CreateInstance(type)
                    ?? throw new OneNoteUnavailableException("Activating OneNote returned null.");
                return (IApplication)_raw;
            }
            catch (COMException ex) when (IsTransient(ex.HResult))
            {
                last = ex;
                Thread.Sleep(TimeSpan.FromMilliseconds(500 * attempt));
            }
            catch (InvalidCastException ex)
            {
                throw new OneNoteUnavailableException(
                    "The OneNote COM object does not expose the expected IApplication interface. " +
                    "This usually means an unsupported OneNote version is installed.", ex);
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
                Release();
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

    private void Release()
    {
        if (_raw is not null)
        {
            try
            {
                Marshal.FinalReleaseComObject(_raw);
            }
            catch
            {
                // Nothing useful to do if the RCW is already gone.
            }
        }

        _raw = null;
        _app = null;
    }

    public void Dispose() => Release();
}
