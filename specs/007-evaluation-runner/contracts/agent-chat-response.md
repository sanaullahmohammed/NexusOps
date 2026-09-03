# Contract: `POST /api/chat` response gains `toolsInvoked`

This is the one interface change this feature makes to an existing service (`NexusOps.AgentHost`). It is additive and backward-compatible: every existing consumer of `ChatResponse` (README's curl examples, any future frontend) continues to work unmodified since `response` and `sessionId` are unchanged in shape and meaning.

## Before

```json
{
  "response": "Order ORD-0003 is delayed due to a stockout on SKU-ELEC-001.",
  "sessionId": "3f2c1a10-....."
}
```

## After

```json
{
  "response": "Order ORD-0003 is delayed due to a stockout on SKU-ELEC-001.",
  "sessionId": "3f2c1a10-.....",
  "toolsInvoked": ["investigate_order_root_cause"]
}
```

- `toolsInvoked`: array of tool names (strings matching `NexusOps.Contracts.ToolNames` constants) the agent invoked while producing this turn's response, in invocation order. Empty (`[]`) when the agent answered without invoking any tool. Never null.
- Populated from the same `AgentResponse` the existing `response` text is already derived from within `AgentService.SendAsync` — no additional model call, no additional latency.
- Present on both the success path and unaffected on the existing `AgentInvocationException` failure path (that path returns a `ProblemDetails` body, not a `ChatResponse`, and is unchanged by this feature).

## Consumers

- `NexusOps.Evaluation`'s live mode reads `toolsInvoked` to score each dataset case (FR-012/FR-013).
- No other consumer exists in the repository today (`NexusOps.Server`/`frontend/` are scaffold placeholders per CLAUDE.md); this field is additive and safe for either to adopt later without a breaking change.
