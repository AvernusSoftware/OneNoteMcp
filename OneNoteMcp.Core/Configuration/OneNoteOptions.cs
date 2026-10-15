namespace OneNoteMcp.Core.Configuration;

public sealed class OneNoteOptions
{
    public const string SectionName = "OneNote";

    public const int MaxIdleReleaseSeconds = 4294967;

    public int IdleReleaseSeconds { get; set; } = 10;

    public void Validate()
    {
        if (IdleReleaseSeconds is < 0 or > MaxIdleReleaseSeconds)
        {
            throw new InvalidOperationException($"OneNote:IdleReleaseSeconds must be between 0 and {MaxIdleReleaseSeconds} (0 releases OneNote right after each operation).");
        }
    }
}
