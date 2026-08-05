namespace OneNoteMcp.Core.Exceptions;

public sealed class OneNoteUnavailableException : OneNoteException
{
    public OneNoteUnavailableException(string message) : base(message)
    {
    }

    public OneNoteUnavailableException(string message, Exception inner) : base(message, inner)
    {
    }
}
