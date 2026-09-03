# Research: Approval-Gated Order Actions

**Branch**: `006-approval-actions` | **Date**: 2026-09-02

## Decision 1: Saga/Consumer Split — Same Shape as Feature 005, Extended With a Decision Gate

**Decision**: `OrderActionSaga` (`MassTransitStateMachine<OrderActionSagaState>`) owns persisted state and finalization only; two plain consumers do the actual imperative work, exactly mirroring 005's `OrderInvestigationSaga` / `InvestigationFanOutConsumer` split:
- `OrderActionValidationConsumer` (`IConsumer<BeginActionValidation>`) — confirms the order exists before the saga ever enters `AwaitingApproval`, by reusing feature 005's existing `RequestOrderFinding`/`OrderFindingReported` contract and `RequestOrderFindingConsumer` in `NexusOps.OrderService` (no new contract needed for this step).
- `OrderActionExecutionConsumer` (`IConsumer<BeginOrderActionExecution>`) — does the order mutation, and for a cancellation, the inventory restock and any compensating reversal, each via its own bounded `IRequestClient<T>` call.

**Rationale**: Reuses a pattern already proven to satisfy Constitution Principle I (saga contains no blocking I/O) and FR-009-style optimistic concurrency (multiple independent messages racing to update the same saga row). Reusing `RequestOrderFinding` for validation is a direct, additive reuse of existing, tested infrastructure rather than a new order-lookup contract.

**Alternatives considered**: Skip validation and let a nonexistent order surface only at execution time → rejected: spec.md User Story 1 Acceptance Scenario 3 requires a not-found order to never produce a pending reference at all, not merely to fail later after an approver has already reviewed it.

**Implementation note — avoiding cross-saga broadcast noise**: `OrderActionValidationConsumer` calls `IRequestClient<RequestOrderFinding>.GetResponse<OrderFindingReported>(...)` (reusing 005's request/response leg verbatim), but does **not** re-publish the raw `OrderFindingReported` event the way 005's `InvestigationFanOutConsumer` does — that event type is also consumed by `OrderInvestigationSaga` (`OnMissingInstance(Discard)` there makes it harmless, but broadcasting a second saga's validation traffic onto a queue 005 owns is unnecessary noise). Instead, the consumer maps the response into a new, 006-owned event, `ActionValidationCompleted(CorrelationId, Status, Order)`, and publishes that. `OrderActionSaga` binds only to `ActionValidationCompleted`, never to `OrderFindingReported` directly — the two sagas share a request/response contract (leg 2) but not a published-event subscription.

---

## Decision 2: Two Initiating Request Types, One Saga

**Decision**: `RequestOrderRefund(OrderId, Amount?, Reason?)` and `RequestOrderCancellation(OrderId, Reason?)` are distinct message contracts (and distinct curated tools), but both correlate into the same `OrderActionSaga` via `Initially(When(RefundRequested)... / When(CancellationRequested)...)`, each setting `ActionType` on the saga state. Both respond through the same `OrderActionRequestResult` shape.

**Rationale**: "Refund" and "cancel" are different curated tools (Constitution II — distinct domain intent, distinct parameters) but functionally identical workflows (validate → await approval → execute one dependency, optionally a second → notify). One saga with an `ActionType` discriminator avoids duplicating the entire approval-gate/compensation/notification machinery twice, matching this project's precedent of one saga type covering a family of closely related operations.

**Alternatives considered**: Two separate saga types (`OrderRefundSaga`, `OrderCancellationSaga`) → rejected: near-total duplication of the approval-gate state machine and notification logic for a difference that is really just "one dependency vs. two," which the execution consumer already branches on cleanly.

---

## Decision 3: Approval/Rejection Are Synchronous Request/Response, Not Fire-and-Forget

**Decision**: `POST /api/approvals/{id}/approve` and `/reject` are backed by `IRequestClient<ApproveOrderAction>` / `IRequestClient<RejectOrderAction>` (registered directly via `x.AddRequestClient<T>()` in `NexusOps.AgentHost/Program.cs` and injected straight into the minimal-API endpoint delegate — endpoints get proper per-request scoped DI, unlike `OrderTools`, so the `IClientFactory` workaround `OrderTools.cs` needs is not needed here). Approval blocks (bounded timeout) until the saga's execution consumer reports a final outcome, so the HTTP response to `/approve` carries the real result (executed / failed / failed-and-compensated), not just an interim acknowledgment. Rejection responds immediately — nothing to wait for.

**Rationale**: `curl -X POST /api/approvals/{id}/approve` returning the actual outcome is a far better manual-verification experience than a bare "accepted" that forces a second call to check what happened, and it directly mirrors 005 Decision 2's own AgentHost-request-client-awaits-saga-completion pattern. A `MassTransitStateMachine`'s `During(...)` event bindings drive the response the same way 005's `FinalizeIfCompleteAsync` does: the state that receives `Approve` captures a *second* response address (distinct from the one captured at the original request), and the state that finalizes execution resolves and sends to it.

**Alternatives considered**: Approve/reject publish fire-and-forget commands and the caller polls a status endpoint → rejected: adds a second HTTP surface and poll-interval latency for no benefit over a bounded request/response, and breaks the "one curl call, one true answer" verification story `ROADMAP.md`'s definition of done assumes.

---

## Decision 4: Idempotent Decisions Fall Out of the State Machine's Own Shape

**Decision**: `Approve`/`Reject` are only handled `During(AwaitingApproval, ...)`. Every other state (`Executing`, `Completed`, `Rejected`, `Failed`) explicitly handles both events by responding `AlreadyDecided` rather than ignoring them (unlike 005's findings, this is request/response — the caller needs an explicit reply, so `Ignore(...)` is wrong here; each terminal-ish state gets its own `.Respond(...)`). A reference with no matching saga instance uses `OnMissingInstance(m => m.ExecuteAsync(...RespondAsync(NotFound)...))` rather than `Discard()`, again because the caller needs a reply.

**Rationale**: This directly implements FR-008/FR-009/SC-008 (a decision applies at most once; a duplicate or racing decision is reported, not silently executed twice or silently dropped) as a structural property of the state machine rather than an extra idempotency table or lock. Two concurrent decisions for the same reference are serialized by the saga repository's optimistic-concurrency row update (Decision 6 below): whichever consumes first sees `AwaitingApproval` and transitions away from it; the other retries against the now-changed row and lands in the terminal handler.

**Alternatives considered**: A separate "decision already recorded" flag checked imperatively inside a single shared handler → rejected: reimplements what the state machine's `During(...)` state gating already gives for free, and is easier to get subtly wrong (e.g., forgetting to check the flag on one new terminal state added later).

---

## Decision 5: Compensation Scope — Cancellation's Second Dependency Is Inventory Restock

**Decision**: A refund has exactly one dependency (the order itself: `ExecuteOrderMutation` → `OrderStatus.Refunded`), so it has no compensation scenario of its own. A cancellation has two: the order (`ExecuteOrderMutation` → `OrderStatus.Cancelled`) and, only if that succeeds, the inventory reserved by the order's line items (`ExecuteInventoryRestock`). If the inventory step fails after the order step succeeded, `OrderActionExecutionConsumer` calls `CompensateOrderMutation` to revert the order back to the status it held before execution began, and the outcome is reported `FailedAndCompensated`, not `Executed`. If the order step itself fails, nothing has changed yet, so no compensation is attempted (`Failed`).

**Rationale**: This is the most direct, honest reading of the originating instruction's own example ("the saga's action succeeds against one dependency but fails against another") using dependencies that already exist in this system — no third domain needs to be invented. It also produces a demonstration path that does not require modifying seed data or fabricating a failure: `quickstart.md` verifies it the same way 005 verified degradation — stopping `InventoryService` mid-cancellation.

**Alternatives considered**: Treat a Notification Service delivery failure as a compensation trigger (the instruction's other example, "the notification step fails after the action already executed") → rejected: reversing an already-successful, already-approved order mutation because a best-effort, asynchronous notification could not be delivered would undo real, human-approved work over an unrelated side channel's hiccup — inconsistent with treating the mutation as the thing of record. Notification delivery failure is instead handled by durability (Decision 9): the event is never lost, only possibly delayed, so nothing needs to be reversed.

---

## Decision 6: Persistence, Optimistic Concurrency, and the Transactional Outbox

**Decision**: `OrderActionSagaState` is persisted the same way as 005's saga (EF Core repository, `ConcurrencyMode.Optimistic`, `uint RowVersion` mapped to Postgres `xmin`), in its own `OrderActionDbContext`/table in the same `workfloworchestrator` database. Unlike 005, this saga's bus registration also adds MassTransit's transactional outbox (`AddEntityFrameworkOutbox<OrderActionDbContext>` + `cfg.UseBusOutbox()` on the WorkflowOrchestrator's `UsingRabbitMq` configuration), scoped to this saga's `DbContext`.

**Rationale**: 005's `plan.md` Open Questions table flagged this precisely: a saga that publishes side-effecting commands (`BeginOrderActionExecution`, which ultimately drives real order/inventory mutations) from the same `Initially(When(...))`/`During(...)` shape as 005's read-only saga cannot rely on "a redelivered message just reruns harmlessly" the way a discarded duplicate finding could — a duplicate execution here means a duplicate refund. The outbox ties the saga's own state write and its outbound publish to one transaction and de-duplicates redelivered inbound messages, which is the standard, documented MassTransit mechanism for exactly this problem, not a hand-rolled one.

**Accepted residual gap** (documented rather than silently left, per this project's practice): the outbox protects the saga's own publish/consume boundary; it does not make `ExecuteOrderMutationConsumer`'s mutation itself idempotent against every conceivable redelivery of `BeginOrderActionExecution` after a mid-execution crash of `OrderActionExecutionConsumer` (a plain consumer, not a saga, so it is not itself outbox-covered). The secondary safety net is FR-013's own eligibility check: a redelivered execution that reruns after already succeeding finds the order already in its target status and reports a (misleading, but non-corrupting) failure rather than silently double-refunding. This is recorded as an accepted POC-scope limitation in `plan.md`'s Open Questions, not engineered further here.

**Alternatives considered**: Hand-rolled idempotency key stored per `CorrelationId` in `OrderActionSagaState`, checked by the execution consumer before mutating → rejected as unnecessary complexity beyond what the outbox already buys, for a POC.

---

## Decision 7: Domain-Service Mutation State — an Additive In-Memory Overlay, Not a Store Rewrite

**Decision**: `NexusOps.OrderService.Data.OrderStore.GetOrders(today)` and `NexusOps.InventoryService.Data.InventoryStore.Records` remain exactly as they are today — pure, regenerated-per-call (Order) or static-readonly (Inventory) seed data, unmodified, so every existing test and call site is untouched. Each service gains a new singleton, thread-safe overlay registered in DI:
- `OrderService`: `OrderMutationOverlay` (`ConcurrentDictionary<string, OrderStatus>`, keyed by `OrderId`) recording a status override. A new extension method applies the overlay on top of a freshly-seeded `Order` wherever one is read (`OrderEndpoints`, `RequestOrderFindingConsumer`), so a refund or cancellation is visible through every existing read path, including feature 005's root-cause investigation, not just this feature's own contracts.
- `InventoryService`: `InventoryMutationOverlay` (`ConcurrentDictionary<string, int>`, keyed by `Sku`) recording a cumulative quantity delta applied on top of the seeded `QuantityOnHand`, applied the same way in `InventoryEndpoints` and `RequestInventoryFindingConsumer`.

`OrderStatus` gains one new value, `Refunded` (purely additive to the existing enum — `Pending, Processing, Shipped, Delivered, Delayed, Cancelled, Refunded`).

**Rationale**: Both stores are currently stateless-by-construction (a real mutation has nothing to persist into), which is correct for a read-only system but cannot represent this feature's requirement that "the order is updated to reflect the refund" (spec.md User Story 2). An overlay is the minimal-touch way to introduce real, process-lifetime-durable mutation state without restructuring either store's existing seeding logic or its existing test suite (`NexusOps.Tests/Orders/OrderStoreTests.cs`, `AnomalySelectorTests.cs` call `OrderStore.GetOrders(today)` directly and are unaffected). It also gives US2's "the order is updated" acceptance scenario a free, correct answer through the *existing* `GET /orders/{id}` and `investigate_order_root_cause` paths, with no special-casing needed in either.

**Alternatives considered**: Convert `OrderStore`/`InventoryStore` into fully stateful, mutable repositories (replace the seed function with a `ConcurrentDictionary<string, Order>` singleton built once at startup) → rejected for this feature: a larger refactor of proven, tested code for no behavior this feature actually needs beyond "remember an override," carrying real regression risk to feature 001/005's existing test coverage for no corresponding benefit.

---

## Decision 8: Refund/Cancellation Execution Contracts

**Decision**: One shared request/response pair handles both action types against `OrderService`: `ExecuteOrderMutation(CorrelationId, ActionType, OrderId, Amount?)` / `OrderMutationExecuted(CorrelationId, Success, FailureReason?, PriorStatus, LineItems)` — `ActionType` (the shared `OrderActionType` enum) tells the consumer which target status to apply (`Refunded` or `Cancelled`); it responds `Success: false` with a `FailureReason` if the order is already in a terminal status inconsistent with the request (FR-013), never throwing or silently mutating an ineligible order. A separate, single-purpose pair reverses it: `CompensateOrderMutation(CorrelationId, OrderId, RevertToStatus)` / `OrderMutationCompensated(CorrelationId, Success)`. Cancellation's inventory leg is `ExecuteInventoryRestock(CorrelationId, OrderId, Lines[])` / `InventoryRestockExecuted(CorrelationId, Success, FailureReason?)` against `InventoryService`.

**Rationale**: One execute contract instead of two (`ExecuteOrderRefund` + `ExecuteOrderCancellation`) avoids duplicating near-identical request/response shapes and consumer logic for what is, from `OrderService`'s point of view, the same operation ("set this order's status, if eligible") with a different target value — matching Constitution IV's "domain services expose consumers for saga-dispatched commands" without inventing more consumers than the actual variation in behavior warrants.

**Alternatives considered**: Separate `ExecuteOrderRefund`/`ExecuteOrderCancellation` contracts and consumers → rejected: the two operations differ only in target status and whether a second dependency follows, both of which `OrderActionExecutionConsumer` (not `OrderService`) already needs to know regardless: `OrderService`'s own consumer logic does not otherwise differ.

---

## Decision 9: Notification Delivery — Fire-and-Forget Publish, Durable Queue, `[EntityName]`-Pinned Exchange

**Decision**: `OrderActionSaga` publishes `NotificationRequested(CorrelationId, OrderId, ActionType, Outcome, Message)` (`Outcome` ∈ the shared `Executed`/`Rejected`/`Failed`/`FailedAndCompensated` vocabulary) via `context.Publish(...)`, once per terminal outcome, and does not wait for or depend on delivery to finalize. The message is decorated `[MassTransit.EntityName("notification-requested")]` (requiring a new, lightweight `MassTransit.Abstractions` package reference on `NexusOps.Contracts` — no transport dependency), which pins the RabbitMQ exchange name MassTransit publishes to, rather than leaving it to MassTransit's default CLR-type-derived naming. The Node.js Notification Service declares a durable fanout exchange of that exact name, binds its own durable queue to it, and consumes with manual ack (`noAck: false`) so an in-flight message survives a Notification Service restart.

**Rationale**: A pinned `EntityName` gives the Node consumer (which has no access to MassTransit's .NET topology conventions) one fixed, documented exchange name to bind against instead of having to reverse-engineer MassTransit's default naming scheme — directly serving Constitution IV's "Notification Service MUST interoperate with MassTransit's wire protocol." Fire-and-forget publish (rather than request/response) matches "the saga executes and publishes a NotificationRequested event" from the originating instruction exactly, and satisfies User Story 5 Acceptance Scenario 4 (a temporarily-unavailable Notification Service does not lose the notification) for free via RabbitMQ's own durable-queue redelivery — no saga-side retry logic is needed for this leg.

**Alternatives considered**: Let MassTransit's default message-type-derived exchange name stand, and have the Node service compute the same name from the same convention → rejected: reimplementing MassTransit's internal naming convention in hand-written JavaScript is exactly the kind of cross-runtime coupling `[EntityName]` exists to avoid; a pinned name is one line in Contracts and removes the whole problem.

---

## Decision 10: Notification Service Shape — Minimal, No Framework, Health-Checked

**Decision**: `notification-service/` is a small TypeScript project (no Express or other web framework) with two concerns run side by side: (1) an `amqplib` consumer loop as described in Decision 9, logging one structured JSON line per notification (`{ timestamp, level: "info", event: "notification.logged", correlationId, orderId, actionType, outcome, message }`) to stdout; (2) a bare `node:http` server exposing `GET /health` returning `{"status":"healthy"}`, satisfying Constitution VI's "every service MUST expose a `/health` HTTP health check endpoint" and giving the Aspire AppHost a `WithHttpHealthCheck("/health")` target exactly like every .NET service. Wired into `NexusOps.AppHost` via `Aspire.Hosting.NodeJs`'s `AddNpmApp("notification-service", "../notification-service")`, referencing RabbitMQ the same way every other bus participant does (`WithReference(rabbitmq).WaitFor(rabbitmq)`).

**Rationale**: `ROADMAP.md`'s locked decision is explicit — "minimal Node/TS RabbitMQ consumer that logs simulated emails — nothing more." A framework, a database, or a richer API surface would be scope creep this project's own roadmap forbids. Structured JSON stdout lines are the practical way to satisfy "emit structured JSON logs compatible with the Aspire telemetry pipeline" for a Node process without adding a full OpenTelemetry JS SDK, which `ROADMAP.md`'s "nothing more" instruction rules out for this feature.

**Alternatives considered**: A full OTEL Node SDK for parity with the .NET services' tracing/metrics → rejected as disproportionate to "logs a simulated email," and not required by the constitution's actual text (which asks for structured JSON logs, not distributed tracing, from this specific service).

---

## Resolved Unknowns

| Unknown | Resolution |
|---|---|
| How does a not-found order avoid ever creating a pending reference? | `BeginActionValidation` + reused `RequestOrderFinding`/`OrderFindingReported` before transitioning to `AwaitingApproval` (Decision 1) |
| How do two curated tools (refund, cancellation) share one saga without duplicating the approval gate? | One `OrderActionSaga`, `ActionType`-discriminated (Decision 2) |
| How does `/approve` return a real outcome instead of a bare acknowledgment? | Synchronous request/response with a second, execution-scoped response address captured at the `Approve` transition (Decision 3) |
| How is "at most once" decision-application enforced? | State-gated `During(...)` handlers + `OnMissingInstance` responding, not discarding (Decision 4) |
| What is cancellation's second, compensable dependency? | Inventory restock of the order's line items (Decision 5) |
| How is a duplicate execution from message redelivery prevented? | EF Core transactional outbox on `OrderActionDbContext`, with a documented residual gap (Decision 6) |
| Where does mutated order/inventory state actually live, given both stores are currently stateless seed functions? | Additive in-memory overlay per service, applied at every existing read path (Decision 7) |
| How many execute/compensate contracts does this feature need? | One shared `ExecuteOrderMutation`, one `CompensateOrderMutation`, one `ExecuteInventoryRestock` (Decision 8) |
| How does a plain Node.js/amqplib consumer bind to a MassTransit-published event without reimplementing MassTransit's naming convention? | `[EntityName("notification-requested")]` pins the exchange name (Decision 9) |
| What does "minimal" bound the Notification Service to? | amqplib consumer + bare `node:http` health endpoint, no framework, no OTEL SDK (Decision 10) |
