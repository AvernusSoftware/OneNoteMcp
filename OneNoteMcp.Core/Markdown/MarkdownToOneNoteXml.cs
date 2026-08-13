using Markdig;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using OneNoteMcp.Core.Configuration;
using OneNoteMcp.Core.Interop;
using System.Globalization;
using System.Text;
using System.Xml.Linq;
using MarkdigTable = Markdig.Extensions.Tables.Table;
using MarkdigTableCell = Markdig.Extensions.Tables.TableCell;
using MarkdigTableRow = Markdig.Extensions.Tables.TableRow;

namespace OneNoteMcp.Core.Markdown;

public static class MarkdownToOneNoteXml
{
    private static readonly XNamespace One = OneNoteNamespaces.One;

    private static bool? FindTaskState(ListItemBlock item) => item.Descendants<TaskList>().FirstOrDefault()?.Checked;

    internal static XElement WrapAsOneT(string html) => new(One + "T", new XCData(html));

    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables()
        .UseTaskLists()
        .UseEmphasisExtras()
        .UseAutoLinks()
        .Build();

    internal const string BodyFont = "Segoe UI";

    internal const string BodySize = "11.0";

    internal const string CodeFont = "Consolas";

    internal const string HeadingSpaceBefore = BodySize;

    public readonly record struct QuickStyle(string Name, string Font, string Size, string Colour, bool Bold);

    internal static readonly QuickStyle[] Styles =
    [
        new("p",          BodyFont, BodySize, "automatic", false),
        new("h1",         BodyFont, "16.0",   "#1E4E79",   false),
        new("h2",         BodyFont, "14.0",   "#2E74B5",   false),
        new("h3",         BodyFont, "12.0",   "#1F4D78",   false),
        new("h4",         BodyFont, BodySize, "#2E74B5",   false),
        new("h5",         BodyFont, BodySize, "#2E74B5",   false),
        new("h6",         BodyFont, BodySize, "#595959",   false),
        new("code",       CodeFont, "10.0",   "#333333",   false),
        new("blockquote", BodyFont, BodySize, "#595959",   false),
    ];

    public static string BuildPageXml(string pageId, string title, string markdown, AgentOptions agent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pageId);
        ArgumentNullException.ThrowIfNull(agent);

        PageSchema schema = PageSchema.ForNewPage();
        XElement body = BuildBody(markdown, schema);
        AiBlocks.StampAuthor(body, agent.DisplayName, agent.Initials);

        XElement page = NewPageElement(pageId);

        foreach (XElement definition in schema.DefinitionsFor(body))
        {
            page.Add(definition);
        }

        page.Add(new XElement(One + "Title", Oe(WrapAsOneT(HtmlEncode(title ?? string.Empty)))));

        page.Add(new XElement(
            One + "Outline",
            new XElement(One + "Position",
                new XAttribute("x", "36.0"), new XAttribute("y", "86.0"), new XAttribute("z", "0")),
            new XElement(One + "Size",
                new XAttribute("width", "624.0"), new XAttribute("height", "36.0")),
            body));

        return Serialise(page);
    }

    public static string BuildOutlineXml(string pageId, string markdown, AgentOptions agent, PageSchema schema, string? objectId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pageId);
        ArgumentNullException.ThrowIfNull(agent);
        ArgumentNullException.ThrowIfNull(schema);

        XElement body = BuildBody(markdown, schema);

        AiBlocks.StampAuthor(body, agent.DisplayName, agent.Initials);

        XElement outline = new(One + "Outline");
        if (!string.IsNullOrWhiteSpace(objectId))
        {
            outline.SetAttributeValue("objectID", objectId);
        }

        outline.Add(body);

        XElement page = NewPageElement(pageId);

        foreach (XElement definition in schema.DefinitionsFor(body))
        {
            page.Add(definition);
        }

        page.Add(outline);
        return Serialise(page);
    }

    internal static XElement NewPageElement(string pageId) => new(
        One + "Page",
        new XAttribute(XNamespace.Xmlns + "one", One.NamespaceName),
        new XAttribute("ID", pageId));

    internal static string Serialise(XElement page) =>
        new XDocument(new XDeclaration("1.0", null, null), page).ToString(SaveOptions.DisableFormatting);

    private static XElement BuildBody(string markdown, PageSchema schema)
    {
        MarkdownDocument document = Markdig.Markdown.Parse(markdown ?? string.Empty, Pipeline);
        XElement children = new(One + "OEChildren");
        WriteBlocks(document, children, schema, nested: false);

        if (!children.HasElements)
        {
            children.Add(Oe(WrapAsOneT(string.Empty)));
        }

        return children;
    }

    private static void WriteBlocks(ContainerBlock container, XElement target, PageSchema schema, bool nested)
    {
        foreach (Block block in container)
        {
            WriteBlock(block, target, schema, nested);
        }
    }

    private static void WriteBlock(Block block, XElement target, PageSchema schema, bool nested)
    {
        switch (block)
        {
            case HeadingBlock heading:
                target.Add(Oe(
                    WrapAsOneT(Inline(heading.Inline)),
                    quickStyleIndex: schema.StyleIndex("h" + Math.Clamp(heading.Level, 1, 6)),
                    spaceBefore: HeadingSpaceBefore));
                break;

            case ParagraphBlock paragraph:
                target.Add(Oe(WrapAsOneT(Inline(paragraph.Inline)), quickStyleIndex: schema.StyleIndex("p")));
                break;

            case ListBlock list:
                WriteList(list, target, schema, indent: !nested);

                if (!nested)
                {
                    AddGap(target, schema);
                }

                break;

            case QuoteBlock quote:
                foreach (Block inner in quote)
                {
                    XElement holder = new("holder");
                    WriteBlock(inner, holder, schema, nested);

                    foreach (XElement? oe in holder.Elements().ToList())
                    {
                        oe.SetAttributeValue(
                            "quickStyleIndex",
                            schema.StyleIndex("blockquote").ToString(CultureInfo.InvariantCulture));
                        target.Add(oe);
                    }
                }

                break;

            case FencedCodeBlock or CodeBlock:
                LeafBlock code = (LeafBlock)block;
                IEnumerable<string> lines = code.Lines.Lines.Take(code.Lines.Count).Select(l => l.ToString());

                if (!nested)
                {
                    AddGap(target, schema);
                }

                foreach (string line in lines)
                {
                    target.Add(Oe(
                        WrapAsOneT(HtmlEncode(line.Length == 0 ? " " : line)),
                        quickStyleIndex: schema.StyleIndex("code"),
                        style: $"font-family:{CodeFont};font-size:10.0pt"));
                }

                if (!nested)
                {
                    AddGap(target, schema);
                }

                break;

            case MarkdigTable table:
                XElement tableOe = new(One + "OE", BuildTable(table, schema));

                if (nested)
                {
                    target.Add(tableOe);
                    break;
                }

                XElement carrier = Oe(WrapAsOneT(string.Empty), quickStyleIndex: schema.StyleIndex("p"));
                XElement indented = new(One + "OEChildren", tableOe);
                carrier.Add(indented);
                target.Add(carrier);

                AddGap(target, schema);
                break;

            case ThematicBreakBlock:
                target.Add(Oe(WrapAsOneT("————————————————")));
                break;

            case ContainerBlock inner2:
                WriteBlocks(inner2, target, schema, nested);
                break;
        }
    }

    private static XElement BlankLine(PageSchema schema) =>
        Oe(WrapAsOneT(string.Empty), quickStyleIndex: schema.StyleIndex("p"));

    private static void AddGap(XElement target, PageSchema schema)
    {
        if (target.Elements().LastOrDefault() is { } last && !IsBlankLine(last))
        {
            target.Add(BlankLine(schema));
        }
    }

    private static bool IsBlankLine(XElement element) =>
        element.Name == One + "OE" &&
        !element.Elements(One + "OEChildren").Any() &&
        element.Element(One + "T")?.Value.Length == 0;

    private static void WriteList(ListBlock list, XElement target, PageSchema schema, bool indent)
    {
        bool ordered = list.IsOrdered;

        if (indent)
        {
            XElement carrier = Oe(WrapAsOneT(string.Empty), quickStyleIndex: schema.StyleIndex("p"));
            XElement items = new(One + "OEChildren");
            carrier.Add(items);
            target.Add(carrier);
            target = items;
        }

        foreach (ListItemBlock item in list.Cast<ListItemBlock>())
        {
            bool? taskState = FindTaskState(item);

            XElement oe = new(One + "OE", new XAttribute("alignment", "left"));

            if (taskState is null)
            {
                oe.Add(ordered
                    ? new XElement(One + "List", new XElement(
                        One + "Number",
                        new XAttribute("numberSequence", "0"),
                        new XAttribute("numberFormat", "##.")))
                    : new XElement(One + "List", new XElement(
                        One + "Bullet",
                        new XAttribute("bullet", "2"),
                        new XAttribute("fontSize", "11.0"))));
            }
            else
            {
                oe.Add(new XElement(
                    One + "Tag",
                    new XAttribute("index", schema.ToDoTagIndex.ToString(CultureInfo.InvariantCulture)),
                    new XAttribute("completed", taskState.Value ? "true" : "false"),
                    new XAttribute("disabled", "false")));
            }

            XElement sub = new(One + "OEChildren");
            bool first = true;

            foreach (Block child in item)
            {
                if (first && child is ParagraphBlock p)
                {
                    oe.Add(WrapAsOneT(Inline(p.Inline)));
                    first = false;
                }
                else
                {
                    WriteBlock(child, sub, schema, nested: true);
                }
            }

            if (first)
            {
                oe.Add(WrapAsOneT(string.Empty));
            }

            if (sub.HasElements)
            {
                oe.Add(sub);
            }

            target.Add(oe);
        }
    }

    private static XElement BuildTable(MarkdigTable table, PageSchema schema)
    {
        List<MarkdigTableRow> rows = [.. table.OfType<MarkdigTableRow>()];
        int width = rows.Count == 0 ? 0 : rows.Max(r => r.Count);
        bool hasHeader = rows.Count > 0 && rows[0].IsHeader;

        XElement element = new(
            One + "Table",
            new XAttribute("bordersVisible", "true"),
            new XAttribute("hasHeaderRow", hasHeader ? "true" : "false"));

        XElement columns = new(One + "Columns");
        for (int i = 0; i < width; i++)
        {
            columns.Add(new XElement(
                One + "Column",
                new XAttribute("index", i.ToString(CultureInfo.InvariantCulture)),
                new XAttribute("width", "120.0")));
        }

        element.Add(columns);

        foreach (MarkdigTableRow? row in rows)
        {
            XElement rowElement = new(One + "Row");

            for (int i = 0; i < width; i++)
            {
                XElement children = new(One + "OEChildren");

                if (i < row.Count && row[i] is MarkdigTableCell cell)
                {
                    WriteBlocks(cell, children, schema, nested: true);
                }

                if (!children.HasElements)
                {
                    children.Add(Oe(WrapAsOneT(string.Empty)));
                }

                rowElement.Add(new XElement(One + "Cell", children));
            }

            element.Add(rowElement);
        }

        return element;
    }

    internal static XElement Oe(XElement content, int? quickStyleIndex = null, string? style = null, string? spaceBefore = null)
    {
        XElement oe = new(One + "OE", new XAttribute("alignment", "left"));

        if (quickStyleIndex is { } index)
        {
            oe.SetAttributeValue("quickStyleIndex", index.ToString(CultureInfo.InvariantCulture));
        }

        if (spaceBefore is not null)
        {
            oe.SetAttributeValue("spaceBefore", spaceBefore);
        }

        if (style is not null)
        {
            oe.SetAttributeValue("style", style);
        }

        oe.Add(content);
        return oe;
    }

    private static string Inline(ContainerInline? container)
    {
        if (container is null)
        {
            return string.Empty;
        }

        StringBuilder sb = new();

        foreach (Inline inline in container)
        {
            AppendInline(inline, sb);
        }

        return sb.ToString();
    }

    private static void AppendInline(Inline inline, System.Text.StringBuilder sb)
    {
        switch (inline)
        {
            case LiteralInline literal:
                sb.Append(HtmlEncode(literal.Content.ToString()));
                break;

            case EmphasisInline emphasis:
                {
                    string style = emphasis.DelimiterChar switch
                    {
                        '~' when emphasis.DelimiterCount == 2 => "text-decoration:line-through",
                        '=' => "background-color:yellow",
                        _ => emphasis.DelimiterCount >= 2 ? "font-weight:bold" : "font-style:italic",
                    };

                    sb.Append("<span style='").Append(style).Append("'>");
                    foreach (Inline child in emphasis)
                    {
                        AppendInline(child, sb);
                    }

                    sb.Append("</span>");
                    break;
                }

            case CodeInline code:
                sb.Append("<span style='font-family:Consolas'>")
                  .Append(HtmlEncode(code.Content))
                  .Append("</span>");
                break;

            case LinkInline link:
                {
                    StringBuilder inner = new();
                    foreach (Inline child in link)
                    {
                        AppendInline(child, inner);
                    }

                    string label = inner.Length > 0 ? inner.ToString() : HtmlEncode(link.Url ?? string.Empty);

                    if (link.IsImage)
                    {
                        sb.Append("[image] <a href=\"").Append(HtmlEncode(link.Url ?? string.Empty))
                          .Append("\">").Append(label).Append("</a>");
                    }
                    else
                    {
                        sb.Append("<a href=\"").Append(HtmlEncode(link.Url ?? string.Empty))
                          .Append("\">").Append(label).Append("</a>");
                    }

                    break;
                }

            case AutolinkInline auto:
                sb.Append("<a href=\"").Append(HtmlEncode(auto.Url)).Append("\">")
                  .Append(HtmlEncode(auto.Url)).Append("</a>");
                break;

            case LineBreakInline lineBreak:
                sb.Append(lineBreak.IsHard ? "<br>" : " ");
                break;

            case TaskList:
                break;

            case HtmlInline html:
                sb.Append(HtmlEncode(html.Tag));
                break;

            case HtmlEntityInline entity:
                sb.Append(HtmlEncode(entity.Transcoded.ToString()));
                break;

            case ContainerInline nested:
                foreach (Inline child in nested)
                {
                    AppendInline(child, sb);
                }

                break;

            default:
                break;
        }
    }

    internal static string HtmlEncode(string value) =>
        value.Replace("&", "&amp;", StringComparison.Ordinal)
             .Replace("<", "&lt;", StringComparison.Ordinal)
             .Replace(">", "&gt;", StringComparison.Ordinal);
}
