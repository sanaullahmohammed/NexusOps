# Phase 1 Data Model: Documentation Honesty Pass

This feature has no application data entities — it edits static documentation content. In place of a data model, this document defines the **content structure** each requirement (FR-001–FR-008) must produce, so `/speckit-tasks` can generate concrete, checkable edit tasks.

## Content Entity: "Why this project" section

| Field | Description |
|---|---|
| Location | `README.md`, immediately after the opening tagline paragraph, before "Architecture" (research.md Decision 3) |
| Heading | `## Why this project` (or equivalent short heading) |
| Required content | States NexusOps is a proof-of-concept; names the source domain (fintech operations engineering); maps at least the three named patterns (multi-source aggregation, maker-checker approval, compensation on partial failure) to their agentic-AI counterparts |
| Consistency source | Must not contradict ROADMAP.md's "Resume framing" line (investigation fan-out ≈ upstream aggregation; approval gate ≈ maker-checker; compensation ≈ reversing partial writes; curated tools ≈ governed API surface) |
| Satisfies | FR-001, FR-002; validated by SC-001 |

## Content Entity: Tech Stack table row

| Field | Description |
|---|---|
| Location | `README.md` Tech Stack table (and CLAUDE.md's Tech Stack table, if a wording inconsistency is found) |
| Columns | Component, Technology, **Status** (new column — `Implemented` \| `Planned`) |
| Rows requiring `Planned` | Durable Orchestration (MassTransit + RabbitMQ), Workflow Orchestrator, Saga Persistence (PostgreSQL), Notification Service |
| Rows requiring `Implemented` | AI Reasoning, Model Provider & Evaluation, App Orchestration & Observability, Agent Host, Domain Services (Product/Order/Inventory) |
| Satisfies | FR-003, FR-006; validated by SC-002 |

## Content Entity: Architecture diagram node

| Field | Description |
|---|---|
| Location | `README.md` Mermaid diagram |
| Marking | `(planned)` text suffix on node label + dashed border style for not-yet-implemented nodes (research.md Decision 2) |
| Nodes requiring the marking | `RMQ` (RabbitMQ), `Orch`/`MT` (Workflow Orchestrator / MassTransit Sagas), `PG` (PostgreSQL), `Notify` (Notification Service) |
| Nodes that stay unmarked (implemented) | `Foundry`, `Client`, `Host`/`AH`, `Direct`/`Prod`/`Order`/`Inv` |
| Satisfies | FR-004, FR-006; validated by SC-002 |

## Content Entity: Saga Designs entry

| Field | Description |
|---|---|
| Location | `README.md` Saga Designs section, one entry per saga |
| Sagas | `OrderInvestigationSaga`, `OrderActionSaga` |
| Required content | A `**Status:** Planned design — not yet implemented.` line directly under the heading, before the state-transition diagram (research.md Decision 4) |
| Satisfies | FR-005, FR-006; validated by SC-002 |

## Cross-cutting check: Consistency ledger

| Component | README.md status (post-edit) | ROADMAP.md status | CLAUDE.md status | Match? |
|---|---|---|---|---|
| Workflow Orchestrator / MassTransit sagas | Planned | Planned | Planned | Must verify after edits |
| RabbitMQ | Planned | Planned | Planned | Must verify after edits |
| PostgreSQL / saga persistence | Planned | Planned | Planned | Must verify after edits |
| Notification Service | Planned | Planned | Planned | Must verify after edits |
| Evaluation runner | Planned | Planned | Planned | Must verify after edits |
| `NexusOps.Server` | Scaffold/reference (neither) | Scaffold/reference | Scaffold placeholder | Must verify after edits |
| `frontend/` | Scaffold/reference (neither) | Scaffold/reference | Scaffold placeholder | Must verify after edits |

Satisfies FR-007, FR-008; validated by SC-003, SC-004.
