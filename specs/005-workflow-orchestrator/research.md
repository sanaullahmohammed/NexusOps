# Research: Order Root-Cause Investigation Workflow

**Branch**: `005-workflow-orchestrator` | **Date**: 2026-09-01

## Decision 1: Saga Choreography — Fan-Out Coordinator + Choreographed State Machine

**Decision**: Split the work across two collaborators instead of one:

- **`OrderInvestigationSaga`** (`MassTransitStateMachine<OrderInvestigationSagaState>`) — owns persisted state and finalization only. It reacts to three independently-arriving events (`OrderFindingReported`, `InventoryFindingReported`, `ProductFindingReported`), each correlated by `CorrelationId`, updating one slice of state per event and checking "have all three sources reported?" after each one. When the saga starts (on `InvestigateOrderRootCause`), its only synchronous work is to persist a new instance and `Publish(BeginInvestigationFanOut)` — it never itself calls a domain service or blocks on I/O.
- **`InvestigationFanOutConsumer`** (a plain `IConsumer<BeginInvestigationFanOut>`, *not* a saga) — does the actual work: three parallel `IRequestClient<T>.GetResponse<TResult>()` calls (one per domain service), each with its own bounded per-source timeout. Whichever way each call resolves (success, fault, or `RequestTimeoutException`), the consumer publishes the corresponding `XFindingReported(CorrelationId, status, ...)` event. This consumer holds no state of its own — MassTransit's standard consumer retry/redelivery covers a crash mid-fan-out.

**Rationale**: This is the only shape that makes every part of the spec true at once:
- FR-003 (concurrent fan-out) — the three `GetResponse` calls run under one `Task.WhenAll` in the coordinator.
- FR-004/FR-005 (partial vs. total failure) — each source's outcome is independently observable and always eventually reported (bounded by that source's own timeout), so the saga's finalization logic is a pure function of "which of the three findings have I received, and were they successes."
- FR-009 (concurrent-update correctness / optimistic concurrency) — because the three `XFindingReported` events are independent messages that can be dispatched to concurrent consumer instances, two of them can genuinely race to update the *same* saga row at the *same* time. A single "do everything inline in one activity" saga would never exhibit this race, making the optimistic-concurrency requirement decorative rather than load-bearing.
- User Story 4 (restart survival) — if `InvestigationFanOutConsumer` crashes before completing, the still-unacknowledged `BeginInvestigationFanOut` message is redelivered by the broker and the (idempotent, read-only) fan-out simply reruns. No saga-level scheduled timeout message is needed for this.

**Alternatives considered**:
- Single saga activity does `Task.WhenAll` over three `IRequestClient` calls inline, updates state once → rejected: only touches saga state once per investigation, so there is no genuine concurrent-write scenario for optimistic concurrency to protect against — it would satisfy the letter of "use optimistic concurrency" without the requirement ever doing anything.
- Fully event-sourced saga with a broker-scheduled timeout message (`Schedule()` via MassTransit's delayed-message scheduler) for an investigation-wide deadline → rejected: requires the RabbitMQ delayed-message exchange plugin to be enabled on the broker image, an extra operational dependency this feature does not need once every source's own request-client timeout already guarantees a bounded reporting time.
- Domain services expose MassTransit request/response consumers directly to the saga (`IConsumer<RequestOrderFinding>` returning via `RespondAsync`) with the *saga itself* as the request client → rejected as the sole mechanism: a `MassTransitStateMachine` consuming behavior is driven by declarative event bindings, not imperative async/await blocking calls; a plain consumer is the correct place for imperative `IRequestClient` fan-out.

---

## Decision 2: AgentHost ⇄ Saga Response Strategy

**Decision**: AgentHost holds a MassTransit `IRequestClient<InvestigateOrderRootCause>` (registered via `AddRequestClient<InvestigateOrderRootCause>(RequestTimeout.After(s: 8))`). The `investigate_order_root_cause` tool handler calls `await client.GetResponse<RootCauseInvestigationResult>(command, cancellationToken)`. The saga does **not** use the `RespondAsync` sugar (that only works from within the consume context that received the original request) — instead, on consuming `InvestigateOrderRootCause`, it captures `ResponseAddress` and `RequestId` from the message headers into its own persisted state, and when it later finalizes (triggered by a `ProductFindingReported`/`InventoryFindingReported`/`OrderFindingReported` consume context — a different context entirely), it resolves a send endpoint for the stored `ResponseAddress` and sends `RootCauseInvestigationResult` with `RequestId` set explicitly, which MassTransit's request client correlates back to the original caller's pending `Task`.

**Rationale**: This is the most direct implementation of the roadmap's literal instruction ("AgentHost publishes its command and awaits the saga result"). It keeps AgentHost's involvement in the bus to a thin, transport-level concern (a request client), never a consumer or state machine, honoring Constitution Principle I. It also gives FR-011 (late responses discarded) for free: once `GetResponse` times out or returns, its temporary reply queue is torn down; a saga that finalizes after that point sends to an address nobody is listening on any more, which is a silent no-op from the caller's perspective — no explicit "is this too late?" check is needed anywhere.

**Alternatives considered**:
- AgentHost publishes the command and polls a `GET /investigations/{id}` HTTP endpoint on `NexusOps.WorkflowOrchestrator` until done → rejected: reintroduces HTTP coupling from AgentHost to the orchestrator, adds poll-interval latency, and gives WorkflowOrchestrator an externally-callable HTTP surface the constitution does not otherwise require it to have.
- AgentHost publishes and separately consumes a `RootCauseInvestigationResult` event over its own dedicated queue → rejected: this is functionally the same idea as the request client but reimplemented by hand (temporary queue lifecycle, correlation, timeout) with no benefit over MassTransit's built-in request/response.

---

## Decision 3: Saga Persistence & Optimistic Concurrency

**Decision**: `OrderInvestigationSagaState` is persisted via MassTransit's EF Core saga repository (`MassTransitEntityFrameworkCoreSagaRepository<OrderInvestigationSagaState>`) against a dedicated `WorkflowOrchestratorDbContext`, using `ConcurrencyMode.Optimistic` with a `RowVersion` (`byte[]`, mapped to Postgres `xmin` via `.IsRowVersion()`) concurrency token column. On a concurrency conflict (`DbUpdateConcurrencyException`), MassTransit's EF Core repository automatically retries the consume from a fresh read of the row — no custom retry code needed in the saga itself.

**Rationale**: This is the standard, documented MassTransit + EF Core + PostgreSQL saga pattern, requires no additional locking infrastructure, and directly implements FR-009 (a conflicting concurrent update is detected and safely resolved, never silently discarded).

**Alternatives considered**:
- Pessimistic locking (`SELECT ... FOR UPDATE` via `ConcurrencyMode.Pessimistic`) → rejected: serializes all writes to a given saga row, which is unnecessary contention for a feature where conflicts are rare (three findings per investigation) and short-lived.
- Application-managed version column with manual retry loop → rejected: reimplements what MassTransit's EF Core repository already provides.

---

## Decision 4: Domain-Service Side of the Fan-Out

**Decision**: Each of `NexusOps.OrderService`, `NexusOps.InventoryService`, and `NexusOps.ProductService` gains one MassTransit consumer apiece:
- `OrderService`: `IConsumer<RequestOrderFinding>` → looks up the order by ID, publishes `OrderFindingReported` with the order snapshot or a not-found status.
- `InventoryService`: `IConsumer<RequestInventoryFinding>` → looks up stock levels for a batch of SKUs, publishes `InventoryFindingReported` with per-SKU results (found/not-found).
- `ProductService`: `IConsumer<RequestProductFinding>` → looks up product details for a batch of SKUs, publishes `ProductFindingReported` similarly.

`InvestigationFanOutConsumer` (in `NexusOps.WorkflowOrchestrator`) issues these as `IRequestClient<RequestOrderFinding>` / `IRequestClient<RequestInventoryFinding>` / `IRequestClient<RequestProductFinding>` calls — request/response at this leg too, so a domain service that never answers surfaces as an ordinary `RequestTimeoutException` the fan-out consumer already handles.

**Rationale**: Matches Constitution Principle IV exactly ("domain services MUST expose MassTransit consumers for saga-dispatched commands") and reuses the same request/response idiom end-to-end rather than mixing patterns.

**Alternatives considered**:
- Fan-out consumer calls domain services' existing HTTP `GET` endpoints directly → rejected outright: Constitution Principle IV prohibits direct HTTP from saga-side code to domain services, full stop.

---

## Decision 5: Message Contract Ownership & Domain-Pluggability Boundary

**Decision**: Split contracts by which side owns the concept:
- **Domain-agnostic** (would exist for *any* domain pack): `BeginInvestigationFanOut`/finding-reported event **shapes as a generic pattern** are not reusable as-is because they carry Order-specific data — so nothing in this feature's message set lives in a domain-agnostic namespace. Instead, domain-agnosticism is achieved structurally: `NexusOps.WorkflowOrchestrator` (the host project — DI wiring, MassTransit bus configuration, the EF Core `DbContext` base plumbing, the generic `ConcurrencyMode.Optimistic` saga-repository setup) contains **zero** references to `OrderInvestigationSaga`, `RequestOrderFinding`, or any other order-specific type. All of that order-specific code lives in a single folder/namespace (`NexusOps.WorkflowOrchestrator.OrderInvestigation`) registered into the generic host via one `AddOrderInvestigationSaga(this IBusRegistrationConfigurator)` extension method call from `Program.cs` — the same "additive registration, no core changes" shape `NexusOps.Contracts`' `ToolNames` and the domain services already use.
- **Message contracts** (`InvestigateOrderRootCause`, `RequestOrderFinding`, `*FindingReported`, `RootCauseInvestigationResult`) live in `NexusOps.Contracts` under a new `Messages/` folder, alongside the existing `Dtos/` and `ToolNames.cs` — consistent with Constitution Principle II ("tool definitions MUST be owned by `NexusOps.Contracts`") extended to the saga-command contracts a tool definition now depends on.

**Rationale**: This directly answers the instruction to "explicitly resolve how Order-specific saga code remains outside the domain-agnostic orchestration core." The test for Principle V compliance is mechanical: deleting the `OrderInvestigation` folder and its one registration call from `Program.cs` must leave `NexusOps.WorkflowOrchestrator` compiling and runnable (bus up, health checks green, no saga registered) — exactly the same bar `investigate_order_anomaly`'s removal would need to clear in `NexusOps.AgentHost` today.

**Alternatives considered**:
- A generic, reusable "N-way fan-out saga" base class in the orchestration core, with `OrderInvestigationSaga` merely supplying types → rejected as premature: one saga does not justify a generic abstraction, and the roadmap's next saga (`OrderActionSaga`, feature 006) has a materially different shape (single mutation + approval gate, not N-way fan-out), so a shared base class would be guessing at a pattern from a sample size of one.

---

## Decision 6: Aspire AppHost Topology for RabbitMQ and PostgreSQL

**Decision**: Add `builder.AddRabbitMQ("rabbitmq").WithManagementPlugin()` and `builder.AddPostgres("postgres").WithDataVolume().AddDatabase("workfloworchestrator")` to `NexusOps.AppHost`, matching the existing `AddRedis("redis").WithDataVolume()` pattern. The new `NexusOps.WorkflowOrchestrator` project references both (`.WithReference(rabbitmq).WithReference(postgres-db).WaitFor(rabbitmq).WaitFor(postgres-db)`), and every project that now participates in the bus (`AgentHost`, `OrderService`, `InventoryService`, `ProductService`, `WorkflowOrchestrator`) also takes `.WithReference(rabbitmq).WaitFor(rabbitmq)`.

**Rationale**: Mirrors the project's own established Redis-onboarding precedent (`specs/002-session-management/plan.md`, Phase A) exactly — new resource, `WithDataVolume()` for dev persistence, `WithReference`/`WaitFor` wiring. `WithManagementPlugin()` gives local debugging visibility into queues/exchanges via the RabbitMQ management UI, surfaced through the Aspire dashboard.

**Alternatives considered**:
- RabbitMQ delayed-message plugin image → not needed; Decision 1 avoids broker-level message scheduling entirely.

---

## Decision 7: Health Checks & Observability (Principle VI)

**Decision**: `NexusOps.WorkflowOrchestrator` calls `AddServiceDefaults()` like every other .NET service, and additionally registers MassTransit's own health check (`services.AddHealthChecks().AddMassTransit(...)` or the built-in bus-observer-driven check MassTransit registers automatically), tagged `ready`, so `/health` reflects actual bus connectivity. The Aspire AppHost registers `.WithHttpHealthCheck("/health")` for the new host, matching every existing service. Domain services' new MassTransit consumers do not need new health-check wiring beyond what `AddServiceDefaults()` already provides — MassTransit's hosted service integrates with the same health-check pipeline.

**Rationale**: Directly satisfies Constitution Principle VI's explicit checklist items (`AddServiceDefaults()`, health checks registered in AppHost via `WithHttpHealthCheck`) with no new pattern invented.

---

## Decision 8: Preserving `investigate_order_anomaly`

**Decision**: No changes to `NexusOps.Contracts.ToolNames.InvestigateOrderAnomaly`, `OrderAnomaly`, `AnomalySelector`, or the `OrderTools.cs` handler wiring it into AgentHost. The new tool is additive: a new constant (`ToolNames.InvestigateOrderRootCause`) and a new handler function registered alongside the existing six via `AIFunctionFactory.Create(...)`.

**Rationale**: Directly implements FR-002. Verified by re-running the existing anomaly-listing test suite unmodified as part of this feature's own test plan (SC-004).

---

## Resolved Unknowns

| Unknown | Resolution |
|---|---|
| How does AgentHost get a result back from a saga over AMQP? | MassTransit `IRequestClient<InvestigateOrderRootCause>`; saga stores `ResponseAddress`/`RequestId` and responds later via a resolved send endpoint (Decision 2) |
| How is "optimistic concurrency" made a real, exercised requirement rather than a checkbox? | Choreographed per-source events updating one persisted saga row concurrently (Decision 1) |
| How are per-source timeouts enforced without RabbitMQ's delayed-message plugin? | Bounded per-source `IRequestClient` timeouts inside `InvestigationFanOutConsumer`, not a saga-level scheduled message (Decision 1) |
| Where do domain-agnostic core and Order-specific saga code separate? | `NexusOps.WorkflowOrchestrator.OrderInvestigation` namespace + one registration extension method; message contracts in `NexusOps.Contracts/Messages/` (Decision 5) |
| MassTransit version | v8.x (locked; `ROADMAP.md` — v9 is commercial, out of scope; add a Dependabot major-version ignore rule for all `MassTransit*` packages) |
| Saga persistence technology | PostgreSQL via `Npgsql.EntityFrameworkCore.PostgreSQL` + `MassTransit.EntityFrameworkCore`, `ConcurrencyMode.Optimistic` (Decision 3) |
| AppHost resources | `Aspire.Hosting.RabbitMQ` (`AddRabbitMQ`), `Aspire.Hosting.PostgreSQL` (`AddPostgres`/`AddDatabase`) (Decision 6) |
