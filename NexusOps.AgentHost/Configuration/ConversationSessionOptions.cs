using System.ComponentModel.DataAnnotations;

namespace NexusOps.AgentHost.Configuration;

public sealed class ConversationSessionOptions
{
    public const string SectionName = "Session";

    /// <summary>Maximum number of individual turns (user + assistant messages) retained per session.</summary>
    [Range(1, int.MaxValue, ErrorMessage = "Session:MaxTurns must be a positive integer.")]
    public int MaxTurns { get; set; } = 20;

    /// <summary>
    /// Sliding inactivity expiry window in minutes.
    /// </summary>
    /// <remarks>
    /// Must be positive. <c>DistributedCacheEntryOptions.SlidingExpiration</c> rejects a non-positive
    /// <see cref="TimeSpan"/>, so a zero or negative value here made every write throw — inside the
    /// store's catch block, where it was swallowed and logged as a Redis connection failure. Sessions
    /// silently stopped persisting and the logs blamed the wrong component. Validated at startup so
    /// the application refuses to run in that state.
    /// </remarks>
    [Range(1, int.MaxValue, ErrorMessage = "Session:SlidingExpirationMinutes must be a positive integer.")]
    public int SlidingExpirationMinutes { get; set; } = 30;
}
