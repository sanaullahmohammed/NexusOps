# Contract: Chat API (Session-Aware)

**Version**: 2.0 (adds session management to v1 stateless endpoint)
**Service**: `NexusOps.AgentHost`
**Endpoint**: `POST /api/chat`

---

## Request

```
POST /api/chat
Content-Type: application/json
```

### Body Schema

```json
{
  "prompt": "<string, required>",
  "sessionId": "<string, optional — opaque UUID v4 token>"
}
```

| Field | Required | Type | Constraints | Behaviour if absent / null / empty |
|-------|----------|------|-------------|-------------------------------------|
| `prompt` | Yes | `string` | Non-empty | 400 Bad Request |
| `sessionId` | No | `string` | Opaque UUID v4 string; absent, null, and empty-string are all equivalent — all mint a new session | New session is minted |

> **Opaque token**: `sessionId` is an opaque handle. Callers MUST NOT parse, sort, or compare it structurally, and MUST NOT include it in URL paths or query parameters. The format is UUID v4 (`xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx`).
>
> The server **validates** the supplied value against the canonical hyphenated form. Anything else — including unhyphenated or brace-wrapped UUIDs — is malformed and starts a fresh session per FR-007. A rejected value never reaches the store key space and is never echoed back in a response or an error body.

**Session ID lookup behaviour:**
- Present (non-empty) + active → conversation history loaded; session expiry timer reset
- Present (non-empty) + expired/unknown/malformed → treated as new session; a **new** ID is minted and returned (the returned `sessionId` will differ from the supplied one)
- Present (non-empty) + **store unreachable** → the supplied ID is **preserved** and echoed back unchanged; the turn is processed without history. A failed lookup is not evidence that the session is absent, so no replacement is minted (FR-013)
- Absent, null, or empty string → new session minted, **without querying the store** — the ID was just generated in-process, so a read would be a guaranteed miss (FR-014)

---

## Response

### 200 OK

```json
{
  "response": "<string>",
  "sessionId": "<string — GUID>"
}
```

| Field | Always present | Notes |
|-------|---------------|-------|
| `response` | Yes | The agent's natural-language reply |
| `sessionId` | Yes | The active session ID — either the caller-supplied one or newly minted |

### 400 Bad Request (ValidationProblemDetails)

Returned when `prompt` is absent, null, empty, or entirely whitespace. Rejected before a session is
minted and before the model is invoked, so a malformed request incurs no model cost and leaves
nothing in the conversation store.

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": {
    "prompt": ["A prompt is required and must not be empty or whitespace."]
  }
}
```

### 500 Internal Server Error (ProblemDetails)

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.6.1",
  "title": "The agent could not complete the request.",
  "detail": "The prompt was recorded in the session below. Continue with this sessionId; resending the same prompt would record it a second time.",
  "status": 500,
  "sessionId": "<string — the active session>"
}
```

Returned only when the agent itself fails. Store unavailability does NOT produce a 500 — the request proceeds statelessly,
and does not affect readiness either: `/health` excludes the conversation store, because the service is designed to keep
serving without it (FR-016).

The prompt is **already recorded** in the session. Continue the conversation with the returned `sessionId`; resending the
same prompt would record it a second time and the agent would see it twice.

The `sessionId` extension is **required** (FR-015). FR-005 persists the user's turn even when the
agent fails; if the session was minted during that same request, a 500 without this field left the
caller unable to name the session the turn was written under, so it was unreachable until its TTL
expired. Callers may retry the prompt against this ID.

---

## Backward Compatibility

**Guaranteed**: Existing callers that omit `sessionId` and do not read `sessionId` from the response receive identical behaviour to the v1 stateless endpoint. The `sessionId` field added to responses is purely additive — callers that ignore it are unaffected. No request that was valid against v1 will receive a different status code or response shape from v2.

---

## Lifecycle Log Events (FR-012)

The following structured log events are emitted by the service. Log level and required fields are normative.

| Event Name | Log Level | Emitted When | Required Fields |
|------------|-----------|--------------|-----------------|
| `session.created` | **Info** | New session ID minted | `sessionId`, `timestamp` |
| `session.history_loaded` | **Debug** | History retrieved from store | `sessionId`, `turnCount`, `timestamp` |
| `session.history_saved` | **Debug** | History written to store | `sessionId`, `turnCount`, `timestamp` |
| `session.degraded` | **Warning** | Store unavailable; stateless fallback | `sessionId`, `errorCategory` (`connection` \| `timeout` \| `serialisation` \| `configuration`), `historyLoadedBeforeFailure` (bool), `turnCountLoaded` (int), `timestamp` |

> **Log safety**: log output MUST identify a session by a short, non-reversible token derived from
> the session ID — the first 8 hexadecimal characters of its SHA-256 digest. Every emitting component
> MUST use the same derivation. Full session IDs MUST NOT appear in log files, and neither may any
> recoverable portion of one.
>
> *Amended by feature 003 (FR-012).* This previously specified truncation to the first 8 characters of
> the raw ID. Two problems: `AgentService` hashed while `RedisConversationStore` truncated, so a
> `session.created` event could never be joined to the `session.degraded` that followed it; and the
> truncated form exposed a third of a real UUID, defeating the intent of the rule.

---

## Example Round-Trip

**Turn 1 — no session ID:**

```
POST /api/chat
{ "prompt": "Show me all delayed orders" }

→ 200 OK
{ "response": "There are 3 delayed orders: ORD-001, ORD-004, ORD-009...", "sessionId": "abc12345-..." }
```

**Turn 2 — follow-up using session ID:**

```
POST /api/chat
{ "prompt": "What's the status of the second one?", "sessionId": "abc12345-..." }

→ 200 OK
{ "response": "ORD-004 is delayed due to a supplier backorder...", "sessionId": "abc12345-..." }
```
