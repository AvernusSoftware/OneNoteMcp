using OneNoteMcp.Core.Interop;
using OneNoteMcp.Core.Models;

namespace OneNoteMcp.Core.Services;

public interface IOneNoteService
{
    Task<HierarchyNode[]> GetHierarchyAsync(HierarchyScope scope, string? startNodeId = null, CancellationToken cancellationToken = default);

    Task<CurrentPageInfo> GetCurrentPageAsync(CancellationToken cancellationToken = default);

    Task<string> GetPageContentAsMarkdownAsync(string pageId, CancellationToken cancellationToken = default);

    Task<SearchHit[]> SearchAsync(string query, int maxResults = 20, CancellationToken cancellationToken = default);

    Task<string> GetPageContentByLinkAsync(string link, CancellationToken cancellationToken = default);

    Task<CreatePageResult> CreatePageAsync(string sectionId, string title, string contentMarkdown, string? parentPageId = null, CancellationToken cancellationToken = default);

    Task<BlockResult> AppendBlockAsync(string pageId, string contentMarkdown, CancellationToken cancellationToken = default);

    Task<BlockResult> UpdateBlockAsync(string pageId, string blockId, string contentMarkdown, CancellationToken cancellationToken = default);

    Task<BlockResult> DeleteBlockAsync(string pageId, string blockId, CancellationToken cancellationToken = default);
}
