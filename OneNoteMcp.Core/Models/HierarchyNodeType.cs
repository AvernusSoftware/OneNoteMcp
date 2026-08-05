using System.Text.Json.Serialization;

namespace OneNoteMcp.Core.Models;

[JsonConverter(typeof(JsonStringEnumConverter<HierarchyNodeType>))]
public enum HierarchyNodeType
{
    Notebook,
    SectionGroup,
    Section,
    Page,
}
