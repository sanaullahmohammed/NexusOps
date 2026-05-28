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

**Session ID lookup behaviour:**
- Present (non-empty) + active → conversation history loaded; session expiry timer reset
- Present (non-empty) + expired/unknown/malformed → treated as new session; a **new** ID is minted and returned (the returned `sessionId` will differ from the supplied one)
- Absent, null, or empty string → new session minted

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

### 500 Internal Server Error (ProblemDetails)

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.6.1",
  "title": "An error occurred while processing your request.",
  "status": 500
}
```

Returned only when the agent itself fails. Store unavailability does NOT produce a 500 — the request proceeds statelessly.

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
| `session.degraded` | **Warning** | Store unavailable; stateless fallback | `sessionId`, `errorCategory` (`connection` \| `timeout` \| `serialisation`), `historyLoadedBeforeFailure` (bool), `turnCountLoaded` (int), `timestamp` |

> **Log safety**: `sessionId` values in log output MUST be truncated to the first 8 characters for correlation. Full session IDs MUST NOT appear in log files.

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
