using OneNoteMcp.Core.Configuration;
using OneNoteMcp.Core.Interop;
using System.Xml.Linq;

namespace OneNoteMcp.Core.Markdown;

public static class AttachmentXml
{
    private static readonly XNamespace One = OneNoteNamespaces.One;

    private static XElement InsertedFile(string pathSource, string preferredName) => new(
        One + "InsertedFile",
        new XAttribute("pathSource", pathSource),
        new XAttribute("preferredName", preferredName));

    public static string BuildOutlineXml(string pageId, string filePath, string preferredName, string? caption, AgentOptions agent, PageSchema schema, string? objectId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(preferredName);
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(schema);

        XElement body = new(One + "OEChildren", MarkdownToOneNoteXml.Oe(InsertedFile(filePath, preferredName), quickStyleIndex: schema.StyleIndex("p")));

        if (!string.IsNullOrWhiteSpace(caption))
        {
            body.Add(MarkdownToOneNoteXml.Oe(MarkdownToOneNoteXml.WrapAsOneT(MarkdownToOneNoteXml.HtmlEncode(caption)), quickStyleIndex: schema.StyleIndex("p")));
        }

        AiBlocks.StampAuthor(body, agent.DisplayName, agent.Initials);

        XElement outline = new(One + "Outline");
        if (!string.IsNullOrWhiteSpace(objectId))
        {
            outline.SetAttributeValue("objectID", objectId);
        }

        outline.Add(body);

        XElement page = MarkdownToOneNoteXml.NewPageElement(pageId);

        foreach (XElement definition in schema.DefinitionsFor(body))
        {
            page.Add(definition);
        }

        page.Add(outline);
        return MarkdownToOneNoteXml.Serialise(page);
    }
}
