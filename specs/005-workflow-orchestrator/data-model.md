# Data Model: Order Root-Cause Investigation Workflow

**Branch**: `005-workflow-orchestrator` | **Date**: 2026-09-01

## Entities

### OrderInvestigationSagaState (Postgres-persisted saga instance)

The durable record backing FR-008/FR-009/FR-010. One row per investigation. Owned entirely by `NexusOps.WorkflowOrchestrator.OrderInvestigation` — not referenced by any domain-agnostic core type.

| Field | Type | Notes |
|---|---|---|
| `CorrelationId` | `Guid` | Primary key; MassTransit saga correlation ID. Generated when `InvestigateOrderRootCause` is consumed. |
| `CurrentState` | `string` | MassTransit state machine's current state name (`Investigating`, `Completed`, `Failed`). |
| `OrderId` | `string` | The order under investigation. |
| `ResponseAddress` | `Uri?` | Captured from the originating request's headers; used to send the final result back to AgentHost's request client (Decision 2). `null` after the response has been sent (cleared to make "already responded" observable). |
| `RequestId` | `Guid?` | Captured from the originating request; required by MassTransit to correlate the late response back to the caller's pending `Task`. |
| `OrderFinding` | `SourceFindingStatus` | Default `Pending`. |
| `InventoryFinding` | `SourceFindingStatus` | Default `Pending`. |
| `ProductFinding` | `SourceFindingStatus` | Default `Pending`. |
| `OrderResultJson` | `string?` | Serialized `OrderSummary` once `OrderFinding = Succeeded`; `null` otherwise. |
| `InventoryResultJson` | `string?` | Serialized `InventoryLevel[]` once `InventoryFinding` is not `Pending`. |
| `ProductResultJson` | `string?` | Serialized `ProductDetail[]` once `ProductFinding` is not `Pending`. |
| `StartedAt` | `DateTimeOffset` | Set on creation. |
| `CompletedAt` | `DateTimeOffset?` | Set when the saga finalizes (all three findings recorded). |
| `RowVersion` | `byte[]` | EF Core concurrency token, mapped to Postgres `xmin` (`.IsRowVersion()`). Enforces FR-009. |

**`SourceFindingStatus` enum**: `Pending`, `Succeeded`, `NotFound`, `Unavailable`, `TimedOut` — mirrors the Key Entities' "Source Finding" status vocabulary from `spec.md` exactly.

**State transitions**:

```
[saga created by InvestigateOrderRootCause]
  → CurrentState = Investigating
  → Publish(BeginInvestigationFanOut { CorrelationId, OrderId, Skus })
  → ResponseAddress/RequestId captured from the request's headers

Investigating, on OrderFindingReported     → OrderFinding = reported status; OrderResultJson set if Succeeded
Investigating, on InventoryFindingReported → InventoryFinding = reported status; InventoryResultJson set if not NotFound-for-all
Investigating, on ProductFindingReported   → ProductFinding = reported status; ProductResultJson set if not NotFound-for-all

After each of the three transitions above:
  if (OrderFinding, InventoryFinding, ProductFinding) all != Pending:
    → CompletedAt = now
    → CurrentState = (OrderFinding == Succeeded) ? Completed : Failed
      // Failed only when the order itself could not be identified at all (Unavailable/TimedOut) —
      // a confirmed NotFound on the order is still a completed investigation (Edge Cases, spec.md).
    → Resolve send endpoint for ResponseAddress; send RootCauseInvestigationResult with RequestId
    → ResponseAddress = null (mark responded)
    → [MassTransit saga instance may be removed/finalized per repository configuration]
```

A `XFindingReported` event that arrives for a `CorrelationId` with no matching saga row (already finalized and removed, or never existed) is ignored — this is the mechanism behind FR-011 ("late responses discarded"): the fan-out consumer's per-source timeout already guarantees a finding is reported before the caller's own `IRequestClient` timeout elapses in the overwhelming majority of cases, and MassTransit sagas silently drop events with no correlated instance by default.

---

### Message Contracts (`NexusOps.Contracts/Messages/`)

All request-side messages below are `sealed record`s, matching the project's existing DTO style.

#### `InvestigateOrderRootCause` (AgentHost → saga; request)

| Field | Type | Notes |
|---|---|---|
| `OrderId` | `string` | Required. |

#### `BeginInvestigationFanOut` (saga → fan-out consumer; internal event)

| Field | Type | Notes |
|---|---|---|
| `CorrelationId` | `Guid` | |
| `OrderId` | `string` | |

*(The fan-out consumer resolves the order's line-item SKUs itself, via `RequestOrderFinding`'s own response, before issuing the inventory/product requests — see below. This keeps `BeginInvestigationFanOut` from needing to know the order's contents up front.)*

**Fan-out sequencing inside `InvestigationFanOutConsumer`**: the order lookup (`RequestOrderFinding`) is awaited first (it is the source of the SKUs the other two calls need); once it returns (or times out/faults), the inventory and product lookups for those SKUs run concurrently with each other via `Task.WhenAll`. If the order lookup itself fails or times out, `InventoryFindingReported` and `ProductFindingReported` are published immediately with status `Unavailable` and an empty finding, since there are no SKUs to look up — the investigation still finalizes as `Failed` (per the state-transition rule above) rather than hanging.

#### `RequestOrderFinding` / `OrderFindingReported`

| Field | Type | Notes |
|---|---|---|
| `CorrelationId` | `Guid` | Request: routes the response; carried through. |
| `OrderId` | `string` | Request only. |
| `Status` | `SourceFindingStatus` | Response only. |
| `Order` | `OrderSummary?` | Response only; present iff `Status == Succeeded`. |

#### `RequestInventoryFinding` / `InventoryFindingReported`

| Field | Type | Notes |
|---|---|---|
| `CorrelationId` | `Guid` | |
| `Skus` | `string[]` | Request only. |
| `Status` | `SourceFindingStatus` | Response only; `Succeeded` if at least one SKU resolved, per Edge Cases (a not-found SKU doesn't fail the whole source). |
| `Levels` | `InventoryLevel[]` | Response only; the SKUs that resolved. |
| `SkusNotFound` | `string[]` | Response only; the SKUs that don't exist in Inventory. |

#### `RequestProductFinding` / `ProductFindingReported`

Same shape as inventory, substituting `ProductDetail[] Products` for `Levels`.

#### `RootCauseInvestigationResult` (saga → AgentHost; response)

| Field | Type | Notes |
|---|---|---|
| `OrderId` | `string` | |
| `OrderFinding` | `SourceFindingStatus` | |
| `Order` | `OrderSummary?` | Present iff `OrderFinding == Succeeded`. |
| `InventoryFinding` | `SourceFindingStatus` | |
| `InventoryLevels` | `InventoryLevel[]` | Empty if not succeeded. |
| `ProductFinding` | `SourceFindingStatus` | |
| `ProductDetails` | `ProductDetail[]` | Empty if not succeeded. |
| `Completeness` | `InvestigationCompleteness` | `Complete` (all three `Succeeded`/confirmed-`NotFound`-for-all-SKUs), `Degraded` (order found but ≥1 source incomplete), or `Failed` (order itself could not be identified). |
| `DegradedSources` | `string[]` | Names of the sources that are `Unavailable`/`TimedOut`/partially-`NotFound`; empty when `Completeness == Complete`. |

`InvestigationCompleteness` enum: `Complete`, `Degraded`, `Failed` — this is the field the `investigate_order_root_cause` tool handler reads to decide how to phrase the agent's answer (fully answered vs. "here's what I found, but X was unavailable" vs. "I couldn't investigate this order").

---

## Postgres Schema (`workfloworchestrator` database)

| Table | Notes |
|---|---|
| `OrderInvestigationSagaState` | One row per investigation; matches the entity above. `CorrelationId` is the primary key. Managed by an EF Core migration in `NexusOps.WorkflowOrchestrator.OrderInvestigation` — the *only* migration/table this feature adds; no other domain-agnostic schema exists in this database yet. |

A future saga (`OrderActionSaga`, feature 006) would add its own table to the same database via its own migration, in its own namespace — it does not extend or share this table.

---

## Tool Contract Delta (`NexusOps.Contracts`)

### `ToolNames.InvestigateOrderRootCause` (new)

```
investigate_order_root_cause
```

Input: `{ orderId: string }`. Output: `ToolResult<RootCauseInvestigationResult>` (existing `ToolResult<T>` wrapper, unchanged).

### `ToolNames.InvestigateOrderAnomaly` (unchanged)

No fields, names, or response shapes change. See Decision 8 in `research.md`.
