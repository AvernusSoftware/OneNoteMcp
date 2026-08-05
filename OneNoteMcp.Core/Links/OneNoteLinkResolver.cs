using OneNoteMcp.Core.Models;

namespace OneNoteMcp.Core.Links;

public static class OneNoteLinkResolver
{
    public sealed record Candidate(HierarchyNode Page, string? SectionId, string? SectionName, string? NotebookName);

    public sealed record ResolveResult(Candidate? Match, string? MatchReason, IReadOnlyList<Candidate> NearMisses)
    {
        public static readonly ResolveResult NotFound = new(null, null, Array.Empty<Candidate>());
    }

    public static ResolveResult Resolve(IEnumerable<HierarchyNode> pageTree, ParsedOneNoteLink link)
    {
        List<Candidate> entries = [.. Flatten(pageTree)];
        string? title = link.PageTitle?.Trim();

        List<Candidate> titleMatches = title is { Length: > 0 } ? [.. entries.Where(e => string.Equals(e.Page.Name.Trim(), title, StringComparison.OrdinalIgnoreCase))] : [];

        if (titleMatches.Count == 1)
        {
            return new ResolveResult(titleMatches[0], "exact title match", []);
        }

        if (titleMatches.Count > 1)
        {
            List<Candidate> narrowed = NarrowBySection(titleMatches, link);
            return narrowed.Count == 1 ? new ResolveResult(narrowed[0], "title match narrowed by section", []) : new ResolveResult(null, null, narrowed.Count > 0 ? narrowed : titleMatches);
        }

        if (link.PageId is { } pageId)
        {
            List<Candidate> idMatches = [.. entries.Where(e => IdContainsGuid(e.Page.Id, pageId))];

            if (idMatches.Count > 1)
            {
                idMatches = NarrowBySection(idMatches, link);
            }

            if (idMatches.Count == 1)
            {
                return new ResolveResult(idMatches[0], "page id embedded in hierarchy id", []);
            }
        }

        List<Candidate> fuzzy = title is { Length: > 3 } ? [.. entries.Where(e => e.Page.Name.Contains(title, StringComparison.OrdinalIgnoreCase) || title.Contains(e.Page.Name.Trim(), StringComparison.OrdinalIgnoreCase))] : [];

        return new ResolveResult(null, null, fuzzy);
    }

    private static List<Candidate> NarrowBySection(List<Candidate> candidates, ParsedOneNoteLink link)
    {
        if (link.SectionFileName is { Length: > 0 } sectionName)
        {
            List<Candidate> bySectionName = [.. candidates.Where(c => string.Equals(c.SectionName?.Trim(), sectionName.Trim(), StringComparison.OrdinalIgnoreCase))];

            if (bySectionName.Count > 0)
            {
                candidates = bySectionName;
            }
        }

        if (candidates.Count > 1 && link.SectionId is { } sectionId)
        {
            List<Candidate> byId = [.. candidates.Where(c => IdContainsGuid(c.SectionId, sectionId))];
            if (byId.Count > 0)
            {
                candidates = byId;
            }
        }

        return candidates;
    }

    private static bool IdContainsGuid(string? hierarchyId, Guid guid)
    {
        if (string.IsNullOrEmpty(hierarchyId))
        {
            return false;
        }

        string normalizedId = new string([.. hierarchyId.Where(Uri.IsHexDigit)]).ToLowerInvariant();
        string normalizedGuid = guid.ToString("N");
        return normalizedId.Contains(normalizedGuid, StringComparison.Ordinal);
    }

    private static IEnumerable<Candidate> Flatten(IEnumerable<HierarchyNode> nodes, string? sectionId = null, string? sectionName = null, string? notebookName = null)
    {
        foreach (HierarchyNode node in nodes)
        {
            switch (node.Type)
            {
                case HierarchyNodeType.Notebook:
                    foreach (Candidate c in Flatten(node.Children ?? [], sectionId, sectionName, node.Name))
                    {
                        yield return c;
                    }

                    break;

                case HierarchyNodeType.SectionGroup:
                    foreach (Candidate c in Flatten(node.Children ?? [], sectionId, sectionName, notebookName))
                    {
                        yield return c;
                    }

                    break;

                case HierarchyNodeType.Section:
                    foreach (Candidate c in Flatten(node.Children ?? [], node.Id, node.Name, notebookName))
                    {
                        yield return c;
                    }

                    break;

                case HierarchyNodeType.Page:
                    yield return new Candidate(node, sectionId, sectionName, notebookName);

                    foreach (Candidate c in Flatten(node.Children ?? [], sectionId, sectionName, notebookName))
                    {
                        yield return c;
                    }

                    break;
            }
        }
    }
}
