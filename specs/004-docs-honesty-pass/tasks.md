---

description: "Task list for Documentation Honesty Pass"

---

# Tasks: Documentation Honesty Pass

**Input**: Design documents from `/specs/004-docs-honesty-pass/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, quickstart.md

**Tests**: Not applicable — this is a documentation-content feature with no automated test suite. Validation is manual, against quickstart.md and the Success Criteria in spec.md.

**Organization**: Tasks are grouped by user story to enable independent implementation and verification of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files/sections, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

## Path Conventions

All work targets repository-root documentation files: `README.md` (primary), `ROADMAP.md`, and `CLAUDE.md` (only if a genuine inconsistency is found). No `src/`/`tests/` structure applies — see plan.md's Structure Decision.

---

## Phase 1: Setup

**Purpose**: Establish the baseline needed to prove no existing honesty statement regresses (SC-004)

- [X] T001 Read README.md, ROADMAP.md, and CLAUDE.md and record the full list of existing "planned, not implemented" and "scaffold only" statements as a baseline checklist for the no-regression check in Phase 6 (T011)

---

## Phase 2: Foundational

**Purpose**: Blocking prerequisites for all user stories

No foundational tasks are required. Each user story below edits a distinct, non-overlapping section of `README.md` (or a distinct file), so nothing must be built before story work can start beyond the Phase 1 baseline.

---

## Phase 3: User Story 1 - Understand why the project exists (Priority: P1) 🎯 MVP

**Goal**: Add a "Why this project" section to README.md that frames NexusOps as a fintech-operations-to-agentic-AI proof of concept.

**Independent Test**: Read only the new section (quickstart.md step 1) and confirm a reader can restate the fintech analogy without reading anything else (SC-001).

### Implementation for User Story 1

- [X] T002 [US1] Insert a "Why this project" section into `README.md` immediately after the opening tagline paragraph and before the "Architecture" section, stating NexusOps is a proof-of-concept and mapping at least multi-source aggregation → investigation fan-out, maker-checker approval → approval gate, and compensation on partial failure → saga compensation (data-model.md "Why this project section"; research.md Decision 3)
- [X] T003 [US1] Cross-check the new section's wording in `README.md` against the "Resume framing" line in `ROADMAP.md` (investigation fan-out ≈ upstream aggregation; approval gate ≈ maker-checker; compensation ≈ reversing partial writes; curated tools ≈ governed API surface) and reconcile any mismatch (FR-002)

**Checkpoint**: User Story 1 is independently verifiable via quickstart.md step 1.

---

## Phase 4: User Story 2 - Tell implemented from planned at a glance (Priority: P1)

**Goal**: Mark every not-yet-implemented component directly in the Tech Stack table, architecture diagram, and Saga Designs section of `README.md`.

**Independent Test**: With all prose hidden, scan only the Tech Stack Status column, diagram labels/styling, and Saga Designs status lines and correctly classify all six planned components (quickstart.md step 2, SC-002).

### Implementation for User Story 2

- [X] T004 [US2] Add a "Status" column (`Implemented` \| `Planned`) to the Tech Stack table in `README.md`; mark Durable Orchestration, Workflow Orchestrator, Notification Service, Saga Persistence, and Message Broker rows `Planned`, and AI Reasoning, App Orchestration & Observability, Agent Host, and Domain Services rows `Implemented` (data-model.md "Tech Stack table row"; research.md Decision 1)
- [X] T005 [US2] Split the "Model Provider & Evaluation" row in the `README.md` Tech Stack table into a "Model Provider" row (Azure AI Foundry, `Implemented`) and an "Evaluation" row (Azure AI Foundry evaluators, `Planned`), since the current single row conflates live model calls (implemented) with the evaluation runner (planned)
- [X] T006 [US2] Mark the not-yet-implemented nodes in the `README.md` Mermaid architecture diagram — RabbitMQ (`RMQ`), the Workflow Orchestrator subgraph (`Orch`/`MT`), PostgreSQL (`PG`), and Notification Service (`Notify`) — with a `(planned)` text suffix and a dashed `stroke-dasharray` border style, leaving `Foundry`, `Client`, `Host`/`AH`, and the Direct-path services unmarked as implemented (data-model.md "Architecture diagram node"; research.md Decision 2)
- [X] T007 [US2] Add a `**Status:** Planned design — not yet implemented.` line under each of the `OrderInvestigationSaga` and `OrderActionSaga` headings in the `README.md` Saga Designs section, before their state-transition diagrams (data-model.md "Saga Designs entry"; research.md Decision 4)
- [X] T008 [US2] Verify the existing "Planned but not yet implemented" callout in the `README.md` Project Structure section stays accurate and consistent with the new Tech Stack Status column from T004–T005; update wording only if a gap is found

**Checkpoint**: User Story 2 is independently verifiable via quickstart.md step 2.

---

## Phase 5: User Story 3 - Trust that the docs agree with each other (Priority: P2)

**Goal**: Confirm README.md's edited status claims match ROADMAP.md and CLAUDE.md, with no contradictions.

**Independent Test**: Cross-reference the data-model.md "Consistency ledger" component list across all three files (quickstart.md step 3, SC-003).

**Depends on**: User Story 1 (T002–T003) and User Story 2 (T004–T008) — this story verifies the edited state of `README.md`, so it must run after those edits exist.

### Implementation for User Story 3

- [X] T009 [US3] Cross-check every component in data-model.md's "Consistency ledger" (Workflow Orchestrator/MassTransit, RabbitMQ, PostgreSQL, Notification Service, Evaluation runner, `NexusOps.Server`, `frontend/`) across the edited `README.md`, `ROADMAP.md`, and `CLAUDE.md`; fix any mismatch by editing only the incorrect document (FR-008)
- [X] T010 [US3] Confirm `CLAUDE.md`'s Tech Stack table wording (e.g., "MassTransit + RabbitMQ (planned)") still matches `README.md`'s new Status-column wording after T004–T005; align phrasing only if genuinely inconsistent, without rewriting CLAUDE.md's Current Build State prose

**Checkpoint**: User Story 3 is independently verifiable via quickstart.md step 3.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Final validation against all Success Criteria and the scope guard

- [X] T011 [P] Diff `README.md` against its pre-edit version and confirm every statement recorded in T001's baseline is still present, verbatim or equivalently (quickstart.md step 4, SC-004)
- [X] T012 [P] Run `git diff --stat` against the base branch and confirm only `README.md`, `ROADMAP.md`, and (optionally) `CLAUDE.md` changed — no `.cs`, `.csproj`, `.esproj`, `.ts`/`.tsx`, or CI workflow files (FR-009; quickstart.md step 5)
- [X] T013 [P] Render-check the Mermaid diagram in `README.md` (e.g., via a Markdown/Mermaid preview) to confirm it remains valid syntax after the `(planned)` labels and dashed styling were added (research.md Decision 2 constraint)
- [X] T014 Check off "Prompt 1 — README honesty pass" in `ROADMAP.md`'s Status checklist now that this feature is complete

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately
- **Foundational (Phase 2)**: None required (see note above)
- **User Story 1 (Phase 3)**: Depends on Phase 1 (baseline captured) — independent of US2
- **User Story 2 (Phase 4)**: Depends on Phase 1 — independent of US1, but edits the same `README.md` file, so run sequentially with US1 to avoid diff conflicts (logically independent, not file-independent)
- **User Story 3 (Phase 5)**: Depends on US1 and US2 being complete — it verifies their combined output
- **Polish (Phase 6)**: Depends on US1, US2, and US3 all being complete

### Within Each User Story

- US1: T002 before T003 (can't cross-check wording that doesn't exist yet)
- US2: T004 before T005 (split refines the row T004 just added); T006, T007, T008 touch different sections and may be done in any order after T004–T005
- US3: T009 before T010 (T010 is a narrower re-check of one finding from T009)

### Parallel Opportunities

- T011, T012, and T013 in Phase 6 are all read-only verification tasks on different aspects (content diff, file scope, diagram syntax) and can run in parallel
- No other tasks are parallelizable — all remaining tasks write to `README.md`, `ROADMAP.md`, or `CLAUDE.md`, and sequencing avoids conflicting edits to the same file

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup (T001)
2. Complete Phase 3: User Story 1 (T002–T003)
3. **STOP and VALIDATE**: Run quickstart.md step 1 — confirm the "Why this project" framing lands on its own
4. This alone delivers the single highest-value credibility signal for a portfolio/POC repo

### Incremental Delivery

1. Setup → baseline captured
2. Add User Story 1 → validate independently (SC-001) — MVP
3. Add User Story 2 → validate independently (SC-002)
4. Add User Story 3 → validate independently (SC-003)
5. Polish (Phase 6) → validate SC-004, FR-009, and close out the ROADMAP.md checklist item

---

## Notes

- [P] tasks = no conflicting file writes with other in-flight tasks
- [Story] label maps task to specific user story for traceability
- This feature has no code, so there are no models/services/endpoints — all tasks are direct content edits or verification reads
- Commit after each phase (or after each task, if preferred) rather than one large commit, to keep the diff reviewable
- Do not touch any `.cs`, `.csproj`, `.esproj`, `.ts`/`.tsx`, or CI workflow file — out of scope per FR-009
