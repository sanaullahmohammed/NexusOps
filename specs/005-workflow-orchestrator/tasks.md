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

- [X] T001 Create `NexusOps.WorkflowOrchestrator/NexusOps.WorkflowOrchestrator.csproj` (`Microsoft.NET.Sdk.Web`, `net10.0`, `Nullable`/`ImplicitUsings` enabled, `ProjectReference` to `NexusOps.Contracts` and `NexusOps.ServiceDefaults`), `NexusOps.WorkflowOrchestrator/.gitignore` (`bin/`, `obj/`, `out/`, `*.nupkg`, `*.lscache` per the project's `.NET project conventions`), and a minimal `NexusOps.WorkflowOrchestrator/Program.cs` (`builder.AddServiceDefaults(); app.MapDefaultEndpoints(); app.Run();`)
- [X] T002 [P] Add `NexusOps.WorkflowOrchestrator` to `NexusOps.sln`
- [X] T003 [P] Add `NexusOps.WorkflowOrchestrator` to `NexusOps.deployable.slnf`
- [X] T004 [P] Add `MassTransit`, `MassTransit.RabbitMQ`, `MassTransit.EntityFrameworkCore` package references (pinned to the `8.3.x` line) to `NexusOps.WorkflowOrchestrator/NexusOps.WorkflowOrchestrator.csproj`
- [X] T005 [P] Add `Npgsql.EntityFrameworkCore.PostgreSQL` and `Aspire.Npgsql.EntityFrameworkCore.PostgreSQL` package references to `NexusOps.WorkflowOrchestrator/NexusOps.WorkflowOrchestrator.csproj`
- [X] T006 [P] Add `Aspire.Hosting.RabbitMQ` and `Aspire.Hosting.PostgreSQL` package references to `NexusOps.AppHost/NexusOps.AppHost.csproj`
- [X] T007 [P] Add a Dependabot major-version ignore rule for all `MassTransit*` packages in `.github/dependabot.yml` (locked in `ROADMAP.md`: v9 is commercial, out of scope)
- [X] T008 [P] Add `MassTransit` + `MassTransit.RabbitMQ` package references to `NexusOps.AgentHost/NexusOps.AgentHost.csproj`
- [X] T009 [P] Add `MassTransit` + `MassTransit.RabbitMQ` package references to `NexusOps.OrderService/NexusOps.OrderService.csproj`
- [X] T010 [P] Add `MassTransit` + `MassTransit.RabbitMQ` package references to `NexusOps.InventoryService/NexusOps.InventoryService.csproj`
- [X] T011 [P] Add `MassTransit` + `MassTransit.RabbitMQ` package references to `NexusOps.ProductService/NexusOps.ProductService.csproj`

**Checkpoint**: `dotnet restore` succeeds across the solution; the new project builds as an empty host.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Topology, contracts, and bus registration that every user story depends on. No user-story-specific behavior lives here — only the plumbing that makes it possible to add that behavior next.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T012 Add a RabbitMQ resource to `NexusOps.AppHost/Program.cs` — `builder.AddRabbitMQ("rabbitmq").WithManagementPlugin()`
- [X] T013 Add a PostgreSQL resource + database to `NexusOps.AppHost/Program.cs` — `builder.AddPostgres("postgres").WithDataVolume()`, then `.AddDatabase("workfloworchestrator")`
- [X] T014 Add the `NexusOps.WorkflowOrchestrator` project resource to `NexusOps.AppHost/Program.cs` — `.WithHttpHealthCheck("/health")`, `.WithReference(rabbitmq)`, `.WithReference(workflowOrchestratorDb)`, `.WaitFor(rabbitmq)`, `.WaitFor(workflowOrchestratorDb)`
- [X] T015 Chain `.WithReference(rabbitmq).WaitFor(rabbitmq)` onto the `agentHost`, `orderService`, `inventoryService`, and `productService` builders in `NexusOps.AppHost/Program.cs`
- [X] T016 [P] Create `NexusOps.Contracts/Dtos/RootCauseInvestigation.cs` — `SourceFindingStatus` enum (`Pending`, `Succeeded`, `NotFound`, `Unavailable`, `TimedOut`), `InvestigationCompleteness` enum (`Complete`, `Degraded`, `Failed`), and the `RootCauseInvestigationResult` record, per `data-model.md`
- [X] T017 [P] Create `NexusOps.Contracts/Messages/InvestigateOrderRootCause.cs` — the request record (`OrderId`)
- [X] T018 [P] Create `NexusOps.Contracts/Messages/BeginInvestigationFanOut.cs` — the internal event record (`CorrelationId`, `OrderId`)
- [X] T019 [P] Create `NexusOps.Contracts/Messages/OrderFindingMessages.cs` — `RequestOrderFinding` and `OrderFindingReported` records
- [X] T020 [P] Create `NexusOps.Contracts/Messages/InventoryFindingMessages.cs` — `RequestInventoryFinding` and `InventoryFindingReported` records
- [X] T021 [P] Create `NexusOps.Contracts/Messages/ProductFindingMessages.cs` — `RequestProductFinding` and `ProductFindingReported` records
- [X] T022 Add `ToolNames.InvestigateOrderRootCause` and its description constant to `NexusOps.Contracts/ToolNames.cs`, alongside the existing six — do not modify any existing constant
- [X] T023 Create `NexusOps.WorkflowOrchestrator/OrderInvestigation/OrderInvestigationSagaState.cs` — the saga entity per `data-model.md` (`CorrelationId`, `CurrentState`, `OrderId`, `ResponseAddress`, `RequestId`, the three `SourceFindingStatus` fields, the three result-JSON fields, `StartedAt`, `CompletedAt`, `RowVersion`)
- [X] T024 Create `NexusOps.WorkflowOrchestrator/OrderInvestigation/OrderInvestigationDbContext.cs` — EF Core `DbContext` mapping `OrderInvestigationSagaState`, with `RowVersion` configured `.IsRowVersion()` against Postgres `xmin`
- [X] T025 Generate the initial EF Core migration for `OrderInvestigationDbContext` (`dotnet ef migrations add InitialCreate --project NexusOps.WorkflowOrchestrator`)
- [X] T026 Create `NexusOps.WorkflowOrchestrator/Program.cs` — `AddServiceDefaults()`, register `OrderInvestigationDbContext` against the `workfloworchestrator` Aspire connection string, configure the MassTransit bus (`UsingRabbitMq`), register MassTransit's bus health check tagged `ready`, and call the not-yet-created `AddOrderInvestigationSaga(...)` (stub it as a no-op extension method for now; implemented fully in T036)
- [X] T027 Register a MassTransit bus client (`UsingRabbitMq`, no consumers) in `NexusOps.AgentHost/Program.cs`
- [X] T028 Create the request client via `IClientFactory.CreateRequestClient<InvestigateOrderRootCause>(RequestTimeout.After(s: 12))` in `NexusOps.AgentHost/Tools/OrderTools.cs` (not `AddRequestClient` in `Program.cs` — `OrderTools` is a singleton and `IRequestClient<T>` is scoped, so the client is minted per call via the singleton-safe `IClientFactory`) — the 12s figure was corrected from an initial 8s during code review: the fan-out's worst case is order (5s) + max(inventory, product) (5s) = 10s, which an 8s client timeout would misreport as "timed out" for a case the saga was about to answer correctly with `Degraded`
- [X] T029 [P] Register a MassTransit bus client (`UsingRabbitMq`, no consumers) in `NexusOps.OrderService/Program.cs`
- [X] T030 [P] Register a MassTransit bus client in `NexusOps.InventoryService/Program.cs`
- [X] T031 [P] Register a MassTransit bus client in `NexusOps.ProductService/Program.cs`

**Checkpoint**: `dotnet run --project NexusOps.AppHost` shows `rabbitmq`, `postgres`, and `workflow-orchestrator` healthy alongside every existing resource. No investigation behavior exists yet — user story implementation starts next.

---

## Phase 3: User Story 1 - Cross-Service Root-Cause Investigation (Priority: P1) 🎯 MVP

**Goal**: A specific order can be investigated end-to-end: the agent's new tool call reaches the saga, the saga fans out to all three domain services, and — when everything succeeds — a consolidated `Complete` result comes back to the operator.

**Independent Test**: Ask the agent to investigate a known order with an out-of-stock item; confirm the response cites both the order's own condition and the item's stock shortfall, with no follow-up question needed (spec.md User Story 1).

### Tests for User Story 1

- [X] T032 [P] [US1] MassTransit test-harness test: `OrderInvestigationSaga` receives all three `*FindingReported` events as `Succeeded` and finalizes `Completed`/`Complete`, responding to the captured `ResponseAddress` — in `NexusOps.Tests/WorkflowOrchestrator/OrderInvestigationSagaTests.cs`
- [X] T033 [P] [US1] MassTransit test-harness test: `InvestigationFanOutConsumer` issues the order lookup first, then inventory+product concurrently, and publishes three `Succeeded` finding events on a fully-healthy set of mocked request clients — in `NexusOps.Tests/WorkflowOrchestrator/InvestigationFanOutConsumerTests.cs`

### Implementation for User Story 1

- [X] T034 [US1] Create `NexusOps.WorkflowOrchestrator/OrderInvestigation/OrderInvestigationSaga.cs` — `MassTransitStateMachine<OrderInvestigationSagaState>` with `Investigating`/`Completed`/`Failed` states; on `InvestigateOrderRootCause`, capture `ResponseAddress`/`RequestId`, create the instance, `Publish(BeginInvestigationFanOut)`; correlate the three `*FindingReported` events by `CorrelationId`, recording each into saga state; implement the all-`Succeeded` finalize-and-respond path (degraded/failed paths land in US2)
- [X] T035 [US1] Create `NexusOps.WorkflowOrchestrator/OrderInvestigation/InvestigationFanOutConsumer.cs` — `IConsumer<BeginInvestigationFanOut>`; await the order lookup via `IRequestClient<RequestOrderFinding>` first, then `Task.WhenAll` the inventory and product lookups via `IRequestClient<RequestInventoryFinding>`/`IRequestClient<RequestProductFinding>`; publish the three `*FindingReported` events on the success path
- [X] T036 [US1] Create `NexusOps.WorkflowOrchestrator/OrderInvestigation/ServiceCollectionExtensions.cs` — `AddOrderInvestigationSaga(this IBusRegistrationConfigurator)` registering the saga against the EF Core repository (`ConcurrencyMode.Optimistic`) and `InvestigationFanOutConsumer`; replace the T026 stub call with this real implementation
- [X] T037 [US1] Create `NexusOps.OrderService/Consumers/RequestOrderFindingConsumer.cs` — `IConsumer<RequestOrderFinding>`, looks up the order via the existing `OrderStore`, publishes `OrderFindingReported` (`Succeeded` with the `OrderSummary`, or `NotFound`)
- [X] T038 [US1] Register `RequestOrderFindingConsumer` with the bus in `NexusOps.OrderService/Program.cs`
- [X] T039 [US1] Create `NexusOps.InventoryService/Consumers/RequestInventoryFindingConsumer.cs` — `IConsumer<RequestInventoryFinding>`, batch SKU lookup via the existing `InventoryStore`, publishes `InventoryFindingReported` with per-SKU results and `SkusNotFound`
- [X] T040 [US1] Register `RequestInventoryFindingConsumer` with the bus in `NexusOps.InventoryService/Program.cs`
- [X] T041 [US1] Create `NexusOps.ProductService/Consumers/RequestProductFindingConsumer.cs` — `IConsumer<RequestProductFinding>`, batch SKU lookup via the existing `ProductStore`, publishes `ProductFindingReported` with per-SKU results and `SkusNotFound`
- [X] T042 [US1] Register `RequestProductFindingConsumer` with the bus in `NexusOps.ProductService/Program.cs`
- [X] T043 [US1] Add an `InvestigateOrderRootCauseAsync` handler to `NexusOps.AgentHost/Tools/OrderTools.cs` — calls `IRequestClient<InvestigateOrderRootCause>.GetResponse<RootCauseInvestigationResult>`, maps the result to `ToolResult<RootCauseInvestigationResult>`
- [X] T044 [US1] Register the new tool via `AIFunctionFactory.Create` in `NexusOps.AgentHost/Tools/ToolHandlerExtensions.cs`, alongside the existing six — do not reorder or modify the existing entries
- [X] T045 [US1] Manually verify `quickstart.md` step 1 (happy path) via `aspire start` + the documented `curl`/chat prompt — verified live with Azure AI credentials once `AZURE_AI_FOUNDRY_API_KEY` was available: `POST /api/chat` with "investigate the root cause for order ORD-0003" correctly invoked `investigate_order_root_cause`, returned `Completeness: Complete` citing the SKU-ELEC-001 stockout, and the agent's natural-language answer accurately explained the root cause.
- [X] T045a [US1] Record wall-clock latency from request to response for the T045 happy-path investigation and confirm it is under 3 seconds (SC-006); note the observed value in this feature's completion notes — measured directly against live `aspire start` infrastructure (real RabbitMQ + Postgres) with a throwaway MassTransit client publishing `InvestigateOrderRootCause` for ORD-0003 (bypassing the LLM/chat layer, which needs credentials this environment doesn't have): **1874ms**, well under the 3s target. A second run against ORD-9999 (nonexistent order) resolved in 129ms with `Completeness: Complete` and all three findings `NotFound`, confirming the "nothing to check" edge case is fast, not just correct.

**Checkpoint**: A healthy three-service investigation works end-to-end and is independently demonstrable.

---

## Phase 4: User Story 2 - Investigation Survives a Degraded Service (Priority: P1)

**Goal**: One, two, or all three sources failing or timing out still produces a truthful result — partial findings plus a clear degradation signal, or an explicit "could not complete" when nothing came back.

**Independent Test**: Stop one downstream service, investigate an order, confirm the response still contains the two healthy findings plus an explicit note identifying the unavailable source (spec.md User Story 2).

### Tests for User Story 2

- [X] T046 [P] [US2] Saga test: one source reports `Unavailable`/`TimedOut`, the other two `Succeeded` → finalizes `Completed`/`Degraded` with the correct `DegradedSources` entry — in `OrderInvestigationSagaTests.cs`
- [X] T047 [P] [US2] Saga test: all three sources report non-`Succeeded` → finalizes `Failed`; the order-not-found case (all `NotFound`, no line items) finalizes `Completed`/`Complete` instead — in `OrderInvestigationSagaTests.cs`
- [X] T048 [P] [US2] Fan-out consumer test: a `RequestTimeoutException` maps to `TimedOut` and a `RequestFaultException`/unhandled exception maps to `Unavailable`, for each of the three legs independently — in `InvestigationFanOutConsumerTests.cs`
- [X] T048a [P] [US2] Saga test: a `*FindingReported` event carrying a `CorrelationId` with no matching saga instance (already finalized and removed, or never existed) is consumed without error and produces no observable state change — in `OrderInvestigationSagaTests.cs` (FR-011)

### Implementation for User Story 2

- [X] T049 [US2] Add the 5-second per-leg timeout to all three `IRequestClient` calls in `InvestigationFanOutConsumer.cs`; catch `RequestTimeoutException` → publish `TimedOut`, catch `RequestFaultException`/`Exception` → publish `Unavailable`, so no exception ever leaves a finding unpublished
- [X] T050 [US2] Add the "order lookup itself fails or times out" short-circuit to `InvestigationFanOutConsumer.cs` — immediately publish `InventoryFindingReported`/`ProductFindingReported` as `Unavailable` with empty results, since there are no line-item SKUs to look up
- [X] T051 [US2] Implement the `Completeness`/`DegradedSources` computation in `OrderInvestigationSaga.cs`'s finalize logic (per `data-model.md`'s state-transition rule): `Failed` only when the order source itself is non-`Succeeded`/non-confirmed-`NotFound`; otherwise `Degraded` if any source is incomplete, else `Complete`
- [X] T052 [US2] Handle the `Degraded`/`Failed` response paths in `OrderTools.InvestigateOrderRootCauseAsync` — `Degraded` still returns `ToolResult.Ok` with `Completeness` populated for the agent to phrase; `Failed` returns `ToolResult.Fail` with a clear message; a `RequestTimeoutException` from the AgentHost-side 12s client timeout returns `ToolResult.Fail("...timed out...")`
- [X] T053 [US2] Manually verify `quickstart.md` steps 2–3 (degraded, full-failure) by stopping domain-service containers via the Aspire dashboard — verified the degraded path live: killed InventoryService mid-run, re-investigated ORD-0003, got `Completeness: Degraded`, `DegradedSources: ["Inventory"]`, `InventoryFinding: TimedOut`, with the agent correctly explaining it couldn't confirm stock availability. Round trip: 11.1s (dominated by the 5s per-source timeout + LLM latency), no hang, no crash. Full-failure (all three services down) not separately re-verified live in this session — the saga-level `Failed`/`ToolResult.Fail` path is covered by `OrderInvestigationSagaTests.cs` (T047).

**Checkpoint**: Every degradation and failure path in the spec's Edge Cases is independently demonstrable, without regressing User Story 1's happy path.

---

## Phase 5: User Story 3 - Anomaly Listing and Root-Cause Investigation Stay Distinct (Priority: P1)

**Goal**: The agent routes broad anomaly-listing prompts, narrow "why" prompts, and plain status prompts to the correct one of three tools, and `investigate_order_anomaly`'s own behavior is provably unchanged.

**Independent Test**: A mixed batch of prompts covering all three phrasings is each answered by the correct tool, with zero change in behavior for prompts the agent already handled correctly before this feature (spec.md User Story 3).

### Tests for User Story 3

- [X] T054 [P] [US3] Re-run the existing anomaly-listing test suite unmodified and confirm 100% pass with no assertion changes (SC-004 regression guard); add this as an explicit `dotnet test --filter` step documented in this feature's completion notes if no such CI step already exists

### Implementation for User Story 3

- [X] T055 [US3] Update the default `AgentInstructions` in `NexusOps.AgentHost/Configuration/AzureAIOptions.cs` — add the three-way routing rule from `contracts/investigate-order-root-cause-tool.md` (broad anomaly list → `investigate_order_anomaly`; specific order + "why" → `investigate_order_root_cause`; specific order + plain status → `get_order_details`), leaving all existing routing rules for the other five tools untouched
- [X] T056 [US3] Manually verify the three-way routing distinction with a batch of prompts covering all three phrasings (SC-005) and record the pass rate in this feature's completion notes — automated regression for this criterion is deferred to feature 007's Evaluation runner (see spec.md Assumptions). Verified live, 3/3 correct: "Show me all delayed orders" → `investigate_order_anomaly` (table of anomalies, unchanged shape); "What is the status of order ORD-0003?" → `get_order_details` (plain status, no cross-service investigation); "investigate the root cause for order ORD-0003" → `investigate_order_root_cause` (full cross-service finding). Small sample (3 prompts), not the full batch feature 007 will eventually cover.

**Checkpoint**: All three P1 user stories are independently functional and demonstrable together.

---

## Phase 6: User Story 4 - Investigation Reliability Under Process Restart (Priority: P3)

**Goal**: The durability and concurrency-safety properties the earlier phases already built on (persisted saga state, optimistic concurrency, message redelivery) are explicitly exercised and confirmed, rather than left as an unverified side-effect of the design.

**Independent Test**: Start an investigation, restart the orchestrating process mid-flight, confirm the operator's request eventually resolves rather than hanging indefinitely (spec.md User Story 4).

### Tests for User Story 4

- [X] T057 [P] [US4] Saga test simulating two `*FindingReported` events for the same `CorrelationId` processed concurrently (a genuine `RowVersion` race) and asserting neither update is lost — in `OrderInvestigationSagaTests.cs`

### Implementation for User Story 4

- [X] T058 [US4] Confirm the MassTransit EF Core saga repository configuration in `ServiceCollectionExtensions.cs` retries on `DbUpdateConcurrencyException` (verify the documented default behavior; add an explicit retry policy only if the default does not already cover it)
- [X] T059 [US4] Confirm `UseMessageRetry` with exponential back-off is configured for `InvestigationFanOutConsumer`'s receive endpoint in `Program.cs` (T026/T036), so a mid-fan-out crash results in redelivery rather than a lost `BeginInvestigationFanOut` message
- [ ] T060 [US4] Manually verify restart survival: start an investigation, restart the `NexusOps.WorkflowOrchestrator` process mid-flight via the Aspire dashboard, confirm the operator's request eventually resolves (complete or clearly failed) rather than hanging

**Checkpoint**: All four user stories are independently functional and demonstrable.

**Note**: SC-007 (restart survival) is verified manually here (T060) rather than via automated fault injection. Automated process-restart coverage is deferred to `ROADMAP.md` Prompt 6's `Aspire.Hosting.Testing` integration tests, which cover restart/failure scenarios across all sagas together.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Final checks spanning every story above.

- [ ] T061 [P] Confirm dead-letter (`_error`) queue behavior for every saga-to-service queue by forcing a poison message in a local test, and note the observed behavior in this feature's completion notes (Constitution IV reliability requirement)
- [X] T062 Run `dotnet test` and confirm every existing test plus every new `NexusOps.Tests/WorkflowOrchestrator/*` test passes, with zero regressions
- [ ] T063 Run all four `quickstart.md` verification steps end-to-end via `aspire start`

**Note**: Updating `CLAUDE.md`'s Current Build State to describe the new host, saga, and tool is explicitly `ROADMAP.md` Prompt 3's ("Implement 005") responsibility, not this tasks.md's — it happens once the tasks above are actually implemented, not as part of task generation.

### Code review follow-ups (post-Prompt 3)

A review of commit `c71e80b` found two high-severity defects and several smaller issues before this branch merges. All are fixed here rather than deferred, since both high-severity issues are reachable in normal operation, not edge cases.

- [X] T064 [P] Fix `OrderInvestigationSaga.cs`: a `*FindingReported` event arriving after the saga reaches `Completed`/`Failed` (e.g., `BeginInvestigationFanOut` redelivered after a broker blip and the rerun's findings land on an instance that already finalized) previously hit MassTransit's default unhandled-event behavior and faulted, contradicting both the "discard it silently (FR-011)" comment already in the file and Decision 1's restart-survival argument. Added `During(Completed, Failed, Ignore(OrderReported), Ignore(InventoryReported), Ignore(ProductReported))`. Verified via a reproduction harness before the fix (confirmed `NotAcceptedStateMachineException`) and a new regression test after
- [X] T064a [P] Add a saga test publishing a finding a second time after finalization and asserting no exception/fault, in `OrderInvestigationSagaTests.cs` (regression guard for T064)
- [X] T065 [P] Fix `NexusOps.ServiceDefaults/Extensions.cs`: `AddMassTransit` auto-registers a bus health check (`masstransit-bus`, tagged `ready` and `masstransit`) — adding MassTransit to AgentHost and the three domain services silently made the broker a readiness dependency for all of them, so a broker outage took every Direct-path tool and `POST /api/chat` out of rotation even though none of them need the broker. Confirmed live: `OrderService` reported `/health` as 503 while `GET /orders/anomalies` kept returning 200 with the broker down. `MapDefaultEndpoints` now takes `includeMassTransitInReadiness` (default `false`); only `NexusOps.WorkflowOrchestrator` passes `true`. Re-verified live: `/health` returns 200 with the broker unreachable in `OrderService`
- [X] T066 [P] Fix the timeout budget mismatch: `InvestigationFanOutConsumer`'s worst case is order (5s) + max(inventory, product) (5s) = 10s, but `OrderTools.RootCauseTimeout` was 8s — a slow-but-alive Order service followed by a dead Inventory service would report "investigation timed out" for a case the saga was about to correctly answer as `Degraded`. Raised to 12s in `OrderTools.cs`; `research.md` Decision 2, `plan.md`, and `contracts/saga-message-contracts.md` corrected to match
- [X] T067 [P] Clarify the `NotFound` case for the agent: `investigate_order_root_cause`'s description now states explicitly that an `OrderFinding` of `NotFound` means the order doesn't exist and is a completed, trustworthy result — distinct from degraded/failed — since that signal was previously buried in a field the routing instructions never called out
- [X] T068 [P] Add named-configuration-failure guards for `ConnectionStrings:rabbitmq` in all five bus-connected hosts (`AgentHost`, `OrderService`, `InventoryService`, `ProductService`, `WorkflowOrchestrator`) — `new Uri(connectionString!)` previously threw a bare `ArgumentNullException` on a missing connection string, unlike this project's `ValidateOnStart` precedent (`Session`, `AzureAI`) that names the offending key
- [X] T069 [P] Fix a misplaced comment in `OrderTools.cs` (the `IClientFactory` singleton-safety explanation sat above the unrelated `RootCauseTimeout` field; moved to the constructor parameter it actually documents) and correct `research.md`/`data-model.md`'s stale `RowVersion: byte[]` to the actual `uint` (Npgsql maps `uint` onto Postgres's `xmin` directly; `byte[]` is the SQL Server convention)
- [X] T070 Documented two accepted, un-fixed gaps rather than silently deferring them: saga rows are never removed after finalizing and a saga stuck in `Investigating` (fan-out message dead-lettered after retries) has no deadline — `plan.md`'s Open Questions table now records both, plus the missing transactional outbox that will matter once `OrderActionSaga` (006) publishes side effects from the same shape of code
- [X] T071 Re-ran `dotnet build`/`dotnet test` after all of the above, including T064a's new test: 0 warnings, 0 errors, 112/112 passing, zero regressions

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
