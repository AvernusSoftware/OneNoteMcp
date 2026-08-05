using System.Text.Json.Serialization;

namespace OneNoteMcp.Core.Models;

public sealed class SearchHit
{
    [JsonPropertyName("page_id")]
    public string PageId { get; init; }

    [JsonPropertyName("title")]
    public string Title { get; init; }

    [JsonPropertyName("section_name")]
    public string? SectionName { get; init; }

    [JsonPropertyName("notebook_name")]
    public string? NotebookName { get; init; }

    [JsonPropertyName("last_modified")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LastModified { get; init; }

    [JsonPropertyName("snippet")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Snippet { get; init; }
}
