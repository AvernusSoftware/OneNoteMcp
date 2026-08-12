using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using OneNoteMcp.Core.Exceptions;
using OneNoteMcp.Core.Interop;
using OneNoteMcp.Core.Models;
using OneNoteMcp.Core.Services;
using System.ComponentModel;

namespace OneNoteMcp.Server.Tools;

[McpServerToolType]
public sealed class OneNoteTools
{
    private readonly IOneNoteService _oneNote;
    private readonly ILogger<OneNoteTools> _logger;

    public OneNoteTools(IOneNoteService oneNote, ILogger<OneNoteTools> logger)
    {
        _oneNote = oneNote;
        _logger = logger;
    }

    [McpServerTool(Name = "get_hierarchy")]
    [Description(
        "Lists the OneNote notebook structure as a tree of ids and names. Use scope 'Notebooks' " +
        "for just notebooks, 'Sections' for notebooks and their sections, or 'Pages' for the full " +
        "tree down to individual pages. Page ids returned here are what get_page_content expects.")]
    public async Task<HierarchyNode[]> GetHierarchyAsync([Description("Depth of the tree: 'Notebooks', 'Sections' or 'Pages'. Defaults to 'Sections'.")] string scope = "Sections", [Description("Optional id of a notebook, section group or section to start from. Omit for everything.")] string? startNodeId = null, CancellationToken cancellationToken = default)
    {
        HierarchyScope parsed = scope?.Trim().ToLowerInvariant() switch
        {
            "notebooks" or "notebook" => HierarchyScope.Notebooks,
            "sections" or "section" or null or "" => HierarchyScope.Sections,
            "pages" or "page" => HierarchyScope.Pages,
            "children" => HierarchyScope.Children,
            "self" => HierarchyScope.Self,
            _ => throw new McpException(
                $"Unknown scope '{scope}'. Use 'Notebooks', 'Sections' or 'Pages'."),
        };

        return await ExecuteAsync(
            nameof(GetHierarchyAsync),
            () => _oneNote.GetHierarchyAsync(parsed, string.IsNullOrWhiteSpace(startNodeId) ? null : startNodeId, cancellationToken));
    }

    [McpServerTool(Name = "get_current_page")]
    [Description(
        "Returns the page currently open in the OneNote desktop window, including its id, title, " +
        "section and notebook. If no page is open the 'note' field explains what to do instead.")]
    public Task<CurrentPageInfo> GetCurrentPageAsync(CancellationToken cancellationToken = default) =>
        ExecuteAsync(nameof(GetCurrentPageAsync), () => _oneNote.GetCurrentPageAsync(cancellationToken));

    [McpServerTool(Name = "get_page_content")]
    [Description(
        "Reads a OneNote page and returns it as Markdown, with a YAML front matter block carrying " +
        "the page id, title and timestamps. Get page ids from get_hierarchy, search_notes or " +
        "get_current_page. Content this server wrote earlier is preceded by an " +
        "'<!-- ai-block: ID -->' comment; that ID is what update_block and delete_block accept. " +
        "Anything with no such marker was written by the user and cannot be changed or removed.")]
    public Task<string> GetPageContentAsync([Description("The OneNote page id, e.g. '{GUID}{1}{B0}'.")] string pageId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(pageId))
        {
            throw new McpException("pageId is required.");
        }

        return ExecuteAsync(
            nameof(GetPageContentAsync),
            () => _oneNote.GetPageContentAsMarkdownAsync(pageId, cancellationToken));
    }

    [McpServerTool(Name = "search_notes")]
    [Description(
        "Full-text search across all OneNote notebooks, returning matching pages with their id, " +
        "title, location and a short excerpt. Accepts the same query syntax as the OneNote search " +
        "box, including uppercase AND / OR. Does not disturb the OneNote UI.")]
    public Task<SearchHit[]> SearchNotesAsync([Description("The search query.")] string query, [Description("Maximum number of pages to return (1-100). Defaults to 20.")] int maxResults = 20, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            throw new McpException("query is required.");
        }

        return ExecuteAsync(
            nameof(SearchNotesAsync),
            () => _oneNote.SearchAsync(query, maxResults, cancellationToken));
    }

    [McpServerTool(Name = "get_page_by_link")]
    [Description(
        "Reads a OneNote page straight from a pasted link, in one call - use this instead of " +
        "get_hierarchy/search_notes when the user gives you a link rather than asking you to find " +
        "something. Accepts either a 'onenote:' URI (from OneNote's 'Copy Link to Page') or a " +
        "SharePoint 'Doc.aspx' share link (the kind with a 'wd=target(...)' parameter) - paste the " +
        "link exactly as given, surrounding text is fine. Returns the same Markdown as " +
        "get_page_content, with an extra leading comment noting how the page was found. Falls back " +
        "to full-text search if the page isn't in an already-loaded notebook.")]
    public Task<string> GetPageByLinkAsync([Description("The OneNote or SharePoint page link, pasted as-is.")] string link, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(link))
        {
            throw new McpException("link is required.");
        }

        return ExecuteAsync(
            nameof(GetPageByLinkAsync),
            () => _oneNote.GetPageContentByLinkAsync(link, cancellationToken));
    }

    [McpServerTool(Name = "create_page")]
    [Description(
        "Creates a NEW page in the given section from Markdown. Supports headings, bold/italic/" +
        "strikethrough, inline code, links, nested bullet and numbered lists, to-do checkboxes " +
        "(- [ ] / - [x]), code blocks and tables. Get section ids from get_hierarchy with scope " +
        "'Sections'. This never modifies an existing page. Pass parentPageId to nest the new page as " +
        "a subpage of an existing page in the same section (or a sub-subpage, if that page is itself " +
        "a subpage - OneNote allows at most two levels of nesting); omit it for an ordinary top-level " +
        "page. Note that OneNote has no code-block element, so a fenced block becomes monospace lines " +
        "in OneNote's Code style and the language tag is dropped; everything else round-trips.")]
    public Task<CreatePageResult> CreatePageAsync([Description("Id of the section to create the page in.")] string sectionId, [Description("Title of the new page.")] string title, [Description("Page body as Markdown.")] string contentMarkdown, [Description("Optional id of an existing page in the same section to nest the new page under, making it a subpage. Omit for a top-level page.")] string? parentPageId = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sectionId))
        {
            throw new McpException("sectionId is required. Call get_hierarchy with scope 'Sections' to find one.");
        }

        return ExecuteAsync(
            nameof(CreatePageAsync),
            () => _oneNote.CreatePageAsync(sectionId, title ?? string.Empty, contentMarkdown ?? string.Empty, string.IsNullOrWhiteSpace(parentPageId) ? null : parentPageId, cancellationToken));
    }

    [McpServerTool(Name = "append_to_page")]
    [Description(
        "Adds a new block of Markdown to the end of an existing page. Nothing already on the page " +
        "is touched, so this is the safe way to contribute to a page the user wrote. Returns the " +
        "new block's id, which update_block and delete_block accept. Supports the same Markdown as " +
        "create_page.")]
    public Task<BlockResult> AppendToPageAsync([Description("Id of the page to add the block to.")] string pageId, [Description("Block content as Markdown.")] string contentMarkdown, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(pageId))
        {
            throw new McpException("pageId is required.");
        }

        return ExecuteAsync(
            nameof(AppendToPageAsync),
            () => _oneNote.AppendBlockAsync(pageId, contentMarkdown ?? string.Empty, cancellationToken));
    }

    [McpServerTool(Name = "update_block")]
    [Description(
        "Replaces the entire body of a block that THIS server wrote, identified by the id in its " +
        "'<!-- ai-block: ID -->' marker from get_page_content. Fails if the block was written by " +
        "the user, or if the user has typed into it since - in that case add a new block with " +
        "append_to_page instead. Read the page first: block ids change as the page is edited.")]
    public Task<BlockResult> UpdateBlockAsync([Description("Id of the page holding the block.")] string pageId, [Description("Block id from an 'ai-block' marker in get_page_content.")] string blockId, [Description("Replacement content as Markdown.")] string contentMarkdown, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(pageId))
        {
            throw new McpException("pageId is required.");
        }

        if (string.IsNullOrWhiteSpace(blockId))
        {
            throw new McpException(
                "blockId is required. Call get_page_content and use an id from an " +
                "'<!-- ai-block: ID -->' marker.");
        }

        return ExecuteAsync(
            nameof(UpdateBlockAsync),
            () => _oneNote.UpdateBlockAsync(pageId, blockId, contentMarkdown ?? string.Empty, cancellationToken));
    }

    [McpServerTool(Name = "delete_block")]
    [Description(
        "Removes a block that THIS server wrote, identified by the id in its " +
        "'<!-- ai-block: ID -->' marker from get_page_content. Fails if the block was written by " +
        "the user, or if the user has typed into it since. Cannot delete a page - only a block it " +
        "authored.")]
    public Task<BlockResult> DeleteBlockAsync([Description("Id of the page holding the block.")] string pageId, [Description("Block id from an 'ai-block' marker in get_page_content.")] string blockId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(pageId))
        {
            throw new McpException("pageId is required.");
        }

        if (string.IsNullOrWhiteSpace(blockId))
        {
            throw new McpException(
                "blockId is required. Call get_page_content and use an id from an " +
                "'<!-- ai-block: ID -->' marker.");
        }

        return ExecuteAsync(
            nameof(DeleteBlockAsync),
            () => _oneNote.DeleteBlockAsync(pageId, blockId, cancellationToken));
    }

    private async Task<T> ExecuteAsync<T>(string operation, Func<Task<T>> body)
    {
        try
        {
            return await body().ConfigureAwait(false);
        }
        catch (OneNoteUnavailableException ex)
        {
            _logger.LogError(ex, "{Operation}: OneNote unavailable", operation);
            throw new McpException(ex.Message);
        }
        catch (OneNoteException ex)
        {
            _logger.LogError(ex, "{Operation}: OneNote call failed", operation);
            throw new McpException($"OneNote reported an error: {ex.Message}");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{Operation}: unexpected failure", operation);
            throw new McpException($"{operation} failed: {ex.Message}");
        }
    }
}
