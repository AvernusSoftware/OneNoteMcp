using System.Text.Json.Serialization;

namespace OneNoteMcp.Core.Models;

public sealed class HierarchyNode
{
    [JsonPropertyName("id")]
    public string Id { get; init; }

    [JsonPropertyName("name")]
    public string Name { get; init; }

    [JsonPropertyName("type")]
    public HierarchyNodeType Type { get; init; }

    [JsonPropertyName("page_level")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? PageLevel { get; init; }

    [JsonPropertyName("last_modified")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LastModified { get; init; }

    [JsonPropertyName("is_currently_viewed")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? IsCurrentlyViewed { get; init; }

    [JsonPropertyName("children")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<HierarchyNode>? Children { get; init; }
}
