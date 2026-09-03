# Contract: OrderActionSaga Internal AMQP Contracts

**Branch**: `006-approval-actions` | **Date**: 2026-09-02

Full field-level shapes are defined in `data-model.md`; this document states the reliability requirements and legs each contract travels across, per Constitution IV.

## Legs

1. **Request** (AgentHost → saga): `RequestOrderRefund` / `RequestOrderCancellation`, request/response, `IRequestClient<T>` created via `IClientFactory` from `OrderTools.cs` (matching feature 005's own pattern for `InvestigateOrderRootCause`).
2. **Validation** (saga → `OrderActionValidationConsumer` → `NexusOps.OrderService`): `BeginActionValidation` (published event) → `RequestOrderFinding`/`OrderFindingReported` (feature 005's existing contract, reused verbatim, request/response) → the consumer maps the response and publishes `ActionValidationCompleted` (006-owned) back to the saga, rather than re-broadcasting `OrderFindingReported` itself (data-model.md).
3. **Approval decision** (AgentHost approval endpoints → saga): `ApproveOrderAction` / `RejectOrderAction`, request/response, `IRequestClient<T>` registered directly on the bus (endpoints get proper scoped DI, unlike the singleton `OrderTools`).
4. **Execution** (saga → `OrderActionExecutionConsumer`): `BeginOrderActionExecution` (published event).
5. **Order mutation** (`OrderActionExecutionConsumer` → `NexusOps.OrderService`): `ExecuteOrderMutation`/`OrderMutationExecuted`, request/response.
6. **Inventory restock** (`OrderActionExecutionConsumer` → `NexusOps.InventoryService`; cancellation only): `ExecuteInventoryRestock`/`InventoryRestockExecuted`, request/response.
7. **Compensation** (`OrderActionExecutionConsumer` → `NexusOps.OrderService`; only on a leg-6 failure following a leg-5 success): `CompensateOrderMutation`/`OrderMutationCompensated`, request/response.
8. **Completion** (`OrderActionExecutionConsumer` → saga): `OrderActionExecutionCompleted` (published event).
9. **Notification** (saga → Notification Service): `NotificationRequested` (published event, `[EntityName("notification-requested")]`), fire-and-forget from the saga's perspective.

## Reliability Requirements (Constitution IV)

- Every leg above travels over RabbitMQ via MassTransit; no leg is ever a direct HTTP call from saga-side (`.WorkflowOrchestrator`) code to a domain service or the Notification Service.
- `cfg.UseMessageRetry(r => r.Intervals(50, 100, 200, 500))` (matching 005's own `Program.cs` configuration) applies to `NexusOps.WorkflowOrchestrator`'s bus, covering legs 2–8's transient failures (including a saga optimistic-concurrency conflict surfacing as `DbUpdateConcurrencyException`) before falling through to broker-level dead-lettering.
- Legs 5, 6, and 7 (the mutation-bearing legs) are additionally covered by the EF Core transactional outbox on `OrderActionDbContext` (`research.md` Decision 6): the saga's own state commit and its `Publish(BeginOrderActionExecution)` are transactional together, so a crash between "saga state saved" and "message actually sent" cannot happen — the outbox either sends the message once the transaction commits, or does not send it at all if the transaction rolled back.
- Every request/response leg (1, 2, 3, 5, 6, 7) carries a bounded per-leg timeout, so no leg can hang the workflow indefinitely; a timed-out leg is treated as a failure by its caller (mirroring 005's `RequestTimeoutException`/`RequestFaultException` → `Unavailable`/`TimedOut` mapping in `InvestigationFanOutConsumer`).
- Leg 9 (notification) is published, not requested — RabbitMQ's own durable-queue redelivery (the Node.js consumer acks only after successfully logging) is the delivery guarantee; the saga does not block on or retry this leg itself (`research.md` Decision 9).

## Timeout Budget

| Leg | Timeout | Rationale |
|---|---|---|
| 2 (order lookup for validation) | 5s | Matches feature 005's own per-source timeout for the identical underlying call. |
| 5 (order mutation) | 5s | Single in-memory operation against `OrderService`; same order of magnitude as a read. |
| 6 (inventory restock) | 5s | Same as leg 5, against `InventoryService`. |
| 7 (compensation) | 5s | Same as leg 5; only invoked after a leg-6 failure. |
| 1 (initial request, AgentHost-side client) | 10s | Covers leg 2's worst case (5s) plus transit/serialization headroom; the saga responds as soon as validation finishes, before any approval-gated work begins. Widened from an initial 8s after live verification observed an occasional spurious timeout under host load with no corresponding broker backlog. |
| 3 (approval decision, AgentHost-side client) | 25s | Covers the worst-case execution chain: leg 5 (5s) + leg 6 (5s) + leg 7 (5s) = 15s, plus real headroom above that worst case — deliberately generous since a human is waiting on a `curl` call for a real answer, not a chat turn. Widened from an initial 20s (which left zero headroom above the 15s worst case) after code review flagged the same "single leg's figure, not the true worst case" mistake this project has now corrected three times: 005's root-cause timeout (8s→12s), this feature's own validation-request timeout (8s→10s, caught live), and this one (caught in review before it recurred live a third time). |
