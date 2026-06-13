namespace NexusOps.AgentHost.Configuration;

public sealed class ConversationSessionOptions
{
    public const string SectionName = "Session";

    /// <summary>Maximum number of individual turns (user + assistant messages) retained per session.</summary>
    public int MaxTurns { get; set; } = 20;

    /// <summary>Sliding inactivity expiry window in minutes.</summary>
    public int SlidingExpirationMinutes { get; set; } = 30;
}
