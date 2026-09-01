# Tasks: Order Root-Cause Investigation Workflow

**Input**: Design documents from `specs/005-workflow-orchestrator/`

**Prerequisites**: plan.md ✅ | spec.md ✅ | research.md ✅ | data-model.md ✅ | contracts/ ✅ | quickstart.md ✅

**Tests**: `plan.md`'s Technical Context commits to specific test files (`OrderInvestigationSagaTests.cs`, `InvestigationFanOutConsumerTests.cs`) using MassTransit's in-memory test harness, credential-free per `ROADMAP.md`'s CI constraint. Test tasks are included below, one set per user story, covering exactly the behavior that story's acceptance scenarios require.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story. All three P1 stories (US1–US3) build on the same foundational plumbing; US4 (P3) is a correctness property of that plumbing rather than new surface area, so its tasks are smaller and largely verification-focused.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: Which user story this task belongs to (US1–US4 matching spec.md)
- All paths are project-relative from repo root

---

## Phase 1: Setup (Projects & Packages)

**Purpose**: Stand up the new host project and every package reference this feature needs, before any code is written.

- [ ] T001 Create `NexusOps.WorkflowOrchestrator/NexusOps.WorkflowOrchestrator.csproj` (`Microsoft.NET.Sdk.Web`, `net10.0`, `Nullable`/`ImplicitUsings` enabled, `ProjectReference` to `NexusOps.Contracts` and `NexusOps.ServiceDefaults`), `NexusOps.WorkflowOrchestrator/.gitignore` (`bin/`, `obj/`, `out/`, `*.nupkg`, `*.lscache` per the project's `.NET project conventions`), and a minimal `NexusOps.WorkflowOrchestrator/Program.cs` (`builder.AddServiceDefaults(); app.MapDefaultEndpoints(); app.Run();`)
- [ ] T002 [P] Add `NexusOps.WorkflowOrchestrator` to `NexusOps.sln`
- [ ] T003 [P] Add `NexusOps.WorkflowOrchestrator` to `NexusOps.deployable.slnf`
- [ ] T004 [P] Add `MassTransit`, `MassTransit.RabbitMQ`, `MassTransit.EntityFrameworkCore` package references (pinned to the `8.3.x` line) to `NexusOps.WorkflowOrchestrator/NexusOps.WorkflowOrchestrator.csproj`
- [ ] T005 [P] Add `Npgsql.EntityFrameworkCore.PostgreSQL` and `Aspire.Npgsql.EntityFrameworkCore.PostgreSQL` package references to `NexusOps.WorkflowOrchestrator/NexusOps.WorkflowOrchestrator.csproj`
- [ ] T006 [P] Add `Aspire.Hosting.RabbitMQ` and `Aspire.Hosting.PostgreSQL` package references to `NexusOps.AppHost/NexusOps.AppHost.csproj`
- [ ] T007 [P] Add a Dependabot major-version ignore rule for all `MassTransit*` packages in `.github/dependabot.yml` (locked in `ROADMAP.md`: v9 is commercial, out of scope)
- [ ] T008 [P] Add `MassTransit` + `MassTransit.RabbitMQ` package references to `NexusOps.AgentHost/NexusOps.AgentHost.csproj`
- [ ] T009 [P] Add `MassTransit` + `MassTransit.RabbitMQ` package references to `NexusOps.OrderService/NexusOps.OrderService.csproj`
- [ ] T010 [P] Add `MassTransit` + `MassTransit.RabbitMQ` package references to `NexusOps.InventoryService/NexusOps.InventoryService.csproj`
- [ ] T011 [P] Add `MassTransit` + `MassTransit.RabbitMQ` package references to `NexusOps.ProductService/NexusOps.ProductService.csproj`

**Checkpoint**: `dotnet restore` succeeds across the solution; the new project builds as an empty host.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Topology, contracts, and bus registration that every user story depends on. No user-story-specific behavior lives here — only the plumbing that makes it possible to add that behavior next.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [ ] T012 Add a RabbitMQ resource to `NexusOps.AppHost/Program.cs` — `builder.AddRabbitMQ("rabbitmq").WithManagementPlugin()`
- [ ] T013 Add a PostgreSQL resource + database to `NexusOps.AppHost/Program.cs` — `builder.AddPostgres("postgres").WithDataVolume()`, then `.AddDatabase("workfloworchestrator")`
- [ ] T014 Add the `NexusOps.WorkflowOrchestrator` project resource to `NexusOps.AppHost/Program.cs` — `.WithHttpHealthCheck("/health")`, `.WithReference(rabbitmq)`, `.WithReference(workflowOrchestratorDb)`, `.WaitFor(rabbitmq)`, `.WaitFor(workflowOrchestratorDb)`
- [ ] T015 Chain `.WithReference(rabbitmq).WaitFor(rabbitmq)` onto the `agentHost`, `orderService`, `inventoryService`, and `productService` builders in `NexusOps.AppHost/Program.cs`
- [ ] T016 [P] Create `NexusOps.Contracts/Dtos/RootCauseInvestigation.cs` — `SourceFindingStatus` enum (`Pending`, `Succeeded`, `NotFound`, `Unavailable`, `TimedOut`), `InvestigationCompleteness` enum (`Complete`, `Degraded`, `Failed`), and the `RootCauseInvestigationResult` record, per `data-model.md`
- [ ] T017 [P] Create `NexusOps.Contracts/Messages/InvestigateOrderRootCause.cs` — the request record (`OrderId`)
- [ ] T018 [P] Create `NexusOps.Contracts/Messages/BeginInvestigationFanOut.cs` — the internal event record (`CorrelationId`, `OrderId`)
- [ ] T019 [P] Create `NexusOps.Contracts/Messages/OrderFindingMessages.cs` — `RequestOrderFinding` and `OrderFindingReported` records
- [ ] T020 [P] Create `NexusOps.Contracts/Messages/InventoryFindingMessages.cs` — `RequestInventoryFinding` and `InventoryFindingReported` records
- [ ] T021 [P] Create `NexusOps.Contracts/Messages/ProductFindingMessages.cs` — `RequestProductFinding` and `ProductFindingReported` records
- [ ] T022 Add `ToolNames.InvestigateOrderRootCause` and its description constant to `NexusOps.Contracts/ToolNames.cs`, alongside the existing six — do not modify any existing constant
- [ ] T023 Create `NexusOps.WorkflowOrchestrator/OrderInvestigation/OrderInvestigationSagaState.cs` — the saga entity per `data-model.md` (`CorrelationId`, `CurrentState`, `OrderId`, `ResponseAddress`, `RequestId`, the three `SourceFindingStatus` fields, the three result-JSON fields, `StartedAt`, `CompletedAt`, `RowVersion`)
- [ ] T024 Create `NexusOps.WorkflowOrchestrator/OrderInvestigation/OrderInvestigationDbContext.cs` — EF Core `DbContext` mapping `OrderInvestigationSagaState`, with `RowVersion` configured `.IsRowVersion()` against Postgres `xmin`
- [ ] T025 Generate the initial EF Core migration for `OrderInvestigationDbContext` (`dotnet ef migrations add InitialCreate --project NexusOps.WorkflowOrchestrator`)
- [ ] T026 Create `NexusOps.WorkflowOrchestrator/Program.cs` — `AddServiceDefaults()`, register `OrderInvestigationDbContext` against the `workfloworchestrator` Aspire connection string, configure the MassTransit bus (`UsingRabbitMq`), register MassTransit's bus health check tagged `ready`, and call the not-yet-created `AddOrderInvestigationSaga(...)` (stub it as a no-op extension method for now; implemented fully in T036)
- [ ] T027 Register a MassTransit bus client (`UsingRabbitMq`, no consumers) in `NexusOps.AgentHost/Program.cs`
- [ ] T028 Register `AddRequestClient<InvestigateOrderRootCause>(RequestTimeout.After(s: 8))` in `NexusOps.AgentHost/Program.cs`
- [ ] T029 [P] Register a MassTransit bus client (`UsingRabbitMq`, no consumers) in `NexusOps.OrderService/Program.cs`
- [ ] T030 [P] Register a MassTransit bus client in `NexusOps.InventoryService/Program.cs`
- [ ] T031 [P] Register a MassTransit bus client in `NexusOps.ProductService/Program.cs`

**Checkpoint**: `dotnet run --project NexusOps.AppHost` shows `rabbitmq`, `postgres`, and `workflow-orchestrator` healthy alongside every existing resource. No investigation behavior exists yet — user story implementation starts next.

---

## Phase 3: User Story 1 - Cross-Service Root-Cause Investigation (Priority: P1) 🎯 MVP

**Goal**: A specific order can be investigated end-to-end: the agent's new tool call reaches the saga, the saga fans out to all three domain services, and — when everything succeeds — a consolidated `Complete` result comes back to the operator.

**Independent Test**: Ask the agent to investigate a known order with an out-of-stock item; confirm the response cites both the order's own condition and the item's stock shortfall, with no follow-up question needed (spec.md User Story 1).

### Tests for User Story 1

- [ ] T032 [P] [US1] MassTransit test-harness test: `OrderInvestigationSaga` receives all three `*FindingReported` events as `Succeeded` and finalizes `Completed`/`Complete`, responding to the captured `ResponseAddress` — in `NexusOps.Tests/WorkflowOrchestrator/OrderInvestigationSagaTests.cs`
- [ ] T033 [P] [US1] MassTransit test-harness test: `InvestigationFanOutConsumer` issues the order lookup first, then inventory+product concurrently, and publishes three `Succeeded` finding events on a fully-healthy set of mocked request clients — in `NexusOps.Tests/WorkflowOrchestrator/InvestigationFanOutConsumerTests.cs`

### Implementation for User Story 1

- [ ] T034 [US1] Create `NexusOps.WorkflowOrchestrator/OrderInvestigation/OrderInvestigationSaga.cs` — `MassTransitStateMachine<OrderInvestigationSagaState>` with `Investigating`/`Completed`/`Failed` states; on `InvestigateOrderRootCause`, capture `ResponseAddress`/`RequestId`, create the instance, `Publish(BeginInvestigationFanOut)`; correlate the three `*FindingReported` events by `CorrelationId`, recording each into saga state; implement the all-`Succeeded` finalize-and-respond path (degraded/failed paths land in US2)
- [ ] T035 [US1] Create `NexusOps.WorkflowOrchestrator/OrderInvestigation/InvestigationFanOutConsumer.cs` — `IConsumer<BeginInvestigationFanOut>`; await the order lookup via `IRequestClient<RequestOrderFinding>` first, then `Task.WhenAll` the inventory and product lookups via `IRequestClient<RequestInventoryFinding>`/`IRequestClient<RequestProductFinding>`; publish the three `*FindingReported` events on the success path
- [ ] T036 [US1] Create `NexusOps.WorkflowOrchestrator/OrderInvestigation/ServiceCollectionExtensions.cs` — `AddOrderInvestigationSaga(this IBusRegistrationConfigurator)` registering the saga against the EF Core repository (`ConcurrencyMode.Optimistic`) and `InvestigationFanOutConsumer`; replace the T026 stub call with this real implementation
- [ ] T037 [US1] Create `NexusOps.OrderService/Consumers/RequestOrderFindingConsumer.cs` — `IConsumer<RequestOrderFinding>`, looks up the order via the existing `OrderStore`, publishes `OrderFindingReported` (`Succeeded` with the `OrderSummary`, or `NotFound`)
- [ ] T038 [US1] Register `RequestOrderFindingConsumer` with the bus in `NexusOps.OrderService/Program.cs`
- [ ] T039 [US1] Create `NexusOps.InventoryService/Consumers/RequestInventoryFindingConsumer.cs` — `IConsumer<RequestInventoryFinding>`, batch SKU lookup via the existing `InventoryStore`, publishes `InventoryFindingReported` with per-SKU results and `SkusNotFound`
- [ ] T040 [US1] Register `RequestInventoryFindingConsumer` with the bus in `NexusOps.InventoryService/Program.cs`
- [ ] T041 [US1] Create `NexusOps.ProductService/Consumers/RequestProductFindingConsumer.cs` — `IConsumer<RequestProductFinding>`, batch SKU lookup via the existing `ProductStore`, publishes `ProductFindingReported` with per-SKU results and `SkusNotFound`
- [ ] T042 [US1] Register `RequestProductFindingConsumer` with the bus in `NexusOps.ProductService/Program.cs`
- [ ] T043 [US1] Add an `InvestigateOrderRootCauseAsync` handler to `NexusOps.AgentHost/Tools/OrderTools.cs` — calls `IRequestClient<InvestigateOrderRootCause>.GetResponse<RootCauseInvestigationResult>`, maps the result to `ToolResult<RootCauseInvestigationResult>`
- [ ] T044 [US1] Register the new tool via `AIFunctionFactory.Create` in `NexusOps.AgentHost/Tools/ToolHandlerExtensions.cs`, alongside the existing six — do not reorder or modify the existing entries
- [ ] T045 [US1] Manually verify `quickstart.md` step 1 (happy path) via `aspire start` + the documented `curl`/chat prompt
- [ ] T045a [US1] Record wall-clock latency from request to response for the T045 happy-path investigation and confirm it is under 3 seconds (SC-006); note the observed value in this feature's completion notes

**Checkpoint**: A healthy three-service investigation works end-to-end and is independently demonstrable.

---

## Phase 4: User Story 2 - Investigation Survives a Degraded Service (Priority: P1)

**Goal**: One, two, or all three sources failing or timing out still produces a truthful result — partial findings plus a clear degradation signal, or an explicit "could not complete" when nothing came back.

**Independent Test**: Stop one downstream service, investigate an order, confirm the response still contains the two healthy findings plus an explicit note identifying the unavailable source (spec.md User Story 2).

### Tests for User Story 2

- [ ] T046 [P] [US2] Saga test: one source reports `Unavailable`/`TimedOut`, the other two `Succeeded` → finalizes `Completed`/`Degraded` with the correct `DegradedSources` entry — in `OrderInvestigationSagaTests.cs`
- [ ] T047 [P] [US2] Saga test: all three sources report non-`Succeeded` → finalizes `Failed`; the order-not-found case (all `NotFound`, no line items) finalizes `Completed`/`Complete` instead — in `OrderInvestigationSagaTests.cs`
- [ ] T048 [P] [US2] Fan-out consumer test: a `RequestTimeoutException` maps to `TimedOut` and a `RequestFaultException`/unhandled exception maps to `Unavailable`, for each of the three legs independently — in `InvestigationFanOutConsumerTests.cs`
- [ ] T048a [P] [US2] Saga test: a `*FindingReported` event carrying a `CorrelationId` with no matching saga instance (already finalized and removed, or never existed) is consumed without error and produces no observable state change — in `OrderInvestigationSagaTests.cs` (FR-011)

### Implementation for User Story 2

- [ ] T049 [US2] Add the 5-second per-leg timeout to all three `IRequestClient` calls in `InvestigationFanOutConsumer.cs`; catch `RequestTimeoutException` → publish `TimedOut`, catch `RequestFaultException`/`Exception` → publish `Unavailable`, so no exception ever leaves a finding unpublished
- [ ] T050 [US2] Add the "order lookup itself fails or times out" short-circuit to `InvestigationFanOutConsumer.cs` — immediately publish `InventoryFindingReported`/`ProductFindingReported` as `Unavailable` with empty results, since there are no line-item SKUs to look up
- [ ] T051 [US2] Implement the `Completeness`/`DegradedSources` computation in `OrderInvestigationSaga.cs`'s finalize logic (per `data-model.md`'s state-transition rule): `Failed` only when the order source itself is non-`Succeeded`/non-confirmed-`NotFound`; otherwise `Degraded` if any source is incomplete, else `Complete`
- [ ] T052 [US2] Handle the `Degraded`/`Failed` response paths in `OrderTools.InvestigateOrderRootCauseAsync` — `Degraded` still returns `ToolResult.Ok` with `Completeness` populated for the agent to phrase; `Failed` returns `ToolResult.Fail` with a clear message; a `RequestTimeoutException` from the AgentHost-side 8s client timeout returns `ToolResult.Fail("...timed out...")`
- [ ] T053 [US2] Manually verify `quickstart.md` steps 2–3 (degraded, full-failure) by stopping domain-service containers via the Aspire dashboard

**Checkpoint**: Every degradation and failure path in the spec's Edge Cases is independently demonstrable, without regressing User Story 1's happy path.

---

## Phase 5: User Story 3 - Anomaly Listing and Root-Cause Investigation Stay Distinct (Priority: P1)

**Goal**: The agent routes broad anomaly-listing prompts, narrow "why" prompts, and plain status prompts to the correct one of three tools, and `investigate_order_anomaly`'s own behavior is provably unchanged.

**Independent Test**: A mixed batch of prompts covering all three phrasings is each answered by the correct tool, with zero change in behavior for prompts the agent already handled correctly before this feature (spec.md User Story 3).

### Tests for User Story 3

- [ ] T054 [P] [US3] Re-run the existing anomaly-listing test suite unmodified and confirm 100% pass with no assertion changes (SC-004 regression guard); add this as an explicit `dotnet test --filter` step documented in this feature's completion notes if no such CI step already exists

### Implementation for User Story 3

- [ ] T055 [US3] Update the default `AgentInstructions` in `NexusOps.AgentHost/Configuration/AzureAIOptions.cs` — add the three-way routing rule from `contracts/investigate-order-root-cause-tool.md` (broad anomaly list → `investigate_order_anomaly`; specific order + "why" → `investigate_order_root_cause`; specific order + plain status → `get_order_details`), leaving all existing routing rules for the other five tools untouched
- [ ] T056 [US3] Manually verify the three-way routing distinction with a batch of prompts covering all three phrasings (SC-005) and record the pass rate in this feature's completion notes — automated regression for this criterion is deferred to feature 007's Evaluation runner (see spec.md Assumptions)

**Checkpoint**: All three P1 user stories are independently functional and demonstrable together.

---

## Phase 6: User Story 4 - Investigation Reliability Under Process Restart (Priority: P3)

**Goal**: The durability and concurrency-safety properties the earlier phases already built on (persisted saga state, optimistic concurrency, message redelivery) are explicitly exercised and confirmed, rather than left as an unverified side-effect of the design.

**Independent Test**: Start an investigation, restart the orchestrating process mid-flight, confirm the operator's request eventually resolves rather than hanging indefinitely (spec.md User Story 4).

### Tests for User Story 4

- [ ] T057 [P] [US4] Saga test simulating two `*FindingReported` events for the same `CorrelationId` processed concurrently (a genuine `RowVersion` race) and asserting neither update is lost — in `OrderInvestigationSagaTests.cs`

### Implementation for User Story 4

- [ ] T058 [US4] Confirm the MassTransit EF Core saga repository configuration in `ServiceCollectionExtensions.cs` retries on `DbUpdateConcurrencyException` (verify the documented default behavior; add an explicit retry policy only if the default does not already cover it)
- [ ] T059 [US4] Confirm `UseMessageRetry` with exponential back-off is configured for `InvestigationFanOutConsumer`'s receive endpoint in `Program.cs` (T026/T036), so a mid-fan-out crash results in redelivery rather than a lost `BeginInvestigationFanOut` message
- [ ] T060 [US4] Manually verify restart survival: start an investigation, restart the `NexusOps.WorkflowOrchestrator` process mid-flight via the Aspire dashboard, confirm the operator's request eventually resolves (complete or clearly failed) rather than hanging

**Checkpoint**: All four user stories are independently functional and demonstrable.

**Note**: SC-007 (restart survival) is verified manually here (T060) rather than via automated fault injection. Automated process-restart coverage is deferred to `ROADMAP.md` Prompt 6's `Aspire.Hosting.Testing` integration tests, which cover restart/failure scenarios across all sagas together.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Final checks spanning every story above.

- [ ] T061 [P] Confirm dead-letter (`_error`) queue behavior for every saga-to-service queue by forcing a poison message in a local test, and note the observed behavior in this feature's completion notes (Constitution IV reliability requirement)
- [ ] T062 Run `dotnet test` and confirm every existing test plus every new `NexusOps.Tests/WorkflowOrchestrator/*` test passes, with zero regressions
- [ ] T063 Run all four `quickstart.md` verification steps end-to-end via `aspire start`

**Note**: Updating `CLAUDE.md`'s Current Build State to describe the new host, saga, and tool is explicitly `ROADMAP.md` Prompt 3's ("Implement 005") responsibility, not this tasks.md's — it happens once the tasks above are actually implemented, not as part of task generation.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately
- **Foundational (Phase 2)**: Depends on Setup — BLOCKS every user story
- **User Story 1 (Phase 3)**: Depends on Foundational only
- **User Story 2 (Phase 4)**: Depends on Foundational **and** on User Story 1's saga/consumer/tool skeleton existing (it extends the same files rather than creating new ones) — not independently implementable before US1, but independently *testable* once both exist
- **User Story 3 (Phase 5)**: Depends on Foundational and on US1's tool registration (T043–T044) existing to route to; otherwise touches only `AzureAIOptions.cs`
- **User Story 4 (Phase 6)**: Depends on Foundational, US1, and US2 (it verifies properties of code those phases wrote; it does not add new production code paths)
- **Polish (Phase 7)**: Depends on all four user stories being complete

### Within Each User Story

- Tests are written before implementation and must fail first (MassTransit test harness against not-yet-implemented consumers/saga behavior)
- Contracts/entities (Phase 2) before consumers before saga logic before the AgentHost tool handler
- Story complete and its Checkpoint verified before moving to the next priority

### Parallel Opportunities

- All Setup tasks marked `[P]` (T002–T011) can run in parallel — different files, no shared dependencies
- Within Foundational, T016–T021 (Contracts message/DTO files) can run in parallel; T029–T031 (bus registration in the three domain services) can run in parallel
- Within US1, T032–T033 (tests) can run in parallel with each other; T037/T039/T041 (the three domain-service consumers) can run in parallel with each other once T034–T036 exist
- Within US2, T046–T048 (tests) can run in parallel with each other
- US3's single implementation task (T055) can proceed in parallel with US2's implementation, since it touches only `AzureAIOptions.cs`

---

## Parallel Example: User Story 1

```bash
# Launch both tests for User Story 1 together:
Task: "MassTransit test-harness test: OrderInvestigationSaga finalizes Completed/Complete on all-Succeeded findings — NexusOps.Tests/WorkflowOrchestrator/OrderInvestigationSagaTests.cs"
Task: "MassTransit test-harness test: InvestigationFanOutConsumer publishes three Succeeded findings on healthy mocked request clients — NexusOps.Tests/WorkflowOrchestrator/InvestigationFanOutConsumerTests.cs"

# Once the saga/consumer skeleton exists, launch the three domain-service consumers together:
Task: "Create RequestOrderFindingConsumer in NexusOps.OrderService/Consumers/RequestOrderFindingConsumer.cs"
Task: "Create RequestInventoryFindingConsumer in NexusOps.InventoryService/Consumers/RequestInventoryFindingConsumer.cs"
Task: "Create RequestProductFindingConsumer in NexusOps.ProductService/Consumers/RequestProductFindingConsumer.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup
2. Complete Phase 2: Foundational (blocks everything)
3. Complete Phase 3: User Story 1
4. **STOP and VALIDATE**: run `quickstart.md` step 1 independently
5. This is the MVP: a working cross-service investigation on the happy path

### Incremental Delivery

1. Setup + Foundational → topology and contracts exist, nothing investigates yet
2. Add User Story 1 → happy-path investigation works → demo-able MVP
3. Add User Story 2 → degradation and failure are truthful, not just absent → demo-able
4. Add User Story 3 → routing is provably correct and non-regressive → demo-able
5. Add User Story 4 → durability/concurrency properties are explicitly proven, not assumed
6. Polish → full regression pass + documented verification

---

## Notes

- `[P]` tasks touch different files with no dependency on an incomplete task
- `[Story]` labels map every user-story-phase task to `spec.md`'s US1–US4 for traceability
- No approval gate appears anywhere in this task list — this feature is read-only end to end (FR-012)
- `OrderActionSaga`, refund/cancel tools, and the Notification Service are explicitly out of scope here (`ROADMAP.md` Prompt 4 / feature 006)
