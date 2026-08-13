using Microsoft.Extensions.Options;
using OneNoteMcp.Core.Configuration;
using OneNoteMcp.Core.Exceptions;
using OneNoteMcp.Core.Hierarchy;
using OneNoteMcp.Core.Interop;
using OneNoteMcp.Core.Links;
using OneNoteMcp.Core.Markdown;
using OneNoteMcp.Core.Models;
using OneNoteMcp.Core.Threading;
using System.Text;
using System.Xml.Linq;

namespace OneNoteMcp.Core.Services;

/// <summary>OneNote access over COM, with every call marshalled onto the STA thread.</summary>
public sealed class OneNoteComService : IOneNoteService, IDisposable
{
    private static readonly XNamespace One = OneNoteNamespaces.One;

    /// <summary>
    /// OneNote refuses content writes while a page is checked out for editing in its own UI.
    /// Reads and <c>CreateNewPage</c> keep working, which makes the failure look arbitrary.
    /// </summary>
    private const int HrPageLockedForEditing = unchecked((int)0x80042030);

    private readonly IStaThreadRunner _sta;
    private readonly OneNoteApplicationHandle _handle = new();
    private readonly AgentOptions _agent;

    public OneNoteComService(IStaThreadRunner sta, IOptions<AgentOptions> agent)
    {
        ArgumentNullException.ThrowIfNull(agent);

        _sta = sta;
        _agent = agent.Value;
        _agent.Validate();
    }

    public Task<HierarchyNode[]> GetHierarchyAsync(HierarchyScope scope, string? startNodeId = null, CancellationToken cancellationToken = default) =>
        _sta.RunAsync(
            () =>
            {
                string xml = _handle.Invoke(app =>
                {
                    app.GetHierarchy(startNodeId, scope, out string? result, XmlSchema.Xs2013);
                    return result;
                });

                XElement root = XDocument.Parse(xml).Root
                    ?? throw new OneNoteException("GetHierarchy returned an empty document.");

                return root.Elements().SelectMany(ReadNode).ToArray();
            },
            cancellationToken);

    public Task<CurrentPageInfo> GetCurrentPageAsync(CancellationToken cancellationToken = default) =>
        _sta.RunAsync(
            () =>
            {
                // Preferred source: the live OneNote window.
                string? pageId = TryGetCurrentPageIdFromWindow();

                // Fallback: OneNote marks the on-screen nodes in the hierarchy itself, which keeps
                // working when no window is open (for example when we started OneNote headless).
                string hierarchyXml = _handle.Invoke(app =>
                {
                    app.GetHierarchy(null, HierarchyScope.Pages, out string? result, XmlSchema.Xs2013);
                    return result;
                });

                XDocument doc = XDocument.Parse(hierarchyXml);

                XElement? page = !string.IsNullOrEmpty(pageId) ? doc.Descendants(One + "Page").FirstOrDefault(p => (string?)p.Attribute("ID") == pageId) : doc.Descendants(One + "Page").FirstOrDefault(p => (string?)p.Attribute("isCurrentlyViewed") == "true");

                if (page is null)
                {
                    return new CurrentPageInfo
                    {
                        PageId = string.IsNullOrEmpty(pageId) ? null : pageId,
                        Note = string.IsNullOrEmpty(pageId) ? "No page is currently open in the OneNote window. Use get_hierarchy to list pages and get_page_content to read one." : "OneNote reported a current page id but it was not found in the hierarchy; the notebook may still be loading.",
                    };
                }

                XElement? section = page.Ancestors(One + "Section").FirstOrDefault();
                XElement? notebook = page.Ancestors(One + "Notebook").FirstOrDefault();

                return new CurrentPageInfo
                {
                    PageId = (string?)page.Attribute("ID"),
                    Title = (string?)page.Attribute("name"),
                    SectionId = (string?)section?.Attribute("ID"),
                    SectionName = (string?)section?.Attribute("name"),
                    NotebookId = (string?)notebook?.Attribute("ID"),
                    NotebookName = (string?)notebook?.Attribute("name"),
                };
            },
            cancellationToken);

    private string? TryGetCurrentPageIdFromWindow()
    {
        try
        {
            return _handle.Invoke(app =>
            {
                string id = app.GetWindows().GetCurrentWindow().GetCurrentPageId();
                return string.IsNullOrWhiteSpace(id) ? null : id;
            });
        }
        catch (Exception)
        {
            // OneNote throws when it has no active window - the hierarchy fallback covers it.
            return null;
        }
    }

    public Task<string> GetPageContentAsMarkdownAsync(string pageId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pageId);

        return _sta.RunAsync(
            () =>
            {
                string xml = _handle.Invoke(app =>
                {
                    app.GetPageContent(pageId, out string? result, PageInfo.Basic, XmlSchema.Xs2013);
                    return result;
                });

                return OneNoteXmlParser.ToMarkdown(xml, agentDisplayName: _agent.DisplayName);
            },
            cancellationToken);
    }

    public Task<SearchHit[]> SearchAsync(string query, int maxResults = 20, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        maxResults = Math.Clamp(maxResults, 1, 100);

        return _sta.RunAsync(() => FindPagesCore(query, maxResults), cancellationToken);
    }

    // Split out of SearchAsync so GetPageContentByLinkAsync can call it directly while already
    // running on the STA thread - going back through _sta.RunAsync from inside a queued work item
    // would deadlock the single-threaded queue.
    private SearchHit[] FindPagesCore(string query, int maxResults)
    {
        string xml = _handle.Invoke(app =>
        {
            // fIncludeUnindexedPages: true - otherwise pages Windows Search has not got to yet
            // (including ones just created) are invisible to search.
            // fDisplay: false - never disturb the user's OneNote UI with our query.
            app.FindPages(null, query, out string? result, true, false, XmlSchema.Xs2013);
            return result;
        });

        List<XElement> pages = [.. XDocument.Parse(xml).Descendants(One + "Page").Take(maxResults)];
        List<SearchHit> hits = new(pages.Count);

        foreach (XElement? page in pages)
        {
            string? id = (string?)page.Attribute("ID");
            if (id is null)
            {
                continue;
            }

            hits.Add(new SearchHit
            {
                PageId = id,
                Title = (string?)page.Attribute("name") ?? "(untitled)",
                SectionName = (string?)page.Ancestors(One + "Section").FirstOrDefault()?.Attribute("name"),
                NotebookName = (string?)page.Ancestors(One + "Notebook").FirstOrDefault()?.Attribute("name"),
                LastModified = (string?)page.Attribute("lastModifiedTime"),
                Snippet = TryBuildSnippet(id, query),
            });
        }

        return [.. hits];
    }

    public Task<string> GetPageContentByLinkAsync(string link, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(link);

        ParsedOneNoteLink parsed = OneNoteLinkParser.TryParse(link)
            ?? throw new OneNoteException(
                "Could not recognise this as a OneNote link. Expected either a 'onenote:' URI " +
                "(from OneNote's 'Copy Link to Page') or a SharePoint 'Doc.aspx' share link " +
                "carrying a 'wd=target(...)' parameter.");

        return _sta.RunAsync(
            () =>
            {
                string hierarchyXml = _handle.Invoke(app =>
                {
                    app.GetHierarchy(null, HierarchyScope.Pages, out string? result, XmlSchema.Xs2013);
                    return result;
                });

                HierarchyNode[] tree = XDocument.Parse(hierarchyXml).Root?.Elements().SelectMany(ReadNode).ToArray() ?? [];

                OneNoteLinkResolver.ResolveResult resolved = OneNoteLinkResolver.Resolve(tree, parsed);

                string pageId;
                string note;

                if (resolved.Match is { } match)
                {
                    pageId = match.Page.Id;
                    note = "<!-- resolved via " + resolved.MatchReason + ": '" + match.Page.Name + "'"
                        + (match.SectionName is null ? string.Empty : $" in section '{match.SectionName}'")
                        + " -->\n\n";
                }
                else
                {
                    // Not found in an already-loaded notebook - fall back to full-text search, the
                    // same last resort a person would reach for.
                    SearchHit? searchHit = TryResolveViaSearch(parsed) ?? throw new OneNoteException(BuildNotFoundMessage(parsed, resolved));
                    pageId = searchHit.PageId;
                    note = $"<!-- resolved via full-text search fallback: '{searchHit.Title}' -->\n\n";
                }

                string xml = _handle.Invoke(app =>
                {
                    app.GetPageContent(pageId, out string? result, PageInfo.Basic, XmlSchema.Xs2013);
                    return result;
                });

                return note + OneNoteXmlParser.ToMarkdown(xml, agentDisplayName: _agent.DisplayName);
            },
            cancellationToken);
    }

    private SearchHit? TryResolveViaSearch(ParsedOneNoteLink parsed)
    {
        if (string.IsNullOrWhiteSpace(parsed.PageTitle))
        {
            return null;
        }

        SearchHit[] hits = FindPagesCore(parsed.PageTitle, maxResults: 5);
        if (hits.Length == 1)
        {
            return hits[0];
        }

        // The full title may not match verbatim (search tokenises), so retry on its most
        // distinctive word - the longest run of letters/digits, which for these links is usually a
        // ticket or code reference (e.g. "BL260652").
        string? keyword = parsed.PageTitle
            .Split([' ', '\t', ';', ',', '[', ']', '(', ')'], StringSplitOptions.RemoveEmptyEntries)
            .OrderByDescending(w => w.Length)
            .FirstOrDefault();

        if (keyword is null)
        {
            return null;
        }

        SearchHit[] keywordHits = FindPagesCore(keyword, maxResults: 5);
        return keywordHits.Length == 1 ? keywordHits[0] : null;
    }

    private static string BuildNotFoundMessage(ParsedOneNoteLink parsed, OneNoteLinkResolver.ResolveResult resolved)
    {
        StringBuilder sb = new("Could not find the page this link points to.");

        if (!string.IsNullOrWhiteSpace(parsed.PageTitle))
        {
            sb.Append(" Parsed title: '").Append(parsed.PageTitle).Append('\'').Append('.');
        }

        if (!string.IsNullOrWhiteSpace(parsed.SectionFileName))
        {
            sb.Append(" Parsed section: '").Append(parsed.SectionFileName).Append('\'').Append('.');
        }

        if (resolved.NearMisses.Count > 0)
        {
            sb.Append(" Closest matches already open locally: ").Append(string.Join(
                "; ",
                resolved.NearMisses.Take(5).Select(c =>
                    $"'{c.Page.Name}'" + (c.SectionName is null ? string.Empty : $" (section '{c.SectionName}')"))));
            sb.Append('.');
        }

        sb.Append(" If the notebook containing this page is not open in the OneNote desktop app, open it there first, then retry.");

        return sb.ToString();
    }

    private string? TryBuildSnippet(string pageId, string query)
    {
        try
        {
            string xml = _handle.Invoke(app =>
            {
                app.GetPageContent(pageId, out string? result, PageInfo.Basic, XmlSchema.Xs2013);
                return result;
            });

            StringBuilder text = new();
            foreach (XElement t in XDocument.Parse(xml).Descendants(One + "T"))
            {
                string run = InlineHtmlReader.ToMarkdown(t.Value).Trim();
                if (run.Length > 0)
                {
                    text.Append(run).Append(' ');
                }
            }

            string body = text.ToString().Trim();
            if (body.Length == 0)
            {
                return null;
            }

            string term = query.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? query;
            int at = body.IndexOf(term, StringComparison.OrdinalIgnoreCase);
            int start = at < 0 ? 0 : Math.Max(0, at - 60);
            int length = Math.Min(200, body.Length - start);

            string snippet = body.Substring(start, length).ReplaceLineEndings(" ").Trim();
            return (start > 0 ? "..." : string.Empty) + snippet + (start + length < body.Length ? "..." : string.Empty);
        }
        catch (Exception)
        {
            // A snippet is a nicety; never fail the whole search because one page could not be read.
            return null;
        }
    }

    public Task<CreatePageResult> CreatePageAsync(string sectionId, string title, string contentMarkdown, string? parentPageId = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionId);

        return _sta.RunAsync(
            () =>
            {
                string pageId = _handle.Invoke(app =>
                {
                    app.CreateNewPage(sectionId, out string? newId, NewPageStyle.BlankPageWithTitle);
                    return newId;
                });

                string xml = MarkdownToOneNoteXml.BuildPageXml(
                    pageId, title ?? string.Empty, contentMarkdown ?? string.Empty, _agent);

                // Writes only to the page created on the line above - never to pre-existing content.
                Write(xml);

                int? pageLevel = null;

                if (!string.IsNullOrWhiteSpace(parentPageId))
                {
                    XElement section = ReadSection(sectionId);
                    (XElement reordered, int childLevel) = SubpagePlacement.PlaceUnderParent(section, parentPageId, pageId, title ?? string.Empty);
                    WriteHierarchy(reordered);
                    pageLevel = childLevel;
                }

                return new CreatePageResult
                {
                    PageId = pageId,
                    Title = title ?? string.Empty,
                    SectionId = sectionId,
                    Status = "created",
                    PageLevel = pageLevel,
                };
            },
            cancellationToken);
    }

    public Task<BlockResult> AppendBlockAsync(string pageId, string contentMarkdown, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pageId);

        return _sta.RunAsync(
            () =>
            {
                XElement page = ReadPage(pageId);
                HashSet<string> before = BlockIds(page);

                string xml = MarkdownToOneNoteXml.BuildOutlineXml(
                    pageId,
                    contentMarkdown ?? string.Empty,
                    _agent,
                    PageSchema.ForExistingPage(page));

                Write(xml);

                // The id is only knowable after the fact, so diff the blocks against the snapshot
                // taken above rather than guessing.
                string? added = BlockIds(ReadPage(pageId)).Except(before).FirstOrDefault();

                return new BlockResult
                {
                    PageId = pageId,
                    BlockId = added,
                    Status = "appended",
                };
            },
            cancellationToken);
    }

    public Task<BlockResult> UpdateBlockAsync(string pageId, string blockId, string contentMarkdown, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(blockId);

        return _sta.RunAsync(
            () =>
            {
                XElement page = ReadPage(pageId);
                RequireNoAttachment(RequireOwnedBlock(page, blockId), blockId);

                string xml = MarkdownToOneNoteXml.BuildOutlineXml(
                    pageId,
                    contentMarkdown ?? string.Empty,
                    _agent,
                    PageSchema.ForExistingPage(page),
                    objectId: blockId);

                Write(xml);

                return new BlockResult
                {
                    PageId = pageId,
                    BlockId = blockId,
                    Status = "updated",
                };
            },
            cancellationToken);
    }

    public Task<BlockResult> DeleteBlockAsync(string pageId, string blockId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(blockId);

        return _sta.RunAsync(
            () =>
            {
                RequireOwnedBlock(ReadPage(pageId), blockId);

                Guarded(() => _handle.Invoke(app =>
                    app.DeletePageContent(pageId, blockId, DateTime.MinValue, false)));

                return new BlockResult
                {
                    PageId = pageId,
                    BlockId = blockId,
                    Status = "deleted",
                };
            },
            cancellationToken);
    }

    public Task<AttachmentResult> AttachFileAsync(string filePath, string? pageId, string? sectionId, string? pageTitle, string? parentPageId, string? caption, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        string preferredName = Path.GetFileName(filePath);
        if (string.IsNullOrWhiteSpace(preferredName))
        {
            throw new OneNoteException($"'{filePath}' names a directory, not a file.");
        }

        return _sta.RunAsync(
            () =>
            {
                (string targetPageId, string? pageStatus, int? pageLevel) = ResolveTargetPage(pageId, sectionId, pageTitle, parentPageId);

                XElement page = ReadPage(targetPageId);
                XElement? owned = AiBlocks.FindOwnedAttachment(page, preferredName, _agent.DisplayName);

                if (owned is null && AiBlocks.HoldsFile(page, preferredName))
                {
                    throw new OneNoteException(
                        $"A file named '{preferredName}' is already attached to this page in a block " +
                        $"that was not written by '{_agent.DisplayName}', or that someone has since " +
                        "typed into. Rename the file, or attach it to a different page.");
                }

                string? existingBlockId = (string?)owned?.Attribute("objectID");
                HashSet<string> before = BlockIds(page);

                Write(AttachmentXml.BuildOutlineXml(
                    targetPageId,
                    filePath,
                    preferredName,
                    caption,
                    _agent,
                    PageSchema.ForExistingPage(page),
                    objectId: existingBlockId));

                return new AttachmentResult
                {
                    PageId = targetPageId,
                    PageStatus = pageStatus,
                    PageLevel = pageLevel,
                    BlockId = existingBlockId ?? BlockIds(ReadPage(targetPageId)).Except(before).FirstOrDefault(),
                    FileName = preferredName,
                    Status = existingBlockId is null ? "attached" : "replaced",
                };
            },
            cancellationToken);
    }

    private (string PageId, string? PageStatus, int? PageLevel) ResolveTargetPage(string? pageId, string? sectionId, string? pageTitle, string? parentPageId)
    {
        if (!string.IsNullOrWhiteSpace(pageId))
        {
            return (pageId, null, null);
        }

        if (string.IsNullOrWhiteSpace(sectionId) || string.IsNullOrWhiteSpace(pageTitle))
        {
            throw new OneNoteException("Either pageId, or both sectionId and pageTitle, must be given.");
        }

        if (AttachmentsPageLookup.FindPage(ReadSection(sectionId), pageTitle, parentPageId) is { } existing)
        {
            string existingId = (string?)existing.Attribute("ID") ?? throw new OneNoteException($"OneNote returned a page named '{pageTitle}' with no id.");
            int level = ParentPage.ReadLevel(existing);

            return (existingId, "reused", level > 0 ? level : null);
        }

        string created = _handle.Invoke(app =>
        {
            app.CreateNewPage(sectionId, out string? newId, NewPageStyle.BlankPageWithTitle);
            return newId;
        });

        Write(MarkdownToOneNoteXml.BuildPageXml(created, pageTitle, string.Empty, _agent));

        if (string.IsNullOrWhiteSpace(parentPageId))
        {
            return (created, "created", null);
        }

        (XElement reordered, int childLevel) = SubpagePlacement.PlaceUnderParent(ReadSection(sectionId), parentPageId, created, pageTitle);
        WriteHierarchy(reordered);

        return (created, "created", childLevel);
    }

    private XElement ReadPage(string pageId)
    {
        string xml = _handle.Invoke(app =>
        {
            app.GetPageContent(pageId, out string? result, PageInfo.Basic, XmlSchema.Xs2013);
            return result;
        });

        return XDocument.Parse(xml).Descendants(One + "Page").FirstOrDefault()
            ?? throw new OneNoteException($"OneNote returned no page for id '{pageId}'.");
    }

    private XElement ReadSection(string sectionId)
    {
        string xml = _handle.Invoke(app =>
        {
            app.GetHierarchy(sectionId, HierarchyScope.Pages, out string? result, XmlSchema.Xs2013);
            return result;
        });

        return XDocument.Parse(xml).Descendants(One + "Section").FirstOrDefault(s => (string?)s.Attribute("ID") == sectionId)
            ?? throw new OneNoteException($"OneNote returned no section for id '{sectionId}'.");
    }

    private void WriteHierarchy(XElement changesXml) =>
        Guarded(() => _handle.Invoke(app =>
            app.UpdateHierarchy(new XDocument(new XDeclaration("1.0", null, null), changesXml).ToString(SaveOptions.DisableFormatting), XmlSchema.Xs2013)));

    private static HashSet<string> BlockIds(XElement page) =>
        page.Descendants(One + "Outline")
            .Select(o => (string?)o.Attribute("objectID"))
            .Where(id => id is not null)
            .ToHashSet(StringComparer.Ordinal)!;

    // Ownership is re-read from OneNote on every write, so a caller cannot get at somebody else's
    // content by passing an id it was never given.
    private XElement RequireOwnedBlock(XElement page, string blockId)
    {
        XElement block = AiBlocks.FindBlock(page, blockId)
            ?? throw new OneNoteException(
                $"No block with id '{blockId}' exists on this page. Call get_page_content again - " +
                "block ids change when the page is edited.");

        if (!AiBlocks.IsOwnedBy(block, _agent.DisplayName))
        {
            throw new OneNoteException(
                $"Block '{blockId}' cannot be modified: it was not written by " +
                $"'{_agent.DisplayName}', or someone has since typed into it. Only blocks marked " +
                "with an 'ai-block' comment in get_page_content may be changed. Add new content " +
                "with append_to_page instead.");
        }

        return block;
    }

    private static void RequireNoAttachment(XElement block, string blockId)
    {
        if (!AiBlocks.HoldsAnyFile(block))
        {
            return;
        }

        string names = string.Join(", ", AiBlocks.FileNames(block).Select(name => $"'{name}'"));
        string held = names.Length > 0 ? $"an attached file ({names})" : "an attached file";

        throw new OneNoteException(
            $"Block '{blockId}' holds {held}, and replacing its content would remove the file " +
            "from the notebook - the '[attachment: ...]' placeholder in the Markdown is a label, " +
            "not the file itself. Call attach_file again with the same file name to update the " +
            "attachment in place, or delete_block to remove the whole block.");
    }

    private void Write(string pageXml) =>
        Guarded(() => _handle.Invoke(app =>
            app.UpdatePageContent(pageXml, DateTime.MinValue, XmlSchema.Xs2013, false)));

    private static void Guarded(Action write)
    {
        try
        {
            write();
        }
        catch (System.Runtime.InteropServices.COMException ex) when (ex.HResult == HrPageLockedForEditing)
        {
            throw new OneNoteException(
                "OneNote refused the write because the page is currently locked for editing in the " +
                "OneNote window (0x80042030). Click away from the page, or close it, and try again.",
                ex);
        }
    }

    private static IEnumerable<HierarchyNode> ReadNode(XElement element)
    {
        switch (element.Name.LocalName)
        {
            case "Notebook":
                yield return Build(element, HierarchyNodeType.Notebook);
                break;

            case "SectionGroup":
                // OneNote exposes the recycle bin as a section group; it is noise for an LLM.
                if ((string?)element.Attribute("isRecycleBin") == "true")
                {
                    yield break;
                }

                yield return Build(element, HierarchyNodeType.SectionGroup);
                break;

            case "Section":
                yield return Build(element, HierarchyNodeType.Section);
                break;

            case "Page":
                yield return Build(element, HierarchyNodeType.Page);
                break;

            case "UnfiledNotes":
                // A pseudo-notebook holding Quick Notes. Surfaced so its sections are reachable.
                yield return new HierarchyNode
                {
                    Id = (string?)element.Attribute("ID") ?? string.Empty,
                    Name = "Quick Notes",
                    Type = HierarchyNodeType.Notebook,
                    Children = ReadChildren(element),
                };
                break;

            case "OpenSections":
                foreach (HierarchyNode? child in element.Elements().SelectMany(ReadNode))
                {
                    yield return child;
                }

                break;
        }
    }

    private static HierarchyNode Build(XElement element, HierarchyNodeType type) => new()
    {
        Id = (string?)element.Attribute("ID") ?? string.Empty,
        Name = (string?)element.Attribute("name") ?? "(unnamed)",
        Type = type,
        PageLevel = type == HierarchyNodeType.Page && int.TryParse((string?)element.Attribute("pageLevel"), out int l) ? l : null,
        LastModified = (string?)element.Attribute("lastModifiedTime"),
        IsCurrentlyViewed = (string?)element.Attribute("isCurrentlyViewed") == "true" ? true : null,
        Children = ReadChildren(element),
    };

    private static List<HierarchyNode>? ReadChildren(XElement element)
    {
        List<HierarchyNode> children = [.. element.Elements().SelectMany(ReadNode)];
        return children.Count == 0 ? null : children;
    }

    public void Dispose()
    {
        // The RCW is apartment-affine, so release it on the thread that created it where possible.
        try
        {
            _sta.RunAsync(() => _handle.Dispose()).GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            _handle.Dispose();
        }
    }
}
