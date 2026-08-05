using OneNoteMcp.Core.Exceptions;
using OneNoteMcp.Core.Interop;
using System.Text;
using System.Xml.Linq;

namespace OneNoteMcp.Core.Markdown;

public static class OneNoteXmlParser
{
    private static readonly XNamespace One = OneNoteNamespaces.One;

    public static string ToMarkdown(string pageXml, bool includeFrontMatter = true, string? agentDisplayName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pageXml);

        XElement page = XDocument.Parse(pageXml).Descendants(One + "Page").FirstOrDefault() ?? throw new OneNoteException("The XML does not contain a one:Page element.");

        QuickStyleTable styles = QuickStyleTable.FromPage(page);
        TagTable tags = TagTable.FromPage(page);
        StringBuilder sb = new();

        string title = ReadTitle(page);

        if (includeFrontMatter)
        {
            sb.Append("---\n");
            sb.Append("id: ").Append((string?)page.Attribute("ID")).Append('\n');
            sb.Append("title: ").Append(YamlScalar(title)).Append('\n');

            if ((string?)page.Attribute("lastModifiedTime") is { } modified)
            {
                sb.Append("last_modified: ").Append(modified).Append('\n');
            }

            if ((string?)page.Attribute("dateTime") is { } created)
            {
                sb.Append("created: ").Append(created).Append('\n');
            }

            sb.Append("---\n\n");
        }

        if (!string.IsNullOrWhiteSpace(title))
        {
            sb.Append("# ").Append(title).Append("\n\n");
        }

        foreach (XElement child in page.Elements())
        {
            switch (child.Name.LocalName)
            {
                case "Outline":
                    if (agentDisplayName is not null
                        && (string?)child.Attribute("objectID") is { } blockId
                        && AiBlocks.IsOwnedBy(child, agentDisplayName))
                    {
                        sb.Append(AiBlocks.MarkerPrefix).Append(blockId).Append(" -->\n\n");
                    }

                    WriteOeChildren(child.Element(One + "OEChildren"), sb, styles, tags, depth: 0);
                    break;

                case "Image":
                    sb.Append(RenderImage(child)).Append("\n\n");
                    break;

                case "InkDrawing":
                    sb.Append("<!-- ink drawing -->\n\n");
                    break;
            }
        }

        return CollapseBlankLinesAndTrimTrailingNewline(sb.ToString());
    }

    private static string ReadTitle(XElement page)
    {
        XElement? titleElement = page.Element(One + "Title");
        if (titleElement is null)
        {
            return (string?)page.Attribute("name") ?? string.Empty;
        }

        string? text = titleElement
            .Descendants(One + "T")
            .Select(t => InlineHtmlReader.ToMarkdown(t.Value))
            .FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));

        return (text ?? (string?)page.Attribute("name") ?? string.Empty).Trim();
    }

    private static void WriteOeChildren(XElement? children, StringBuilder sb, QuickStyleTable styles, TagTable tags, int depth)
    {
        if (children is null)
        {
            return;
        }

        int ordinal = 0;
        BlockKind previousKind = BlockKind.None;

        foreach (XElement oe in children.Elements(One + "OE"))
        {
            BlockKind kind = KindOf(oe, styles, tags);
            if (previousKind != BlockKind.None && kind != previousKind)
            {
                sb.Append('\n');
            }

            WriteOe(oe, sb, styles, tags, depth, ref ordinal);
            previousKind = kind;
        }
    }

    private enum BlockKind
    { None, Bullet, Number, ToDo, Code, Other }

    private static BlockKind KindOf(XElement oe, QuickStyleTable styles, TagTable tags)
    {
        if (IsIndentCarrier(oe))
        {
            XElement? inner = oe.Element(One + "OEChildren")?.Elements(One + "OE").FirstOrDefault();
            return inner is null ? BlockKind.Other : KindOf(inner, styles, tags);
        }

        if (tags.ToDoFor(oe) is not null)
        {
            return BlockKind.ToDo;
        }

        XElement? list = oe.Element(One + "List");
        if (list?.Element(One + "Bullet") is not null)
        {
            return BlockKind.Bullet;
        }

        if (list?.Element(One + "Number") is not null)
        {
            return BlockKind.Number;
        }

        string? styleName = styles.NameFor((string?)oe.Attribute("quickStyleIndex"));
        return styleName == "code" || IsMonospaceStyle((string?)oe.Attribute("style")) ? BlockKind.Code : BlockKind.Other;
    }

    private static void WriteOe(XElement oe, StringBuilder sb, QuickStyleTable styles, TagTable tags, int depth, ref int ordinal)
    {
        if (IsIndentCarrier(oe))
        {
            ordinal = 0;
            WriteOeChildren(oe.Element(One + "OEChildren"), sb, styles, tags, depth);
            return;
        }

        string indent = new(' ', depth * 2);
        XElement? list = oe.Element(One + "List");
        bool isBullet = list?.Element(One + "Bullet") is not null;
        XElement? number = list?.Element(One + "Number");
        bool isNumber = number is not null;

        if (isNumber)
        {
            ordinal = ordinal == 0 ? int.TryParse((string?)number!.Attribute("startAt"), out int start) ? start : 1 : ordinal + 1;
        }
        else
        {
            ordinal = 0;
        }

        string? styleName = styles.NameFor((string?)oe.Attribute("quickStyleIndex"));
        bool isHeading = styleName is not null && HeadingLevels.ContainsKey(styleName);
        bool? todo = tags.ToDoFor(oe);
        List<string> otherTags = tags.LabelsFor(oe);

        StringBuilder prefix = new(indent);

        if (todo is not null)
        {
            prefix.Append(todo.Value ? "- [x] " : "- [ ] ");
        }
        else if (isBullet)
        {
            prefix.Append("- ");
        }
        else if (isNumber)
        {
            prefix.Append(ordinal).Append(". ");
        }
        else if (isHeading)
        {
            prefix.Append(new string('#', HeadingLevels[styleName!])).Append(' ');
        }
        else if (styleName is "blockquote" or "cite")
        {
            prefix.Append("> ");
        }

        bool isCodeStyle = styleName == "code" || IsMonospaceStyle((string?)oe.Attribute("style"));

        List<string> runs = [.. oe.Elements(One + "T").Select(t => InlineHtmlReader.ToMarkdown(t.Value, isHeading))];
        string text = string.Concat(runs).Trim();

        if (!isHeading && !isCodeStyle && todo is null)
        {
            text = EscapeLeadingBlockMarker(text);
        }

        if (otherTags.Count > 0)
        {
            text = string.Join(" ", otherTags.Select(t => $"`#{t}`")) + (text.Length > 0 ? " " + text : string.Empty);
        }

        if (text.Length > 0)
        {
            if (isCodeStyle && prefix.Length == indent.Length)
            {
                sb.Append(indent).Append("    ").Append(text).Append('\n');
            }
            else
            {
                sb.Append(prefix).Append(text).Append('\n');

                if (!isBullet && !isNumber && todo is null)
                {
                    sb.Append('\n');
                }
            }
        }
        else if (oe.Element(One + "T") is not null && prefix.Length == indent.Length)
        {
            sb.Append('\n');
        }

        foreach (XElement table in oe.Elements(One + "Table"))
        {
            WriteTable(table, sb, styles, tags, indent);
        }

        foreach (XElement image in oe.Elements(One + "Image"))
        {
            sb.Append(indent).Append(RenderImage(image)).Append("\n\n");
        }

        if (oe.Element(One + "InkDrawing") is not null)
        {
            sb.Append(indent).Append("<!-- ink drawing -->\n\n");
        }

        WriteOeChildren(oe.Element(One + "OEChildren"), sb, styles, tags, depth + 1);
    }

    private static string EscapeLeadingBlockMarker(string text)
    {
        if (text.Length == 0)
        {
            return text;
        }

        if (IsThematicBreakLike(text))
        {
            return "\\" + text;
        }

        if (text[0] == '>')
        {
            return "\\" + text;
        }

        if (text[0] is '-' or '*' or '+' && (text.Length == 1 || char.IsWhiteSpace(text[1])))
        {
            return "\\" + text;
        }

        if (text[0] == '#')
        {
            int hashes = 0;
            while (hashes < text.Length && hashes < 6 && text[hashes] == '#')
            {
                hashes++;
            }

            if (hashes == text.Length || char.IsWhiteSpace(text[hashes]))
            {
                return "\\" + text;
            }
        }

        if (char.IsAsciiDigit(text[0]))
        {
            int digits = 0;
            while (digits < text.Length && digits < 9 && char.IsAsciiDigit(text[digits]))
            {
                digits++;
            }

            if (digits < text.Length && text[digits] is '.' or ')'
                && (digits + 1 == text.Length || char.IsWhiteSpace(text[digits + 1])))
            {
                return text[..digits] + "\\" + text[digits..];
            }
        }

        return text;
    }

    private static bool IsThematicBreakLike(string text)
    {
        char[] marks = [.. text.Where(c => !char.IsWhiteSpace(c))];
        return marks.Length >= 3 && marks[0] is '-' or '*' or '_' && marks.All(c => c == marks[0]);
    }

    private static bool IsIndentCarrier(XElement oe)
    {
        if (oe.Element(One + "OEChildren") is null
            || oe.Element(One + "List") is not null
            || oe.Elements(One + "Tag").Any()
            || oe.Elements(One + "Table").Any()
            || oe.Elements(One + "Image").Any()
            || oe.Element(One + "InkDrawing") is not null)
        {
            return false;
        }

        return oe.Elements(One + "T").All(t => InlineHtmlReader.ToMarkdown(t.Value).Trim().Length == 0);
    }

    private static void WriteTable(XElement table, StringBuilder sb, QuickStyleTable styles, TagTable tags, string indent)
    {
        List<XElement> rows = [.. table.Elements(One + "Row")];
        if (rows.Count == 0)
        {
            return;
        }

        List<List<string>> grid = [.. rows.Select(r => r.Elements(One + "Cell").Select(c => CellText(c, styles, tags)).ToList())];

        int width = grid.Max(r => r.Count);
        foreach (List<string>? row in grid)
        {
            while (row.Count < width)
            {
                row.Add(string.Empty);
            }
        }

        bool hasHeader = (string?)table.Attribute("hasHeaderRow") == "true";
        List<string> header = hasHeader ? grid[0] : [.. Enumerable.Repeat(string.Empty, width)];
        IEnumerable<List<string>> body = hasHeader ? grid.Skip(1) : grid;

        sb.Append(indent).Append("| ").Append(string.Join(" | ", header)).Append(" |\n");
        sb.Append(indent).Append('|').Append(string.Concat(Enumerable.Repeat(" --- |", width))).Append('\n');

        foreach (List<string>? row in body)
        {
            sb.Append(indent).Append("| ").Append(string.Join(" | ", row)).Append(" |\n");
        }

        sb.Append('\n');
    }

    private static string CellText(XElement cell, QuickStyleTable styles, TagTable tags)
    {
        StringBuilder inner = new();
        WriteOeChildren(cell.Element(One + "OEChildren"), inner, styles, tags, depth: 0);

        string text = inner.ToString()
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("|", "\\|", StringComparison.Ordinal)
            .Trim('\n', ' ')
            .Replace("\n", "<br>", StringComparison.Ordinal);

        while (text.EndsWith("<br>", StringComparison.Ordinal))
        {
            text = text[..^4].TrimEnd();
        }

        return text.Trim();
    }

    private static string RenderImage(XElement image)
    {
        string? alt = (string?)image.Attribute("alt");
        if (string.IsNullOrWhiteSpace(alt))
        {
            alt = image.Element(One + "OCRData")?.Value.Trim() is { Length: > 0 } ocr ? Shorten(ocr, 60) : "image";
        }

        string id = (string?)image.Element(One + "CallbackID")?.Attribute("callbackID")
                 ?? (string?)image.Attribute("objectID")
                 ?? string.Empty;

        return $"![{alt.Replace("]", "\\]", StringComparison.Ordinal)}](onenote-object:{id})";
    }

    private static string Shorten(string value, int max)
    {
        value = value.ReplaceLineEndings(" ").Trim();
        return value.Length <= max ? value : value[..max] + "...";
    }

    private static bool IsMonospaceStyle(string? style) =>
        style is not null
        && (style.Contains("Consolas", StringComparison.OrdinalIgnoreCase)
            || style.Contains("Courier", StringComparison.OrdinalIgnoreCase)
            || style.Contains("Cascadia", StringComparison.OrdinalIgnoreCase));

    private static string YamlScalar(string value) =>
        value.Length == 0 ? "\"\"" : "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private static string CollapseBlankLinesAndTrimTrailingNewline(string markdown)
    {
        string[] lines = markdown.Replace("\r\n", "\n").Split('\n');
        StringBuilder sb = new();
        int blank = 0;

        foreach (string line in lines)
        {
            if (line.Trim().Length == 0)
            {
                if (++blank > 1)
                {
                    continue;
                }
            }
            else
            {
                blank = 0;
            }

            sb.Append(line.TrimEnd()).Append('\n');
        }

        return sb.ToString().Trim('\n') + "\n";
    }

    private static readonly Dictionary<string, int> HeadingLevels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["h1"] = 1,
        ["h2"] = 2,
        ["h3"] = 3,
        ["h4"] = 4,
        ["h5"] = 5,
        ["h6"] = 6,
    };

    private sealed class QuickStyleTable
    {
        private readonly Dictionary<string, string> _byIndex;

        private QuickStyleTable(Dictionary<string, string> byIndex) => _byIndex = byIndex;

        public static QuickStyleTable FromPage(XElement page)
        {
            Dictionary<string, string> map = new(StringComparer.Ordinal);

            foreach (XElement def in page.Elements(One + "QuickStyleDef"))
            {
                string? index = (string?)def.Attribute("index");
                string? name = (string?)def.Attribute("name");

                if (index is not null && name is not null)
                {
                    map[index] = name;
                }
            }

            return new QuickStyleTable(map);
        }

        public string? NameFor(string? index) =>
            index is not null && _byIndex.TryGetValue(index, out string? name) ? name : null;
    }

    private sealed class TagTable
    {
        private const string ToDoTagType = "3";

        private readonly Dictionary<string, (string Type, string Name)> _byIndex;

        private TagTable(Dictionary<string, (string, string)> byIndex) => _byIndex = byIndex;

        public static TagTable FromPage(XElement page)
        {
            Dictionary<string, (string, string)> map = new(StringComparer.Ordinal);

            foreach (XElement def in page.Elements(One + "TagDef"))
            {
                string? index = (string?)def.Attribute("index");
                if (index is null)
                {
                    continue;
                }

                map[index] = (
                    (string?)def.Attribute("type") ?? string.Empty,
                    (string?)def.Attribute("name") ?? "tag");
            }

            return new TagTable(map);
        }

        public bool? ToDoFor(XElement oe)
        {
            foreach (XElement tag in oe.Elements(One + "Tag"))
            {
                string? index = (string?)tag.Attribute("index");
                if (index is not null && _byIndex.TryGetValue(index, out (string Type, string Name) def) && def.Type == ToDoTagType)
                {
                    return (string?)tag.Attribute("completed") == "true";
                }
            }

            return null;
        }

        public List<string> LabelsFor(XElement oe)
        {
            List<string> labels = new();

            foreach (XElement tag in oe.Elements(One + "Tag"))
            {
                string? index = (string?)tag.Attribute("index");
                if (index is not null && _byIndex.TryGetValue(index, out (string Type, string Name) def) && def.Type != ToDoTagType)
                {
                    labels.Add(def.Name);
                }
            }

            return labels;
        }
    }
}
