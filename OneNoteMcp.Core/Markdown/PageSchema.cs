using OneNoteMcp.Core.Interop;
using System.Globalization;
using System.Xml.Linq;

namespace OneNoteMcp.Core.Markdown;

public sealed class PageSchema
{
    public int StyleIndex(string name) => _styleIndexByName.TryGetValue(name, out int index) ? index : _styleIndexByName["p"];

    private static readonly XNamespace One = OneNoteNamespaces.One;

    private readonly Dictionary<string, int> _styleIndexByName;

    private readonly Dictionary<string, XElement> _quickStyleDefsByIndex;

    private readonly XElement _toDoTagDef;
    private readonly bool _emitAllStyles;

    private PageSchema(Dictionary<string, int> styleIndexByName, Dictionary<string, XElement> quickStyleDefsByIndex, int toDoTagIndex, XElement toDoTagDef, bool emitAllStyles)
    {
        _styleIndexByName = styleIndexByName;
        _quickStyleDefsByIndex = quickStyleDefsByIndex;
        _toDoTagDef = toDoTagDef;
        _emitAllStyles = emitAllStyles;
        ToDoTagIndex = toDoTagIndex;
    }

    public int ToDoTagIndex { get; }

    public static PageSchema ForNewPage()
    {
        Dictionary<string, int> map = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, XElement> defs = new(StringComparer.Ordinal);

        for (int i = 0; i < MarkdownToOneNoteXml.Styles.Length; i++)
        {
            MarkdownToOneNoteXml.QuickStyle style = MarkdownToOneNoteXml.Styles[i];
            map[style.Name] = i;
            defs[Key(i)] = QuickStyleDef(i, style);
        }

        return new PageSchema(map, defs, toDoTagIndex: 0, toDoTagDef: TagDef(0), emitAllStyles: true);
    }

    public static PageSchema ForExistingPage(XElement page)
    {
        ArgumentNullException.ThrowIfNull(page);

        HashSet<int> usedStyleIndices = [];
        Dictionary<string, XElement> existingByName = new(StringComparer.OrdinalIgnoreCase);

        foreach (XElement def in page.Elements(One + "QuickStyleDef"))
        {
            if (!int.TryParse((string?)def.Attribute("index"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int index))
            {
                continue;
            }

            usedStyleIndices.Add(index);

            if ((string?)def.Attribute("name") is { } name && !existingByName.ContainsKey(name))
            {
                existingByName[name] = def;
            }
        }

        Dictionary<string, int> map = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, XElement> defs = new(StringComparer.Ordinal);

        foreach (MarkdownToOneNoteXml.QuickStyle style in MarkdownToOneNoteXml.Styles)
        {
            if (existingByName.TryGetValue(style.Name, out XElement? existing)
                && int.TryParse((string?)existing.Attribute("index"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int reusedIndex))
            {
                map[style.Name] = reusedIndex;

                defs[Key(reusedIndex)] = new XElement(existing);
                continue;
            }

            int free = NextFree(usedStyleIndices);
            usedStyleIndices.Add(free);
            map[style.Name] = free;
            defs[Key(free)] = QuickStyleDef(free, style);
        }

        HashSet<int> usedTagIndices = [];
        XElement? existingToDo = null;

        foreach (XElement def in page.Elements(One + "TagDef"))
        {
            if (!int.TryParse((string?)def.Attribute("index"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int index))
            {
                continue;
            }

            usedTagIndices.Add(index);

            if (existingToDo is null && (string?)def.Attribute("type") == "3")
            {
                existingToDo = def;
            }
        }

        if (existingToDo is not null
            && int.TryParse((string?)existingToDo.Attribute("index"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int reusedTag))
        {
            return new PageSchema(map, defs, reusedTag, new XElement(existingToDo), emitAllStyles: false);
        }

        int freeTag = NextFree(usedTagIndices);
        return new PageSchema(map, defs, freeTag, TagDef(freeTag), emitAllStyles: false);
    }

    public IEnumerable<XElement> DefinitionsFor(XElement body)
    {
        ArgumentNullException.ThrowIfNull(body);

        if (body.DescendantsAndSelf(One + "Tag").Any())
        {
            yield return _toDoTagDef;
        }

        IEnumerable<XElement> defs = _emitAllStyles ? _quickStyleDefsByIndex.Values : Referenced(body).Select(index => _quickStyleDefsByIndex.GetValueOrDefault(index)).OfType<XElement>();

        foreach (XElement? def in defs.OrderBy(Index))
        {
            yield return def;
        }
    }

    private static IEnumerable<string> Referenced(XElement body) =>
        body.DescendantsAndSelf(One + "OE")
            .Select(oe => (string?)oe.Attribute("quickStyleIndex"))
            .OfType<string>()
            .Distinct(StringComparer.Ordinal);

    private static int Index(XElement def) => int.TryParse((string?)def.Attribute("index"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int index) ? index : int.MaxValue;

    private static string Key(int index) => index.ToString(CultureInfo.InvariantCulture);

    private static int NextFree(HashSet<int> used)
    {
        int candidate = 0;
        while (used.Contains(candidate))
        {
            candidate++;
        }

        return candidate;
    }

    private static XElement QuickStyleDef(int index, MarkdownToOneNoteXml.QuickStyle style) => new(
        One + "QuickStyleDef",
        new XAttribute("index", index.ToString(CultureInfo.InvariantCulture)),
        new XAttribute("name", style.Name),
        new XAttribute("fontColor", style.Colour),
        new XAttribute("highlightColor", "automatic"),
        new XAttribute("font", style.Font),
        new XAttribute("fontSize", style.Size),
        // Paragraph spacing is set on the paragraph, not here: OneNote multiplies a quick style's
        // spaceBefore by 36 on the way in, turning one line of leading into most of a page.
        new XAttribute("spaceBefore", "0.0"),
        new XAttribute("spaceAfter", "0.0"),
        new XAttribute("bold", style.Bold ? "true" : "false"));

    private static XElement TagDef(int index) => new(
        One + "TagDef",
        new XAttribute("index", index.ToString(CultureInfo.InvariantCulture)),
        new XAttribute("type", "3"),
        new XAttribute("symbol", "3"),
        new XAttribute("name", "To Do"));
}
