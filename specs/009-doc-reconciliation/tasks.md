# Tasks: Documentation Reconciliation

**Input**: Design documents from `/specs/009-doc-reconciliation/`

**Prerequisites**: plan.md, spec.md, quickstart.md

**Tests**: Not applicable — this is a documentation-only feature (spec §FR-014). Verification is the re-check commands in quickstart.md, run as part of Polish below.

**Organization**: Tasks are grouped by user story from spec.md (US1 = P1 factual accuracy, US2 = P2 architecture diagram, US3 = P3 flagged constitution tensions).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to
- Tasks touching the same file are sequential even across stories, to avoid edit conflicts

## Phase 1: Setup

**Purpose**: Ground-truth verification (already performed during specify/plan; recorded here for traceability, not re-run per task)

- [x] T001 Re-verify all 11 known drift items and 3 constitution tensions against the live repo (`.slnf`/`.sln`, `.github/workflows/ci.yml`, `NexusOps.AppHost/AppHost.cs`, `.specify/memory/constitution.md`, `NexusOps.Contracts/ToolNames.cs`, seed data) — completed during spec authoring; no items were stale

---

## Phase 2: Foundational

**Purpose**: None required — this feature has no shared infrastructure or blocking prerequisite beyond Phase 1's verification, which is already complete.

**Checkpoint**: Foundation ready — all three user stories can proceed.

---

## Phase 3: User Story 1 - Factual accuracy across README.md, CLAUDE.md, ROADMAP.md (Priority: P1) 🎯 MVP

**Goal**: Every project-list, CI-behavior, roadmap-status, and example-query claim in the three docs matches the live repo.

**Independent Test**: Re-run quickstart.md's "Re-verify project/solution facts", "Re-verify CI workflow facts", "Re-verify repository structure facts", "Re-verify example queries and seed ID formats", and "Re-verify the local run command" sections; every claim checked should now match.

### Implementation for User Story 1

- [x] T002 [US1] Fix CLAUDE.md's Solution Filter section (~line 169): correct project count (13 total in `.sln`, 12 .NET) and state both `.slnf` exclusions (`frontend.esproj`, `NexusOps.IntegrationTests`), consistent with the CI table above it, in `CLAUDE.md`
- [x] T003 [US1] Move README.md's Roadmap "Integration test suite" line from Planned to Implemented in `README.md`
- [x] T004 [US1] Add `NexusOps.IntegrationTests/` to README.md's Project Structure block in `README.md`
- [x] T005 [P] [US1] Add `NexusOps.Evaluation/` and `specs/` to CLAUDE.md's Repository Structure block in `CLAUDE.md`
- [x] T006 [US1] Rewrite README.md's Example Queries table: replace the "customer Alice" row (no such capability exists) with a real Direct-path example, and replace "order #4521"/"Wireless headphones" with real `ORD-####`/`SKU-####` identifiers, in `README.md`
- [x] T007 [US1] Add the dotnet job's compile-only "Build integration tests" step and the `integration-tests` job's `timeout-minutes: 30` to CLAUDE.md's CI table/Solution Filter section in `CLAUDE.md`
- [x] T008 [US1] Expand README.md's Testing section to document `NexusOps.IntegrationTests` (build/run commands or a pointer to CLAUDE.md) and correct the "both commands run in CI on every push and pull request" framing to describe the real four-job workflow in `README.md`
- [x] T009 [US1] Point CLAUDE.md's header "Active Feature Plan" at `specs/009-doc-reconciliation/plan.md` in `CLAUDE.md` (completed during Phase 1 planning)
- [x] T010 [US1] Change ROADMAP.md's "definition of done" from `aspire start` to `dotnet run --project NexusOps.AppHost`, matching README.md/CLAUDE.md, in `ROADMAP.md`
- [x] T011 [US1] Add a Current Build State entry for feature 009 (this reconciliation) to `CLAUDE.md`, following the existing pattern used by features 005–008 (depends on T002–T010 being complete, since it summarizes them)
- [x] T012 [US1] Sweep README.md, CLAUDE.md, and ROADMAP.md once more for any other factual drift not on the known list (per spec FR-013) and correct anything found, across `README.md`, `CLAUDE.md`, `ROADMAP.md`

**Checkpoint**: User Story 1 fully addressed — docs are factually accurate independent of US2/US3.

---

## Phase 4: User Story 2 - Architecture diagram matches reality (Priority: P2)

**Goal**: README.md's Mermaid diagram includes every resource the AppHost actually provisions.

**Independent Test**: Re-run quickstart.md's "Re-verify the architecture diagram" section; Redis, `NexusOps.Server`, and the frontend should each appear as a node.

### Implementation for User Story 2

- [x] T013 [US2] Add Redis (session store), `NexusOps.Server` (BFF/static host), and the frontend (client-facing UI) as nodes to README.md's Mermaid architecture diagram, wired to their real connections (AgentHost↔Redis for session cache, Server↔frontend, Server↔AgentHost per `AppHost.cs`), in `README.md`

**Checkpoint**: User Stories 1 AND 2 both hold independently.

---

## Phase 5: User Story 3 - Flagged constitution tensions (Priority: P3)

**Goal**: Three known gaps between constitution principles and actual practice are visibly documented, without changing the constitution or the diverging practice.

**Independent Test**: Re-run quickstart.md's "Re-verify the flagged constitution tensions" section; confirm none of those commands' outputs changed, and the flagged-tensions note accurately describes all three.

### Implementation for User Story 3

- [x] T014 [US3] Add a "Flagged: Constitution Tensions" note to CLAUDE.md (new subsection near the Spec-Kit Workflow section, after the Constitution reference) describing: (a) Principle III lists notifications among gated mutations, but `NotificationRequested` publishes unconditionally on every terminal outcome; (b) the constitution's `###-short-name` branch mandate vs. the repo's actual `chore/...`-style branches and shipped `conventional-branch` skill; (c) Principle VI's `WithHttpHealthCheck` mandate vs. `webfrontend` having none — each stated as an open item for a human governance decision, not resolved here, in `CLAUDE.md`
- [x] T015 [US3] Update README.md's Key Design Decisions "Side effects require approval" bullet (which currently repeats "refund, notification" as if both are gated) to note the notification-gating tension, or point to CLAUDE.md's flagged-tensions note, in `README.md`

**Checkpoint**: All three user stories independently functional; constitution and application code remain unchanged.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Final verification that nothing outside scope changed and the reconciliation actually holds together

- [x] T016 Run every command in `specs/009-doc-reconciliation/quickstart.md` and confirm each doc claim now matches its source of truth
- [x] T017 Run `git diff --stat` and confirm only `README.md`, `CLAUDE.md`, `ROADMAP.md`, and `specs/009-doc-reconciliation/*` changed (spec FR-014) — no `.cs`, `.ts`, `.yml`, or config file touched
- [x] T018 Run `dotnet test NexusOps.deployable.slnf --configuration Release` and confirm it's still green (SC-004)
- [x] T019 Check off ROADMAP.md's Prompt 7 as complete, since its done-criteria are now met, in `ROADMAP.md`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: Already complete (T001).
- **Foundational (Phase 2)**: None — no blocking prerequisite.
- **User Stories (Phase 3–5)**: All can start immediately; no cross-story dependency, though T002–T008 (US1) and T014 (US3) both touch `CLAUDE.md` and should be applied as one coherent edit pass rather than true concurrent edits.
- **Polish (Phase 6)**: Depends on Phases 3–5 all being complete.

### User Story Dependencies

- **User Story 1 (P1)**: No dependency on US2/US3.
- **User Story 2 (P2)**: No dependency on US1/US3 — touches only README.md's diagram.
- **User Story 3 (P3)**: No dependency on US1/US2 — touches only the flagged-tensions note and one README bullet.

### Within User Story 1

T002 → T011 → T012 in rough order since T011 (Current Build State entry) summarizes T002–T010, and T012 (final sweep) should come last. T005 is parallelizable with the README-only tasks (different file).

## Parallel Example: Cross-Story

```bash
# CLAUDE.md-only tasks (US1) and README.md-only tasks (US1/US2) touch different files:
Task: "T005 [US1] Add NexusOps.Evaluation/ and specs/ to CLAUDE.md's Repository Structure"
Task: "T006 [US1] Rewrite README.md's Example Queries table"
Task: "T013 [US2] Add Redis/Server/frontend nodes to README.md's architecture diagram"
```

`T006` and `T013` both touch README.md, so treat them as sequential within a single edit pass even though they're listed under different stories.

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1 (already done).
2. Complete Phase 3 (User Story 1) — this alone eliminates 10 of the 11 known factual-drift findings.
3. **STOP and VALIDATE**: re-run the relevant quickstart.md sections.

### Incremental Delivery

1. User Story 1 → factual accuracy restored (MVP).
2. User Story 2 → architecture diagram completed.
3. User Story 3 → constitution tensions made visible.
4. Polish → full quickstart re-run, diff-scope check, test-suite green check, ROADMAP Prompt 7 checked off.

## Notes

- No [P] task shares a file with another task run concurrently; README.md and CLAUDE.md each accumulate multiple sequential edits across this task list.
- This feature has no code, so "commit after each task" is looser here — group related edits into one commit per user story if committing incrementally, per the user's own commit-workflow preference (commits happen only when explicitly requested).
