using System.Runtime.InteropServices;

namespace OneNoteMcp.Core.Interop;

// ---------------------------------------------------------------------------------------------
// Why these interfaces are hand-declared as DIRECT VTABLE bindings
// ---------------------------------------------------------------------------------------------
// Three obvious approaches to talking to OneNote all fail on a stock Microsoft 365 install:
//
//   1. C# `dynamic`      - the runtime binder calls IDispatch::GetTypeInfo before dispatching
//                          anything (ComRuntimeHelpers.GetITypeInfoFromIDispatch). OneNote
//                          returns E_FAIL from GetTypeInfo, so *every* member access throws
//                          0x80004005 before the call even reaches OneNote.
//
//   2. Type.InvokeMember - the CLR's late-bound path resolves through the registered type
//                          library and fails with TYPE_E_LIBNOTREGISTERED (0x8002801D).
//
//   3. [ComImport] with  - marshals through IDispatch::Invoke, which OneNote implements on top
//      InterfaceIsIDispatch of its own type library, so it also fails TYPE_E_LIBNOTREGISTERED.
//
// The root cause of 2 and 3: Office registers the OneNote type library ONLY under
//   HKLM\SOFTWARE\Classes\TypeLib\{0EA692EE-BB50-4E3C-AEF0-356D91732725}\1.1\0\Win32
// There is no Win64 entry (in either registry view), so 64-bit OneNote cannot load its own
// type library. Building the client as x86 does not help - the failure is inside OneNote.
//
// Declaring the interfaces as InterfaceIsDual gives direct vtable calls: no GetTypeInfo, no
// IDispatch::Invoke, no type library. This is the only approach that works, and it is also the
// fastest. It requires no PIA and no <COMReference>, so the solution builds on machines that do
// not have OneNote installed.
//
// CONSEQUENCE: every member of each interface MUST be declared, in exact vtable order. Members
// we never call are declared as bare no-argument slots purely to hold their position - their
// signatures are irrelevant because they are never marshaled. Adding, removing or reordering a
// member silently corrupts every call after it. This applies to every interface in this
// directory (see also IWindows, IWindow), not just this one.
//
// Layout below was extracted from the type library embedded in ONENOTE.EXE (resource 3,
// "Microsoft OneNote 15.0 Object Library" {0EA692EE-BB50-4E3C-AEF0-356D91732725} v1.1).
// ---------------------------------------------------------------------------------------------

/// <summary>OneNote <c>IApplication</c> (dual). 29 members after the 7 IUnknown/IDispatch slots.</summary>
[ComImport]
[Guid("452AC71A-B655-4967-A208-A4CC39DD7949")]
[InterfaceType(ComInterfaceType.InterfaceIsDual)]
public interface IApplication
{
    // 1
    void GetHierarchy([MarshalAs(UnmanagedType.BStr)] string? bstrStartNodeId, HierarchyScope hsScope, [MarshalAs(UnmanagedType.BStr)] out string pbstrHierarchyXmlOut, XmlSchema xsSchema);

    // 2
    void UpdateHierarchy([MarshalAs(UnmanagedType.BStr)] string bstrChangesXmlIn, XmlSchema xsSchema);

    void OpenHierarchy();        // 3

    void DeleteHierarchy();      // 4

    // 5
    void CreateNewPage([MarshalAs(UnmanagedType.BStr)] string bstrSectionId, [MarshalAs(UnmanagedType.BStr)] out string pbstrPageId, NewPageStyle npsNewPageStyle);

    void CloseNotebook();        // 6

    void GetHierarchyParent();   // 7

    // 8
    void GetPageContent([MarshalAs(UnmanagedType.BStr)] string bstrPageId, [MarshalAs(UnmanagedType.BStr)] out string pbstrPageXmlOut, PageInfo pageInfoToExport, XmlSchema xsSchema);

    // 9
    void UpdatePageContent([MarshalAs(UnmanagedType.BStr)] string bstrPageChangesXmlIn, DateTime dateExpectedLastModified, XmlSchema xsSchema, [MarshalAs(UnmanagedType.VariantBool)] bool force);

    void GetBinaryPageContent();   // 10

    // 11
    void DeletePageContent([MarshalAs(UnmanagedType.BStr)] string bstrPageId, [MarshalAs(UnmanagedType.BStr)] string bstrObjectId, DateTime dateExpectedLastModified, [MarshalAs(UnmanagedType.VariantBool)] bool force);

    void NavigateTo();             // 12

    void NavigateToUrl();          // 13

    void Publish();                // 14

    void OpenPackage();            // 15

    void GetHyperlinkToObject();   // 16

    // 17
    void FindPages([MarshalAs(UnmanagedType.BStr)] string? bstrStartNodeId, [MarshalAs(UnmanagedType.BStr)] string bstrSearchString, [MarshalAs(UnmanagedType.BStr)] out string pbstrHierarchyXmlOut, [MarshalAs(UnmanagedType.VariantBool)] bool fIncludeUnindexedPages, [MarshalAs(UnmanagedType.VariantBool)] bool fDisplay, XmlSchema xsSchema);

    void FindMeta();               // 18

    // 19
    void GetSpecialLocation(SpecialLocation slToGet, [MarshalAs(UnmanagedType.BStr)] out string pbstrSpecialLocationPath);

    void MergeFiles();             // 20

    void QuickFiling();            // 21

    void SyncHierarchy();          // 22

    void SetFilingLocation();      // 23

    // 24 - get_Windows
    [return: MarshalAs(UnmanagedType.Interface)]
    IWindows GetWindows();

    void ReservedDummy1();         // 25 - get_Dummy1

    void MergeSections();          // 26

    void ReservedComAddIns();      // 27 - get_COMAddIns

    void ReservedLanguageSettings(); // 28 - get_LanguageSettings

    void GetWebHyperlinkToObject();  // 29
}
