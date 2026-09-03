# Implementation Plan: Order Investigation Saga Reliability Fix

**Branch**: `008-order-investigation-outbox` | **Date**: 2026-09-03 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/008-order-investigation-outbox/spec.md`

## Summary

Close a real, reproducible race in `OrderInvestigationSaga` (feature 005): `Initially(When(Requested))` publishes `BeginInvestigationFanOut` before the saga's own row-creating `INSERT` commits, so a fast `OrderFindingReported` reply (the fan-out consumer's order lookup is sequential and always the first of the three findings back) can arrive and be silently discarded by `OnMissingInstance(m => m.Discard())` before the saga instance is visible. The investigation then never finalizes and the caller times out. The fix is the same one already applied to `OrderActionSaga` in feature 006 for the identical class of problem: a receive-endpoint-scoped MassTransit transactional EF Core outbox on `OrderInvestigationSagaState`'s endpoint, so the saga's state commit and its `Publish(BeginInvestigationFanOut)` become one atomic unit — the message is not visible to any consumer until the saga's row has actually committed.

## Technical Context

**Language/Version**: C# / .NET 10 (no change from feature 005)

**Primary Dependencies**: `MassTransit.EntityFrameworkCore` 8.5.10 (already referenced by `NexusOps.WorkflowOrchestrator`; only its outbox extension methods are newly used, no new package)

**Storage**: PostgreSQL (via Aspire; `workfloworchestrator` database, shared with `OrderActionSaga`) — no new tables. `OrderInvestigationDbContext` is configured to use the *same* `InboxState`/`OutboxState`/`OutboxMessage` tables `OrderActionDbContext` already created (feature 006), rather than a private copy — see research.md Decision 3/4 for why a private copy (tried first, twice) does not work with MassTransit's Postgres outbox implementation.

**Testing**: `dotnet test` (xUnit) — feature 005's existing `OrderInvestigationSagaTests.cs` uses MassTransit's in-memory test harness with an in-memory saga repository (no outbox, no real Postgres), so this race cannot manifest there and that suite is expected to keep passing unchanged. The actual regression test is `NexusOps.IntegrationTests`' `InvestigationSaga_HappyPath_ReturnsAggregatedResults` and `InvestigationSaga_ReturnsPartialResults_WhenInventoryServiceIsStopped` (added in this repository's Prompt 6 work), run against real RabbitMQ and PostgreSQL — these are what actually reproduced the bug and are the acceptance check for the fix.

**Target Platform**: Linux container (Aspire-orchestrated), no change from feature 005

**Performance Goals**: No new performance goal; the fix must not measurably change a healthy investigation's latency (SC-002) — an EF Core outbox adds one extra table write per publish, not a new network hop, so this is expected to be negligible.

**Constraints**: Fix must not change `investigate_order_root_cause`'s external contract (FR-005); must not touch `OrderActionSaga` (already correct); must not add an approval gate or any mutation (FR-006, Constitution III unaffected).

**Scale/Scope**: Three files change (`OrderInvestigationDbContext.cs`, `OrderInvestigation/ServiceCollectionExtensions.cs`, `Program.cs`'s endpoint configuration for `OrderInvestigationSagaState`), one new EF Core migration.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design — see note after each item.*

- [x] **I. Cognition/Durability boundary** — Untouched. This fix is entirely internal to `NexusOps.WorkflowOrchestrator`'s durable-execution layer; AgentHost's `investigate_order_root_cause` tool handler is unmodified. *Re-checked post-design: no file under `NexusOps.AgentHost/` appears in the change set.*
- [x] **II. Curated tool boundaries** — N/A. No tool is added, removed, or reshaped. *Re-checked post-design: `NexusOps.Contracts` is unmodified.*
- [x] **III. Approval-gated side effects** — N/A. `OrderInvestigationSaga` remains read-only; this fix does not add a mutation or touch `OrderActionSaga`'s existing approval gate. *Re-checked post-design: no new message contract is capable of mutating order/inventory/product state.*
- [x] **IV. Message-driven service integration** — Unaffected; the fix changes *when* an existing AMQP publish becomes visible, not the transport (still AMQP) or the fact that it's message-driven. *Re-checked post-design: no HTTP call is introduced anywhere in the saga or its consumers.*
- [x] **V. Domain pluggability** — The entire fix stays inside `NexusOps.WorkflowOrchestrator/OrderInvestigation/` plus its one registration call, exactly matching the boundary feature 005 already established. Deleting the folder and the `AddOrderInvestigationSaga(...)` call remains the whole removal story. *Re-checked post-design: `Program.cs`'s change is confined to how `OrderInvestigationSagaState`'s endpoint is configured, mirroring the existing `OrderActionSagaState` carve-out already present in the same file.*
- [x] **VI. Observability first** — Unaffected; no new service, no change to health checks. *Re-checked post-design: `MapDefaultEndpoints(includeMassTransitInReadiness: true)` call is unchanged.*

No violations. Complexity Tracking is not needed.

## Project Structure

### Documentation (this feature)

```text
specs/008-order-investigation-outbox/
├── plan.md                                    ← this file
├── research.md                                ← Phase 0 decisions
├── data-model.md                              ← schema delta
├── quickstart.md                              ← Phase 1 output
└── tasks.md                                   ← generated by /speckit-tasks
```

No `contracts/` directory: this feature adds no new external interface (no new tool, no new message contract, no new HTTP endpoint) — it changes only internal message-visibility timing behind an existing, unchanged contract.

### Source Code Changes

```text
NexusOps.WorkflowOrchestrator/
├── OrderInvestigation/
│   ├── OrderInvestigationDbContext.cs         ← add AddInboxStateEntity()/AddOutboxMessageEntity()/
│   │                                             AddOutboxStateEntity() to OnModelCreating, using
│   │                                             MassTransit's *default* (unqualified) table mapping
│   │                                             deliberately -- these resolve to the same physical
│   │                                             tables OrderActionDbContext already owns, not a
│   │                                             private copy (research.md Decision 3/4)
│   └── ServiceCollectionExtensions.cs         ← add AddEntityFrameworkOutbox<OrderInvestigationDbContext>
│                                                 (o => { o.UsePostgres(); o.UseBusOutbox(); }),
│                                                 mirroring OrderAction/ServiceCollectionExtensions.cs
├── Migrations/
│   └── <timestamp>_AddTransactionalOutbox.cs  ← new, but structurally a no-op: InboxState/
│                                                 OutboxState/OutboxMessage already exist (created by
│                                                 OrderActionDbContext's own migration); this migration
│                                                 exists only to record, in OrderInvestigationDbContext's
│                                                 own migration history, that its model now includes them
└── Program.cs                                 ← OrderInvestigationSagaState's endpoint gains the same
                                                   manual ReceiveEndpoint(...) + Exclude<...>() treatment
                                                   OrderActionSagaState already has, so it can carry
                                                   UseEntityFrameworkOutbox<OrderInvestigationDbContext>

NexusOps.IntegrationTests/                      ← no code change; existing
WorkflowOrchestratorIntegrationTests.cs's two investigation tests are the fix's acceptance check,
re-run against real infrastructure after the fix lands

CLAUDE.md, ROADMAP.md                           ← updated once the fix is verified
```

**Structure Decision**: The fix is entirely additive within `NexusOps.WorkflowOrchestrator/OrderInvestigation/` plus one line in `Program.cs`'s bus configuration, following feature 006's own precedent for `OrderActionSagaState` exactly (same pattern, same file, same shape of change — that precedent is why this fix carries very low implementation risk). No new project, no new namespace, no new registration call beyond what `AddOrderInvestigationSaga(...)` already does internally.

## Complexity Tracking

No constitution violations. No complexity justification required.
