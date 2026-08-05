using System.Text.RegularExpressions;

namespace OneNoteMcp.Core.Links;

public static partial class OneNoteLinkParser
{
    private const string GuidPattern = "[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}";

    private static readonly Regex OneNoteUriSectionId = OneNoteUriSectionIdRegex();
    private static readonly Regex OneNoteUriPageId = OneNoteUriPageIdRegex();
    private static readonly Regex WdParam = WdParamRegex();
    private static readonly Regex WdSectionFileId = WdSectionFileIdRegex();
    private static readonly Regex WdTarget = WdTargetRegex();

    public static ParsedOneNoteLink? TryParse(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        return TryParseOneNoteUri(input) ?? TryParseSharePointDocUrl(input);
    }

    private static ParsedOneNoteLink? TryParseOneNoteUri(string input)
    {
        int start = input.IndexOf("onenote:", StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return null;
        }

        string uri = input[start..];
        int hashIndex = uri.IndexOf('#');
        if (hashIndex < 0)
        {
            return null;
        }

        string fragment = uri[(hashIndex + 1)..];
        Match sectionMatch = OneNoteUriSectionId.Match(fragment);
        Match pageMatch = OneNoteUriPageId.Match(fragment);
        int titleEnd = fragment.Length;

        foreach (string? marker in new[] { "&section-id=", "&page-id=", "&end" })
        {
            int idx = fragment.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0 && idx < titleEnd)
            {
                titleEnd = idx;
            }
        }

        string title = DecodeTitle(fragment[..titleEnd]);

        return new ParsedOneNoteLink
        {
            PageTitle = string.IsNullOrWhiteSpace(title) ? null : title,
            SectionId = sectionMatch.Success ? Guid.Parse(sectionMatch.Groups[1].Value) : null,
            PageId = pageMatch.Success ? Guid.Parse(pageMatch.Groups[1].Value) : null,
        };
    }

    private static ParsedOneNoteLink? TryParseSharePointDocUrl(string input)
    {
        Match wdMatch = WdParam.Match(input);
        if (!wdMatch.Success)
        {
            return null;
        }

        string decoded = DecodeTitle(wdMatch.Groups[1].Value);
        Match targetMatch = WdTarget.Match(decoded);
        if (!targetMatch.Success)
        {
            return null;
        }

        string path = targetMatch.Groups["path"].Value.Trim();
        string sectionFileName = path.Contains('/') ? path[(path.LastIndexOf('/') + 1)..] : path;
        sectionFileName = sectionFileName.EndsWith(".one", StringComparison.OrdinalIgnoreCase) ? sectionFileName[..^".one".Length] : sectionFileName;

        Guid sectionId = Guid.Parse(targetMatch.Groups["sectionId"].Value);
        Match fallbackSectionId = WdSectionFileId.Match(input);
        if (fallbackSectionId.Success)
        {
            sectionId = Guid.Parse(fallbackSectionId.Groups[1].Value);
        }

        return new ParsedOneNoteLink
        {
            PageTitle = targetMatch.Groups["title"].Value.Trim(),
            SectionId = sectionId,
            PageId = Guid.Parse(targetMatch.Groups["pageId"].Value),
            SectionFileName = string.IsNullOrWhiteSpace(sectionFileName) ? null : sectionFileName,
        };
    }

    private static string DecodeTitle(string value)
    {
        try
        {
            return Uri.UnescapeDataString(value).Trim();
        }
        catch (UriFormatException)
        {
            return value.Trim();
        }
    }

    [GeneratedRegex(@"section-id=\{?([0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12})\}?", RegexOptions.IgnoreCase | RegexOptions.Compiled, "pl-PL")]
    private static partial Regex OneNoteUriSectionIdRegex();

    [GeneratedRegex(@"page-id=\{?([0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12})\}?", RegexOptions.IgnoreCase | RegexOptions.Compiled, "pl-PL")]
    private static partial Regex OneNoteUriPageIdRegex();

    [GeneratedRegex(@"[?&]wd=([^&]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled, "pl-PL")]
    private static partial Regex WdParamRegex();

    [GeneratedRegex(@"wdsectionfileid=\{?([0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12})\}?", RegexOptions.IgnoreCase | RegexOptions.Compiled, "pl-PL")]
    private static partial Regex WdSectionFileIdRegex();

    [GeneratedRegex(@"^target\((?<path>[^|]+)\|(?<sectionId>[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12})/(?<title>.+)\|(?<pageId>[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12})/\)$", RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.Singleline, "pl-PL")]
    private static partial Regex WdTargetRegex();
}
