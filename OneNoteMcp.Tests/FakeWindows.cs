using OneNoteMcp.Core.Interop;

namespace OneNoteMcp.Tests;

internal sealed class FakeWindows : IWindows
{
    public FakeWindow CurrentWindow { get; } = new();

    public bool HasActiveWindow { get; set; } = true;

    public void ReservedItem()
    {
    }

    public void ReservedCount()
    {
    }

    public void ReservedNewEnum()
    {
    }

    public IWindow GetCurrentWindow() => HasActiveWindow ? CurrentWindow : null!;
}
