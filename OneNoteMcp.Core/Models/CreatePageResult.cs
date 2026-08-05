using System.Text.Json.Serialization;

namespace OneNoteMcp.Core.Models;

public sealed class CreatePageResult
{
    [JsonPropertyName("page_id")]
    public string PageId { get; init; }

    [JsonPropertyName("title")]
    public string Title { get; init; }

    [JsonPropertyName("section_id")]
    public string SectionId { get; init; }

    [JsonPropertyName("status")]
    public string Status { get; init; }
}
