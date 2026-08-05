using System.Runtime.InteropServices;

namespace OneNoteMcp.Core.Interop;

[ComImport]
[Guid("6D4B9C3E-CC05-493F-85E2-43D1006DF96A")]
[InterfaceType(ComInterfaceType.InterfaceIsDual)]
public interface IWindows
{
    void ReservedItem();      // 1 - get_Item

    void ReservedCount();     // 2 - get_Count

    void ReservedNewEnum();   // 3 - get__NewEnum

    // 4 - get_CurrentWindow
    [return: MarshalAs(UnmanagedType.Interface)]
    IWindow GetCurrentWindow();
}
