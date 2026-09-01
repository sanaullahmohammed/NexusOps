# Feature Specification: Documentation Honesty Pass

**Feature Branch**: `004-docs-honesty-pass`

**Created**: 2026-09-01

**Status**: Draft

**Input**: User description: "Read ROADMAP.md, README.md, and CLAUDE.md. Preserve the existing honest current-state and Roadmap sections: sagas, MassTransit, RabbitMQ, Postgres, Notification Service, and the Evaluation runner remain planned, not implemented, and NexusOps.Server plus frontend/ remain scaffold reference artifacts. Add a short 'Why this project' section framing NexusOps as a POC translating fintech operations engineering (multi-source aggregation, maker-checker approval, compensation on partial failure) into agentic-AI workflows. Mark planned components directly in the Tech Stack table, architecture diagram, and Saga Designs section so they cannot be mistaken for implemented components. Do not touch code."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Understand why the project exists (Priority: P1)

A technical reviewer (recruiter, prospective collaborator, or engineer evaluating the repo) opens the README for the first time and wants to understand, in under a minute, what real-world problem NexusOps is a proof-of-concept for and why its architecture looks the way it does.

**Why this priority**: Without framing, the README reads as an arbitrary technical exercise. The framing is what turns the repo into evidence of applied domain expertise (fintech operations engineering) rather than a generic CRUD demo — this is the single highest-value addition.

**Independent Test**: Can be fully tested by reading only the new "Why this project" section and confirming a reader can restate, in their own words, the fintech analogy (multi-source aggregation → investigation fan-out, maker-checker → approval gate, compensation on partial failure → saga compensation) without reading any other section.

**Acceptance Scenarios**:

1. **Given** a reader who has never seen the repo, **When** they read the README top-to-bottom, **Then** they encounter a "Why this project" section before or immediately after the architecture overview that names the fintech domain analogy explicitly.
2. **Given** the "Why this project" section, **When** a reader compares it against ROADMAP.md's "Resume framing" note, **Then** the two are consistent (same analogy, no contradiction).

---

### User Story 2 - Tell implemented from planned at a glance (Priority: P1)

A reviewer scanning the Tech Stack table, architecture diagram, or Saga Designs section wants to know — without reading surrounding prose — which components are running code today versus which are design intent for later.

**Why this priority**: The most damaging failure mode for a portfolio/POC repo is a reviewer assuming something works when it doesn't. This directly protects credibility and is equally critical to the "why" framing.

**Independent Test**: Can be fully tested by covering all prose paragraphs and checking whether the Tech Stack table, architecture diagram, and Saga Designs section alone (labels/markings only) correctly classify every component as implemented or planned.

**Acceptance Scenarios**:

1. **Given** the Tech Stack table, **When** a reader scans only the Component/Technology columns and any status markers, **Then** every row for MassTransit, RabbitMQ, PostgreSQL, Notification Service, and the Evaluation runner is visibly marked as planned/not-yet-implemented, distinct from implemented rows.
2. **Given** the architecture diagram, **When** a reader looks only at node styling/labels, **Then** planned components (Workflow Orchestrator/MassTransit sagas, RabbitMQ, PostgreSQL, Notification Service) are visually distinguishable from implemented components (Agent Host, domain services).
3. **Given** the Saga Designs section, **When** a reader reads only the section heading and any status marker, **Then** both `OrderInvestigationSaga` and `OrderActionSaga` are identified as planned designs, not implemented behavior.

---

### User Story 3 - Trust that the docs agree with each other (Priority: P2)

A returning contributor cross-references README.md, ROADMAP.md, and CLAUDE.md and wants the implementation-status claims to match across all three, so they can trust any one of them without re-verifying against the others.

**Why this priority**: Lower priority than the two above because CLAUDE.md and ROADMAP.md are already largely accurate; this is a consistency check on top of the primary edits rather than new content.

**Independent Test**: Can be fully tested by listing every component named in ROADMAP.md's "Locked Decisions" and Current Build State sections and confirming README.md and CLAUDE.md assign it the same implemented/planned status.

**Acceptance Scenarios**:

1. **Given** the set of components marked "planned" in CLAUDE.md's Current Build State, **When** the same components are located in README.md, **Then** README.md marks them planned too (never implemented).
2. **Given** ROADMAP.md's Status checklist, **When** cross-checked against README.md's Roadmap section, **Then** the same items appear checked/unchecked consistently.

### Edge Cases

- What happens to a component that is partially built (e.g., `NexusOps.Server` and `frontend/` exist as scaffolding but serve no real feature)? These MUST be labeled distinctly from both "implemented" and "planned" — as scaffold/reference artifacts — so they are not miscounted either way.
- How does the diagram handle a node that has no clean visual equivalent for "planned" (e.g., limited styling options in the diagramming syntax used)? A textual label (e.g., "(planned)") MUST accompany or substitute for styling so the status survives even if color/style rendering is lost (e.g., copy-pasted as plain text, accessibility tools, dark/light theme).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: README.md MUST include a "Why this project" section (or equivalently named) explaining NexusOps as a proof-of-concept that translates fintech operations engineering patterns — multi-source aggregation, maker-checker approval, compensation on partial failure — into agentic-AI workflow design.
- **FR-002**: The "Why this project" framing MUST be consistent with the fintech analogy already recorded in ROADMAP.md's "Resume framing" note (investigation fan-out ≈ upstream aggregation; approval gate ≈ maker-checker; compensation ≈ reversing partial writes; curated tools ≈ governed API surface).
- **FR-003**: The Tech Stack table (README.md and, if present, CLAUDE.md) MUST mark every not-yet-implemented row (MassTransit, RabbitMQ, PostgreSQL/saga persistence, Notification Service) so it cannot be mistaken for an implemented row.
- **FR-004**: The architecture diagram in README.md MUST visually and/or textually distinguish not-yet-implemented components (Workflow Orchestrator, MassTransit sagas, RabbitMQ, PostgreSQL, Notification Service) from implemented components (Agent Host, Product/Order/Inventory services).
- **FR-005**: The Saga Designs section in README.md MUST state that `OrderInvestigationSaga` and `OrderActionSaga` are planned designs, not currently running behavior.
- **FR-006**: All existing statements that sagas, MassTransit, RabbitMQ, Postgres, the Notification Service, and the Evaluation runner are planned/not implemented MUST be preserved — this pass MUST NOT introduce any statement, list item, or diagram element implying they are implemented.
- **FR-007**: All existing statements that `NexusOps.Server` and `frontend/` are scaffold/reference-only artifacts MUST be preserved and MUST NOT be reworded to imply completed functionality.
- **FR-008**: The Roadmap/Status sections in ROADMAP.md and README.md MUST remain mutually consistent after edits — no component's status may differ between the two files.
- **FR-009**: This pass MUST NOT modify any source code, project (`.csproj`/`.esproj`), configuration, or CI file — only `ROADMAP.md`, `README.md`, and `CLAUDE.md` (if a status wording fix is needed there for consistency) may be edited.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A reader who reads only the "Why this project" section can correctly restate the fintech-to-agentic-AI analogy in their own words (verified by manual review against ROADMAP.md's resume framing note).
- **SC-002**: A reader who scans only the Tech Stack table, diagram labels, and Saga Designs headings — with all other prose hidden — correctly classifies 100% of the six planned components (sagas/Workflow Orchestrator, MassTransit, RabbitMQ, Postgres, Notification Service, Evaluation runner) as not-yet-implemented.
- **SC-003**: Zero contradictions exist between README.md, ROADMAP.md, and CLAUDE.md regarding any single component's implementation status, checked item-by-item.
- **SC-004**: 100% of previously existing "planned, not implemented" and "scaffold only" statements remain present in the updated documents (no accidental removal or softening).

## Assumptions

- The audience for this documentation pass is technical reviewers evaluating the project (e.g., recruiters, prospective collaborators, engineers auditing the repo) — not end users of the sample e-commerce chat application.
- "Mark planned components" means adding clear labels/status markers (e.g., a "(planned)" suffix, a dedicated status column, distinct diagram node styling) rather than removing planned components from the diagram or tables — the goal is honesty, not omission.
- The fintech-operations analogy to use is the one already locked in ROADMAP.md: investigation fan-out ≈ multi-source aggregation, approval gate ≈ maker-checker, compensation ≈ reversing partial writes, curated tools ≈ governed API surface.
- CLAUDE.md's Tech Stack table already marks planned rows correctly (e.g., "MassTransit + RabbitMQ (planned)"); this pass verifies and preserves that state rather than assuming it needs equivalent new work, and only touches CLAUDE.md if an inconsistency with README.md/ROADMAP.md is found.
- Scope is strictly limited to `README.md`, `ROADMAP.md`, and (only if needed for consistency) `CLAUDE.md`. No source code, `.csproj`/`.esproj` files, or CI configuration are in scope.
