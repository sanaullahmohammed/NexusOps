namespace NexusOps.AgentHost.Services;

/// <summary>What happened when conversation history was requested.</summary>
public enum HistoryOutcome
{
    /// <summary>The store was reached and holds this session.</summary>
    Found,

    /// <summary>The store was reached and holds no such session — it expired, or never existed.</summary>
    Missing,

    /// <summary>
    /// The store could not be reached or read. This says nothing about whether the session exists,
    /// which is precisely why it must not be confused with <see cref="Missing"/>.
    /// </summary>
    Unavailable
}

/// <summary>
/// The result of a history retrieval.
/// </summary>
/// <remarks>
/// The store previously returned a bare turn list, so a cache miss and a store outage were
/// indistinguishable — both surfaced as an empty list. Since 002 FR-007 mints a fresh session
/// whenever a supplied ID yields no history, every request during a Redis outage was handed a new
/// session ID and the caller's conversation silently restarted. Separating the two outcomes is
/// what allows 003 FR-009 to mint on <see cref="HistoryOutcome.Missing"/> while preserving the
/// caller's identifier on <see cref="HistoryOutcome.Unavailable"/>.
/// </remarks>
public sealed record HistoryResult(IReadOnlyList<ConversationTurn> Turns, HistoryOutcome Outcome)
{
    public static HistoryResult Found(IReadOnlyList<ConversationTurn> turns) => new(turns, HistoryOutcome.Found);

    public static HistoryResult Missing() => new([], HistoryOutcome.Missing);

    public static HistoryResult Unavailable() => new([], HistoryOutcome.Unavailable);
}
