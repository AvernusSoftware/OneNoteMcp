using OneNoteMcp.Core.Exceptions;
using System.Runtime.InteropServices;

namespace OneNoteMcp.Core.Interop;

public sealed class ComObjects : IComObjects
{
    private const string ProgId = "OneNote.Application";

    public IApplication CreateApplication()
    {
        Type type = Type.GetTypeFromProgID(ProgId, throwOnError: false)
            ?? throw new OneNoteUnavailableException(
                "OneNote desktop is not installed (the 'OneNote.Application' COM class is not " +
                "registered). This server requires the desktop OneNote that ships with Microsoft " +
                "365 or Office; the OneNote app from the Microsoft Store has no COM API.");

        object raw = Activator.CreateInstance(type) ?? throw new OneNoteUnavailableException("Activating OneNote returned null.");

        try
        {
            return (IApplication)raw;
        }
        catch (InvalidCastException ex)
        {
            Marshal.FinalReleaseComObject(raw);
            throw new OneNoteUnavailableException(
                "The OneNote COM object does not expose the expected IApplication interface. " +
                "This usually means an unsupported OneNote version is installed.", ex);
        }
    }

    public void Release(object comObject)
    {
        try
        {
            Marshal.FinalReleaseComObject(comObject);
        }
        catch
        {
            // Nothing useful to do if the RCW is already gone.
        }
    }
}
