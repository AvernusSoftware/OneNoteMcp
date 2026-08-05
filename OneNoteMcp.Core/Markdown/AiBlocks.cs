using OneNoteMcp.Core.Interop;
using System.Xml.Linq;

namespace OneNoteMcp.Core.Markdown;

public static class AiBlocks
{
    private static readonly XNamespace One = OneNoteNamespaces.One;

    public const string MarkerPrefix = "<!-- ai-block: ";

    public static bool IsOwnedBy(XElement outline, string agentDisplayName)
    {
        ArgumentNullException.ThrowIfNull(outline);

        if (string.IsNullOrWhiteSpace(agentDisplayName))
        {
            return false;
        }

        if (!string.Equals((string?)outline.Attribute("author"), agentDisplayName, StringComparison.Ordinal))
        {
            return false;
        }

        return !outline.Descendants(One + "OE").Any(oe =>
            (string?)oe.Attribute("author") is { } author
            && !string.Equals(author, agentDisplayName, StringComparison.Ordinal));
    }

    public static XElement? FindBlock(XElement page, string blockId) =>
        page.Descendants(One + "Outline")
            .FirstOrDefault(o => string.Equals((string?)o.Attribute("objectID"), blockId, StringComparison.Ordinal));

    public static void StampAuthor(XElement root, string displayName, string initials)
    {
        foreach (XElement oe in root.DescendantsAndSelf(One + "OE"))
        {
            oe.SetAttributeValue("author", displayName);
            oe.SetAttributeValue("authorInitials", initials);
            oe.SetAttributeValue("lastModifiedBy", displayName);
            oe.SetAttributeValue("lastModifiedByInitials", initials);
        }
    }
}
