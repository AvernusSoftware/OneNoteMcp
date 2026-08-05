using System.Text.Json.Serialization;

namespace OneNoteMcp.Core.Models;

public sealed class BlockResult
{
    [JsonPropertyName("page_id")]
    public string PageId { get; init; }

    [JsonPropertyName("block_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? BlockId { get; init; }

    [JsonPropertyName("status")]
    public string Status { get; init; }
}
