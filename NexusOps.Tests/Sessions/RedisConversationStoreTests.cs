using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NexusOps.AgentHost.Configuration;
using NexusOps.AgentHost.Services;

namespace NexusOps.Tests.Sessions;

/// <summary>
/// Covers the turn-trimming and graceful-degradation behaviour specified by
/// 002 FR-008 and FR-010, against an in-memory cache rather than a live Redis.
/// </summary>
public class RedisConversationStoreTests
{
    private static RedisConversationStore CreateStore(IDistributedCache cache) =>
        new(cache, NullLogger<RedisConversationStore>.Instance);

    private static IDistributedCache CreateCache() =>
        new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));

    private static ConversationSessionOptions Options_(int maxTurns = 20) =>
        new() { MaxTurns = maxTurns, SlidingExpirationMinutes = 30 };

    private static ConversationTurn Turn(string content, string role = "user") =>
        new(role, content, DateTimeOffset.UtcNow);

    [Fact]
    public async Task GetHistory_ForUnknownSession_ReturnsEmpty()
    {
        var store = CreateStore(CreateCache());

        var history = await store.GetHistoryAsync("does-not-exist");

        Assert.Empty(history);
    }

    [Fact]
    public async Task AppendTurns_RoundTripsContentAndOrder()
    {
        var store = CreateStore(CreateCache());

        await store.AppendTurnsAsync("s1", [Turn("first"), Turn("second", "assistant")], Options_());
        var history = await store.GetHistoryAsync("s1");

        Assert.Collection(history,
            t => { Assert.Equal("user", t.Role); Assert.Equal("first", t.Content); },
            t => { Assert.Equal("assistant", t.Role); Assert.Equal("second", t.Content); });
    }

    [Fact]
    public async Task AppendTurns_TrimsOldestFirst_WhenExceedingMaxTurns()
    {
        var store = CreateStore(CreateCache());
        var options = Options_(maxTurns: 4);

        for (var i = 1; i <= 6; i++)
        {
            await store.AppendTurnsAsync("s1", [Turn($"turn-{i}")], options);
        }

        var history = await store.GetHistoryAsync("s1");

        Assert.Equal(4, history.Count);
        Assert.Equal(["turn-3", "turn-4", "turn-5", "turn-6"], history.Select(t => t.Content));
    }

    [Fact]
    public async Task AppendTurns_TrimsInOnePass_WhenASingleAppendOverflows()
    {
        var store = CreateStore(CreateCache());
        var options = Options_(maxTurns: 3);

        await store.AppendTurnsAsync(
            "s1",
            [Turn("a"), Turn("b"), Turn("c"), Turn("d"), Turn("e")],
            options);

        var history = await store.GetHistoryAsync("s1");

        Assert.Equal(3, history.Count);
        Assert.Equal(["c", "d", "e"], history.Select(t => t.Content));
    }

    [Fact]
    public async Task AppendTurns_AtExactlyMaxTurns_DoesNotTrim()
    {
        var store = CreateStore(CreateCache());
        var options = Options_(maxTurns: 3);

        await store.AppendTurnsAsync("s1", [Turn("a"), Turn("b"), Turn("c")], options);

        var history = await store.GetHistoryAsync("s1");

        Assert.Equal(["a", "b", "c"], history.Select(t => t.Content));
    }

    [Fact]
    public async Task Sessions_AreIsolatedFromOneAnother()
    {
        var store = CreateStore(CreateCache());

        await store.AppendTurnsAsync("s1", [Turn("belongs to s1")], Options_());
        await store.AppendTurnsAsync("s2", [Turn("belongs to s2")], Options_());

        Assert.Equal("belongs to s1", (await store.GetHistoryAsync("s1")).Single().Content);
        Assert.Equal("belongs to s2", (await store.GetHistoryAsync("s2")).Single().Content);
    }

    [Fact]
    public async Task DeleteSession_RemovesHistory()
    {
        var store = CreateStore(CreateCache());
        await store.AppendTurnsAsync("s1", [Turn("a")], Options_());

        await store.DeleteSessionAsync("s1");

        Assert.Empty(await store.GetHistoryAsync("s1"));
    }

    [Fact]
    public async Task GetHistory_WhenStoreThrows_DoesNotPropagate()
    {
        var store = CreateStore(new ThrowingCache());

        var history = await store.GetHistoryAsync("s1");

        Assert.NotNull(history);
    }

    [Fact]
    public async Task AppendTurns_WhenStoreThrows_DoesNotPropagate()
    {
        var store = CreateStore(new ThrowingCache());

        await store.AppendTurnsAsync("s1", [Turn("a")], Options_());
    }

    /// <summary>An <see cref="IDistributedCache"/> that fails every operation, standing in for an outage.</summary>
    private sealed class ThrowingCache : IDistributedCache
    {
        private static InvalidOperationException Boom() => new("simulated store outage");

        public byte[]? Get(string key) => throw Boom();
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => throw Boom();
        public void Refresh(string key) => throw Boom();
        public Task RefreshAsync(string key, CancellationToken token = default) => throw Boom();
        public void Remove(string key) => throw Boom();
        public Task RemoveAsync(string key, CancellationToken token = default) => throw Boom();
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => throw Boom();
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) => throw Boom();
    }
}
