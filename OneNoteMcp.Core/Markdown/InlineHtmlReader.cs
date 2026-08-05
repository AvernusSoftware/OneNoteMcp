using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace OneNoteMcp.Core.Markdown;

public static partial class InlineHtmlReader
{
    private static readonly System.Buffers.SearchValues<char> MarkdownSpecials = System.Buffers.SearchValues.Create("*_`[]\\");

    public static string ToMarkdown(string? html, bool suppressBold = false)
    {
        if (string.IsNullOrEmpty(html))
        {
            return string.Empty;
        }

        string normalised = NormaliseForXmlParsing(html);

        XElement root;
        try
        {
            root = XElement.Parse("<r>" + normalised + "</r>", LoadOptions.PreserveWhitespace);
        }
        catch (System.Xml.XmlException)
        {
            return StripTags(normalised);
        }

        StringBuilder sb = new();
        WriteChildren(root, sb, new Format(SuppressBold: suppressBold));
        return sb.ToString();
    }

    private static string NormaliseForXmlParsing(string html)
    {
        html = TagRegex().Replace(html, match => UnquotedAttributeRegex().Replace(match.Value, "$1=\"$2\""));
        html = VoidTagRegex().Replace(html, "<$1$2/>");
        html = html
            .Replace("&nbsp;", " ", StringComparison.Ordinal)
            .Replace("&#160;", " ", StringComparison.Ordinal);
        html = BareAmpersandRegex().Replace(html, "&amp;");

        return html;
    }

    private static void WriteChildren(XElement element, StringBuilder sb, Format format)
    {
        foreach (XNode node in element.Nodes())
        {
            switch (node)
            {
                case XText text:
                    Emit(sb, text.Value, format);
                    break;

                case XElement child:
                    WriteElement(child, sb, format);
                    break;
            }
        }
    }

    private static void WriteElement(XElement element, StringBuilder sb, Format format)
    {
        switch (element.Name.LocalName.ToLowerInvariant())
        {
            case "br":
                sb.Append("  \n");
                return;

            case "a":
                string? href = (string?)element.Attribute("href");
                StringBuilder inner = new();
                WriteChildren(element, inner, format);
                string textPart = inner.ToString();

                if (string.IsNullOrWhiteSpace(href))
                {
                    sb.Append(textPart);
                }
                else
                {
                    sb.Append('[')
                      .Append(string.IsNullOrWhiteSpace(textPart) ? href : textPart)
                      .Append("](")
                      .Append(href)
                      .Append(')');
                }

                return;

            case "span":
            case "font":
                WriteChildren(element, sb, format.With(ParseStyle((string?)element.Attribute("style"))));
                return;

            case "b":
            case "strong":
                WriteChildren(element, sb, format with { Bold = true });
                return;

            case "i":
            case "em":
                WriteChildren(element, sb, format with { Italic = true });
                return;

            case "u":
                WriteChildren(element, sb, format with { Underline = true });
                return;

            case "s":
            case "strike":
            case "del":
                WriteChildren(element, sb, format with { Strike = true });
                return;

            default:
                WriteChildren(element, sb, format);
                return;
        }
    }

    private static void Emit(StringBuilder sb, string value, Format format)
    {
        if (value.Length == 0)
        {
            return;
        }

        if (format.IsPlain || string.IsNullOrWhiteSpace(value))
        {
            sb.Append(Escape(value));
            return;
        }

        int lead = value.Length - value.TrimStart().Length;
        int trail = value.Length - value.TrimEnd().Length;
        string core = value[lead..(value.Length - trail)];

        sb.Append(value[..lead]);

        if (format.Code)
        {
            AppendCodeSpan(sb, core);
        }
        else
        {
            StringBuilder open = new();
            StringBuilder close = new();

            if (format.EffectiveBold) { open.Append("**"); close.Insert(0, "**"); }
            if (format.Italic) { open.Append('*'); close.Insert(0, '*'); }
            if (format.Strike) { open.Append("~~"); close.Insert(0, "~~"); }
            if (format.Underline) { open.Append("<u>"); close.Insert(0, "</u>"); }

            sb.Append(open).Append(Escape(core)).Append(close);
        }

        sb.Append(value[(value.Length - trail)..]);
    }

    private static void AppendCodeSpan(StringBuilder sb, string content)
    {
        string fence = new('`', LongestBacktickRun(content) + 1);
        bool needsPadding = content.StartsWith('`') || content.EndsWith('`');

        sb.Append(fence);
        if (needsPadding) { sb.Append(' '); }
        sb.Append(content);
        if (needsPadding) { sb.Append(' '); }
        sb.Append(fence);
    }

    private static int LongestBacktickRun(string value)
    {
        int longest = 0;
        int current = 0;

        foreach (char c in value)
        {
            current = c == '`' ? current + 1 : 0;
            longest = Math.Max(longest, current);
        }

        return longest;
    }

    private static string Escape(string value)
    {
        if (value.AsSpan().IndexOfAny(MarkdownSpecials) < 0)
        {
            return value;
        }

        StringBuilder sb = new(value.Length + 8);
        foreach (char c in value)
        {
            if (c is '*' or '_' or '`' or '[' or ']' or '\\')
            {
                sb.Append('\\');
            }

            sb.Append(c);
        }

        return sb.ToString();
    }

    private static Format ParseStyle(string? style)
    {
        Format f = new();
        if (string.IsNullOrWhiteSpace(style))
        {
            return f;
        }

        foreach (string part in style.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int sep = part.IndexOf(':');
            if (sep <= 0)
            {
                continue;
            }

            string name = part[..sep].Trim().ToLowerInvariant();
            string value = part[(sep + 1)..].Trim().ToLowerInvariant();

            switch (name)
            {
                case "font-weight" when value is "bold" or "bolder" || (int.TryParse(value, out int w) && w >= 600):
                    f = f with { Bold = true };
                    break;

                case "font-style" when value is "italic" or "oblique":
                    f = f with { Italic = true };
                    break;

                case "text-decoration" when value.Contains("line-through", StringComparison.Ordinal):
                    f = f with { Strike = true };
                    break;

                case "text-decoration" when value.Contains("underline", StringComparison.Ordinal):
                    f = f with { Underline = true };
                    break;

                case "font-family" when IsMonospace(value):
                    f = f with { Code = true };
                    break;
            }
        }

        return f;
    }

    private static bool IsMonospace(string fontFamily) =>
        fontFamily.Contains("consolas", StringComparison.Ordinal)
        || fontFamily.Contains("courier", StringComparison.Ordinal)
        || fontFamily.Contains("monospace", StringComparison.Ordinal)
        || fontFamily.Contains("cascadia", StringComparison.Ordinal)
        || fontFamily.Contains("lucida console", StringComparison.Ordinal);

    private static string StripTags(string html) =>
        System.Net.WebUtility.HtmlDecode(AnyTagRegex().Replace(html, string.Empty));

    private readonly record struct Format(bool Bold = false, bool Italic = false, bool Underline = false, bool Strike = false, bool Code = false, bool SuppressBold = false)
    {
        public bool EffectiveBold => Bold && !SuppressBold;

        public bool IsPlain => !EffectiveBold && !Italic && !Underline && !Strike && !Code;

        public Format With(Format other) => this with
        {
            Bold = Bold || other.Bold,
            Italic = Italic || other.Italic,
            Underline = Underline || other.Underline,
            Strike = Strike || other.Strike,
            Code = Code || other.Code,
        };
    }

    [GeneratedRegex(@"<(br|hr|img)\b([^>]*?)(?<!/)>", RegexOptions.IgnoreCase)]
    private static partial Regex VoidTagRegex();

    [GeneratedRegex(@"<[^>]*>")]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"([a-zA-Z_:][-\w:.]*)\s*=\s*([^\s""'<>/][^\s""'<>]*)")]
    private static partial Regex UnquotedAttributeRegex();

    [GeneratedRegex(@"&(?!(?:amp|lt|gt|quot|apos|#\d+|#x[0-9a-fA-F]+);)")]
    private static partial Regex BareAmpersandRegex();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex AnyTagRegex();
}
