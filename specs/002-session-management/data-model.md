# Data Model: Session Management

**Branch**: `002-session-management` | **Date**: 2026-05-28

## Entities

### ConversationTurn

Represents a single message in a conversation — either from the user or the assistant.

| Field | Type | Constraints | Notes |
|-------|------|-------------|-------|
| `Role` | `string` | Required; `"user"` or `"assistant"` | Maps to `ChatRole` when passed to the agent |
| `Content` | `string` | Required; non-empty | The raw message text |
| `Timestamp` | `DateTimeOffset` | Required; UTC | Set at the time the turn is recorded |

**Invariants:**
- A `ConversationTurn` is immutable once appended to a session.
- A failed assistant response is never appended (FR-005: only successful responses are persisted).

---

### ConversationSession (Redis-stored aggregate)

Represents the full history of one conversation thread. Stored as a JSON-serialised `List<ConversationTurn>` in Redis.

| Field | Type | Notes |
|-------|------|-------|
| `SessionId` | `string` (GUID) | Key: `nexusops:session:{sessionId}` |
| `Turns` | `List<ConversationTurn>` | Ordered oldest→newest; trimmed to `MaxTurns` |
| *(implicit)* `LastActive` | Redis TTL | Sliding expiration; renewed on every write |

**Lifecycle:**
```
New request, no session ID
  → Generate new SessionId (Guid.NewGuid())
  → Turns = []
  → Save after successful response

Request with known SessionId
  → Load Turns from Redis
  → Append user turn + agent response
  → Trim to MaxTurns if needed
  → Save (renews TTL)

Request with expired/unknown SessionId
  → Treat as new session (start fresh, issue new SessionId)

Inactivity > TTL (30 min default)
  → Redis evicts the key automatically (permanent deletion)
```

**Trimming rule:** If `Turns.Count > MaxTurns` after append, remove `Turns[0..n]` where `n = Turns.Count - MaxTurns`. Always drops oldest turns first.

---

## Redis Key Schema

| Key Pattern | Value | TTL | Notes |
|-------------|-------|-----|-------|
| `nexusops:session:{guid}` | JSON `List<ConversationTurn>` | 30 min sliding | One key per active session |

The `nexusops:` namespace prefix avoids collisions with future Redis uses (e.g., rate limit counters, cache).

---

## Configuration

Configurable via `appsettings.json` under `Session:`:

| Setting | Default | Description |
|---------|---------|-------------|
| `Session:MaxTurns` | `20` | Maximum conversation turns retained per session |
| `Session:SlidingExpirationMinutes` | `30` | Inactivity window before session is deleted |

---

## ChatMessage Mapping (agent integration)

When calling `AIAgent.RunAsync`, stored turns are mapped to `Microsoft.Extensions.AI.ChatMessage`:

| `ConversationTurn.Role` | `ChatRole` |
|------------------------|------------|
| `"user"` | `ChatRole.User` |
| `"assistant"` | `ChatRole.Assistant` |

The new user message is appended to the list before the agent call. The assistant turn is appended only after a successful response.

---

## API Contract Delta

### `POST /api/chat` — Request

```json
{
  "prompt": "What is the status of order ORD-001?",
  "sessionId": "3f2504e0-4f89-11d3-9a0c-0305e82c3301"
}
```

`sessionId` is optional. Omit to start a new conversation.

### `POST /api/chat` — Response

```json
{
  "response": "Order ORD-001 is currently delayed ...",
  "sessionId": "3f2504e0-4f89-11d3-9a0c-0305e82c3301"
}
```

`sessionId` is always present — either the caller-supplied value (if the session was active) or a newly minted one.
