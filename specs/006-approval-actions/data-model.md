# Data Model: Approval-Gated Order Actions

**Branch**: `006-approval-actions` | **Date**: 2026-09-02

## Entities

### OrderActionSagaState (Postgres-persisted saga instance)

One row per refund or cancellation request, from creation through its terminal outcome. Owned entirely by `NexusOps.WorkflowOrchestrator.OrderAction` — not referenced by any domain-agnostic core type. Lives in the same `workfloworchestrator` database as feature 005's `OrderInvestigationSagaState`, in its own table via its own `OrderActionDbContext`/migration (per 005's `data-model.md`: "a future saga would add its own table to the same database via its own migration, in its own namespace").

| Field | Type | Notes |
|---|---|---|
| `CorrelationId` | `Guid` | Primary key; also serves as the human-facing "approval reference" returned to callers. |
| `CurrentState` | `string` | `Validating`, `AwaitingApproval`, `Executing`, `Completed`, `Rejected`, `Failed`. |
| `ActionType` | `OrderActionType` | `Refund` or `Cancellation`. |
| `OrderId` | `string` | The order being acted on. |
| `Amount` | `decimal?` | Refund amount; defaulted to the order's `TotalAmount` once validation confirms the order (Decision 5/Assumptions). `null` for cancellation. |
| `Reason` | `string?` | Optional, as given by the requester. |
| `RequestResponseAddress` / `RequestRequestId` | `Uri?` / `Guid?` | Captured from `RequestOrderRefund`/`RequestOrderCancellation`'s headers; used to send `OrderActionRequestResult` back once validation finishes (found → `AwaitingApproval`, not found → `Failed`). Cleared once sent. |
| `ApprovalResponseAddress` / `ApprovalRequestId` | `Uri?` / `Guid?` | Captured from `ApproveOrderAction`'s headers at the moment of approval; used to send `OrderActionDecisionResult` back once execution finishes. `null` until an approval is in flight; cleared once sent. |
| `PriorStatus` | `string?` | The order's status immediately before execution began; required input to a compensating reversal. Set when execution begins, per FR-011. |
| `ExecutionOutcome` | `OrderActionExecutionOutcome?` | `Executed`, `Failed`, or `FailedAndCompensated`, set once execution finishes. |
| `RequestedAt` | `DateTimeOffset` | Set on creation. |
| `DecidedAt` | `DateTimeOffset?` | Set when an approval or rejection is recorded. |
| `CompletedAt` | `DateTimeOffset?` | Set when the saga reaches a terminal state. |
| `RowVersion` | `uint` | EF Core optimistic-concurrency token (Postgres `xmin`, per 005's `research.md` Decision 3/Decision 3 above). |

**State transitions**:

```
[saga created by RequestOrderRefund or RequestOrderCancellation]
  → CurrentState = Validating
  → ActionType, OrderId, Amount (refund only, as given), Reason set; RequestResponseAddress/RequestRequestId captured
  → Publish(BeginActionValidation { CorrelationId, OrderId })

Validating, on ActionValidationCompleted (006-owned; see below — NOT the raw, cross-saga-shared OrderFindingReported):
  if Status == Succeeded:
    → PriorStatus is NOT yet meaningful (nothing executed); Amount defaults to Order.TotalAmount if ActionType == Refund and Amount was not given
    → Respond(RequestResponseAddress/RequestRequestId, OrderActionRequestResult { ApprovalReference = CorrelationId, Status = AwaitingApproval, Amount })
    → RequestResponseAddress = null
    → CurrentState = AwaitingApproval
  else (NotFound / Unavailable / TimedOut — deliberately collapsed to one tool-facing status, unlike
        005's three-way SourceFindingStatus distinction: this is a validation gate deciding whether
        a reference is even creatable, not a diagnostic result, so "order not found" and "order
        service currently unreachable" both simply mean "no pending action was created"):
    → Respond(RequestResponseAddress/RequestRequestId, OrderActionRequestResult { ApprovalReference = CorrelationId, Status = NotFound })
    → RequestResponseAddress = null
    → CurrentState = Failed   // no pending reference is ever approvable; spec.md User Story 1 Acceptance Scenario 3

AwaitingApproval, on ApproveOrderAction:
  → DecidedAt = now; ApprovalResponseAddress/ApprovalRequestId captured
  → Publish(BeginOrderActionExecution { CorrelationId, ActionType, OrderId, Amount })
  → CurrentState = Executing

AwaitingApproval, on RejectOrderAction:
  → DecidedAt = now; CompletedAt = now
  → Respond(OrderActionDecisionResult { ApprovalReference = CorrelationId, DecisionStatus = Rejected })
  → Publish(NotificationRequested { ..., Outcome = Rejected })
  → CurrentState = Rejected

Executing, on OrderActionExecutionCompleted:
  → ExecutionOutcome = Executed | Failed | FailedAndCompensated (per the event); CompletedAt = now
  → Respond(ApprovalResponseAddress/ApprovalRequestId, OrderActionDecisionResult { DecisionStatus = Approved, ExecutionOutcome })
  → ApprovalResponseAddress = null
  → Publish(NotificationRequested { ..., Outcome = ExecutionOutcome })
  → CurrentState = Completed | Failed   // Completed iff ExecutionOutcome == Executed, else Failed

AwaitingApproval / Executing / Completed / Rejected / Failed, on a second ApproveOrderAction or RejectOrderAction
  for a reference already past AwaitingApproval:
    → Respond(OrderActionDecisionResult { DecisionStatus = AlreadyDecided })   // FR-008/FR-009, SC-008

Any ApproveOrderAction/RejectOrderAction for a CorrelationId with no matching instance:
    → Respond(OrderActionDecisionResult { DecisionStatus = NotFound })   // OnMissingInstance(ExecuteAsync(...)), not Discard — this is request/response
```

A `RequestOrderRefund`/`RequestOrderCancellation` retried after its `Initially` transition already committed mints a second `CorrelationId` (same shape as 005's own accepted gap) and creates a second, independent pending reference — an operator-visible duplicate request, not a duplicate execution; the EF Core transactional outbox (research.md Decision 6) is what protects the higher-stakes `Approve → Execute` leg from double-executing a mutation, which is the leg that actually matters per 005's own `plan.md` Open Questions note.

---

### Message Contracts (`NexusOps.Contracts/Messages/`)

All records are `sealed record`s, matching the project's existing style. `OrderActionType`, `OrderActionStatus`, `OrderActionDecisionOutcome`, `OrderActionExecutionOutcome` live in `NexusOps.Contracts/Dtos/OrderAction.cs` (enums + the two response-DTO-shaped records), per 005's own Dtos/Messages split.

#### `RequestOrderRefund` (AgentHost → saga; request)

| Field | Type |
|---|---|
| `OrderId` | `string` |
| `Amount` | `decimal?` |
| `Reason` | `string?` |

#### `RequestOrderCancellation` (AgentHost → saga; request)

| Field | Type |
|---|---|
| `OrderId` | `string` |
| `Reason` | `string?` |

#### `OrderActionRequestResult` (saga → AgentHost; response to both of the above)

| Field | Type | Notes |
|---|---|---|
| `OrderId` | `string` | |
| `ActionType` | `OrderActionType` | |
| `ApprovalReference` | `Guid` | The saga's `CorrelationId`. Meaningless to act on when `Status == NotFound`. |
| `Status` | `OrderActionStatus` | `AwaitingApproval` or `NotFound` at this point in the lifecycle. |
| `Amount` | `decimal?` | The (possibly defaulted) refund amount; `null` for cancellation. |

#### `BeginActionValidation` (saga → validation consumer; internal event)

| Field | Type |
|---|---|
| `CorrelationId` | `Guid` |
| `OrderId` | `string` |

*(Handled by `OrderActionValidationConsumer`, which issues `RequestOrderFinding` and reacts to the response `OrderFindingReported` — both already defined by feature 005; no new contract for that specific request/response call.)*

#### `ActionValidationCompleted` (validation consumer → saga; internal event, 006-owned)

| Field | Type | Notes |
|---|---|---|
| `CorrelationId` | `Guid` | |
| `Status` | `SourceFindingStatus` | Reuses feature 005's enum (`Dtos/RootCauseInvestigation.cs`) — `Succeeded` or otherwise. |
| `Order` | `OrderSummary?` | Present iff `Status == Succeeded`. |

Published by `OrderActionValidationConsumer` after mapping `RequestOrderFinding`'s response, instead of re-broadcasting the shared `OrderFindingReported` event — `OrderActionSaga` binds only to this event, never to `OrderFindingReported` directly, so 006's validation traffic never reaches `OrderInvestigationSaga`'s queue and vice versa (research.md Decision 1, implementation note).

#### `ApproveOrderAction` / `RejectOrderAction` (AgentHost approval endpoints → saga; request)

| Field | Type | Notes |
|---|---|---|
| `ApprovalReference` | `Guid` | Correlates to `OrderActionSagaState.CorrelationId`. |
| `Reason` | `string?` | `RejectOrderAction` only. |

#### `OrderActionDecisionResult` (saga → AgentHost approval endpoints; response to both of the above)

| Field | Type | Notes |
|---|---|---|
| `ApprovalReference` | `Guid` | |
| `DecisionStatus` | `OrderActionDecisionOutcome` | `Approved`, `Rejected`, `AlreadyDecided`, `NotFound`. |
| `ExecutionOutcome` | `OrderActionExecutionOutcome?` | Set only when `DecisionStatus == Approved`; the real result of execution (Decision 3 — approval blocks for this). |
| `Message` | `string` | Human-readable summary for the `curl` caller. |

#### `BeginOrderActionExecution` (saga → execution consumer; internal event)

| Field | Type |
|---|---|
| `CorrelationId` | `Guid` |
| `ActionType` | `OrderActionType` |
| `OrderId` | `string` |
| `Amount` | `decimal?` |

#### `ExecuteOrderMutation` / `OrderMutationExecuted` (execution consumer ↔ `NexusOps.OrderService`)

| Field | Type | Notes |
|---|---|---|
| `CorrelationId` | `Guid` | Request + response. |
| `ActionType` | `OrderActionType` | Request only — target status is `Refunded` or `Cancelled`. |
| `OrderId` | `string` | Request only. |
| `Amount` | `decimal?` | Request only; recorded, not otherwise validated against the order. |
| `Success` | `bool` | Response only. |
| `FailureReason` | `string?` | Response only; set when the order is not eligible (FR-013) — e.g. already in the target or another terminal status. |
| `PriorStatus` | `string` | Response only; the order's status immediately before this call — required by a later compensating call regardless of `Success`. |
| `LineItems` | `OrderLineItem[]` | Response only; needed by cancellation's inventory-restock leg. Empty when `Success == false`. |

#### `ExecuteInventoryRestock` / `InventoryRestockExecuted` (execution consumer ↔ `NexusOps.InventoryService`; cancellation only)

| Field | Type | Notes |
|---|---|---|
| `CorrelationId` | `Guid` | |
| `OrderId` | `string` | Request only; carried for logging/traceability. |
| `Lines` | `InventoryRestockLine[]` | Request only — `(string Sku, int Quantity)` per line item. |
| `Success` | `bool` | Response only. |
| `FailureReason` | `string?` | Response only. |

#### `CompensateOrderMutation` / `OrderMutationCompensated` (execution consumer ↔ `NexusOps.OrderService`; compensation only)

| Field | Type | Notes |
|---|---|---|
| `CorrelationId` | `Guid` | |
| `OrderId` | `string` | Request only. |
| `RevertToStatus` | `string` | Request only — the `PriorStatus` captured by the earlier `OrderMutationExecuted`. |
| `Success` | `bool` | Response only. |

#### `OrderActionExecutionCompleted` (execution consumer → saga; internal event)

| Field | Type |
|---|---|
| `CorrelationId` | `Guid` |
| `Outcome` | `OrderActionExecutionOutcome` |
| `Detail` | `string` |

#### `NotificationRequested` (saga → Notification Service; published, `[EntityName("notification-requested")]`)

| Field | Type |
|---|---|
| `CorrelationId` | `Guid` |
| `OrderId` | `string` |
| `ActionType` | `OrderActionType` |
| `Outcome` | `OrderActionExecutionOutcome \| "Rejected"` (see note) |
| `Message` | `string` |

*Note*: `Outcome` here is carried as a plain `string` (not the .NET-only `OrderActionExecutionOutcome` enum) specifically because this message crosses into a non-.NET consumer (the Node.js Notification Service) — its JSON shape must not depend on a .NET enum's serialization convention. Values: `"Executed"`, `"Rejected"`, `"Failed"`, `"FailedAndCompensated"`.

---

## Domain-Service Mutation State (research.md Decision 7)

| Store | Overlay | Applied at |
|---|---|---|
| `NexusOps.OrderService` | `OrderMutationOverlay : ConcurrentDictionary<string, OrderStatus>` | `OrderEndpoints` (`GET /orders/anomalies`, `GET /orders/{id}`), `RequestOrderFindingConsumer` (feature 005, unchanged contract, overlay-aware implementation) |
| `NexusOps.InventoryService` | `InventoryMutationOverlay : ConcurrentDictionary<string, int>` (cumulative delta) | `InventoryEndpoints`, `RequestInventoryFindingConsumer` (feature 005, unchanged contract, overlay-aware implementation) |

`OrderStatus` (in `NexusOps.OrderService.Models`) gains one new value: `Refunded`, appended after `Cancelled`.

---

## Postgres Schema (`workfloworchestrator` database)

| Table | Notes |
|---|---|
| `OrderInvestigationSagaState` | Unchanged, owned by feature 005. |
| `OrderActionSagaState` | New; one row per refund/cancellation request. Managed by its own EF Core migration in `NexusOps.WorkflowOrchestrator.OrderAction`. |
| MassTransit outbox tables (`InboxState`, `OutboxState`, `OutboxMessage`) | New; added by `AddEntityFrameworkOutbox<OrderActionDbContext>` (research.md Decision 6), scoped to `OrderActionDbContext`. |

---

## Tool Contract Delta (`NexusOps.Contracts`)

### `ToolNames.RequestOrderRefund` (new)

```
request_order_refund
```

Input: `{ orderId: string, amount?: decimal, reason?: string }`. Output: `ToolResult<OrderActionRequestResult>`.

### `ToolNames.RequestOrderCancellation` (new)

```
request_order_cancellation
```

Input: `{ orderId: string, reason?: string }`. Output: `ToolResult<OrderActionRequestResult>`.

Both tool descriptions MUST instruct the model to report the result as pending approval with the reference, per FR-005, and MUST NOT be phrased in a way that lets the model claim completion (mirrors 005's `research.md` Decision 8/`ToolNames.InvestigateOrderRootCauseDescription`'s precedent of embedding a phrasing constraint directly in the tool description).

### Existing tool contracts (unchanged)

`investigate_order_anomaly`, `get_order_details`, `investigate_order_root_cause`, `get_inventory_alerts`, `get_inventory_level`, `get_product_details`, `list_products_by_category` — no fields, names, or response shapes change.
