using OneNoteMcp.Core.Exceptions;
using OneNoteMcp.Core.Interop;
using System.Globalization;
using System.Xml.Linq;

namespace OneNoteMcp.Core.Hierarchy;

public static class SubpagePlacement
{
    private static readonly XNamespace One = OneNoteNamespaces.One;

    private static int ReadLevel(XElement page) => int.TryParse((string?)page.Attribute("pageLevel"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int level) ? level : 0;

    private const int MaxPageLevel = 2;

    public static (XElement Section, int ChildLevel) PlaceUnderParent(XElement section, string parentPageId, string newPageId, string newPageName)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentException.ThrowIfNullOrWhiteSpace(parentPageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(newPageId);

        List<XElement> pages = section.Elements(One + "Page").Where(p => (string?)p.Attribute("ID") != newPageId).Select(p => new XElement(p)).ToList();

        int parentIndex = pages.FindIndex(p => (string?)p.Attribute("ID") == parentPageId);
        if (parentIndex < 0)
        {
            throw new OneNoteException($"parentPageId '{parentPageId}' is not a page in this section. Subpage grouping is section-local, so the parent must already be in the same section as the new page - pick one from get_hierarchy, or omit parentPageId for a top-level page.");
        }

        int parentLevel = ReadLevel(pages[parentIndex]);
        if (parentLevel >= MaxPageLevel)
        {
            throw new OneNoteException($"Page '{parentPageId}' is already a sub-subpage - the deepest level OneNote supports - so a new page cannot be nested under it. Choose a top-level page or a subpage as the parent instead.");
        }

        int childLevel = parentLevel + 1;
        int insertAt = parentIndex + 1;

        while (insertAt < pages.Count && ReadLevel(pages[insertAt]) > parentLevel)
        {
            insertAt++;
        }

        XElement newPage = new(One + "Page", new XAttribute("ID", newPageId), new XAttribute("name", newPageName), new XAttribute("pageLevel", childLevel.ToString(CultureInfo.InvariantCulture)));
        pages.Insert(insertAt, newPage);

        XElement result = new(One + "Section", new XAttribute("ID", (string?)section.Attribute("ID") ?? string.Empty));
        if ((string?)section.Attribute("name") is { } sectionName)
        {
            result.SetAttributeValue("name", sectionName);
        }

        foreach (XElement page in pages)
        {
            result.Add(page);
        }

        return (result, childLevel);
    }
}
