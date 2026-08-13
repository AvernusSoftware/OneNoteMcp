using OneNoteMcp.Core.Interop;
using System.Globalization;
using System.Xml.Linq;

namespace OneNoteMcp.Core.Hierarchy;

public static class SubpagePlacement
{
    private static readonly XNamespace One = OneNoteNamespaces.One;

    public static (XElement Section, int ChildLevel) PlaceUnderParent(XElement section, string parentPageId, string newPageId, string newPageName)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentException.ThrowIfNullOrWhiteSpace(parentPageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(newPageId);

        List<XElement> pages = section.Elements(One + "Page").Where(p => (string?)p.Attribute("ID") != newPageId).Select(p => new XElement(p)).ToList();

        int parentIndex = ParentPage.RequireNestable(pages, parentPageId);
        int parentLevel = ParentPage.ReadLevel(pages[parentIndex]);

        int childLevel = parentLevel + 1;
        int insertAt = parentIndex + 1;

        while (insertAt < pages.Count && ParentPage.ReadLevel(pages[insertAt]) > parentLevel)
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
