# Research: Session Management

**Branch**: `002-session-management` | **Date**: 2026-05-28

## Decision 1: Conversation History Strategy

**Decision**: Store conversation history as our own `ConversationTurn` DTO list (role + content + timestamp) serialised as JSON in Redis. On each request, deserialise the list, convert turns to `IEnumerable<Microsoft.Extensions.AI.ChatMessage>`, append the new user message, and call `AIAgent.RunAsync(IEnumerable<ChatMessage>, AgentSession, AgentRunOptions, CancellationToken)`.

**Rationale**: `AIAgent` (returned by `chatClient.AsAIAgent(...)`) exposes a `RunAsync` overload that accepts `IEnumerable<ChatMessage>`. Passing the full message list on every call gives the agent complete context without coupling our storage layer to the SDK's internal `AgentSession` object. `AgentSession` is passed as `null` each call — the framework creates a fresh one internally. Our own DTO is stable across SDK version upgrades.

**Alternatives considered**:
- Serialise `AgentSession` to Redis → rejected: `AgentSession` is an opaque SDK object; JSON serialisability is not guaranteed and would couple us to internal SDK state format.
- Keep the single-string `RunAsync(prompt, ct)` overload and prepend history as a prompt prefix → rejected: brittle, degrades token budget, loses role metadata.

---

## Decision 2: Conversation Store Interface

**Decision**: Introduce `IConversationStore` (in AgentHost) with `GetHistoryAsync`, `AppendTurnsAsync`, and `DeleteSessionAsync`. A `RedisConversationStore` implements it using `IDistributedCache`. The interface is registered as a singleton and injected into `AgentService`.

**Rationale**: Isolates the cache technology behind a testable abstraction. A null/in-memory implementation can replace it in unit tests without spinning up Redis.

**Alternatives considered**:
- Inject `IDistributedCache` directly into `AgentService` → rejected: spreads serialisation/key-management logic across the service layer.

---

## Decision 3: Redis Integration (Aspire)

**Decision**:
- AppHost: `Aspire.Hosting.Redis` package + `builder.AddRedis("redis")` resource
- AgentHost: `Aspire.StackExchange.Redis.DistributedCaching` package + `builder.AddRedisDistributedCache("redis")` registration
- Inject `IDistributedCache` into `RedisConversationStore`

**Rationale**: Aspire's Redis integration auto-wires health checks (via `WithHttpHealthCheck` on the resource) and OpenTelemetry traces for Redis operations via the transitive `OpenTelemetry.Instrumentation.StackExchangeRedis` dependency. No manual OTel wiring required.

**Alternatives considered**:
- Raw `StackExchange.Redis` `IConnectionMultiplexer` → rejected: more boilerplate; misses Aspire health check and OTel auto-wiring.
- `IMemoryCache` (in-process) → rejected: not shared across replicas; violates the spec's requirement for a durable store.

---

## Decision 4: Session ID Format

**Decision**: Server-generated `Guid` rendered as a lowercase hyphenated string (e.g., `"3f2504e0-4f89-11d3-9a0c-0305e82c3301"`). Client-supplied IDs that match a live session are accepted; unknown IDs start a new session.

**Rationale**: `Guid.NewGuid()` is globally unique with no coordination required. Opaque to the caller per the spec's assumption. Sufficient entropy to prevent guessing.

**Alternatives considered**:
- ULID (sortable) → acceptable but adds a dependency; `Guid` requires no additional package.
- Incrementing integer → rejected: guessable; violates opaque-token assumption.

---

## Decision 5: Redis Key Schema and TTL

**Decision**:
- Key: `nexusops:session:{sessionId}` (namespaced to avoid collisions with future Redis use)
- Value: JSON-serialised `List<ConversationTurn>` using `System.Text.Json`
- TTL: Sliding 30 minutes (renewed via `DistributedCacheEntryOptions.SlidingExpiration` on every read-triggered write)

**Rationale**: Sliding expiration implements the inactivity-window spec requirement automatically. JSON is human-readable during debugging. Namespacing allows safe Redis sharing with future features (e.g., rate limiting counters).

**Alternatives considered**:
- Redis `EXPIRE` via raw client → same semantics; `IDistributedCache` achieves this transparently.
- MessagePack binary serialisation → reduces payload size but adds a dependency; at ≤20 turns of text, size is negligible.

---

## Decision 6: History Trimming

**Decision**: After appending new turns, if the stored list exceeds the configured maximum (default 20 turns), drop the oldest turns from the front of the list before saving.

**Rationale**: Simple oldest-first eviction matches the spec (FR-008) and keeps the hot path (load → trim → save) entirely in-process with no additional Redis round-trips.

---

## Decision 7: Graceful Degradation on Store Failure

**Decision**: All `IConversationStore` calls are wrapped in `try/catch`. On exception, `GetHistoryAsync` returns an empty list (stateless fallback), and `AppendTurnsAsync` is a no-op. The chat request proceeds and returns a response. An `ActivityEvent` or log at `Warning` level records the degradation.

**Rationale**: Matches FR-010 and SC-005. The agent always returns a useful response; the operator may not notice the degradation for a single request.

---

## Resolved Unknowns

| Unknown | Resolution |
|---------|-----------|
| `AIAgent.RunAsync` history signature | `RunAsync(IEnumerable<ChatMessage>, AgentSession, AgentRunOptions, CancellationToken)` — pass full message list; `AgentSession` = null |
| Aspire Redis AppHost package | `Aspire.Hosting.Redis` v13.2.2 |
| Aspire Redis client package | `Aspire.StackExchange.Redis.DistributedCaching` v13.2.1 |
| OTel auto-wiring for Redis | Confirmed — transitive `OpenTelemetry.Instrumentation.StackExchangeRedis` auto-registered |
| Session ID format | `Guid` (lowercase hyphenated string) |
| Concurrent write strategy | Last-write-wins (see clarification Q5) |
| Rate limiting | Deferred to AuthN (feature #3) |
