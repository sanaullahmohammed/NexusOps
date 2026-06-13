using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using NexusOps.AgentHost.Configuration;

namespace NexusOps.AgentHost.Services;

public sealed class RedisConversationStore(IDistributedCache cache, ILogger<RedisConversationStore> logger) : IConversationStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static string Key(string sessionId) => $"nexusops:session:{sessionId}";

    public async Task<IReadOnlyList<ConversationTurn>> GetHistoryAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        try
        {
            var json = await cache.GetStringAsync(Key(sessionId), cancellationToken);
            if (json is null)
                return [];

            return JsonSerializer.Deserialize<List<ConversationTurn>>(json, JsonOptions) ?? [];
        }
        catch (Exception ex)
        {
            LogDegraded(sessionId, ex, historyLoadedBeforeFailure: false, turnCountLoaded: 0);
            return [];
        }
    }

    public async Task AppendTurnsAsync(string sessionId, IReadOnlyList<ConversationTurn> newTurns, ConversationSessionOptions options, CancellationToken cancellationToken = default)
    {
        List<ConversationTurn>? loaded = null;
        try
        {
            var json = await cache.GetStringAsync(Key(sessionId), cancellationToken);
            loaded = json is null
                ? []
                : JsonSerializer.Deserialize<List<ConversationTurn>>(json, JsonOptions) ?? [];

            loaded.AddRange(newTurns);

            if (loaded.Count > options.MaxTurns)
                loaded.RemoveRange(0, loaded.Count - options.MaxTurns);

            var updated = JsonSerializer.Serialize(loaded, JsonOptions);
            await cache.SetStringAsync(Key(sessionId), updated, new DistributedCacheEntryOptions
            {
                SlidingExpiration = TimeSpan.FromMinutes(options.SlidingExpirationMinutes)
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            LogDegraded(sessionId, ex, historyLoadedBeforeFailure: loaded is not null, turnCountLoaded: loaded?.Count ?? 0);
        }
    }

    public async Task DeleteSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        try
        {
            await cache.RemoveAsync(Key(sessionId), cancellationToken);
        }
        catch (Exception ex)
        {
            LogDegraded(sessionId, ex, historyLoadedBeforeFailure: false, turnCountLoaded: 0);
        }
    }

    private static string SanitiseSessionIdPrefix(string sessionId, int maxLength = 8)
    {
        if (string.IsNullOrEmpty(sessionId))
            return string.Empty;

        var prefix = sessionId[..Math.Min(maxLength, sessionId.Length)];
        return prefix.Replace("\r", string.Empty).Replace("\n", string.Empty);
    }

    private void LogDegraded(string sessionId, Exception ex, bool historyLoadedBeforeFailure, int turnCountLoaded)
    {
        var errorCategory = ex switch
        {
            JsonException => "serialisation",
            OperationCanceledException => "timeout",
            _ when ex.GetType().Name.Contains("Timeout", StringComparison.OrdinalIgnoreCase) => "timeout",
            _ => "connection"
        };

        logger.LogWarning(
            "session.degraded {SessionIdPrefix} {ErrorCategory} {HistoryLoadedBeforeFailure} {TurnCountLoaded} {Timestamp}",
            SanitiseSessionIdPrefix(sessionId),
            errorCategory,
            historyLoadedBeforeFailure,
            turnCountLoaded,
            DateTimeOffset.UtcNow);
    }
}
