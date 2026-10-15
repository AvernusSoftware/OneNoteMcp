using OneNoteMcp.Core.Interop;

namespace OneNoteMcp.Tests;

internal sealed class FakeWindow : IWindow
{
    public string CurrentPageId { get; set; } = string.Empty;

    public Exception? CurrentPageIdFailure { get; set; }

    public void ReservedWindowHandle()
    {
    }

    public string GetCurrentPageId() => CurrentPageIdFailure is null ? CurrentPageId : throw CurrentPageIdFailure;

    public string GetCurrentSectionId() => string.Empty;

    public string GetCurrentSectionGroupId() => string.Empty;

    public string GetCurrentNotebookId() => string.Empty;
}
