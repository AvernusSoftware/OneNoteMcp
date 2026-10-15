namespace OneNoteMcp.Core.Configuration;

public sealed class OneNoteOptions
{
    public const string SectionName = "OneNote";

    public int IdleReleaseSeconds { get; set; } = 10;

    public void Validate()
    {
        if (IdleReleaseSeconds < 0)
        {
            throw new InvalidOperationException("OneNote:IdleReleaseSeconds must be 0 or greater (0 releases OneNote right after each operation).");
        }
    }
}
