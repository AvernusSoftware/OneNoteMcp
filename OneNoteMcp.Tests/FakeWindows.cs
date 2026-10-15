using OneNoteMcp.Core.Interop;

namespace OneNoteMcp.Tests;

internal sealed class FakeWindows : IWindows
{
    public FakeWindow CurrentWindow { get; } = new();

    public void ReservedItem()
    {
    }

    public void ReservedCount()
    {
    }

    public void ReservedNewEnum()
    {
    }

    public IWindow GetCurrentWindow() => CurrentWindow;
}
