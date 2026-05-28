# Feature Specification: Session Management

**Feature Branch**: `002-session-management`

**Created**: 2026-05-28

**Status**: Implemented

**Input**: User description: "Session management with Redis-backed conversation history on POST /api/chat — makes the agent truly conversational; client sends optional sessionId, server echoes it back or mints a new one; conversation history loaded from store, passed to agent, then saved back."

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Multi-Turn Conversation Continuity (Priority: P1)

An operator opens the NexusOps chat interface and asks "Show me all delayed orders." The agent lists several. The operator then asks "What's the status of the second one?" The agent correctly resolves "the second one" as the specific order from the prior response, without the operator needing to repeat the order ID.

**Why this priority**: This is the core value of the feature. Without it, the agent treats every message as isolated and cannot answer follow-up questions — making it barely usable for real operations workflows.

**Independent Test**: Can be fully tested by sending two consecutive POST /api/chat requests with the session ID from the first response used in the second request, then verifying the second response references context from the first exchange.

**Acceptance Scenarios**:

1. **Given** a first request with no session ID, **When** the operator asks a domain question, **Then** the response includes a new session ID and the agent's answer.
2. **Given** a session ID from a prior exchange, **When** the operator sends a follow-up referencing "it" or "the second one", **Then** the agent correctly resolves the reference using prior conversation context.
3. **Given** a valid session ID, **When** the operator asks an unrelated question in the same session, **Then** the agent answers it correctly and prior context remains available.

---

### User Story 2 — New Session Minting (Priority: P1)

An operator sends a chat message without including a session ID. The system automatically creates a new conversation session and returns its identifier alongside the agent's response. The operator can then use that session ID for subsequent messages.

**Why this priority**: This is a prerequisite for session continuity — every conversation must start with a session ID, even if the client doesn't supply one.

**Independent Test**: Can be fully tested by sending a POST /api/chat with no session ID, verifying the response contains a valid session ID, and confirming a second request using that ID receives contextual answers.

**Acceptance Scenarios**:

1. **Given** a request with no session ID, **When** the operator sends a prompt, **Then** the response body includes a newly generated, globally-unique session ID.
2. **Given** the same request, **When** the session ID is used in a follow-up request, **Then** the conversation history from the first exchange is available to the agent.
3. **Given** two concurrent requests with no session ID, **When** both are submitted, **Then** each response contains a distinct session ID.

---

### User Story 3 — Session Expiry and Isolation (Priority: P2)

An operator starts a session in the morning, investigates several orders, then leaves for a meeting. After an extended period of inactivity, the session expires automatically. If the same session ID is used later, the system treats it as a new conversation rather than erroring.

**Why this priority**: Session isolation prevents context leakage between conversations and bounds the lifetime of stored data. Graceful expiry is important for usability and storage hygiene.

**Independent Test**: Can be fully tested by submitting a request with an expired or unknown session ID and verifying that a new session is started cleanly without an error response.

**Acceptance Scenarios**:

1. **Given** a session that has been inactive beyond the configured expiry window, **When** a request arrives with that session ID, **Then** the system starts a fresh session (returning a new session ID) rather than returning an error.
2. **Given** two different session IDs used concurrently, **When** each sends a question, **Then** each agent response reflects only its own session's history, not the other's.
3. **Given** an active session, **When** a request arrives within the activity window, **Then** the session expiry timer resets and context remains available.

---

### User Story 4 — Bounded History for Long Conversations (Priority: P3)

An operator holds a long troubleshooting session spanning dozens of exchanges. The system retains only the most recent N turns to stay within the agent's context capacity, gracefully dropping the oldest messages while maintaining the most recent and relevant history.

**Why this priority**: Without a history cap, a very long conversation could exceed the agent's context limit and cause errors. This story ensures stability for power users.

**Independent Test**: Can be fully tested by simulating a conversation exceeding the history limit and confirming later responses remain coherent and error-free.

**Acceptance Scenarios**:

1. **Given** a session with more messages than the configured history limit, **When** a new message is processed, **Then** the oldest messages are dropped and the agent receives the most recent N turns.
2. **Given** a session at exactly the history limit, **When** a new message is added, **Then** exactly the oldest message is dropped.

---

### Edge Cases

- What happens when a session ID is malformed or fails validation? → Request is treated as a new session (no error surfaced to caller). See FR-007.
- What happens when the conversation store is temporarily unavailable? → The request proceeds as a stateless exchange; no history is loaded or saved, and no error is returned to the caller. The caller receives a normal 200 response with no indication of degradation.
- What if a session's TTL expires between the history load and the history save within a single request? → The save operation implicitly creates a new session entry with the same ID. The session ID is NOT reminted; the saved turns form the new session history starting from that point.
- What if the store recovers after a degradation episode? → The session resumes from the last successfully persisted state. Turns exchanged during degraded requests are not recovered.
- What if two requests arrive simultaneously for the same session ID? → Last-write-wins: the second write succeeds; the first write's turn may be silently dropped. This is an accepted tradeoff and will be revisited when feature #3 (AuthN/AuthZ) binds sessions to user identities, enabling per-user serialisation or optimistic locking.
- What if the agent's response is empty or an error? → The user's message is still recorded; the failed assistant turn is not persisted to avoid poisoning future context. See FR-005.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The chat endpoint MUST accept an optional session identifier in the request.
- **FR-002**: When no session identifier is provided, the system MUST generate a new globally-unique session identifier using a cryptographically random algorithm (UUID v4) and return it in the response. The generation algorithm MUST produce distinct identifiers for concurrent requests without coordination.
- **FR-003**: When a valid session identifier is provided, the system MUST retrieve the prior conversation history for that session before invoking the agent.
- **FR-004**: The agent MUST receive the full conversation history (up to the configured maximum) alongside the new user message when processing a request.
- **FR-005**: After a successful agent response, the system MUST persist both the new user message and the agent response to the session's conversation history. If the agent invocation fails, the system MUST still persist the user message to the session history; the failed assistant turn MUST NOT be persisted.
- **FR-006**: Each session MUST have an inactivity expiry window; sessions not written to within that window become inaccessible (TTL expires). Once inaccessible, no caller can retrieve or extend the session. Physical memory reclamation is subject to the store's eviction scheduling and is not an observable guarantee.
- **FR-007**: When a request arrives with an expired, unknown, or malformed session ID, the system MUST start a fresh session and return a newly minted session ID rather than returning an error to the caller.
- **FR-008**: The system MUST cap the number of turns retained per session at a configurable maximum (`MaxTurns`). A turn is a single message (user OR assistant), not a user+assistant pair; `MaxTurns: 20` accommodates 10 complete exchanges. Excess turns are dropped oldest-first in a single pass whenever the total exceeds `MaxTurns`, regardless of how many turns were appended in the current request. If `MaxTurns` is configured to 0 or a negative value, the system MUST fail at startup with a descriptive configuration error.
- **FR-009**: Sessions MUST be isolated: history from one session MUST NOT be visible to another session. Isolation is enforced via namespaced per-session store keys (e.g., `nexusops:session:{id}`). The key namespace MUST NOT conflict with other system uses of the same store instance.
- **FR-010**: If the conversation store is unavailable — including connection timeout, connection refused, serialisation/deserialisation failure, and partial write failure — the system MUST degrade gracefully by processing the request statelessly rather than returning a 5xx error. During degradation the caller MUST NOT be notified; the response MUST appear identical to a successful stateless request. The system does not impose an explicit timeout on store operations beyond the underlying client defaults; latency alone does not trigger degradation.
- **FR-011**: The chat endpoint response MUST always include the active session identifier (either the caller-supplied one or the newly minted one).
- **FR-012**: The system MUST emit a structured log event for each of the following session lifecycle moments, with the specified log level and required fields:
  - `session.created` **(Info)**: `sessionId`, `timestamp`
  - `session.history_loaded` **(Debug)**: `sessionId`, `turnCount`, `timestamp`
  - `session.history_saved` **(Debug)**: `sessionId`, `turnCount`, `timestamp`
  - `session.degraded` **(Warning)**: `sessionId`, `errorCategory` (one of: `connection`, `timeout`, `serialisation`), `historyLoadedBeforeFailure` (bool), `turnCountLoaded` (int; 0 if load failed before any turns were read), `timestamp`

### Key Entities

- **Session**: Represents a single conversation thread. Identified by a unique, opaque identifier. Has an ordered list of conversation turns. Creation time is inferred from the first turn's timestamp; last-active time is tracked implicitly by the store's inactivity TTL — neither is stored as an explicit field.
- **ConversationTurn**: A single exchange within a session. Contains the speaker role (user or assistant), the message content, and a timestamp.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An operator can ask a follow-up question referencing a prior response and receive a contextually correct answer — verifiable by a two-turn test script.
- **SC-002**: 100% of requests without a session ID receive a unique, valid session ID in the response body.
- **SC-003**: Conversation history is available to the agent within the same round-trip that created it — no eventual-consistency gap for the caller. The combined session load + save overhead MUST NOT add more than 100 ms at the p95 percentile to a chat request's end-to-end latency.
- **SC-004**: Sessions with no activity for longer than the configured window become inaccessible within 1× that window's duration (TTL expiry); no caller can retrieve or continue the session after expiry. Physical memory reclamation is subject to Redis eviction scheduling and is not an observable guarantee.
- **SC-005**: When the conversation store is unavailable, the chat endpoint continues to return valid (stateless) responses rather than errors — verifiable via a simulated store outage.
- **SC-006**: A conversation history exceeding the configured turn limit does not cause agent invocation to fail.

## Clarifications

### Session 2026-05-28

- Q: When a session expires, is the session history permanently deleted, soft-deleted, or archived? → A: Permanently deleted — expired session data is purged from the store with no residual retention.
- Q: What is the acceptable latency overhead for session load + save operations on the critical path of a chat request? → A: 100 ms maximum additional overhead per request (relaxed; accommodates early development and remote store scenarios).
- Q: Should session creation be rate-limited per caller to prevent resource exhaustion? → A: Deferred to feature #3 (AuthN/AuthZ) — per-identity limits are the correct enforcement point; document as a known security gap in the interim.
- Q: What session events should be observable (logged/traced)? → A: Key lifecycle events only — session created, history loaded, history saved, and graceful degradation triggered.
- Q: How should concurrent writes to the same session be handled? → A: Last-write-wins — simplest strategy; one concurrent turn may be silently dropped. Concurrency control deferred until AuthN binds sessions to identities.

## Assumptions

- The session identifier is opaque to the caller — clients MUST treat it as an opaque string token and MUST NOT parse, sort, or compare it structurally. It MUST NOT appear in URL paths or query parameters.
- Session identifiers are generated server-side using UUID v4 (128-bit cryptographic randomness, 122 bits of entropy). The probability of collision or successful enumeration at expected session volumes is negligible. Client-supplied IDs are accepted only if they match an existing active session.
- The default inactivity expiry window is 30 minutes, chosen to match typical operator workflow duration and web-session idle conventions. This is configurable without a code change.
- The default maximum conversation history is 20 turns (10 user + 10 assistant messages), chosen to cover most operational workflows while keeping per-session Redis payload under ~80 KB (assuming ≤4 KB per turn). This is configurable without a code change.
- Individual turn content is assumed to be ≤4 KB. The 100 ms p95 latency budget applies at the worst-case payload of MaxTurns=20 turns at this size. Larger average turn sizes may require tuning.
- If `MaxTurns` is configured to 0 or a negative value, the application MUST fail at startup with a descriptive configuration error. Disabling history entirely is not a supported configuration in this version.
- The configuration section name `Session` and key names `MaxTurns` and `SlidingExpirationMinutes` are stable across patch and minor releases. Any rename constitutes a breaking change and requires a migration path.
- The chat endpoint remains HTTP — no WebSocket or streaming protocol changes are in scope for this feature.
- Authentication is out of scope: sessions are identified by token only, with no user identity bound to a session at this stage (that comes with feature #3, AuthN/AuthZ).
- **Known security gap**: Session creation is not rate-limited in this feature. Attack vector: any network-reachable caller can create an unbounded number of sessions, potentially exhausting Redis memory. Per-identity rate limiting is the correct mitigation and will be enforced when AuthN (feature #3) binds caller identity to requests. Until then, network-perimeter controls are the only protection.
- **Security scope boundary**: This feature protects session data integrity (namespaced keys, TTL isolation) but does NOT protect against: session ID brute-force enumeration (mitigated by UUID v4 entropy, not enforced), session fixation (no identity binding), or resource exhaustion (rate limiting deferred). These will be addressed in feature #3.
- Session ID values MUST NOT appear in structured log output in plaintext beyond the first 8 characters (for correlation purposes). Transport security (HTTPS-only) is enforced at the infrastructure/reverse-proxy level and is not within scope of this feature.
- The frontend chat UI replacement (feature #5) is not in scope here; the spec targets the backend `POST /api/chat` contract only.
- The AppHost is responsible for provisioning the conversation store resource; the AgentHost consumes it as a named service dependency.
