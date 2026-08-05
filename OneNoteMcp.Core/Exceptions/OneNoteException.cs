namespace OneNoteMcp.Core.Exceptions;

public class OneNoteException : Exception
{
    public OneNoteException(string message) : base(message)
    {
    }

    public OneNoteException(string message, Exception inner) : base(message, inner)
    {
    }
}
