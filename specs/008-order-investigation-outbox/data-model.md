# Phase 1 Data Model: Order Investigation Saga Reliability Fix

No change to `OrderInvestigationSagaState`'s own columns, and **no new physical tables** (revised
during implementation — see research.md Decision 4). `OrderInvestigationDbContext`'s model gains
mappings for three entities MassTransit's outbox requires, but they resolve to the *same* physical
tables `OrderActionDbContext` already created (feature 006), in the shared `workfloworchestrator`
database — not a private copy. A private copy (tried twice: a renamed table, then a renamed schema)
does not work with this version of MassTransit's Postgres outbox implementation; see research.md
Decision 4 for the empirical detail. The shapes below describe the one, shared table set both
`DbContext`s now use.

## Shared tables (already existed, created by `OrderActionDbContext`'s own migration; `OrderInvestigationDbContext` now maps to them too)

### `InboxState`

MassTransit's inbox deduplication record — one row per `(MessageId, ConsumerId)` pair consumed on the
outbox-carrying endpoint, used to detect and skip a redelivered message that was already processed.

| Column | Type | Notes |
|---|---|---|
| `Id` | `bigint` (identity) | PK |
| `MessageId` | `uuid` | part of unique `(MessageId, ConsumerId)` |
| `ConsumerId` | `uuid` | part of unique `(MessageId, ConsumerId)` |
| `LockId` | `uuid` | |
| `RowVersion` | `bytea` (row version) | optimistic concurrency |
| `Received` | `timestamp with time zone` | |
| `ReceiveCount` | `integer` | |
| `ExpirationTime` | `timestamp with time zone`, nullable | |
| `Consumed` | `timestamp with time zone`, nullable | |
| `Delivered` | `timestamp with time zone`, nullable | indexed |
| `LastSequenceNumber` | `bigint`, nullable | |

### `OutboxState`

One row per consume context that used the outbox (i.e., one per `BeginInvestigationFanOut` publish
triggered from `Initially(When(Requested))`), tracking delivery progress of that context's queued
messages.

| Column | Type | Notes |
|---|---|---|
| `OutboxId` | `uuid` | PK |
| `LockId` | `uuid` | |
| `RowVersion` | `bytea` (row version) | optimistic concurrency |
| `Created` | `timestamp with time zone` | indexed |
| `Delivered` | `timestamp with time zone`, nullable | |
| `LastSequenceNumber` | `bigint`, nullable | |

### `OutboxMessage`

The actual queued message (e.g., a `BeginInvestigationFanOut` publish), held here until the owning
transaction commits and the outbox delivery service sends it to the broker.

| Column | Type | Notes |
|---|---|---|
| `SequenceNumber` | `bigint` (identity) | PK |
| `EnqueueTime` | `timestamp with time zone`, nullable | indexed |
| `SentTime` | `timestamp with time zone` | |
| `Headers` / `Properties` | `text`, nullable | |
| `InboxMessageId` / `InboxConsumerId` | `uuid`, nullable | FK → `InboxState (MessageId, ConsumerId)` |
| `OutboxId` | `uuid`, nullable | FK → `OutboxState (OutboxId)`; indexed with `SequenceNumber`, unique |
| `MessageId` | `uuid` | |
| `ContentType` | `character varying(256)` | |
| `MessageType` | `text` | |
| `Body` | `text` | |
| `ConversationId` / `CorrelationId` / `InitiatorId` / `RequestId` | `uuid`, nullable | |
| `SourceAddress` / `DestinationAddress` / `ResponseAddress` / `FaultAddress` | `character varying(256)`, nullable | |
| `ExpirationTime` | `timestamp with time zone`, nullable | indexed |

## No change

- `OrderInvestigationSagaState` — unchanged columns, unchanged saga logic, unchanged state chart.
- `NexusOps.Contracts` message contracts (`InvestigateOrderRootCause`, `BeginInvestigationFanOut`,
  `OrderFindingReported`, `InventoryFindingReported`, `ProductFindingReported`,
  `RootCauseInvestigationResult`) — all unchanged. This is a delivery-visibility fix, not a contract change.
