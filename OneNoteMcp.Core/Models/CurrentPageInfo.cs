using System.Text.Json.Serialization;

namespace OneNoteMcp.Core.Models;

public sealed class CurrentPageInfo
{
    [JsonPropertyName("page_id")]
    public string? PageId { get; init; }

    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("section_id")]
    public string? SectionId { get; init; }

    [JsonPropertyName("section_name")]
    public string? SectionName { get; init; }

    [JsonPropertyName("notebook_id")]
    public string? NotebookId { get; init; }

    [JsonPropertyName("notebook_name")]
    public string? NotebookName { get; init; }

    [JsonPropertyName("note")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Note { get; init; }
}
