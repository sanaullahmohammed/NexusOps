# Tasks: Order Investigation Saga Reliability Fix

**Input**: Design documents from `specs/008-order-investigation-outbox/`
**Prerequisites**: plan.md, research.md, data-model.md, quickstart.md

**Tests**: `NexusOps.IntegrationTests`' two investigation tests already exist (from this repository's
integration-test work) and already reproduce this bug — they are the acceptance test for this fix, not
new tests to write. `NexusOps.Tests/WorkflowOrchestrator/OrderInvestigationSagaTests.cs` is unaffected
(in-memory harness, no outbox involved) and needs no change.

**Organization**: Single user story (P1) — this is a bug fix, not a multi-story feature.

## Phase 1: Setup

No new project, no new package reference. Nothing to set up.

## Phase 2: Foundational

No shared/blocking prerequisites beyond the fix itself — this is a small, self-contained change to one
existing saga's endpoint configuration.

## Phase 3: User Story 1 - A root-cause investigation always finalizes (Priority: P1)

**Goal**: Close the race so `OrderFindingReported` can never be discarded due to the saga's own row not
yet being committed.

**Independent Test**: Run `NexusOps.IntegrationTests`' `InvestigationSaga_HappyPath_ReturnsAggregatedResults`
against real infrastructure; it must pass (previously failed with `RequestTimeoutException` on every run).

- [x] T001 [US1] Add `AddInboxStateEntity()`, `AddOutboxMessageEntity()`, `AddOutboxStateEntity()` calls to `OnModelCreating` in `NexusOps.WorkflowOrchestrator/OrderInvestigation/OrderInvestigationDbContext.cs`, using MassTransit's *default* (unqualified) table mapping — deliberately, not renamed (see research.md Decision 4: a renamed table, then a renamed schema, were each tried and both failed against real Postgres — MassTransit's row-lock query ignores the custom mapping and always hits the default-named table, which must therefore be the *same* table `OrderActionDbContext` already owns)
- [x] T002 [US1] Add `configurator.AddEntityFrameworkOutbox<OrderInvestigationDbContext>(o => { o.UsePostgres(); o.UseBusOutbox(); })` to `AddOrderInvestigationSaga(...)` in `NexusOps.WorkflowOrchestrator/OrderInvestigation/ServiceCollectionExtensions.cs`, mirroring `NexusOps.WorkflowOrchestrator/OrderAction/ServiceCollectionExtensions.cs`'s existing call for `OrderActionDbContext`
- [x] T003 [US1] In `NexusOps.WorkflowOrchestrator/Program.cs`, exclude `OrderInvestigationSagaState` from the generic `cfg.ConfigureEndpoints(context, ...)` sweep and add a manual `cfg.ReceiveEndpoint("OrderInvestigationSagaState", e => { e.UseEntityFrameworkOutbox<OrderInvestigationDbContext>(context); e.ConfigureSaga<OrderInvestigationSagaState>(context); })` block, mirroring the existing `OrderActionSagaState` treatment in the same file
- [x] T004 [US1] Add a new EF Core migration (`20260903150000_AddTransactionalOutbox`) to `NexusOps.WorkflowOrchestrator/Migrations/` for `OrderInvestigationDbContext` — structurally a no-op `Up()`/`Down()`, since `InboxState`/`OutboxState`/`OutboxMessage` already exist (created by `OrderActionDbContext`'s own migration); it exists only to record, in this context's own migration history, that its model now includes those three entities. Update `OrderInvestigationDbContextModelSnapshot.cs` to include them with MassTransit's default table mapping (written by hand — the `dotnet-ef` CLI tool is unavailable in this environment, see research.md)
- [x] T005 [US1] `dotnet build NexusOps.deployable.slnf --configuration Release` — confirm the solution builds with zero errors/warnings after T001–T004
- [x] T006 [US1] Run `NexusOps.IntegrationTests`' `InvestigationSaga_HappyPath_ReturnsAggregatedResults` and `InvestigationSaga_ReturnsPartialResults_WhenInventoryServiceIsStopped` against real RabbitMQ/PostgreSQL (Docker); confirm both pass — **done**: both pass, happy path in ~1s (previously a 30s timeout, 100% reproducible)
- [x] T007 [US1] Run the full `dotnet test NexusOps.deployable.slnf` suite (all of `NexusOps.Tests` and `NexusOps.IntegrationTests`); confirm no regression anywhere, including `OrderInvestigationSagaTests.cs` and every `OrderActionSaga`/evaluation test — **done**: 174/174 unit + 4/4 integration, all green

**Checkpoint**: Investigation capability is reliable from a cold start — the bug this feature exists to fix is closed and verified against real infrastructure. ✅ Verified.

## Phase 4: Polish & Cross-Cutting Concerns

- [x] T008 Update `CLAUDE.md`'s Current Build State with this fix, and `ROADMAP.md`'s Prompt 6 notes, once T006/T007 are green
- [x] T009 Update `NexusOps.IntegrationTests`' `WorkflowOrchestratorFixture`/test doc comments if any language there still implies the investigation tests are expected to be flaky/failing (they no longer should be) — none found; the fixture's own comments never claimed flakiness, only the top-level `ROADMAP.md`/`CLAUDE.md` notes did, addressed by T008

## Dependencies & Execution Order

- T001 → T002 → T003 are strictly sequential (each builds on the file the previous step touched being in its final shape before the next compiles against it) — not parallelizable.
- T004 depends on T001 (the model must be final before writing its snapshot/migration by hand).
- T005 depends on T001–T004.
- T006 depends on T005 (must build first) and requires Docker.
- T007 depends on T006.
- T008–T009 depend on T007 passing.

## Implementation Strategy

This is a single, small, sequential change set (six files) with one clear acceptance gate (T006/T007
passing against real infrastructure). There is no meaningful "MVP subset" smaller than the whole fix —
partial application (e.g., the outbox configured but the migration missing) would fail to build or fail
to run, not degrade gracefully. Implement T001–T007 in one pass.
