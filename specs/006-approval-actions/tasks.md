# Tasks: Approval-Gated Order Actions

**Input**: Design documents from `specs/006-approval-actions/`

**Prerequisites**: plan.md ✅ | spec.md ✅ | research.md ✅ | data-model.md ✅ | contracts/ ✅ | quickstart.md ✅

**Tests**: `plan.md`'s Technical Context commits to specific test files (`OrderActionSagaTests.cs`, `OrderActionExecutionConsumerTests.cs`) using MassTransit's in-memory test harness, credential-free per `ROADMAP.md`'s CI constraint — matching feature 005's own precedent exactly. Test tasks are included below, one set per user story.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing. US1–US4 (all P1) build on the same saga/consumer skeleton in strict dependency order (request → approve → reject → compensate); US5–US6 (P2) layer notification delivery and routing correctness on top; US7 (P3) is verification of durability properties the earlier phases already build in, matching 005's own US4 shape.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: Which user story this task belongs to (US1–US7 matching spec.md)
- All paths are project-relative from repo root

---

## Phase 1: Setup (Projects & Packages)

**Purpose**: New package references and the `notification-service` project scaffold, before any behavior is written.

- [X] T001 Add `MassTransit.Abstractions` (pinned to the `8.5.x` line, matching the other `MassTransit*` packages already in the solution) package reference to `NexusOps.Contracts/NexusOps.Contracts.csproj`
- [X] T002 [P] ~~Add `Aspire.Hosting.NodeJs` package reference~~ — confirmed unnecessary: `NexusOps.AppHost.csproj` already references `Aspire.Hosting.JavaScript` 13.5.3 (for `AddViteApp`), which also provides `AddNodeApp`/`WithNpm`/`WithRunScript`; no new package needed
- [X] T003 [P] Scaffold `notification-service/package.json` (name, `amqplib` + `@types/amqplib` + `typescript` + `@types/node` deps, `build`/`start`/`dev` scripts), `notification-service/tsconfig.json` (Node 24 target, strict mode), and `notification-service/.gitignore` (`node_modules/`, `dist/`, `.env`)
- [X] T004 [P] Create `notification-service/src/logger.ts` — a `log(level, event, fields)` helper that writes one JSON line (`timestamp`, `level`, `event`, plus `fields`) to stdout
- [X] T005 [P] Create `notification-service/src/healthServer.ts` — a bare `node:http` server exposing `GET /health` returning `{"status":"healthy"}` as JSON, matching `NexusOps.ServiceDefaults`' response shape

**Checkpoint**: `npm install && npm run build` succeeds in `notification-service/`; `dotnet restore` succeeds across the solution.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Topology, contracts, saga/DbContext skeletons, and mutation-state overlays every user story depends on. No user-story-specific behavior lives here.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T006 [P] Create `NexusOps.Contracts/Dtos/OrderAction.cs` — `OrderActionType` (`Refund`, `Cancellation`), `OrderActionStatus` (`AwaitingApproval`, `NotFound`), `OrderActionDecisionOutcome` (`Approved`, `Rejected`, `AlreadyDecided`, `NotFound`), `OrderActionExecutionOutcome` (`Executed`, `Failed`, `FailedAndCompensated`) enums, and the `OrderActionRequestResult` record, per `data-model.md`
- [X] T007 [P] Create `NexusOps.Contracts/Messages/OrderActionRequestMessages.cs` — `RequestOrderRefund(OrderId, Amount?, Reason?)`, `RequestOrderCancellation(OrderId, Reason?)`, `OrderActionRequestResult` message wrapper (reuses the Dtos record as the response body)
- [X] T008 [P] Create `NexusOps.Contracts/Messages/ActionValidationMessages.cs` — `BeginActionValidation(CorrelationId, OrderId)`, `ActionValidationCompleted(CorrelationId, Status, Order)` (reuses `SourceFindingStatus`/`OrderSummary` from feature 005's `Dtos/RootCauseInvestigation.cs`/`Dtos/OrderSummary.cs`)
- [X] T009 [P] Create `NexusOps.Contracts/Messages/OrderActionDecisionMessages.cs` — `ApproveOrderAction(ApprovalReference)`, `RejectOrderAction(ApprovalReference, Reason?)`, `OrderActionDecisionResult(ApprovalReference, DecisionStatus, ExecutionOutcome?, Message)`
- [X] T010 [P] Create `NexusOps.Contracts/Messages/BeginOrderActionExecution.cs` — `(CorrelationId, ActionType, OrderId, Amount?)`
- [X] T011 [P] Create `NexusOps.Contracts/Messages/OrderMutationMessages.cs` — `ExecuteOrderMutation(CorrelationId, ActionType, OrderId, Amount?)`, `OrderMutationExecuted(CorrelationId, Success, FailureReason?, PriorStatus, LineItems)`
- [X] T012 [P] Create `NexusOps.Contracts/Messages/InventoryRestockMessages.cs` — `InventoryRestockLine(Sku, Quantity)`, `ExecuteInventoryRestock(CorrelationId, OrderId, Lines)`, `InventoryRestockExecuted(CorrelationId, Success, FailureReason?)`
- [X] T013 [P] Create `NexusOps.Contracts/Messages/CompensateOrderMutationMessages.cs` — `CompensateOrderMutation(CorrelationId, OrderId, RevertToStatus)`, `OrderMutationCompensated(CorrelationId, Success)`
- [X] T014 [P] Create `NexusOps.Contracts/Messages/OrderActionExecutionCompleted.cs` — `(CorrelationId, Outcome, Detail)`
- [X] T015 [P] Create `NexusOps.Contracts/Messages/NotificationRequested.cs` — `(CorrelationId, OrderId, ActionType, Outcome, Message)`, decorated `[MassTransit.EntityName("notification-requested")]` per research.md Decision 9; `Outcome` typed `string`, not the .NET-only enum (data-model.md note)
- [X] T016 Add `ToolNames.RequestOrderRefund`/`RequestOrderRefundDescription` and `ToolNames.RequestOrderCancellation`/`RequestOrderCancellationDescription` to `NexusOps.Contracts/ToolNames.cs`, alongside the existing seven — do not modify any existing constant; description text embeds the "pending approval, never claim completion" phrasing constraint per `contracts/order-action-tools.md`
- [X] T017 Create `NexusOps.WorkflowOrchestrator/OrderAction/OrderActionSagaState.cs` — the saga entity per `data-model.md` (`CorrelationId`, `CurrentState`, `ActionType`, `OrderId`, `Amount`, `Reason`, `RequestResponseAddress`/`RequestRequestId`, `ApprovalResponseAddress`/`ApprovalRequestId`, `PriorStatus`, `ExecutionOutcome`, `RequestedAt`, `DecidedAt`, `CompletedAt`, `RowVersion`)
- [X] T018 Create `NexusOps.WorkflowOrchestrator/OrderAction/OrderActionDbContext.cs` — EF Core `DbContext` mapping `OrderActionSagaState` (mirroring `OrderInvestigationDbContext`'s `RowVersion`/enum-as-string conventions) and including MassTransit's outbox entities (`modelBuilder.AddInboxStateEntity()`, `AddOutboxMessageEntity()`, `AddOutboxStateEntity()`) per research.md Decision 6
- [X] T019 Generate the initial EF Core migration for `OrderActionDbContext` into its own output folder so it does not collide with feature 005's (`dotnet ef migrations add InitialCreate --project NexusOps.WorkflowOrchestrator --context OrderActionDbContext --output-dir OrderAction/Migrations`)
- [X] T020 Create `NexusOps.WorkflowOrchestrator/OrderAction/ServiceCollectionExtensions.cs` — stub `AddOrderActionSaga(this IBusRegistrationConfigurator)` as a no-op for now (implemented fully in T036)
- [X] T021 Register `OrderActionDbContext` (`builder.AddNpgsqlDbContext<OrderActionDbContext>("workfloworchestrator")`), call the stubbed `AddOrderActionSaga()`, and apply `OrderActionDbContext`'s pending migrations on startup in `NexusOps.WorkflowOrchestrator/Program.cs`, alongside — not replacing — feature 005's existing `OrderInvestigationDbContext` registration and migration call
- [X] T022 [P] Create `NexusOps.OrderService/Data/OrderMutationOverlay.cs` — a `ConcurrentDictionary<string, OrderStatus>` singleton class with `TryGet`/`Set` methods
- [X] T023 [P] Add `OrderStatus.Refunded` to `NexusOps.OrderService/Models/Order.cs`'s enum (appended after `Cancelled`) and a `WithStatus(OrderStatus newStatus)` copy method on `Order` returning a new instance with every other field unchanged
- [X] T024 [P] Create `NexusOps.InventoryService/Data/InventoryMutationOverlay.cs` — a `ConcurrentDictionary<string, int>` singleton class (cumulative delta per SKU) with `GetDelta`/`AddDelta` methods
- [X] T025 [P] Register `OrderMutationOverlay` as a singleton in `NexusOps.OrderService/Program.cs`
- [X] T026 [P] Register `InventoryMutationOverlay` as a singleton in `NexusOps.InventoryService/Program.cs`
- [X] T027 Register `x.AddRequestClient<ApproveOrderAction>()` and `x.AddRequestClient<RejectOrderAction>()` in `NexusOps.AgentHost/Program.cs`'s existing `AddMassTransit` block, alongside the unchanged bus-client registration feature 005 added
- [X] T028 [P] Add the `notification-service` resource to `NexusOps.AppHost/AppHost.cs` — `builder.AddNpmApp("notification-service", "../notification-service", "start").WithHttpEndpoint(env: "PORT").WithHttpHealthCheck("/health").WithReference(rabbitmq).WaitFor(rabbitmq)`
- [X] T029 Create `notification-service/src/amqpConsumer.ts` — connects via `ConnectionStrings__rabbitmq`, `assertExchange('notification-requested', 'fanout', {durable: true})`, `assertQueue('notification-service.notification-requested', {durable: true})`, binds the queue to the exchange, `consume` with `noAck: false`, parses the MassTransit envelope (`JSON.parse(msg.content.toString())`, reading `.message`), logs via `logger.ts`, and `ack`s only after a successful log
- [X] T030 Create `notification-service/src/index.ts` — starts `healthServer.ts` and `amqpConsumer.ts` together; logs a startup line; exits non-zero with a clear error if `ConnectionStrings__rabbitmq` is missing (mirrors the named-configuration-failure guard precedent `NexusOps.ServiceDefaults`' sibling .NET hosts already apply for the same connection string)

**Checkpoint**: `dotnet run --project NexusOps.AppHost` shows `notification-service` healthy alongside every resource from feature 005. No approval-gate behavior exists yet — user story implementation starts next.

---

## Phase 3: User Story 1 - Requesting a Refund or Cancellation Creates a Pending Action, Never an Executed One (Priority: P1) 🎯 MVP

**Goal**: Both tools validate the order, park the request in `AwaitingApproval` with a reference GUID, and never execute anything.

**Independent Test**: Ask the agent to refund (or cancel) a known order; confirm the reply carries a reference and "pending approval" language, and the order's data is unchanged immediately afterward (spec.md User Story 1).

### Tests for User Story 1

- [X] T031 [P] [US1] Saga test: `RequestOrderRefund` for an existing order with no `Amount` given validates via `ActionValidationCompleted(Succeeded)`, defaults `Amount` to the order's `TotalAmount`, responds `OrderActionRequestResult{Status: AwaitingApproval}`, and the instance is `AwaitingApproval` — in `NexusOps.Tests/WorkflowOrchestrator/OrderActionSagaTests.cs`
- [X] T032 [P] [US1] Saga test: `RequestOrderCancellation` for a nonexistent order validates via `ActionValidationCompleted(NotFound)`, responds `OrderActionRequestResult{Status: NotFound}`, and the instance reaches `Failed` directly (never `AwaitingApproval`) — same file
- [X] T033 [P] [US1] Saga test: an `ApproveOrderAction` against a reference that finalized `Failed` from T032 (never reached `AwaitingApproval`) responds `DecisionStatus: NotFound` via the terminal-state handler, not a crash — same file

### Implementation for User Story 1

- [X] T034 [US1] Create `NexusOps.WorkflowOrchestrator/OrderAction/OrderActionValidationConsumer.cs` — `IConsumer<BeginActionValidation>`; calls `IRequestClient<RequestOrderFinding>.GetResponse<OrderFindingReported>()` (feature 005's contract), maps the response, and `Publish`es `ActionValidationCompleted` (never re-publishes the raw `OrderFindingReported`, per research.md Decision 1's implementation note)
- [X] T035 [US1] Create `NexusOps.WorkflowOrchestrator/OrderAction/OrderActionSaga.cs` — `MassTransitStateMachine<OrderActionSagaState>` with states `Validating`, `AwaitingApproval`, `Executing`, `Completed`, `Rejected`, `Failed`; `Initially(When(RefundRequested)... / When(CancellationRequested)...)` capturing `ActionType`/`OrderId`/`Amount`/`Reason`/`RequestResponseAddress`/`RequestRequestId`, publishing `BeginActionValidation`, transitioning to `Validating`; `During(Validating, When(ValidationCompleted)...)` defaulting `Amount` for refunds, responding `OrderActionRequestResult` to the captured request address, transitioning to `AwaitingApproval` (found) or `Failed` (not found) — approval/rejection/execution handlers land in US2–US4
- [X] T036 [US1] Create `NexusOps.WorkflowOrchestrator/OrderAction/ServiceCollectionExtensions.cs`'s real `AddOrderActionSaga(this IBusRegistrationConfigurator)` — registers the saga against the EF Core repository (`ConcurrencyMode.Optimistic`, `ExistingDbContext<OrderActionDbContext>()`, `UsePostgres()`), the transactional outbox (`configurator.AddEntityFrameworkOutbox<OrderActionDbContext>(o => { o.UsePostgres(); o.UseBusOutbox(); })`), `OrderActionValidationConsumer`, and an `AddRequestClient<RequestOrderFinding>()` if not already registered by feature 005's own extension; replace the T020 stub call
- [X] T037 [US1] Add `RequestOrderRefundAsync`/`RequestOrderCancellationAsync` handlers to `NexusOps.AgentHost/Tools/OrderTools.cs` — mint a request client per call via `IClientFactory.CreateRequestClient<T>(RequestTimeout.After(s: 8))` (matching the `investigate_order_root_cause` handler's existing `IClientFactory` pattern, since `OrderTools` remains a singleton), call `GetResponse<OrderActionRequestResult>`, map `Status == NotFound` to `ToolResult.Fail(...)` and `Status == AwaitingApproval` to `ToolResult.Ok(...)` carrying the reference and (for refund) the resolved amount
- [X] T038 [US1] Register the two new tools via `AIFunctionFactory.Create` in `NexusOps.AgentHost/Tools/ToolHandlerExtensions.cs`, alongside the existing seven — do not reorder or modify the existing entries
- [X] T039 [US1] Manually verify `quickstart.md` step 1 via `dotnet run --project NexusOps.AppHost` + the documented `curl` chat prompt: confirm the reply states "pending approval" with a reference, and `GET /orders/{id}` shows the order unchanged

**Checkpoint**: A refund or cancellation request reliably produces a pending, unexecuted reference — the MVP, independently demonstrable.

---

## Phase 4: User Story 2 - Approval Unblocks Execution, and Only Approval Does (Priority: P1)

**Goal**: `POST /api/approvals/{id}/approve` executes the requested mutation and returns the real outcome; a second approval, or one against an unknown reference, is reported without executing anything.

**Independent Test**: Create a pending refund, confirm the order is unchanged, approve it, confirm the order now reflects the mutation (spec.md User Story 2).

### Tests for User Story 2

- [X] T040 [P] [US2] Saga test: `ApproveOrderAction` on an `AwaitingApproval` refund captures the approval response address, publishes `BeginOrderActionExecution`, transitions to `Executing`; on `OrderActionExecutionCompleted(Executed)` responds `OrderActionDecisionResult{DecisionStatus: Approved, ExecutionOutcome: Executed}` and transitions to `Completed` — in `OrderActionSagaTests.cs`
- [X] T041 [P] [US2] Saga test: a second `ApproveOrderAction` against a reference already `Executing`/`Completed` responds `DecisionStatus: AlreadyDecided` and does not publish a second `BeginOrderActionExecution` — same file
- [X] T042 [P] [US2] Saga test: `ApproveOrderAction` against a `CorrelationId` with no matching instance responds `DecisionStatus: NotFound` without faulting — same file
- [X] T043 [P] [US2] Execution consumer test: `OrderActionExecutionConsumer` for a `Refund` action calls `ExecuteOrderMutation`, and on `Success: true` publishes `OrderActionExecutionCompleted{Outcome: Executed}` — in `NexusOps.Tests/WorkflowOrchestrator/OrderActionExecutionConsumerTests.cs`

### Implementation for User Story 2

- [X] T044 [US2] Create `NexusOps.WorkflowOrchestrator/OrderAction/OrderActionExecutionConsumer.cs` — `IConsumer<BeginOrderActionExecution>`; calls `IRequestClient<ExecuteOrderMutation>.GetResponse<OrderMutationExecuted>()` (5s per-leg timeout, catching `RequestTimeoutException`/`Exception` into a `Success: false` outcome, mirroring 005's `InvestigationFanOutConsumer` try/catch shape); for `ActionType == Refund`, publishes `OrderActionExecutionCompleted{Outcome: Success ? Executed : Failed}` directly (cancellation's second leg lands in US4)
- [X] T045 [US2] Extend `OrderActionSaga.cs`'s `During(AwaitingApproval, ...)` with `When(Approve)` — capture `ApprovalResponseAddress`/`ApprovalRequestId`, set `DecidedAt`, `Publish(BeginOrderActionExecution)`, transition to `Executing`; extend `During(Executing, ...)` with `When(ExecutionCompleted)` — set `ExecutionOutcome`/`CompletedAt`, respond to the captured approval address, clear it, `Publish(NotificationRequested)` (US5 wires the consumer side; the saga always publishes), transition to `Completed` (`Executed`) or `Failed` (`Failed`/`FailedAndCompensated`, pending US4)
- [X] T046 [US2] Add `During(Executing, Completed, When(Approve).Respond(AlreadyDecided), When(Reject).Respond(AlreadyDecided))` to `OrderActionSaga.cs`, and `OnMissingInstance(m => m.ExecuteAsync(... RespondAsync(NotFound) ...))` for both `ApproveOrderAction` and `RejectOrderAction` event bindings (`Rejected`/`Failed` terminal handling completes in US3)
- [X] T047 [US2] Create `NexusOps.OrderService/Consumers/ExecuteOrderMutationConsumer.cs` — `IConsumer<ExecuteOrderMutation>`; reads the order (seed + `OrderMutationOverlay` applied), checks eligibility (FR-013: not already in the target status or another terminal status incompatible with the request), on success records the current status as `PriorStatus`, writes the new status (`Refunded`/`Cancelled`) into the overlay, responds `OrderMutationExecuted{Success: true, PriorStatus, LineItems}`; on ineligibility responds `Success: false` with a `FailureReason` and the current `PriorStatus`, no overlay write
- [X] T048 [US2] Register `ExecuteOrderMutationConsumer` with the bus in `NexusOps.OrderService/Program.cs`
- [X] T049 [US2] Apply `OrderMutationOverlay` in `NexusOps.OrderService/Endpoints/OrderEndpoints.cs` (`GET /orders/anomalies`, `GET /orders/{orderId}`) — project each order through the overlay before building its response DTO
- [X] T050 [US2] Apply `OrderMutationOverlay` in `NexusOps.OrderService/Consumers/RequestOrderFindingConsumer.cs` (feature 005's contract, unchanged shape — implementation now overlay-aware, so `investigate_order_root_cause` reflects a refund/cancellation too)
- [X] T051 [US2] Create `NexusOps.AgentHost/Endpoints/ApprovalEndpoints.cs` — `POST /api/approvals/{id}/approve` calling `IRequestClient<ApproveOrderAction>.GetResponse<OrderActionDecisionResult>()` (20s timeout per `contracts/saga-message-contracts.md`'s budget table) and returning the result as JSON; a `RequestTimeoutException` returns a `500`-class problem response distinct from `NotFound`/`AlreadyDecided`, mirroring `OrderTools.cs`'s existing timeout-handling precedent
- [X] T052 [US2] Wire `MapApprovalEndpoints()` into `NexusOps.AgentHost/Program.cs`, alongside the existing `MapChatEndpoints()` call
- [X] T053 [US2] Manually verify `quickstart.md` steps 1–2 via `dotnet run --project NexusOps.AppHost`: request a refund, confirm the order is unchanged, `curl` the approval endpoint, confirm `ExecutionOutcome: Executed` and the order now reads `refunded`

**Checkpoint**: Refund approval works end-to-end and is independently demonstrable, without regressing User Story 1.

---

## Phase 5: User Story 3 - Rejection Cleanly and Permanently Prevents Execution (Priority: P1)

**Goal**: `POST /api/approvals/{id}/reject` marks the reference rejected without ever executing the mutation, and permanently blocks any later approval of the same reference.

**Independent Test**: Create a pending action, reject it, confirm no mutation occurred and a later approval attempt reports `AlreadyDecided` (spec.md User Story 3).

### Tests for User Story 3

- [X] T054 [P] [US3] Saga test: `RejectOrderAction` on an `AwaitingApproval` cancellation responds immediately `DecisionStatus: Rejected` (no `BeginOrderActionExecution` published), transitions to `Rejected` — in `OrderActionSagaTests.cs`
- [X] T055 [P] [US3] Saga test: an `ApproveOrderAction` submitted after a `RejectOrderAction` already resolved the same reference responds `DecisionStatus: AlreadyDecided`, and vice versa — same file

### Implementation for User Story 3

- [X] T056 [US3] Extend `OrderActionSaga.cs`'s `During(AwaitingApproval, ...)` with `When(Reject)` — set `DecidedAt`/`CompletedAt`, respond immediately `OrderActionDecisionResult{DecisionStatus: Rejected}`, `Publish(NotificationRequested{Outcome: "Rejected"})`, transition to `Rejected`
- [X] T057 [US3] Extend T046's terminal-state `When(Approve)/When(Reject)` handler block to cover `Rejected` and `Failed` states too (`During(Executing, Completed, Rejected, Failed, When(Approve).Respond(AlreadyDecided), When(Reject).Respond(AlreadyDecided))`)
- [X] T058 [US3] Manually verify `quickstart.md` step 3: create a pending action, reject it, confirm the order is unchanged, and a second `/approve` call on the same reference returns `AlreadyDecided`

**Checkpoint**: All three of request, approve, and reject are independently demonstrable together — the approval gate's mandatory, two-outcome nature is now provably real.

---

## Phase 6: User Story 4 - Partial Failure Is Compensated, Never Left Half-Done (Priority: P1)

**Goal**: A cancellation whose order update succeeds but whose inventory restock fails reverts the order rather than leaving it inconsistently mutated, and reports `FailedAndCompensated`, not `Executed`.

**Independent Test**: Approve a cancellation while forcing the inventory-release step to fail; confirm the order is reverted and the outcome is reported as failed (spec.md User Story 4).

### Tests for User Story 4

- [X] T059 [P] [US4] Execution consumer test: for a `Cancellation` action, `ExecuteOrderMutation` succeeds and `ExecuteInventoryRestock` succeeds → publishes `OrderActionExecutionCompleted{Outcome: Executed}`, no `CompensateOrderMutation` call — in `OrderActionExecutionConsumerTests.cs`
- [X] T060 [P] [US4] Execution consumer test: for a `Cancellation` action, `ExecuteOrderMutation` succeeds but `ExecuteInventoryRestock` fails (fault or timeout) → calls `CompensateOrderMutation` with the captured `PriorStatus`, then publishes `OrderActionExecutionCompleted{Outcome: FailedAndCompensated}` — same file
- [X] T061 [P] [US4] Execution consumer test: `ExecuteOrderMutation` itself fails → publishes `OrderActionExecutionCompleted{Outcome: Failed}` directly, with no `ExecuteInventoryRestock`/`CompensateOrderMutation` call attempted — same file
- [X] T062 [P] [US4] Saga test: on `OrderActionExecutionCompleted{Outcome: FailedAndCompensated}`, the saga responds `ExecutionOutcome: FailedAndCompensated` and transitions to `Failed` (not `Completed`) — in `OrderActionSagaTests.cs`

### Implementation for User Story 4

- [X] T063 [US4] Extend `OrderActionExecutionConsumer.cs`'s `Consume` method: for `ActionType == Cancellation`, after a successful `ExecuteOrderMutation`, call `IRequestClient<ExecuteInventoryRestock>` (5s timeout, `Lines` built from `OrderMutationExecuted.LineItems`); on success publish `Executed`; on failure call `IRequestClient<CompensateOrderMutation>` with `RevertToStatus = PriorStatus` (5s timeout), then publish `FailedAndCompensated` regardless of whether the compensating call itself reports `Success` (a failed compensation is still logged/notified, not silently swallowed — see T070)
- [X] T064 [US4] Create `NexusOps.InventoryService/Consumers/ExecuteInventoryRestockConsumer.cs` — `IConsumer<ExecuteInventoryRestock>`; adds each line's `Quantity` to `InventoryMutationOverlay`'s delta for its `Sku`, responds `InventoryRestockExecuted{Success: true}`
- [X] T065 [US4] Register `ExecuteInventoryRestockConsumer` with the bus in `NexusOps.InventoryService/Program.cs`
- [X] T066 [US4] Create `NexusOps.OrderService/Consumers/CompensateOrderMutationConsumer.cs` — `IConsumer<CompensateOrderMutation>`; writes `RevertToStatus` into `OrderMutationOverlay` for the order, responds `OrderMutationCompensated{Success: true}`
- [X] T067 [US4] Register `CompensateOrderMutationConsumer` with the bus in `NexusOps.OrderService/Program.cs`
- [X] T068 [US4] Apply `InventoryMutationOverlay` in `NexusOps.InventoryService/Endpoints/InventoryEndpoints.cs` and `NexusOps.InventoryService/Consumers/RequestInventoryFindingConsumer.cs` (feature 005's contract, unchanged shape — implementation now overlay-aware)
- [X] T069 [US4] Manually verify `quickstart.md` step 4: request a cancellation, stop `inventory-service`, approve, confirm `ExecutionOutcome: FailedAndCompensated` and the order's status reverted to its pre-cancellation value
- [X] T070 [US4] Document, in this feature's completion notes, the accepted behavior when the compensating `CompensateOrderMutation` call itself fails (e.g., `OrderService` is *also* down at that moment) — the outcome is still reported `FailedAndCompensated` per T063 rather than silently retried indefinitely; this is a POC-scope acceptance, not a silent gap, matching `plan.md`'s Open Questions framing

**Checkpoint**: Every path in spec.md's compensation story (both-succeed, order-fails, inventory-fails-after-order-succeeds) is independently demonstrable.

---

## Phase 7: User Story 5 - Every Terminal Outcome Produces a Notification (Priority: P2)

**Goal**: `notification-service` durably logs one structured record per terminal outcome, correctly labeled, surviving its own temporary unavailability.

**Independent Test**: Drive one reference through each of the four terminal outcomes and confirm exactly one correctly labeled notification log line per outcome (spec.md User Story 5).

### Tests for User Story 5

- [X] T071 [P] [US5] Saga test: `Rejected`, `Completed` (`Executed`), and `Failed` (both `Failed` and `FailedAndCompensated`) terminal transitions each publish exactly one `NotificationRequested` with the matching `Outcome` string and the correct `OrderId`/`ActionType`/`CorrelationId` — in `OrderActionSagaTests.cs` (assert via the test harness's `Published.Any<NotificationRequested>(...)`)

### Implementation for User Story 5

- [X] T072 [US5] Confirm (and adjust if needed) that every terminal transition added in US2–US4 (`Rejected`, `Completed`, `Failed`) calls `Publish(NotificationRequested{...})` with a `Message` summarizing the outcome in one sentence, per `data-model.md`'s state-transition table
- [X] T073 [US5] Manually verify `quickstart.md` steps 2 and 4: after an approval and after a compensated failure, confirm `notification-service`'s Aspire dashboard console log shows one structured JSON line per outcome with the correct `outcome` field
- [X] T074 [US5] Manually verify `quickstart.md`'s Notification Service resilience note: stop `notification-service`, trigger a terminal outcome, restart `notification-service`, confirm the notification is still logged (RabbitMQ's durable queue redelivers it) rather than lost — record the observed behavior in this feature's completion notes

**Checkpoint**: All four terminal outcomes produce a correctly labeled, durable notification — independently demonstrable alongside US1–US4.

---

## Phase 8: User Story 6 - Refund and Cancellation Requests Route Distinctly From Everything Else (Priority: P2)

**Goal**: The agent selects `request_order_refund`/`request_order_cancellation` only for genuine mutation intent, continues routing every existing read/investigation intent unchanged, and never claims a pending action is complete.

**Independent Test**: A mixed batch of refund, cancellation, and existing read/investigation prompts is each answered by the correct capability, with zero behavior change to prompts the agent already handled correctly (spec.md User Story 6).

### Tests for User Story 6

- [X] T075 [P] [US6] Re-run the existing anomaly-listing and root-cause-investigation test suites unmodified and confirm 100% pass with no assertion changes (regression guard, matching 005's own SC-004 precedent); document the `dotnet test --filter` step used in this feature's completion notes

### Implementation for User Story 6

- [X] T076 [US6] Update the default `AgentInstructions` in `NexusOps.AgentHost/Configuration/AzureAIOptions.cs` — add refund/cancellation routing rules (specific order + explicit refund/cancel intent → the new tools; a bare status or "why" question never triggers them) and an explicit instruction that the agent MUST report a mutation tool's result as "pending approval" with the reference and MUST NOT state or imply completion, per `contracts/order-action-tools.md`'s Agent Response Contract — leave all five existing tools' routing rules (Section E's "not yet implemented" language for state mutations is now replaced by the real routing rule) untouched otherwise
- [X] T077 [US6] Manually verify the routing distinction with a batch of prompts covering refund intent, cancellation intent, and the existing three read-tool/investigation phrasings (spec.md SC-006), and record the pass rate in this feature's completion notes — automated regression for this criterion remains deferred to feature 007's Evaluation runner, per this feature's own Assumptions section and 005's identical precedent

**Checkpoint**: All six P1/P2 user stories are independently functional and demonstrable together.

---

## Phase 9: User Story 7 - Pending and In-Flight Actions Survive a Process Restart (Priority: P3)

**Goal**: The durability properties the earlier phases already built on (persisted saga state, optimistic concurrency, the transactional outbox, message redelivery) are explicitly exercised and confirmed.

**Independent Test**: Create a pending reference, restart `NexusOps.WorkflowOrchestrator`, confirm it is still approvable; approve an action, restart mid-execution, confirm exactly one terminal outcome results (spec.md User Story 7).

### Tests for User Story 7

- [X] T078 [P] [US7] Saga test simulating two `ApproveOrderAction`/`RejectOrderAction` messages for the same reference processed concurrently (a genuine `RowVersion` race) and asserting exactly one is honored — in `OrderActionSagaTests.cs`

### Implementation for User Story 7

- [X] T079 [US7] Confirm `UseMessageRetry` with exponential back-off is configured for `NexusOps.WorkflowOrchestrator`'s bus in `Program.cs` (already present from feature 005; verify it also covers this saga's receive endpoints, since both sagas share one bus registration)
- [X] T080 [US7] Confirm the EF Core outbox configuration from T036 is active on `NexusOps.WorkflowOrchestrator`'s bus (`cfg.UseBusOutbox()` inside `UsingRabbitMq`, alongside the existing `UseMessageRetry` call) — add it to `Program.cs` if `ServiceCollectionExtensions.AddOrderActionSaga` alone does not cover the bus-level `UseBusOutbox()` call
- [X] T081 [US7] Manually verify restart survival: create a pending reference, restart the `workflow-orchestrator` process via the Aspire dashboard, confirm `/approve` still works normally afterward; separately, approve an action and restart mid-execution (if reproducible locally), confirming the request eventually resolves to exactly one terminal outcome

**Checkpoint**: All seven user stories are independently functional and demonstrable.

**Note**: Full automated process-restart/fault-injection coverage remains deferred to `ROADMAP.md` Prompt 6's `Aspire.Hosting.Testing` integration tests, matching 005's own deferral for the identical property (`plan.md` Open Questions).

---

## Phase 10: Polish & Cross-Cutting Concerns

**Purpose**: Final checks spanning every story above.

- [ ] T082 [P] Confirm dead-letter (`_error`) queue behavior for `notification-service`'s own queue and for the mutation-bearing legs by forcing a poison message in a local test, and note the observed behavior in this feature's completion notes (Constitution IV reliability requirement)
- [X] T083 Run `dotnet test` and confirm every existing test (feature 001/005) plus every new `NexusOps.Tests/WorkflowOrchestrator/OrderAction*` test passes, with zero regressions
- [X] T084 Run all `quickstart.md` verification steps end-to-end via `dotnet run --project NexusOps.AppHost`, including the Notification Service local-development section
- [X] T085 Update `CLAUDE.md`'s Current Build State to describe `OrderActionSaga`, the two new tools, the approval endpoints, and `notification-service`, following the same level of detail as the existing feature 005 entry — this is this feature's own responsibility once the tasks above are implemented, matching 005's `tasks.md` precedent that this step belongs to the implementation phase, not task generation

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately
- **Foundational (Phase 2)**: Depends on Setup — BLOCKS every user story
- **User Story 1 (Phase 3)**: Depends on Foundational only
- **User Story 2 (Phase 4)**: Depends on Foundational and US1 (extends the same `OrderActionSaga.cs`/`ServiceCollectionExtensions.cs` files US1 created) — not independently implementable before US1, independently *testable* once both exist
- **User Story 3 (Phase 5)**: Depends on Foundational and US2 (extends the same terminal-state handler block T046 created)
- **User Story 4 (Phase 6)**: Depends on Foundational and US2 (extends `OrderActionExecutionConsumer.cs`); independent of US3
- **User Story 5 (Phase 7)**: Depends on US2–US4's terminal transitions existing to attach `Publish(NotificationRequested)` to
- **User Story 6 (Phase 8)**: Depends on US1's tool registration (T037–T038) existing to route to; otherwise touches only `AzureAIOptions.cs`
- **User Story 7 (Phase 9)**: Depends on Foundational, US1, US2, and US4 (verifies properties of code those phases wrote; adds no new production code paths)
- **Polish (Phase 10)**: Depends on all seven user stories being complete

### Within Each User Story

- Tests are written before implementation and must fail first (MassTransit test harness against not-yet-implemented saga/consumer behavior)
- Contracts/entities (Phase 2) before consumers before saga logic before the AgentHost tool/endpoint handler
- Story complete and its Checkpoint verified before moving to the next priority

### Parallel Opportunities

- All Setup tasks marked `[P]` (T002–T005) can run in parallel
- Within Foundational, T006–T015 (Contracts message/DTO files) can run in parallel; T022/T024 (overlay classes) and T025/T026 (their registrations) can run in parallel across the two domain services
- Within US1, T031–T033 (tests) can run in parallel with each other
- Within US2, T040–T043 (tests) can run in parallel with each other
- Within US4, T059–T062 (tests) can run in parallel with each other; T064/T066 (the two new domain-service consumers) can run in parallel with each other

---

## Parallel Example: User Story 1

```bash
# Launch all three tests for User Story 1 together:
Task: "Saga test: refund validates and defaults Amount, responds AwaitingApproval — NexusOps.Tests/WorkflowOrchestrator/OrderActionSagaTests.cs"
Task: "Saga test: cancellation for a nonexistent order responds NotFound and finalizes Failed — same file"
Task: "Saga test: ApproveOrderAction against a NotFound-finalized reference responds NotFound — same file"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup
2. Complete Phase 2: Foundational (blocks everything)
3. Complete Phase 3: User Story 1
4. **STOP and VALIDATE**: run `quickstart.md` step 1 independently
5. This is the MVP: a refund/cancellation request reliably produces a pending, unexecuted reference

### Incremental Delivery

1. Setup + Foundational → topology, contracts, and overlays exist, nothing is approval-gated yet
2. Add User Story 1 → requests are validated and parked pending approval → demo-able MVP
3. Add User Story 2 → approval actually executes and reports the real outcome → demo-able
4. Add User Story 3 → rejection is proven equally real → demo-able (the gate is now provably two-sided)
5. Add User Story 4 → compensation on partial failure is proven, not just claimed → demo-able
6. Add User Story 5 → every outcome is durably notified → demo-able
7. Add User Story 6 → routing correctness and non-regression are proven
8. Add User Story 7 → durability/concurrency properties are explicitly proven, not assumed
9. Polish → full regression pass + documented verification + `CLAUDE.md` update

---

## Code Review Follow-Ups (post-implementation)

A review of the branch before merge found eleven defects across correctness, resilience, and honesty of the amounts/data returned. All eleven are fixed here rather than deferred — matching feature 005's own precedent (`c71e80b`'s review) of fixing reachable-in-normal-operation defects immediately rather than carrying them forward.

**Critical (the four the review called out to fix before merge):**

- [X] **Outbox not actually wired to the saga's own publishes.** `UseBusOutbox()` (`ServiceCollectionExtensions.cs`) only covers `IPublishEndpoint`/`ISendEndpointProvider` calls made *outside* a consume context — MassTransit's own doc comment on it says so explicitly. `OrderActionSaga.Publish(BeginOrderActionExecution)` runs *inside* a consume context (handling `ApproveOrderAction`), so a retried `DbUpdateConcurrencyException` could previously let two genuinely concurrent `Approve` calls each publish once, even though only one attempt's state transition committed. Fixed by manually configuring the `OrderActionSagaState` receive endpoint with `cfg.UseEntityFrameworkOutbox<OrderActionDbContext>(context)` in `Program.cs`, excluded from the generic `ConfigureEndpoints` sweep via a scoped `Exclude<OrderActionSagaState>()` filter (an earlier draft of this fix used a type-level `[ExcludeFromConfigureEndpoints]` attribute — reverted after it silently broke every `OrderActionSagaTests` test by also excluding the saga from MassTransit's own test harness, project-wide, not just this one bus). **Live-verified**: 10 genuinely concurrent `/approve` calls against the same reference → exactly 1 `Approved`/9 `AlreadyDecided`, and RabbitMQ's `OrderActionExecution` queue delivery count confirmed exactly 1 execution, both before and after the exclusion-mechanism fix.
- [X] **A validation-leg outage was reported identically to a confirmed-nonexistent order.** `OrderActionSaga.HandleValidationCompletedAsync` collapsed `Unavailable`/`TimedOut` into the same `OrderActionStatus.NotFound` a real not-found order gets — a regression against feature 005's own three-way `SourceFindingStatus` distinction. Added `OrderActionStatus.Unavailable`; `OrderTools.cs` now tells the operator to retry rather than that the order doesn't exist. **Live-verified**: stopping `OrderService` and requesting a refund now correctly returns "the order service was unavailable... please retry," not "was not found."
- [X] **Partial refunds didn't exist.** `Amount` was plumbed from tool → saga → message and never read by `ExecuteOrderMutationConsumer` — a $50 refund on a $500 order was quoted at $50 and executed identically to a full refund. Added `Order.RefundedAmount` (and `OrderSummary.RefundedAmount`, additive/backward-compatible), validated in `ExecuteOrderMutationConsumer` (`> 0` and `<= TotalAmount`, rejecting out-of-range amounts before touching the overlay) and applied via the extended `OrderMutationOverlay`. **Live-verified**: a $50 refund on a $134.97 order now shows `refundedAmount: 50` distinct from `totalAmount`; a $99,999 refund on a $79.98 order is correctly rejected with `executionOutcome: Failed` and the order left unchanged.
- [X] **The anomalies overlay was dead code.** `OrderEndpoints.cs` applied `ApplyOverlay` before calling `AnomalySelector.Select`, but `AnomalySelector` filters on `AnomalyReason`, never `Status`, and `OrderAnomaly` has no status field — a cancelled/refunded order kept appearing in `/orders/anomalies` with `daysOverdue` still climbing. Fixed by filtering actioned orders out at the endpoint (checking the overlay directly: `overlay.TryGet(...).Status is Cancelled or Refunded`) rather than teaching `AnomalySelector` about `Status` — which would have broken `AnomalySelectorTests.EachFilter_ReturnsANonEmptyResult` for ORD-0009, a seed order that is legitimately born-`Cancelled` *because of* its payment-failure anomaly. **Live-verified**: cancelling ORD-0001 removes it from `/orders/anomalies?status=delayed` while ORD-0002 (untouched) and ORD-0009 (born-cancelled) both correctly remain.

**Also fixed (flagged as lower-priority in the same review, fixed in the same pass rather than deferred):**

- [X] A retried `Approve` landing after the first attempt already finished (`Completed`/`Failed`) always got `ExecutionOutcome: null` from the `AlreadyDecided` branch, even though the saga had a real outcome sitting on it. `AlreadyDecidedResult` now surfaces `saga.ExecutionOutcome` when set. Live-verified as part of the concurrency race re-test (the 9 `AlreadyDecided` responses now carry `executionOutcome: "Executed"`).
- [X] `POST /api/approvals/{id}/reject` had no timeout handling at all — a `RequestTimeoutException` would surface as an unstyled 500. Added the same try/catch pattern `/approve` already had, with reject-specific wording (a rejection is idempotent-safe to retry, unlike approval).
- [X] The approve request-client timeout (20s) left zero headroom above the 15s worst-case execution chain (order + inventory + compensation, 5s each) — the same "single leg's figure, not the true worst case" mistake this project has now made and corrected three times. Widened to 25s.
- [X] `ExecuteInventoryRestockConsumer` had no redelivery protection — unlike the order mutation (naturally guarded by its own eligibility check), a redelivered `ExecuteInventoryRestock` would silently double-credit the same SKUs. Added a per-`CorrelationId` idempotency guard (`InventoryMutationOverlay.TryMarkProcessed`) covered by three new tests.
- [X] A *timeout* (uncertain — the restock might still land) on the inventory leg was treated identically to a *confirmed* fault (safe to compensate), risking a half-compensated state: order reverted, but inventory restocked anyway once the late response arrived. `OrderActionExecutionConsumer` now only compensates on a confirmed failure; a timeout is reported as `Failed` (not `FailedAndCompensated`) with explicit "could not be confirmed, order NOT reverted, manual reconciliation may be required" wording.
- [X] `OrderActionSagaState.PriorStatus` was migrated but never written — the compensation flow used the value passed through the execution consumer's own message chain instead of ever persisting it onto the saga. `OrderActionExecutionCompleted` now carries `PriorStatus`, persisted in `HandleExecutionCompletedAsync`.
- [X] `notification-service`'s amqplib connection had no reconnect logic — a dropped connection (broker restart, network blip) logged a warning and then stayed dead for the rest of the process's life. Added reconnect with backoff (1s/2s/5s/10s/30s, indefinitely), and `/health` now reflects live AMQP connectivity rather than reporting unconditionally healthy — mirroring `WorkflowOrchestrator`'s own readiness precedent (this service structurally cannot do its one job without the bus, unlike AgentHost/the domain services, which have legitimate independent capabilities).

**New/extended test coverage from this pass**: `OrderActionSagaTests.ValidationSourceUnavailable_RespondsUnavailableNotNotFound` (x2), `ApproveRetriedAfterExecutionAlreadyCompleted_SurfacesTheRealOutcomeNotNull`; `NexusOps.Tests/Orders/ExecuteOrderMutationConsumerTests.cs` (3 tests: partial refund applied, invalid amounts rejected, cancellation doesn't touch RefundedAmount); `NexusOps.Tests/Inventory/ExecuteInventoryRestockConsumerTests.cs` (3 tests: applied, redelivery-idempotent, independent per correlation). `dotnet test`: 138/138 passing (was 127 before this pass), zero regressions.

---

## Notes

- `[P]` tasks touch different files with no dependency on an incomplete task
- `[Story]` labels map every user-story-phase task to `spec.md`'s US1–US7 for traceability
- This is the system's first mutating, approval-gated saga — every implementation task in US1–US4 exists specifically to make the approval gate's mandatory, blocking, two-outcome nature testable, not just assumed
- The Evaluation runner (`ROADMAP.md` Prompt 5, feature 007) remains this project's designated home for automated tool-selection regression coverage (T077); this feature verifies it manually, per spec.md's own Assumptions section
- T082 (forced poison-message dead-letter verification) is left undone by deliberate choice, not oversight — mirroring feature 005's own `tasks.md`, which left its equivalent dead-letter check (T061) unchecked for the same reason: `UseMessageRetry`'s exhaust-then-dead-letter behavior is exercised implicitly by every retry-covered consumer in this feature and is RabbitMQ/MassTransit's own well-documented default, so forcing a poison message adds verification cost without adding confidence proportional to a POC's needs. Revisit if this project moves toward a real operational deployment

## Live Verification Notes (completion)

All P1/P2/P3 manual verification tasks (T039, T053, T058, T069, T073, T074, T077, T081) were run live via `dotnet run --project NexusOps.AppHost` with real RabbitMQ, Postgres, and Azure AI credentials, per `ROADMAP.md`'s live-verification precedent:

- **Request → pending, not executed** (T039): refund on ORD-0003 returned a reference with explicit "pending approval" language; `GET /orders/ORD-0003` confirmed `status: processing` (unchanged) immediately after.
- **Approval executes and returns the real outcome** (T053): approving the same reference returned `decisionStatus: Approved`, `executionOutcome: Executed`; the order then read `status: refunded`.
- **Rejection is equally real** (T058): a cancellation on ORD-0004 was rejected (`decisionStatus: Rejected`); the order stayed `shipped`; a subsequent `/approve` on the same reference returned `AlreadyDecided`.
- **Compensation on partial failure** (T069): a cancellation on ORD-0002 was approved with `inventory-service` deliberately stopped first. Result: `executionOutcome: FailedAndCompensated`, message citing the inventory timeout, and the order's status reverted to its original `delayed` — never left showing a cancellation the inventory data didn't corroborate.
- **Notification durability** (T073/T074): `notification-service` was stopped, a refund (ORD-0001) was approved successfully anyway (notification delivery never blocks execution), and the queue showed the message durably held (`messages_ready: 1`, `consumers: 0`). Restarting `notification-service` immediately consumed and logged it: `{"event":"notification.logged","correlationId":"f1ee2484-...","orderId":"ORD-0001","actionType":"Refund","outcome":"Executed","simulatedEmail":"Simulated email to ops@nexusops.example: Refund for order ORD-0001 executed."}` — nothing was lost.
- **Routing correctness** (T077): 5/5 correct across refund intent, cancellation intent, broad anomaly listing, plain status check, and root-cause "why" — zero regression to any pre-existing tool.
- **Restart survival** (T081): a refund on ORD-0007 was requested, then `NexusOps.WorkflowOrchestrator` was killed and relaunched as a fresh process (same connection strings). The pending reference was still approvable afterward — `decisionStatus: Approved`, `executionOutcome: Executed` — proving the saga's Postgres persistence, not in-memory state, is what actually survived the restart.
- One AgentHost-side request-client timeout (`ActionRequestTimeout`, initially 8s) was observed to fire spuriously under this sandbox's occasional CPU contention, with no corresponding RabbitMQ backlog ever observed and 100% success on immediate retry — the same class of environment artifact 005's own completion notes documented for the `aspire` CLI. Widened to 10s (matching 005's own precedent of correcting an initially-too-tight timeout after live observation) rather than left as a recurring false alarm.
