using System.Text.Json.Serialization;

namespace OneNoteMcp.Core.Models;

public sealed class AttachmentResult
{
    [JsonPropertyName("page_id")]
    public required string PageId { get; init; }

    [JsonPropertyName("page_status")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PageStatus { get; init; }

    [JsonPropertyName("page_level")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? PageLevel { get; init; }

    [JsonPropertyName("block_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BlockId { get; init; }

    [JsonPropertyName("file_name")]
    public required string FileName { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; }
}
