using OneNoteMcp.Core.Interop;

namespace OneNoteMcp.Tests;

internal sealed class FakeApplication : IApplication
{
    public const string EmptyHierarchy = $"<one:Notebooks xmlns:one=\"{PageXml.Ns}\" />";

    public Queue<Exception> GetHierarchyFailures { get; } = new();

    public FakeWindows Windows { get; } = new();

    public void GetHierarchy(string? bstrStartNodeId, HierarchyScope hsScope, out string pbstrHierarchyXmlOut, XmlSchema xsSchema)
    {
        if (GetHierarchyFailures.TryDequeue(out Exception? failure))
        {
            throw failure;
        }

        pbstrHierarchyXmlOut = EmptyHierarchy;
    }

    public void UpdateHierarchy(string bstrChangesXmlIn, XmlSchema xsSchema)
    {
    }

    public void OpenHierarchy()
    {
    }

    public void DeleteHierarchy()
    {
    }

    public void CreateNewPage(string bstrSectionId, out string pbstrPageId, NewPageStyle npsNewPageStyle) => pbstrPageId = "{new-page}";

    public void CloseNotebook()
    {
    }

    public void GetHierarchyParent()
    {
    }

    public void GetPageContent(string bstrPageId, out string pbstrPageXmlOut, PageInfo pageInfoToExport, XmlSchema xsSchema)
    {
        pbstrPageXmlOut = PageXml.Page(string.Empty);
    }

    public void UpdatePageContent(string bstrPageChangesXmlIn, DateTime dateExpectedLastModified, XmlSchema xsSchema, bool force)
    {
    }

    public void GetBinaryPageContent()
    {
    }

    public void DeletePageContent(string bstrPageId, string bstrObjectId, DateTime dateExpectedLastModified, bool force)
    {
    }

    public void NavigateTo()
    {
    }

    public void NavigateToUrl()
    {
    }

    public void Publish()
    {
    }

    public void OpenPackage()
    {
    }

    public void GetHyperlinkToObject()
    {
    }

    public void FindPages(string? bstrStartNodeId, string bstrSearchString, out string pbstrHierarchyXmlOut, bool fIncludeUnindexedPages, bool fDisplay, XmlSchema xsSchema)
    {
        pbstrHierarchyXmlOut = EmptyHierarchy;
    }

    public void FindMeta()
    {
    }

    public void GetSpecialLocation(SpecialLocation slToGet, out string pbstrSpecialLocationPath) => pbstrSpecialLocationPath = string.Empty;

    public void MergeFiles()
    {
    }

    public void QuickFiling()
    {
    }

    public void SyncHierarchy()
    {
    }

    public void SetFilingLocation()
    {
    }

    public IWindows GetWindows() => Windows;

    public void ReservedDummy1()
    {
    }

    public void MergeSections()
    {
    }

    public void ReservedComAddIns()
    {
    }

    public void ReservedLanguageSettings()
    {
    }

    public void GetWebHyperlinkToObject()
    {
    }
}
