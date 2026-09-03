# Contract: Refund/Cancellation Tools and Approval Endpoints

**Branch**: `006-approval-actions` | **Date**: 2026-09-02

## `request_order_refund` (Saga path, mutating, approval-gated)

**Input**: `orderId: string` (required), `amount?: decimal`, `reason?: string`

**Output**: `ToolResult<OrderActionRequestResult>` — see `data-model.md`.

**Description text** (embedded in `ToolNames.RequestOrderRefundDescription`, following `ToolNames.InvestigateOrderRootCauseDescription`'s precedent of putting a phrasing constraint directly in the description the model reads): explains this creates a pending refund request that requires human approval before anything is executed; instructs the model to report the reference identifier and state explicitly that the refund is pending, never that it has happened; notes the amount defaults to the order's full total if omitted.

**Routing**: the agent selects this tool only when the operator names one specific order and expresses refund intent (e.g., "refund order X", "give a refund for X"). It MUST NOT be selected for a bare status check or investigation request (those remain `get_order_details`/`investigate_order_root_cause`).

## `request_order_cancellation` (Saga path, mutating, approval-gated)

**Input**: `orderId: string` (required), `reason?: string`

**Output**: `ToolResult<OrderActionRequestResult>`.

**Description text**: same pending-approval phrasing constraint as the refund tool, adapted for cancellation; notes that on approval both the order and its reserved inventory are affected.

**Routing**: selected only for explicit cancellation intent naming one specific order (e.g., "cancel order X").

## Agent Response Contract (both tools)

Regardless of the tool's own output shape, the agent's *natural-language* reply after calling either tool MUST, per spec.md FR-005:
- State the action is **pending approval**, not completed.
- Include the `ApprovalReference` (the GUID `OrderActionRequestResult.ApprovalReference`) verbatim, so the operator (or a separate approver) can act on it.
- If `Status == NotFound`, state plainly that the order could not be found and that no action was created — never present a reference in this case.

This constraint is enforced by the tool description text (a prompt-engineering control, consistent with every existing tool in this system) and verified manually per `quickstart.md` and `tasks.md`'s User Story 6 verification step — the same enforcement mechanism 005 used for its own routing correctness (SC-005/SC-006 in each feature's respective spec).

## `POST /api/approvals/{id}/approve`

Not an agent tool — a plain HTTP endpoint on `NexusOps.AgentHost`, called directly (e.g., via `curl`) by whoever holds approval authority, per `ROADMAP.md`'s locked "no UI" decision. `{id}` is the `ApprovalReference` GUID.

**Behavior**: publishes `ApproveOrderAction` as a MassTransit request (`IRequestClient<ApproveOrderAction>`, registered via `x.AddRequestClient<ApproveOrderAction>()`, injected directly into the endpoint delegate) and blocks — bounded by a request-client timeout sized to cover the worst-case execution path (order mutation + inventory restock + a possible compensating reversal, each with its own per-leg timeout; see `research.md` Decision 3 and the timeout budget worked out in `plan.md`'s Technical Context) — until the saga responds with the real, final `OrderActionDecisionResult`.

**Response** (`200 OK`, body = `OrderActionDecisionResult` serialized as JSON):
- `DecisionStatus: "Approved"`, `ExecutionOutcome: "Executed"` — the mutation succeeded.
- `DecisionStatus: "Approved"`, `ExecutionOutcome: "Failed"` — the order's own mutation failed (e.g., already refunded); nothing was changed.
- `DecisionStatus: "Approved"`, `ExecutionOutcome: "FailedAndCompensated"` — the order mutation succeeded, a subsequent dependency failed, and the order was reverted.
- `DecisionStatus: "AlreadyDecided"` — this reference was already approved or rejected; nothing happened as a result of this call.
- `DecisionStatus: "NotFound"` — no pending action exists for this reference (typo, or it never existed).

A client-side timeout while waiting on execution is reported as a `500`-class problem response, distinctly worded from `NotFound`/`AlreadyDecided` (the decision *was* recorded — the caller just did not see the outcome in time), consistent with `OrderTools.cs`'s existing `RequestTimeoutException` handling precedent for `investigate_order_root_cause`.

## `POST /api/approvals/{id}/reject`

Same shape as `/approve`, backed by `IRequestClient<RejectOrderAction>`. Responds immediately (no execution to wait for): `DecisionStatus: "Rejected"`, or `"AlreadyDecided"`/`"NotFound"` under the same conditions as above. Accepts an optional `reason` as a query parameter (`POST /reject?reason=...`), carried into the eventual `NotificationRequested` event's `Message`.

## Non-Goals

- No new agent tool exposes the approval/rejection mechanism to the model — approval is deliberately outside the agent's own reach, per Constitution Principle III ("the agent MUST inform the user that the request is pending approval — never claim the action was completed autonomously") and this feature's spec (the approver is a human acting independently of the chat agent).
- No authentication/authorization is added to the approval endpoints in this feature (`spec.md` Assumptions).
