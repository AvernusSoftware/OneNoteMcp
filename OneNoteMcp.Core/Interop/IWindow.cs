using System.Runtime.InteropServices;

namespace OneNoteMcp.Core.Interop;

[ComImport]
[Guid("8E8304B8-CBD1-44F8-B0E8-89C625B2002E")]
[InterfaceType(ComInterfaceType.InterfaceIsDual)]
public interface IWindow
{
    void ReservedWindowHandle();  // 1 - get_WindowHandle

    [return: MarshalAs(UnmanagedType.BStr)] string GetCurrentPageId();          // 2

    [return: MarshalAs(UnmanagedType.BStr)] string GetCurrentSectionId();       // 3

    [return: MarshalAs(UnmanagedType.BStr)] string GetCurrentSectionGroupId();  // 4

    [return: MarshalAs(UnmanagedType.BStr)] string GetCurrentNotebookId();      // 5
}
