# Implementation Plan: Order Root-Cause Investigation Workflow

**Branch**: `005-workflow-orchestrator` | **Date**: 2026-09-01 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/005-workflow-orchestrator/spec.md`

## Summary

Add a new `NexusOps.WorkflowOrchestrator` host that runs a MassTransit v8 `OrderInvestigationSaga` over RabbitMQ, with saga state persisted in PostgreSQL via EF Core using optimistic concurrency. A new AgentHost tool, `investigate_order_root_cause`, publishes a request over the bus for a specific order; the saga fans out to Order, Inventory, and Product services in parallel (via a stateless fan-out coordinator consumer, not the saga itself), aggregates whatever responds, and returns a consolidated result — complete, degraded, or failed — back to AgentHost's waiting request client. `investigate_order_anomaly` and its Direct-path contract are untouched. Order-specific saga code is isolated to a single namespace and one registration call, kept out of the domain-agnostic AppHost/AgentHost/WorkflowOrchestrator core (research.md, Decision 5).

## Technical Context

**Language/Version**: C# / .NET 10

**Primary Dependencies**:
- `MassTransit` 8.3.x + `MassTransit.RabbitMQ` 8.3.x — bus, saga state machine, request/response (pinned to v8; v9 is commercial and out of scope per `ROADMAP.md`)
- `MassTransit.EntityFrameworkCore` 8.3.x — EF Core saga repository with `ConcurrencyMode.Optimistic`
- `Npgsql.EntityFrameworkCore.PostgreSQL` 9.x + `Aspire.Npgsql.EntityFrameworkCore.PostgreSQL` — saga `DbContext`, Aspire connection wiring, health check, OTel
- `Aspire.Hosting.RabbitMQ`, `Aspire.Hosting.PostgreSQL` — AppHost resource provisioning
- `Aspire.RabbitMQ.Client` — client-side bus connection wiring (connection string, health check, OTel) for every project that joins the bus (AgentHost, the three domain services, WorkflowOrchestrator)

**Storage**: PostgreSQL (via Aspire; `workfloworchestrator` database), one table (`OrderInvestigationSagaState`) for this feature

**Testing**: `dotnet test` (xUnit) for consumer/saga unit tests using MassTransit's `InMemoryTestHarness`/`ITestHarness` (no real broker or Postgres required — credential-free, matches `ROADMAP.md`'s CI constraint); an `Aspire.Hosting.Testing` integration test is scoped to feature 006's roadmap prompt (Prompt 6), not this feature, per `ROADMAP.md`

**Target Platform**: Linux container (Aspire-orchestrated), consistent with all existing services

**Performance Goals**: A non-degraded investigation resolves within 3 seconds under typical local development conditions (SC-006); per-source request timeout 5s (order lookup, then inventory+product concurrently) and overall AgentHost-side request-client timeout 12s bound the worst case, not the expected case — 12s, not 8s, because the worst case is the *sum* of the sequential order leg and the parallel inventory/product leg (5s + 5s = 10s), not a single leg's timeout (research.md Decision 2)

**Constraints**: No approval gate (FR-012); no direct HTTP from saga-side code to domain services (Constitution IV); Order-specific code isolated from the orchestration core (Constitution V, FR-015)

**Scale/Scope**: One new host project (`NexusOps.WorkflowOrchestrator`), one new AppHost topology addition (RabbitMQ + Postgres), one new MassTransit consumer added to each of the three existing domain services, one new tool + message contracts in `NexusOps.Contracts`, one new AgentHost tool handler + routing-instruction update

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design — see note after each item.*

- [x] **I. Cognition/Durability boundary** — AgentHost's only saga-facing code is a MassTransit `IRequestClient<InvestigateOrderRootCause>` (a thin transport concern) inside the new tool handler; it contains no state-machine, no fan-out logic, no retry/timeout policy beyond the single request-client timeout. All durable execution — the state machine, the fan-out coordinator, the finding aggregation, the finalize/respond logic — lives in `NexusOps.WorkflowOrchestrator`. *Re-checked post-design: `research.md` Decision 2 confirms the saga responds via a captured `ResponseAddress`, not by AgentHost reaching back into the orchestrator — the boundary is the message bus in both directions.*
- [x] **II. Curated tool boundaries** — `investigate_order_root_cause` is a new curated tool defined in `NexusOps.Contracts` (not a raw endpoint passthrough), expresses domain intent (root-cause investigation, not "GET three services"), and maps unambiguously to the Saga path. *Re-checked post-design: `contracts/investigate-order-root-cause-tool.md` defines the tool's full input/output contract in Contracts terms only.*
- [x] **III. Approval-gated side effects** — N/A by design: this feature is read-only (FR-012); nothing here mutates order, inventory, or product state, so no approval gate applies. `OrderActionSaga` (the approval-gated mutation saga) is out of scope, reserved for feature 006. *Re-checked post-design: no mutation appears anywhere in `data-model.md`'s message contracts.*
- [x] **IV. Message-driven service integration** — Every saga-to-service hop (fan-out coordinator → Order/Inventory/Product) is MassTransit request/response over RabbitMQ; no HTTP call from any saga-side type to a domain service anywhere in the design. Retry (`UseMessageRetry`) and dead-letter queues are specified on every leg. *Re-checked post-design: `contracts/saga-message-contracts.md`'s "Reliability Requirements" section makes this explicit and testable.*
- [x] **V. Domain pluggability** — All Order-specific saga code (the state machine, the fan-out coordinator, their message contracts' *handling* logic) lives in `NexusOps.WorkflowOrchestrator.OrderInvestigation`, wired into the generic host via one `AddOrderInvestigationSaga(...)` extension call from `Program.cs`. Deleting that namespace and its one registration line leaves the host compiling, runnable, and domain-empty. *Re-checked post-design: `research.md` Decision 5 states the mechanical test for this explicitly.*
- [x] **VI. Observability first** — `NexusOps.WorkflowOrchestrator` calls `AddServiceDefaults()`; MassTransit's bus health check is registered and, for this host only, tagged into readiness; the AppHost registers `.WithHttpHealthCheck("/health")` for the new host and for RabbitMQ/Postgres via their Aspire integrations. *Re-checked post-design and post-implementation: `research.md` Decision 7. A code-review pass found that adding MassTransit to AgentHost and the three domain services made the broker a readiness dependency for all of them too — the same mistake CLAUDE.md already documents having fixed for Redis (`/health` excludes it precisely because those services keep serving when it's down). `NexusOps.ServiceDefaults.Extensions.MapDefaultEndpoints` now takes an `includeMassTransitInReadiness` parameter, defaulting to `false`; only `NexusOps.WorkflowOrchestrator` passes `true`.*

No violations. Complexity Tracking is not needed.

## Project Structure

### Documentation (this feature)

```text
specs/005-workflow-orchestrator/
├── plan.md                                    ← this file
├── research.md                                ← Phase 0 decisions
├── data-model.md                              ← saga state schema + message contracts
├── quickstart.md                              ← Phase 1 output
├── contracts/
│   ├── investigate-order-root-cause-tool.md   ← AgentHost tool contract
│   └── saga-message-contracts.md              ← internal AMQP contracts
└── tasks.md                                   ← generated by /speckit-tasks
```

### Source Code Changes

```text
NexusOps.AppHost/
└── Program.cs                                          ← add RabbitMQ + Postgres resources; reference from
                                                            AgentHost, OrderService, InventoryService,
                                                            ProductService, and the new WorkflowOrchestrator

NexusOps.Contracts/
├── NexusOps.Contracts.csproj                           ← add MassTransit.Abstractions (message contract base, if needed)
├── ToolNames.cs                                        ← add InvestigateOrderRootCause + description
├── Dtos/
│   └── RootCauseInvestigation.cs                       ← new: RootCauseInvestigationResult, InvestigationCompleteness, SourceFindingStatus
└── Messages/                                           ← new folder
    ├── InvestigateOrderRootCause.cs                    ← new
    ├── BeginInvestigationFanOut.cs                      ← new
    ├── OrderFindingMessages.cs                          ← new: RequestOrderFinding, OrderFindingReported
    ├── InventoryFindingMessages.cs                      ← new: RequestInventoryFinding, InventoryFindingReported
    └── ProductFindingMessages.cs                        ← new: RequestProductFinding, ProductFindingReported

NexusOps.WorkflowOrchestrator/                          ← new project
├── NexusOps.WorkflowOrchestrator.csproj
├── .gitignore                                          ← bin/, obj/, out/, *.nupkg, *.lscache (project convention)
├── Program.cs                                          ← AddServiceDefaults(), MassTransit bus config, EF Core DbContext,
│                                                            calls AddOrderInvestigationSaga(...)
└── OrderInvestigation/                                 ← the ONLY Order-specific folder (Constitution V boundary)
    ├── OrderInvestigationSaga.cs                       ← MassTransitStateMachine<OrderInvestigationSagaState>
    ├── OrderInvestigationSagaState.cs                  ← saga entity (data-model.md)
    ├── OrderInvestigationDbContext.cs                  ← EF Core DbContext + migration
    ├── InvestigationFanOutConsumer.cs                  ← IConsumer<BeginInvestigationFanOut>
    └── ServiceCollectionExtensions.cs                  ← AddOrderInvestigationSaga(this IBusRegistrationConfigurator)

NexusOps.OrderService/
└── Consumers/
    └── RequestOrderFindingConsumer.cs                  ← new: IConsumer<RequestOrderFinding>

NexusOps.InventoryService/
└── Consumers/
    └── RequestInventoryFindingConsumer.cs               ← new: IConsumer<RequestInventoryFinding>

NexusOps.ProductService/
└── Consumers/
    └── RequestProductFindingConsumer.cs                 ← new: IConsumer<RequestProductFinding>

NexusOps.AgentHost/
├── Tools/
│   └── OrderTools.cs                                   ← add investigate_order_root_cause handler (new function,
│                                                            existing investigate_order_anomaly handler untouched)
├── Program.cs                                          ← add MassTransit bus (request client created per call via
│                                                            IClientFactory in OrderTools.cs, not AddRequestClient here)
└── (agent instructions)                                ← update routing rules per contracts/investigate-order-root-cause-tool.md

NexusOps.Tests/
└── WorkflowOrchestrator/
    ├── OrderInvestigationSagaTests.cs                  ← MassTransit test harness: happy path, degraded, all-failed, concurrent-finding race
    └── InvestigationFanOutConsumerTests.cs             ← per-source timeout/fault mapping
```

**Structure Decision**: One new host project (`NexusOps.WorkflowOrchestrator`), following the same shape as the three existing domain services (`Microsoft.NET.Sdk.Web`, references `NexusOps.Contracts` + `NexusOps.ServiceDefaults`, own `.gitignore` per the project's `.NET project conventions`). No new solution folders beyond the existing `NexusOps.*` flat layout; the domain-agnostic/domain-specific split happens *within* `NexusOps.WorkflowOrchestrator` via the `OrderInvestigation/` sub-namespace (research.md Decision 5), not via a separate project — a separate project per saga was considered and rejected as premature until a second saga (feature 006) exists to prove the boundary is real.

## Complexity Tracking

No constitution violations. No complexity justification required.

## Open Questions / Deferred

| Item | Deferred To | Notes |
|---|---|---|
| `OrderActionSaga`, approval gate, refund/cancel tools | Feature 006 (`ROADMAP.md` Prompt 4) | This feature is investigation-only, read-only, no mutation |
| Notification Service (Node.js/amqplib) | Feature 006 | Not needed until a mutating saga emits `NotificationRequested` |
| Aspire.Hosting.Testing integration test for this saga | `ROADMAP.md` Prompt 6 | Unit-level MassTransit test harness coverage ships with this feature; full Aspire integration test is a later, dedicated prompt covering all sagas together |
| Exact per-source/overall timeout tuning (5s/12s chosen here) | Implementation | Spec's Assumptions section explicitly leaves these as implementation parameters bound only by SC-006's "a few seconds" |
| Saga rows are never removed after finalizing, and a saga stuck in `Investigating` (fan-out message dead-lettered after exhausting retries) has no deadline to force it to a terminal state | Future hardening | FR-010's caller-facing guarantee still holds (the AgentHost-side 12s timeout resolves the caller's request regardless), but the persisted record itself can be left non-terminal indefinitely. Acceptable for a POC's read-only saga; revisit if `OrderInvestigationSagaState` row growth or stuck-row cleanup becomes an operational concern |
| No transactional outbox (`AddEntityFrameworkOutbox`) on the saga | Feature 006 — stops being optional | A retried `Initially(When(Requested))` consume mints a second `CorrelationId` and republishes `BeginInvestigationFanOut` for an instance that may never commit; harmless here since the findings for an uncommitted instance are simply discarded by `OnMissingInstance`. `OrderActionSaga` (006) publishes side effects (refund, cancel, notify) from the same `Initially(When(...))` shape — a duplicate `CorrelationId` there means a duplicate refund, not a discarded finding, so the outbox is mandatory rather than a hardening nice-to-have |
| Message-scheduler-based investigation-wide deadline | Rejected (research.md Decision 1) | Bounded per-source `IRequestClient` timeouts make it unnecessary |
| `During(Completed, Failed, Ignore(...))` (T064) is a saga-wide invariant, not a one-off fix for this saga | Feature 006 | Any state machine that can finalize while related messages are still in flight needs an explicit `Ignore(...)` for its terminal state(s), or a late arrival faults the consumer. `OrderActionSaga`'s human approval gate widens that window considerably versus this saga's sub-15s fan-out — an approval can land hours after the saga has already timed out or been otherwise finalized — so 006's state machine must apply the same pattern to every terminal state from the start, not add it after a live fault surfaces it |
