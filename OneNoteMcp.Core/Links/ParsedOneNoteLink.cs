namespace OneNoteMcp.Core.Links;

public sealed class ParsedOneNoteLink
{
    public string? PageTitle { get; init; }

    public Guid? SectionId { get; init; }

    public Guid? PageId { get; init; }

    public string? SectionFileName { get; init; }
}
