# Implementation Plan: Approval-Gated Order Actions

**Branch**: `006-approval-actions` | **Date**: 2026-09-02 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/006-approval-actions/spec.md`

## Summary

Add `OrderActionSaga` — this system's first mutating, approval-gated saga — to `NexusOps.WorkflowOrchestrator`, handling two curated tools' worth of requests (`request_order_refund`, `request_order_cancellation`). A request is validated (order must exist, reusing feature 005's `RequestOrderFinding` contract), then parked in `AwaitingApproval` with a reference GUID returned immediately to the caller — never executed. `POST /api/approvals/{id}/approve` and `/reject` on `NexusOps.AgentHost`, backed by MassTransit request/response, are the only path to a decision; approval blocks until execution finishes and returns the real outcome, rejection returns immediately. On approval, a plain consumer (`OrderActionExecutionConsumer`, mirroring 005's `InvestigationFanOutConsumer` shape) mutates the order via `NexusOps.OrderService`, and for a cancellation, restocks the reserved inventory via `NexusOps.InventoryService` — compensating (reverting) the order mutation if the inventory leg fails after the order leg succeeded. Every terminal outcome publishes `NotificationRequested`, consumed by a new minimal Node.js/TypeScript/amqplib project, `notification-service/`, wired into the Aspire AppHost. Order-specific saga code is isolated to `NexusOps.WorkflowOrchestrator.OrderAction`, registered via one `AddOrderActionSaga(...)` call, per the domain-pluggability precedent `NexusOps.WorkflowOrchestrator.OrderInvestigation` already sets.

## Technical Context

**Language/Version**: C# / .NET 10 (orchestration, domain services, AgentHost); TypeScript / Node.js 24+ (`notification-service`)

**Primary Dependencies**:
- `MassTransit` 8.5.x + `MassTransit.RabbitMQ` 8.5.x + `MassTransit.EntityFrameworkCore` 8.5.x (already present in `NexusOps.WorkflowOrchestrator`; no version change — pinned to v8 per `ROADMAP.md`)
- `MassTransit.Abstractions` 8.5.x — new reference on `NexusOps.Contracts`, for the `[EntityName]` attribute only (research.md Decision 9); carries no transport/broker dependency
- `Npgsql.EntityFrameworkCore.PostgreSQL` + `Aspire.Npgsql.EntityFrameworkCore.PostgreSQL` (already present)
- `amqplib` + `@types/amqplib`, TypeScript, `tsx`/`tsc` for `notification-service/` — no web framework (research.md Decision 10)
- `Aspire.Hosting.NodeJs` — new AppHost package reference, for `AddNpmApp`

**Storage**: PostgreSQL (via Aspire; `workfloworchestrator` database, shared with feature 005) — a new `OrderActionSagaState` table plus MassTransit's outbox tables (`InboxState`/`OutboxState`/`OutboxMessage`), all via one new EF Core migration scoped to `OrderActionDbContext`

**Testing**: `dotnet test` (xUnit) using MassTransit's `ITestHarness`, matching 005's approach exactly — no real broker/Postgres/Node process required, credential-free per `ROADMAP.md`; an `Aspire.Hosting.Testing` integration test covering the full approve/reject/compensate flow remains scoped to `ROADMAP.md` Prompt 6, not this feature (same deferral 005 made for its own saga)

**Target Platform**: Linux container (Aspire-orchestrated) for every project including `notification-service`, consistent with the rest of the system

**Performance Goals**: A refund/cancellation request resolves (validated, reference returned) within the request-client's 8s budget under typical local development conditions; an approval resolves (mutation + compensation, worst case) within its 20s budget — both budgets and their derivation are in `contracts/saga-message-contracts.md`'s Timeout Budget table, following 005's own "size above the true worst case, not a single leg's figure" precedent (corrected there from an initially under-budgeted figure during code review)

**Constraints**: No mutation MUST ever occur before an explicit approval (Constitution III, FR-003/FR-005); no domain service MUST accept a mutation directly from AgentHost (FR-018); no direct HTTP from saga-side code to any service including the Notification Service (Constitution IV, FR-017); Order-specific code isolated from the orchestration core (Constitution V, FR-021)

**Scale/Scope**: One new saga + two new plain consumers in `NexusOps.WorkflowOrchestrator`; one new MassTransit outbox configuration; two new mutation-handling consumers plus a read-path overlay in `NexusOps.OrderService`; one new mutation-handling consumer plus a read-path overlay in `NexusOps.InventoryService`; two new curated tools + one new approval-endpoints file in `NexusOps.AgentHost`; one new AppHost resource; one entirely new project, `notification-service/`

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design — see note after each item.*

- [x] **I. Cognition/Durability boundary** — AgentHost's only saga-facing code is three thin transport concerns: two `IClientFactory`-minted request clients in `OrderTools.cs` (mirroring 005's existing `investigate_order_root_cause` handler exactly) for the two request tools, and two bus-registered `IRequestClient<T>` injected into `ApprovalEndpoints.cs` for approve/reject. None contain state-machine, retry-policy, or compensation logic — all of that lives in `NexusOps.WorkflowOrchestrator`. *Re-checked post-design: `research.md` Decisions 1–8 place every piece of state, validation, execution, and compensation logic inside `OrderActionSaga`/`OrderActionExecutionConsumer`/`OrderActionValidationConsumer`; nothing crosses into AgentHost.*
- [x] **II. Curated tool boundaries** — `request_order_refund` and `request_order_cancellation` are new curated tools in `NexusOps.Contracts`, expressing domain intent (not raw endpoints), mapping unambiguously to the Saga path. *Re-checked post-design: `contracts/order-action-tools.md` defines both tools' full contracts in Contracts terms only, including the phrasing constraint that keeps the agent from ever claiming completion.*
- [x] **III. Approval-gated side effects** — This is the principle this entire feature exists to satisfy. Both tools place a request into `AwaitingApproval` and return without executing anything (FR-003); `POST /api/approvals/{id}/approve`/`reject` are the only path to a decision; no domain service accepts a mutation directly from AgentHost (FR-018) — every mutation arrives via `OrderActionExecutionConsumer` over AMQP. *Re-checked post-design: `data-model.md`'s state-transition table shows no path from `Requested` to a mutation that does not pass through an explicit `ApproveOrderAction` event.*
- [x] **IV. Message-driven service integration** — Every leg (validation, execution, compensation, notification) is MassTransit request/response or publish over RabbitMQ; no HTTP call from any saga-side type to `OrderService`, `InventoryService`, or `notification-service` anywhere in the design. Retry and the transactional outbox are specified on the mutation-bearing legs. *Re-checked post-design: `contracts/saga-message-contracts.md`'s "Reliability Requirements" section makes this explicit per leg.*
- [x] **V. Domain pluggability** — All Order-specific saga code lives in `NexusOps.WorkflowOrchestrator.OrderAction`, wired into the generic host via one `AddOrderActionSaga(this IBusRegistrationConfigurator)` call from `Program.cs`, alongside the existing `AddOrderInvestigationSaga(...)` call — deleting either namespace and its one registration line leaves the host compiling and running with the other saga (or neither) intact. *Re-checked post-design: mirrors 005's own mechanical test exactly, extended to a second, independent saga in the same host.*
- [x] **VI. Observability first** — `notification-service` exposes `GET /health` (bare `node:http`, no framework) and is registered in AppHost via `.WithHttpHealthCheck("/health")`, matching every .NET service; it emits structured JSON log lines to stdout (research.md Decision 10). No new .NET service is added by this feature — `NexusOps.WorkflowOrchestrator` already calls `AddServiceDefaults()` (005) and that is unchanged; `OrderService`/`InventoryService` are unchanged in this respect. *Re-checked post-design and cross-checked against 005's own hard-won Decision 7 (`includeMassTransitInReadiness`): this feature adds no new MassTransit-registering .NET host, so that footgun does not recur here.*

No violations. Complexity Tracking is not needed.

## Project Structure

### Documentation (this feature)

```text
specs/006-approval-actions/
├── plan.md                                    ← this file
├── research.md                                ← Phase 0 decisions
├── data-model.md                              ← saga state schema + message contracts
├── quickstart.md                              ← Phase 1 output
├── contracts/
│   ├── order-action-tools.md                  ← AgentHost tool + approval-endpoint contracts
│   └── saga-message-contracts.md              ← internal AMQP contracts + reliability/timeout budget
└── tasks.md                                   ← generated by /speckit-tasks
```

### Source Code Changes

```text
NexusOps.Contracts/
├── NexusOps.Contracts.csproj                           ← add MassTransit.Abstractions
├── ToolNames.cs                                         ← add RequestOrderRefund + RequestOrderCancellation
├── Dtos/
│   └── OrderAction.cs                                   ← new: OrderActionType, OrderActionStatus,
│                                                            OrderActionDecisionOutcome, OrderActionExecutionOutcome,
│                                                            OrderActionRequestResult
└── Messages/                                            ← new files, alongside feature 005's existing ones
    ├── OrderActionRequestMessages.cs                    ← RequestOrderRefund, RequestOrderCancellation,
    │                                                        OrderActionRequestResult
    ├── ActionValidationMessages.cs                       ← BeginActionValidation, ActionValidationCompleted
    ├── OrderActionDecisionMessages.cs                   ← ApproveOrderAction, RejectOrderAction,
    │                                                        OrderActionDecisionResult
    ├── BeginOrderActionExecution.cs
    ├── OrderMutationMessages.cs                         ← ExecuteOrderMutation, OrderMutationExecuted
    ├── InventoryRestockMessages.cs                      ← ExecuteInventoryRestock, InventoryRestockExecuted,
    │                                                        InventoryRestockLine
    ├── CompensateOrderMutationMessages.cs                ← CompensateOrderMutation, OrderMutationCompensated
    ├── OrderActionExecutionCompleted.cs
    └── NotificationRequested.cs                          ← [EntityName("notification-requested")]

NexusOps.WorkflowOrchestrator/
├── NexusOps.WorkflowOrchestrator.csproj                 ← no new package versions; MassTransit.Abstractions
│                                                            comes transitively via Contracts
├── Program.cs                                           ← register OrderActionDbContext, call
│                                                            AddOrderActionSaga(...), keep
│                                                            AddOrderInvestigationSaga(...) unchanged
└── OrderAction/                                         ← the ONLY Order-action-specific folder
    ├── OrderActionSaga.cs                                ← MassTransitStateMachine<OrderActionSagaState>
    ├── OrderActionSagaState.cs
    ├── OrderActionDbContext.cs                           ← + outbox entity configuration
    ├── OrderActionValidationConsumer.cs                  ← IConsumer<BeginActionValidation>
    ├── OrderActionExecutionConsumer.cs                   ← IConsumer<BeginOrderActionExecution>
    └── ServiceCollectionExtensions.cs                    ← AddOrderActionSaga(this IBusRegistrationConfigurator)

NexusOps.OrderService/
├── Models/Order.cs                                      ← OrderStatus gains Refunded; Order gains a
│                                                             WithStatus(...) copy helper
├── Data/OrderMutationOverlay.cs                          ← new: ConcurrentDictionary<string, OrderStatus>
├── Endpoints/OrderEndpoints.cs                           ← apply overlay before projecting responses
├── Consumers/
│   ├── RequestOrderFindingConsumer.cs                    ← apply overlay (contract unchanged; feature 005 owns it)
│   ├── ExecuteOrderMutationConsumer.cs                    ← new: IConsumer<ExecuteOrderMutation>
│   └── CompensateOrderMutationConsumer.cs                 ← new: IConsumer<CompensateOrderMutation>
└── Program.cs                                            ← register overlay + two new consumers

NexusOps.InventoryService/
├── Data/InventoryMutationOverlay.cs                      ← new: ConcurrentDictionary<string, int>
├── Endpoints/InventoryEndpoints.cs                        ← apply overlay before projecting responses
├── Consumers/
│   ├── RequestInventoryFindingConsumer.cs                 ← apply overlay (contract unchanged)
│   └── ExecuteInventoryRestockConsumer.cs                  ← new: IConsumer<ExecuteInventoryRestock>
└── Program.cs                                            ← register overlay + new consumer

NexusOps.AgentHost/
├── Tools/
│   └── OrderTools.cs                                     ← add RequestOrderRefundAsync/RequestOrderCancellationAsync
│                                                             (existing handlers untouched)
├── Tools/ToolHandlerExtensions.cs                        ← register the two new tools alongside the existing seven
├── Endpoints/
│   └── ApprovalEndpoints.cs                               ← new: POST /api/approvals/{id}/approve, /reject
├── Program.cs                                            ← AddRequestClient<ApproveOrderAction>/<RejectOrderAction>;
│                                                             MapApprovalEndpoints()
└── Configuration/AzureAIOptions.cs                        ← add refund/cancellation routing rules + the
                                                              "never claim completion" instruction

notification-service/                                     ← new project
├── package.json / tsconfig.json / .gitignore              ← node_modules/, dist/, .env
├── src/
│   ├── index.ts                                          ← wires the AMQP consumer + health server together
│   ├── amqpConsumer.ts                                    ← assertExchange/assertQueue/bind/consume,
│   │                                                         MassTransit envelope parsing
│   ├── healthServer.ts                                    ← bare node:http GET /health
│   └── logger.ts                                          ← structured JSON stdout logger
└── (build output: dist/, not tracked)

NexusOps.AppHost/
├── NexusOps.AppHost.csproj                               ← add Aspire.Hosting.NodeJs
└── AppHost.cs                                            ← AddNpmApp("notification-service", "../notification-service")
                                                              .WithHttpEndpoint(...).WithHttpHealthCheck("/health")
                                                              .WithReference(rabbitmq).WaitFor(rabbitmq)

NexusOps.Tests/
└── WorkflowOrchestrator/
    ├── OrderActionSagaTests.cs                            ← validation, awaiting-approval, approve/reject,
    │                                                         already-decided, not-found, execution finalize
    └── OrderActionExecutionConsumerTests.cs                ← per-leg timeout/fault mapping, compensation trigger
```

**Structure Decision**: `NexusOps.WorkflowOrchestrator` gains a second Order-specific sub-namespace, `OrderAction/`, sitting alongside 005's `OrderInvestigation/` — exactly the shape 005's own `research.md` Decision 5 anticipated ("the roadmap's next saga... has a materially different shape... so a shared base class would be guessing at a pattern from a sample size of one"), now proven out as two independent, additively-registered sagas in one domain-agnostic host. `notification-service/` is a new top-level project directory, matching `frontend/`'s precedent of a non-.NET project living outside the `NexusOps.*` naming convention. It is **not** added to `NexusOps.sln`/`NexusOps.deployable.slnf`, since it is not a .NET project — `frontend/`'s `frontend.esproj` is present in `NexusOps.sln` only for Aspire's benefit and is explicitly excluded from `NexusOps.deployable.slnf` to keep npm tooling out of the dotnet CI job; `notification-service` follows the stricter version of that same precedent by not needing an `.esproj` at all, since `AddNpmApp` requires none.

## Complexity Tracking

No constitution violations. No complexity justification required.

## Open Questions / Deferred

| Item | Deferred To | Notes |
|---|---|---|
| `Aspire.Hosting.Testing` integration test exercising the full approve→execute→notify and approve→execute→compensate flows against real RabbitMQ/Postgres | `ROADMAP.md` Prompt 6 | Unit-level MassTransit test harness coverage ships with this feature, matching 005's own precedent and explicit deferral of the same tier |
| Execution-consumer-level idempotency beyond the EF Core outbox | Future hardening | `research.md` Decision 6's accepted residual gap: the outbox protects the saga's own publish/consume boundary, not `OrderActionExecutionConsumer`'s mutation calls themselves against every redelivery of `BeginOrderActionExecution` after a mid-execution crash; FR-013's eligibility check is the secondary safety net (a misleading but non-corrupting reported failure, not a silent double-refund) |
| No expiration on a pending `AwaitingApproval` reference | Accepted, per spec.md Assumptions | Consistent with this feature's explicit no-UI, no-auth, POC scope; revisit only if this moves toward a real operational tool |
| Notification Service has no retry/backoff of its own beyond RabbitMQ's redelivery, and no dead-letter handling distinct from the broker's default | Future hardening | `ROADMAP.md`'s "nothing more" instruction for this service; a `notification.logged` failure (e.g., a disk-full stdout write) is not handled specially — matches the service's deliberately minimal scope |
| `OrderActionSaga` rows are never removed after finalizing (same accepted gap 005 documented for `OrderInvestigationSagaState`) | Future hardening | Acceptable for a POC; revisit if row growth becomes an operational concern |
