# Phase 0 Research: Documentation Honesty Pass

No `NEEDS CLARIFICATION` markers exist in [spec.md](spec.md) or the Technical Context above — the user's instructions and repository state (ROADMAP.md, README.md, CLAUDE.md) fully determine scope. This research resolves the presentation-mechanism decisions needed before drafting the actual edits.

## Decision 1: How to mark "planned" in the Tech Stack table

**Decision**: Add an explicit **Status** column with values `Implemented` / `Planned`, rather than relying on inline text or subtle styling.

**Rationale**: A dedicated column is scannable in isolation (satisfies spec US2's "labels/markings only" test) and survives copy-paste into plain text, unlike color or italics. It also matches the pattern CLAUDE.md's Tech Stack table already uses informally ("MassTransit + RabbitMQ (planned)").

**Alternatives considered**:
- Emoji-only markers (✅/🚧) — rejected: not reliably readable by screen readers or in plain-text contexts (e.g., `git show`, terminal `cat`).
- Italicizing planned rows — rejected: too subtle, easy to miss on a quick scan, doesn't survive copy-paste.
- Inline `(planned)` suffix on the Technology cell only — kept as a *secondary* reinforcement in prose/diagram (see Decision 2), but the table itself gets a real column since it's the primary at-a-glance reference.

## Decision 2: How to mark "planned" in the Mermaid architecture diagram

**Decision**: Append a `(planned)` text suffix to the labels of not-yet-implemented nodes (RabbitMQ, MassTransit Sagas / Workflow Orchestrator subgraph, PostgreSQL, Notification Service), combined with a dashed border style (`stroke-dasharray`) for implemented nodes to be visually distinct at a glance.

**Rationale**: Text labels survive regardless of rendering context (raw Markdown view, copy-paste, accessibility tools) per spec's Edge Cases section. Dashed styling adds a visual scan aid for readers viewing the rendered diagram on GitHub, without being the only signal.

**Alternatives considered**:
- A separate "planned" subgraph cluster physically isolated from the real topology — rejected: would misrepresent how these components actually integrate into the message flow once built, reducing the diagram's value as a design reference.
- Color-only distinction (already partially used for `AH`, `Foundry`, `RMQ`, `MT` via `style` directives) — rejected as the *sole* signal: color meaning isn't self-evident (e.g., orange currently denotes RabbitMQ specifically, not "planned" generally) and doesn't survive grayscale/print/colorblind viewing.

## Decision 3: Placement of the "Why this project" section

**Decision**: Insert immediately after the existing opening tagline paragraph (currently README.md lines 1–5) and before the "Architecture" section.

**Rationale**: Satisfies spec SC-001 (a reader restates the analogy within ~30 seconds) by putting motivation before mechanism — a reader should know *why* the two-path architecture exists before being shown the diagram of *how* it works.

**Alternatives considered**:
- After "Architecture," before "Tech Stack" — rejected: makes a reader parse the diagram first without knowing why it's shaped that way.
- As a new top-level section at the end of the README — rejected: buries the most important credibility signal for a portfolio/POC repo (per spec US1's priority P1).

## Decision 4: How to mark the Saga Designs section

**Decision**: Add a one-line `**Status:** Planned design — not yet implemented.` note directly under each saga's heading (`OrderInvestigationSaga`, `OrderActionSaga`), before the state-transition diagram.

**Rationale**: Consistent, explicit signal in the same style as the table/diagram markers (Decisions 1–2). The section currently reads as present-tense behavior description ("Coordinates parallel data gathering…", "Pauses for human approval…") which, without a marker, reads as implemented.

**Alternatives considered**:
- Rewording all verbs to future/conditional tense ("Will coordinate…") — rejected: harder to keep grammatically consistent across edits, more error-prone than a single explicit status line, and loses the crisp design-doc voice the section currently has.

## Decision 5: Cross-document consistency check scope

**Decision**: Cross-check the following component list across README.md, ROADMAP.md, and CLAUDE.md: Workflow Orchestrator / MassTransit sagas, RabbitMQ, PostgreSQL / saga persistence, Notification Service, Evaluation runner, `NexusOps.Server`, `frontend/`. Based on the files already read during specification, all three currently agree (all seven marked planned/scaffold, nothing overclaimed) — so no CLAUDE.md edits are anticipated. The check itself remains a required implementation task in case drift is found once README.md's wording changes.

**Rationale**: FR-008 and User Story 3 require consistency, but the spec's own Assumptions section notes CLAUDE.md is already accurate. Re-verifying after README.md edits (rather than skipping the check) guards against the edit introducing new wording that accidentally drifts from CLAUDE.md's existing phrasing.

**Alternatives considered**: Skipping the CLAUDE.md check entirely since it's "probably fine" — rejected: cheap to verify, and US3's acceptance scenario explicitly requires the cross-check to happen, not just to have a predicted outcome.

**Output**: All decisions resolved; no `NEEDS CLARIFICATION` markers remain.
