# Tasks: Session Management

**Input**: Design documents from `specs/002-session-management/`

**Prerequisites**: plan.md ✅ | spec.md ✅ | research.md ✅ | data-model.md ✅ | contracts/chat-api.md ✅

**Tests**: Not explicitly requested in spec. No test tasks generated.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story. US2 (Session Minting) is ordered before US1 (Multi-Turn Continuity) because the spec identifies it as a prerequisite.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: Which user story this task belongs to (US1–US4 matching spec.md)
- All paths are project-relative from repo root

---

## Phase 1: Setup (NuGet Packages)

**Purpose**: Add required package references before any code changes.

- [ ] T001 Add `Aspire.Hosting.Redis` package reference to `NexusOps.AppHost/NexusOps.AppHost.csproj`
- [ ] T002 [P] Add `Aspire.StackExchange.Redis.DistributedCaching` package reference to `NexusOps.AgentHost/NexusOps.AgentHost.csproj`

**Checkpoint**: `dotnet restore` succeeds across the solution.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core abstractions and infrastructure that ALL user stories depend on. No user story work begins until this phase is complete.

**⚠️ CRITICAL**: Phases 3–6 cannot start until this phase is complete.

- [ ] T003 Add Redis resource to `NexusOps.AppHost/AppHost.cs` — `builder.AddRedis("redis")` with `.WithDataVolume()`, and chain `.WithReference(redis).WaitFor(redis)` onto the `agentHost` builder
- [ ] T004 [P] Create `NexusOps.AgentHost/Services/ConversationTurn.cs` — `record ConversationTurn(string Role, string Content, DateTimeOffset Timestamp)` with `"user"` and `"assistant"` as the only valid role values
- [ ] T005 [P] Create `NexusOps.AgentHost/Configuration/SessionOptions.cs` — class with `int MaxTurns` (default 20) and `int SlidingExpirationMinutes` (default 30); bind from `"Session"` config section
- [ ] T006 [P] Create `NexusOps.AgentHost/Services/IConversationStore.cs` — interface with three methods: `GetHistoryAsync(string sessionId, CancellationToken)`, `AppendTurnsAsync(string sessionId, IReadOnlyList<ConversationTurn> newTurns, SessionOptions options, CancellationToken)`, `DeleteSessionAsync(string sessionId, CancellationToken)`
- [ ] T007 Add `Session` configuration section to `NexusOps.AgentHost/appsettings.json` with defaults `MaxTurns: 20` and `SlidingExpirationMinutes: 30`
- [ ] T008 Register dependencies in `NexusOps.AgentHost/Program.cs` — `builder.AddRedisDistributedCache("redis")`, `builder.Services.Configure<SessionOptions>(...)`, `builder.Services.AddSingleton<IConversationStore, RedisConversationStore>()` (forward reference; `RedisConversationStore` stub is created in T008a and replaced in T014)
- [ ] T008a Create a compile-only stub `NexusOps.AgentHost/Services/RedisConversationStore.cs` implementing `IConversationStore` with all three methods throwing `NotImplementedException`; add a code comment `// Stub — replaced by full implementation in T014`

**Checkpoint**: Solution builds cleanly. DI registration compiles; stub satisfies the type reference.

---

## Phase 3: User Story 2 — New Session Minting (Priority: P1) 🎯 First Independent Slice

**Goal**: Every chat request returns a session ID. Callers that omit `sessionId` receive a freshly minted one; callers that supply one receive it echoed back. Agent still runs statelessly (no history loaded yet — that comes in Phase 4).

**Independent Test**: `POST /api/chat` with no `sessionId` → response contains a non-empty string `sessionId`. A second call using that `sessionId` also works and echoes it back.

- [ ] T009 [US2] Update `ChatRequest` record in `NexusOps.AgentHost/Endpoints/ChatEndpoints.cs` — add `string? SessionId` property
- [ ] T010 [US2] Update `ChatResponse` record in `NexusOps.AgentHost/Endpoints/ChatEndpoints.cs` — add `string SessionId` property
- [ ] T011 [US2] Update `IAgentService` in `NexusOps.AgentHost/Services/IAgentService.cs` — change `SendAsync` signature to `Task<(string Response, string SessionId)> SendAsync(string prompt, string? sessionId, CancellationToken cancellationToken = default)`
- [ ] T012 [US2] Update `AgentService.SendAsync` in `NexusOps.AgentHost/Services/AgentService.cs` — if `sessionId` is null or empty generate `Guid.NewGuid().ToString()`; call `_agent.RunAsync` with a single-message list containing the user prompt (no history yet); return `(responseText, sessionId)`
- [ ] T013 [US2] Update the `POST /api/chat` handler in `NexusOps.AgentHost/Endpoints/ChatEndpoints.cs` — pass `request.SessionId` to `agentService.SendAsync`, destructure the returned tuple, and include `sessionId` in the `ChatResponse`

**Checkpoint**: `POST /api/chat {}` (no sessionId) returns `{ "response": "...", "sessionId": "<guid>" }`. Supplying the returned ID in a follow-up also works and echoes it back.

---

## Phase 4: User Story 1 — Multi-Turn Conversation Continuity (Priority: P1)

**Goal**: Conversation history is persisted to Redis and loaded on each request. The agent receives the full prior context alongside the new user message, enabling it to resolve follow-up references.

**Independent Test**: Send turn 1 ("Show me delayed orders"), capture `sessionId`. Send turn 2 ("What's the status of the second one?") with that `sessionId`. The response correctly resolves "the second one" without repeating the order ID.

- [ ] T014 [US1] Replace the stub in `NexusOps.AgentHost/Services/RedisConversationStore.cs` with the full implementation — inject `IDistributedCache` and `IOptions<SessionOptions>`; Redis key pattern `nexusops:session:{sessionId}`; serialise/deserialise `List<ConversationTurn>` with `System.Text.Json`
- [ ] T015 [US1] Implement `RedisConversationStore.GetHistoryAsync` — call `cache.GetStringAsync(key, ct)`; deserialise JSON to `List<ConversationTurn>`; return empty list on cache miss or any exception (graceful degradation per FR-010)
- [ ] T016 [US1] Implement `RedisConversationStore.AppendTurnsAsync` — load current list, append `newTurns`, serialise, save with `DistributedCacheEntryOptions { SlidingExpiration = TimeSpan.FromMinutes(options.SlidingExpirationMinutes) }`; wrap entirely in try/catch and suppress exceptions (graceful degradation)
- [ ] T017 [US1] Update `AgentService.SendAsync` in `NexusOps.AgentHost/Services/AgentService.cs` — inject `IConversationStore` and `IOptions<SessionOptions>`; load history via `GetHistoryAsync`; build `List<ChatMessage>` from stored turns (map `"user"` → `ChatRole.User`, `"assistant"` → `ChatRole.Assistant`) then append the new user `ChatMessage`; wrap `_agent.RunAsync(messages, session: null, options: null, ct)` in try/catch: on success call `AppendTurnsAsync` with both the new user turn and the assistant turn; on exception call `AppendTurnsAsync` with only the user turn (no assistant turn) then re-throw — ensuring the user's message is always persisted even when the agent fails (spec edge case); return `(responseText, sessionId)` on success

**Checkpoint**: Two-turn conversation works end-to-end. Second response demonstrates context from first turn.

---

## Phase 5: User Story 3 — Session Expiry and Isolation (Priority: P2)

**Goal**: Sessions expire after inactivity and are permanently deleted from Redis. Expired or unknown session IDs start a fresh session rather than erroring. Two concurrent sessions have no visibility into each other's history.

**Independent Test**: Submit a request with a random unknown GUID as `sessionId` → response starts a fresh session and returns a new `sessionId`. Two parallel sessions each return context from only their own history.

- [ ] T018 [US3] Verify sliding expiration in `RedisConversationStore.AppendTurnsAsync` in `NexusOps.AgentHost/Services/RedisConversationStore.cs` — confirm `DistributedCacheEntryOptions.SlidingExpiration` is set (not `AbsoluteExpiration`); each save call renews the TTL so the inactivity window resets on every write (per acceptance scenario 3 of US3)
- [ ] T019 [US3] Add FR-007 handling in `AgentService.SendAsync` in `NexusOps.AgentHost/Services/AgentService.cs` — when a caller-supplied `sessionId` returns an empty history from `GetHistoryAsync` (miss = expired or unknown), mint a new `sessionId` and proceed; never return an error to the caller for an unrecognised session ID
- [ ] T020 [US3] Implement `RedisConversationStore.DeleteSessionAsync` in `NexusOps.AgentHost/Services/RedisConversationStore.cs` — call `cache.RemoveAsync(key, ct)`; wrap in try/catch and suppress exceptions

**Checkpoint**: Calling `POST /api/chat` with an unrecognised session ID returns 200 with a fresh `sessionId`. No 4xx or 5xx from expired sessions.

---

## Phase 6: User Story 4 — Bounded History for Long Conversations (Priority: P3)

**Goal**: Sessions with more than `MaxTurns` stored turns drop the oldest turns before saving, ensuring the agent never receives a history that exceeds its context capacity.

**Independent Test**: Configure `MaxTurns: 4` for the test. Send 3 turns. Send a 4th — list stays at 4. Send a 5th — oldest turn is dropped; list is still 4. Agent invocation succeeds.

- [ ] T021 [US4] Add trimming logic to `RedisConversationStore.AppendTurnsAsync` in `NexusOps.AgentHost/Services/RedisConversationStore.cs` — after appending new turns, if `turns.Count > options.MaxTurns` remove turns from the front of the list (`turns.RemoveRange(0, turns.Count - options.MaxTurns)`) before serialising and saving

**Checkpoint**: After 5 turns with `MaxTurns: 4`, the stored list contains exactly 4 turns (oldest dropped). Agent invocation does not fail.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Observability, configuration validation, and end-to-end smoke test.

- [ ] T022 [P] Add structured lifecycle logging in `NexusOps.AgentHost/Services/AgentService.cs` — inject `ILogger<AgentService>`; emit `LogInformation` (or `LogWarning` for degraded) at: session created, history loaded (include turn count), history saved (include turn count), store unavailable/degraded (include exception message) — satisfies FR-012
- [ ] T023 [P] Add `RedisConversationStore.DeleteSessionAsync` — verify it is wired and callable (already implemented in T020; confirm it is accessible via the `IConversationStore` interface and registered in DI)
- [ ] T024 Update the OpenAPI description on the `POST /api/chat` endpoint in `NexusOps.AgentHost/Endpoints/ChatEndpoints.cs` — update `WithDescription` to document the `sessionId` field and session lifecycle behaviour
- [ ] T025 Run end-to-end smoke test via Aspire — start all services; send a two-turn `POST /api/chat` sequence; confirm Aspire dashboard shows Redis GET/SET spans as child spans of the chat request trace; confirm `session.created` and `session.history_loaded` log lines appear in AgentHost structured logs

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: No dependencies — start immediately
- **Phase 2 (Foundational)**: Depends on Phase 1 — **BLOCKS Phases 3–7**
- **Phase 3 (US2)**: Depends on Phase 2
- **Phase 4 (US1)**: Depends on Phase 3 (RedisConversationStore is wired into the service updated in Phase 3)
- **Phase 5 (US3)**: Depends on Phase 4 (expiry behaviour is in the store implemented in Phase 4)
- **Phase 6 (US4)**: Depends on Phase 4 (trimming is inside `AppendTurnsAsync` from Phase 4)
- **Phase 7 (Polish)**: Depends on Phases 4–6

### User Story Dependencies

- **US2 (P1)**: Can start after Phase 2 — no dependency on other user stories
- **US1 (P1)**: Depends on US2 (Phase 3) — builds on the endpoint contract and session ID generation established there
- **US3 (P2)**: Depends on US1 (Phase 4) — expiry wiring and FR-007 are inside components from Phase 4
- **US4 (P3)**: Depends on US1 (Phase 4) — trimming is a modification to `AppendTurnsAsync`

### Within Each Phase

- Tasks marked `[P]` within a phase can run in parallel
- T004, T005, T006 in Phase 2 are all parallel (different files)
- T009, T010 in Phase 3 are parallel (both are record modifications but independent fields)

### Parallel Opportunities

```bash
# Phase 1 — both tasks touch different files:
T001  NexusOps.AppHost/NexusOps.AppHost.csproj
T002  NexusOps.AgentHost/NexusOps.AgentHost.csproj

# Phase 2 — T004, T005, T006 are independent new files:
T004  NexusOps.AgentHost/Services/ConversationTurn.cs
T005  NexusOps.AgentHost/Configuration/SessionOptions.cs
T006  NexusOps.AgentHost/Services/IConversationStore.cs

# Phase 7 — T022 and T023 touch different files:
T022  NexusOps.AgentHost/Services/AgentService.cs
T023  (verification only)
```

---

## Implementation Strategy

### MVP (US2 + US1 — Phases 1–4)

1. Complete Phase 1: NuGet packages
2. Complete Phase 2: Foundational abstractions
3. Complete Phase 3 (US2): Endpoint emits session IDs → **demo: every response has a session ID**
4. Complete Phase 4 (US1): Full history threading → **demo: follow-up questions work**
5. **STOP and VALIDATE**: Two-turn conversation produces contextually correct second response

### Incremental Delivery

1. Phases 1–3 → US2 working → callers can begin integrating the session ID field
2. Phase 4 → US1 working → multi-turn conversations work end-to-end
3. Phase 5 → US3 working → sessions expire gracefully; unknown IDs handled
4. Phase 6 → US4 working → long conversations don't break the agent
5. Phase 7 → observability complete; feature shippable

---

## Notes

- `[P]` tasks = touch different files with no dependency on incomplete tasks in the same phase
- `[Story]` label maps each task to a user story for traceability
- T008 registers `RedisConversationStore` before it is created (Phase 4). If a build-clean pass is needed between phases, add a temporary stub `RedisConversationStore` after T008 and remove it in T014.
- The `AgentSession session: null` argument to `RunAsync` in T017 is intentional — we manage history externally; the SDK creates a fresh internal session object on each call.
- Sliding expiration (T018) means every successful write renews the 30-minute TTL. A session stays alive as long as there is activity.
