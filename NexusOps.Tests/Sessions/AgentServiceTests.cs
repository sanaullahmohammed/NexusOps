using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NexusOps.AgentHost.Configuration;
using NexusOps.AgentHost.Services;

namespace NexusOps.Tests.Sessions;

/// <summary>
/// Covers 003 FR-009 to FR-012: which session a request belongs to, and what survives a store outage.
/// </summary>
public class AgentServiceTests
{
    private const string CallerSessionId = "11111111-2222-3333-4444-555555555555";

    private static AgentService Create(FakeConversationStore store, FakeAgent? agent = null) =>
        new(agent ?? FakeAgent.Echoing(),
            store,
            Options.Create(new ConversationSessionOptions()),
            NullLogger<AgentService>.Instance);

    private static ConversationTurn Turn(string content, string role = "user") =>
        new(role, content, DateTimeOffset.UtcNow);

    // ---- FR-009: a store outage must not rotate the caller's session ----

    [Fact]
    public async Task WhenStoreIsUnavailable_TheCallersSessionIdIsPreserved()
    {
        var store = new FakeConversationStore { IsUnavailable = true };

        var (_, sessionId) = await Create(store).SendAsync("hello", CallerSessionId);

        Assert.Equal(CallerSessionId, sessionId);
    }

    [Fact]
    public async Task WhenStoreIsUnavailable_TheSessionIdIsStableAcrossManyRequests()
    {
        var store = new FakeConversationStore { IsUnavailable = true };
        var service = Create(store);

        var ids = new List<string>();
        for (var i = 0; i < 5; i++)
        {
            var (_, id) = await service.SendAsync($"message {i}", CallerSessionId);
            ids.Add(id);
        }

        Assert.All(ids, id => Assert.Equal(CallerSessionId, id));
    }

    [Fact]
    public async Task WhenStoreIsUnavailable_TheTurnIsProcessedWithoutHistory()
    {
        var store = new FakeConversationStore { IsUnavailable = true };
        store.Seed(CallerSessionId, Turn("an earlier turn"));
        var agent = FakeAgent.Echoing();

        await Create(store, agent).SendAsync("hello", CallerSessionId);

        // Only the new user message reaches the agent — history could not be read.
        Assert.Single(agent.LastMessages);
    }

    [Fact]
    public async Task WhenSessionIsGenuinelyMissing_ANewIdIsMinted()
    {
        var store = new FakeConversationStore();   // reachable, holds nothing

        var (_, sessionId) = await Create(store).SendAsync("hello", CallerSessionId);

        Assert.NotEqual(CallerSessionId, sessionId);
        Assert.True(Guid.TryParse(sessionId, out _));
    }

    [Fact]
    public async Task WhenSessionExists_TheIdAndHistoryAreBothPreserved()
    {
        var store = new FakeConversationStore();
        store.Seed(CallerSessionId, Turn("first"), Turn("second", "assistant"));
        var agent = FakeAgent.Echoing();

        var (_, sessionId) = await Create(store, agent).SendAsync("third", CallerSessionId);

        Assert.Equal(CallerSessionId, sessionId);
        Assert.Equal(3, agent.LastMessages.Count);   // two restored turns plus the new one
    }

    // ---- FR-010: a new session must not perform a guaranteed-miss read ----

    [Fact]
    public async Task WhenNoSessionIdIsSupplied_TheStoreIsNotQueried()
    {
        var store = new FakeConversationStore();

        var (_, sessionId) = await Create(store).SendAsync("hello", sessionId: null);

        Assert.Equal(0, store.GetCallCount);
        Assert.True(Guid.TryParse(sessionId, out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AnAbsentOrBlankSessionId_MintsWithoutReading(string? supplied)
    {
        var store = new FakeConversationStore();

        var (_, sessionId) = await Create(store).SendAsync("hello", supplied);

        Assert.Equal(0, store.GetCallCount);
        Assert.False(string.IsNullOrWhiteSpace(sessionId));
    }

    // ---- FR-011: the failure path must return the session the turn was written under ----

    [Fact]
    public async Task WhenTheAgentFails_TheExceptionCarriesTheSessionId()
    {
        var store = new FakeConversationStore();

        var ex = await Assert.ThrowsAsync<AgentInvocationException>(
            () => Create(store, FakeAgent.Failing()).SendAsync("hello", sessionId: null));

        Assert.False(string.IsNullOrWhiteSpace(ex.SessionId));
        Assert.True(Guid.TryParse(ex.SessionId, out _));
    }

    [Fact]
    public async Task WhenTheAgentFails_TheUserTurnIsReachableUnderTheReturnedSessionId()
    {
        var store = new FakeConversationStore();

        var ex = await Assert.ThrowsAsync<AgentInvocationException>(
            () => Create(store, FakeAgent.Failing()).SendAsync("the prompt worth keeping", sessionId: null));

        // 002 FR-005: the user turn is persisted. It is only useful if the caller learns where.
        var persisted = store.TurnsFor(ex.SessionId);
        Assert.Equal("the prompt worth keeping", Assert.Single(persisted).Content);
    }

    [Fact]
    public async Task WhenTheAgentFails_TheFailedAssistantTurnIsNotPersisted()
    {
        var store = new FakeConversationStore();

        var ex = await Assert.ThrowsAsync<AgentInvocationException>(
            () => Create(store, FakeAgent.Failing()).SendAsync("hello", sessionId: null));

        Assert.DoesNotContain(store.TurnsFor(ex.SessionId), t => t.Role == "assistant");
    }

    [Fact]
    public async Task WhenTheAgentFails_TheOriginalCauseIsPreserved()
    {
        var store = new FakeConversationStore();

        var ex = await Assert.ThrowsAsync<AgentInvocationException>(
            () => Create(store, FakeAgent.Failing("upstream exploded")).SendAsync("hello", sessionId: null));

        Assert.Equal("upstream exploded", ex.InnerException?.Message);
    }

    // ---- Happy path ----

    [Fact]
    public async Task OnSuccess_BothTurnsArePersistedUnderTheActiveSession()
    {
        var store = new FakeConversationStore();

        var (response, sessionId) = await Create(store, FakeAgent.Echoing("the answer")).SendAsync("the question", null);

        Assert.Equal("the answer", response);
        var turns = store.TurnsFor(sessionId);
        Assert.Equal(2, turns.Count);
        Assert.Equal("user", turns[0].Role);
        Assert.Equal("assistant", turns[1].Role);
    }
}
