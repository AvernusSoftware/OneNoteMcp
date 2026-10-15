namespace OneNoteMcp.Core.Interop;

public interface IComObjects
{
    IApplication CreateApplication();

    void Release(object comObject);
}
