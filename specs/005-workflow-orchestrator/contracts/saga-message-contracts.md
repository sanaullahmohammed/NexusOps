# Contract: Saga-Internal AMQP Message Contracts

**Version**: 1.0 (new)
**Service boundary**: `NexusOps.WorkflowOrchestrator` ⇄ `NexusOps.OrderService` / `NexusOps.InventoryService` / `NexusOps.ProductService`
**Transport**: RabbitMQ via MassTransit v8 (request/response for every leg — see `research.md` Decisions 1, 2, 4)

These contracts are internal to the orchestration layer — no external caller (including AgentHost) publishes or consumes them directly. AgentHost's only contact point is `InvestigateOrderRootCause` / `RootCauseInvestigationResult`, documented in `investigate-order-root-cause-tool.md`.

---

## Leg 1 — AgentHost → Saga

| Message | Direction | Delivery |
|---|---|---|
| `InvestigateOrderRootCause { OrderId }` | AgentHost → `OrderInvestigationSaga` | MassTransit request (`IRequestClient`), 8s timeout |
| `RootCauseInvestigationResult { ... }` | `OrderInvestigationSaga` → AgentHost | Sent to the captured `ResponseAddress`/`RequestId` (Decision 2) — **not** `RespondAsync`, since the saga responds from a later, unrelated consume context |

## Leg 2 — Saga → Fan-Out Coordinator (internal)

| Message | Direction | Delivery |
|---|---|---|
| `BeginInvestigationFanOut { CorrelationId, OrderId }` | `OrderInvestigationSaga` → `InvestigationFanOutConsumer` | `Publish` (fire-and-forget; the coordinator's own retry/redelivery on consumer failure covers durability, per User Story 4) |

## Leg 3 — Fan-Out Coordinator → Domain Services

| Message | Direction | Delivery | Per-call timeout |
|---|---|---|---|
| `RequestOrderFinding { CorrelationId, OrderId }` | `InvestigationFanOutConsumer` → `OrderService` | Request/response | 5s |
| `RequestInventoryFinding { CorrelationId, Skus }` | `InvestigationFanOutConsumer` → `InventoryService` | Request/response | 5s |
| `RequestProductFinding { CorrelationId, Skus }` | `InvestigationFanOutConsumer` → `ProductService` | Request/response | 5s |

The order lookup runs first (its result supplies the SKUs); inventory and product lookups then run concurrently with each other (see `data-model.md`, "Fan-out sequencing"). A `RequestTimeoutException` or fault on any leg is caught by the coordinator and turned into a `Status: Unavailable` (fault) or `Status: TimedOut` (timeout) finding — the coordinator never lets an exception propagate unhandled, since that would leave the corresponding `XFindingReported` event unpublished and the investigation permanently `Pending` on that source.

## Leg 4 — Fan-Out Coordinator → Saga (findings)

| Message | Direction | Delivery |
|---|---|---|
| `OrderFindingReported { CorrelationId, Status, Order? }` | `InvestigationFanOutConsumer` → `OrderInvestigationSaga` | `Publish`, correlated by `CorrelationId` |
| `InventoryFindingReported { CorrelationId, Status, Levels[], SkusNotFound[] }` | same | same |
| `ProductFindingReported { CorrelationId, Status, Products[], SkusNotFound[] }` | same | same |

Each of these three is independent — they can arrive in any order, and (per `research.md` Decision 1) can be dispatched to concurrent consumer instances, which is exactly the race the saga's optimistic-concurrency configuration (`data-model.md`, `RowVersion`) exists to make safe.

---

## Reliability Requirements on Every Queue (Constitution Principle IV)

- All five request/response legs and the two internal `Publish` legs use MassTransit's default retry policy (`UseMessageRetry`) with a bounded exponential back-off, plus a dead-letter (`_error`) queue for exhausted retries.
- No leg in this contract is a plain HTTP call between saga-side code and a domain service; every cross-service hop is AMQP via MassTransit.

## Failure-Mode Summary

| Failure | Observed as |
|---|---|
| One domain service down/slow | That leg's `IRequestClient` call throws `RequestTimeoutException` in the coordinator → `Status: TimedOut` finding → saga finalizes `Degraded` (or `Failed`, if it was the Order leg) |
| Domain service returns a fault (e.g., unhandled exception) | `RequestFaultException` in the coordinator → `Status: Unavailable` finding |
| All three legs down | All three findings end up non-`Succeeded` → saga finalizes `Failed`, tool returns `success: false` |
| Coordinator process crashes mid-fan-out | `BeginInvestigationFanOut` redelivered by broker; fan-out reruns from scratch (idempotent, read-only) |
| Orchestrator process crashes after some findings recorded, before all three | Saga row already persisted with partial state; remaining `XFindingReported` events (once their sources' consumers finish, or once redelivered) are applied to the same row on restart — no data lost (FR-010) |
