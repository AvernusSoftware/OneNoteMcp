using OneNoteMcp.Core.Exceptions;
using System.Globalization;
using System.Xml.Linq;

namespace OneNoteMcp.Core.Hierarchy;

public static class ParentPage
{
    public const int MaxLevel = 2;

    public static int ReadLevel(XElement page) => int.TryParse((string?)page.Attribute("pageLevel"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int level) ? level : 0;

    public static int RequireNestable(List<XElement> pages, string parentPageId)
    {
        ArgumentNullException.ThrowIfNull(pages);
        ArgumentException.ThrowIfNullOrWhiteSpace(parentPageId);

        int parentIndex = pages.FindIndex(p => (string?)p.Attribute("ID") == parentPageId);
        if (parentIndex < 0)
        {
            throw new OneNoteException($"parentPageId '{parentPageId}' is not a page in this section. Subpage grouping is section-local, so the parent must already be in the same section - pick one from get_hierarchy, or omit parentPageId for a top-level page.");
        }

        if (ReadLevel(pages[parentIndex]) >= MaxLevel)
        {
            throw new OneNoteException($"Page '{parentPageId}' is already a sub-subpage - the deepest level OneNote supports - so a new page cannot be nested under it. Choose a top-level page or a subpage as the parent instead.");
        }

        return parentIndex;
    }
}
