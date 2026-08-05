namespace OneNoteMcp.Core.Configuration;

public sealed class AgentOptions
{
    public const string SectionName = "Agent";

    public string DisplayName { get; set; } = "OneNoteMcp Agent";

    public string Initials { get; set; } = "AI_MCP";

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(DisplayName))
        {
            throw new InvalidOperationException(
                "Agent:DisplayName must not be empty - it is what distinguishes blocks written by " +
                "this server from your own notes, and an empty value would make every unattributed " +
                "block look like the agent's.");
        }

        if (string.IsNullOrWhiteSpace(Initials))
        {
            throw new InvalidOperationException("Agent:Initials must not be empty.");
        }
    }
}
