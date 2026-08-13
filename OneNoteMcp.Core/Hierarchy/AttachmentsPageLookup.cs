using OneNoteMcp.Core.Interop;
using System.Xml.Linq;

namespace OneNoteMcp.Core.Hierarchy;

public static class AttachmentsPageLookup
{
    private static readonly XNamespace One = OneNoteNamespaces.One;

    private static bool Matches(XElement page, string title) => string.Equals(((string?)page.Attribute("name") ?? string.Empty).Trim(), title.Trim(), StringComparison.OrdinalIgnoreCase);

    public static XElement? FindPage(XElement section, string title, string? parentPageId)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        List<XElement> pages = [.. section.Elements(One + "Page")];

        if (string.IsNullOrWhiteSpace(parentPageId))
        {
            return pages.FirstOrDefault(p => ParentPage.ReadLevel(p) == 0 && Matches(p, title));
        }

        int parentIndex = ParentPage.RequireNestable(pages, parentPageId);
        int parentLevel = ParentPage.ReadLevel(pages[parentIndex]);
        int childLevel = parentLevel + 1;

        for (int i = parentIndex + 1; i < pages.Count && ParentPage.ReadLevel(pages[i]) > parentLevel; i++)
        {
            if (ParentPage.ReadLevel(pages[i]) == childLevel && Matches(pages[i], title))
            {
                return pages[i];
            }
        }

        return null;
    }
}
