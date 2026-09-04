# Tasks: Constitution Reconciliation

**Input**: Design documents from `/specs/010-constitution-reconciliation/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, quickstart.md

**Tests**: Not requested in spec.md or plan.md. This feature changes no application behavior for two of its three tensions (research.md Decision 1: notification gating already compliant; Decision 2: branch naming resolved by documentation, not a skill rework) and one line of infrastructure config for the third; verification is manual per quickstart.md, and existing automated tests are re-run for regression only — no new test code is written.

**Organization**: Tasks are grouped by user story (spec.md's three tensions), each independently completable and verifiable.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3)
- File paths are exact

---

## Phase 1: Setup

**No tasks.** This feature modifies existing files only — no new project, dependency, or scaffolding is introduced (plan.md's Technical Context: no new language, package, or storage).

---

## Phase 2: Foundational

**No tasks.** The three user stories touch disjoint concerns (two documentation corrections and one AppHost config line) with no shared prerequisite and no ordering dependency between stories — each can be implemented and verified independently, in any order.

---

## Phase 3: User Story 1 - Correct the notification-gating record (Priority: P1) 🎯 MVP

**Goal**: Fix CLAUDE.md's inaccurate claim that `NotificationRequested` fires "unconditionally on every terminal outcome." No application code changes — research.md Decision 1 confirmed `OrderActionSaga` already gates every notification it actually sends behind a prior human `Approve`/`Reject` decision.

**Independent Test**: Re-read Principle III against `OrderActionSaga.cs` and confirm the updated CLAUDE.md text matches; confirm existing tests are unaffected.

- [X] T001 [US1] Update CLAUDE.md's "Flagged: Constitution Tensions" entry #1 (currently the numbered list item starting "**Principle III names 'notifications' as a gated mutation...**", ~line 207) to state the verified behavior: `NotificationRequested` is published only from `HandleRejectAsync` (after a human `Reject`) and `HandleExecutionCompletedAsync` (only reachable after a human `Approve`) — covering `Rejected`, `Completed`, and `Failed`-via-execution; a validation-failure `Failed` (order not found / service unavailable) has no prior human decision and publishes no notification, and mutates nothing. Mark the item **Resolved** — the code already complies with Principle III once the triggering decision is read as the notification's consent; no code change was needed. In `CLAUDE.md`.
- [X] T002 [P] [US1] Run `dotnet test NexusOps.deployable.slnf` (and, if Docker is available, `dotnet test NexusOps.IntegrationTests`) to confirm the full suite is still green with zero notification-flow regression, since no `OrderActionSaga`/`notification-service` code changed. Record the pass count (expect 174/174 unit, 4/4 integration, matching CLAUDE.md's last-recorded baseline).

**Checkpoint**: CLAUDE.md's tension #1 accurately describes the code; no behavior changed; tests confirm it.

---

## Phase 4: User Story 2 - Document `###-short-name` as the sole NexusOps branch convention (Priority: P2)

**Goal**: Correct CLAUDE.md's flagged-tensions entry #2 to state that NexusOps branches are created via `/speckit-git-feature` (already `###-short-name`-compliant) and that `/conventional-branch` is an out-of-scope general-purpose skill, not a competing convention needing reconciliation. `conventional-branch` implements the general Conventional Branch spec, reused across the maintainer's other projects, not authored for NexusOps — no skill rework.

**Independent Test**: Re-read CLAUDE.md's updated entry #2 and confirm it unambiguously names `/speckit-git-feature` as the tool NexusOps branches are created with, with no unresolved reference to `/conventional-branch` as a second convention.

- [X] T003 [US2] Update CLAUDE.md's "Flagged: Constitution Tensions" entry #2 (currently "**The branch-naming mandate doesn't match how branches are actually named.**", ~line 208) to mark it **Resolved**: NexusOps branches are created via `/speckit-git-feature`, which already produces `###-short-name` names; `/conventional-branch` is a general-purpose skill (implements the general Conventional Branch spec, reused across the maintainer's other projects) and is explicitly out of scope for this repo's branch naming, not a competing convention. Note that historical `chore/...`-style branches predate this clarification and are not retroactively renamed. In `CLAUDE.md`.
- [X] T004 [US2] Manually verify per quickstart.md's step 2: re-read CLAUDE.md's updated entry #2 for clarity, and optionally invoke `/speckit-git-feature` for a throwaway description to confirm it still produces a `###-short-name` branch (unchanged behavior), then delete the throwaway branch and return to `010-constitution-reconciliation`. Depends on: T003.

**Checkpoint**: CLAUDE.md's tension #2 documents `/speckit-git-feature` as the sole NexusOps branch-naming tool, with `/conventional-branch`'s out-of-scope status recorded; no skill code changed.

---

## Phase 5: User Story 3 - Add a health check for webfrontend (Priority: P3)

**Goal**: `webfrontend` gets a real `WithHttpHealthCheck` registration in `AppHost.cs`, so every AppHost resource complies with Principle VI.

**Independent Test**: Inspect `AppHost.cs` for the new registration; run the app and confirm the Aspire dashboard reports a live (non-static) health status for `webfrontend` that changes when the dev server is stopped/restarted.

- [X] T005 [P] [US3] Add `.WithHttpHealthCheck("/")` to the `webfrontend` resource definition in `NexusOps.AppHost/AppHost.cs` (currently `var webfrontend = builder.AddViteApp("webfrontend", "../frontend").WithReference(server).WaitFor(server);`, ~line 65-67), per research.md Decision 3 — targeting the HTTP endpoint `AddViteApp` already registers. In `NexusOps.AppHost/AppHost.cs`.
- [X] T006 [US3] Update CLAUDE.md's "Flagged: Constitution Tensions" entry #3 (currently "**Principle VI's health-check mandate isn't met by `webfrontend`.**", ~line 209) to mark it **Resolved**: `webfrontend` now has a `WithHttpHealthCheck` registration (T005); Principle VI needed no wording change since practice now matches it. In `CLAUDE.md`. Depends on: T005.
- [X] T007 [US3] Verify the health check actually transitions live. Depends on: T005. **Done, via an equivalent method to quickstart.md's prescribed one**: rather than a browser on the Aspire dashboard, verified by booting the real `NexusOps.AppHost` topology through `Aspire.Hosting.Testing` (reduced to `server`/`webfrontend`/`webfrontend-installer` — no Docker needed) and reading the `ResourceNotifications` stream, the same health-state source the dashboard itself renders. Observed for `webfrontend`: `Starting` → `Waiting` → `Running/Unhealthy` (probe failing while Vite boots) → `Running/Healthy` (probe gets `200` at `/`) → `Stopping`/`Finished` (resource stopped) → `Starting` → `Running/Unhealthy` → `Running/Healthy` (recovered after restart) — confirmed by `npm run dev`'s logged port matching the probed endpoint. The `Running/Unhealthy` → `Running/Healthy` pair is the part that matters: a static always-healthy stub could never report `Unhealthy` on a running resource. Two caveats on method, neither affecting the check itself (it's per-resource, independent of what else is running): (1) this reads the notification stream, not the dashboard UI; (2) the reduced topology only ran `server`/`webfrontend`, not the full app.

**Checkpoint**: Every AppHost resource has a registered health check; CLAUDE.md's tension #3 reflects this.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Close out the section-level record and ROADMAP.md now that all three tensions are individually resolved.

- [X] T008 Update CLAUDE.md's "Flagged: Constitution Tensions" section intro (the paragraph starting "Identified during the feature 009 documentation reconciliation and deliberately left unresolved...", ~lines 203-205) to reflect that all three tensions are now resolved — reference `specs/010-constitution-reconciliation/` instead of describing them as an open flag for a future agent to avoid silently fixing. Keep the section (as a resolved record future readers can trust), keep it outside the `<!-- SPECKIT START/END -->` markers. In `CLAUDE.md`. Depends on: T001, T003, T006.
- [X] T009 Update `ROADMAP.md`: change the Status checklist's Prompt 8 line (~line 15) from `- [ ]` to `- [x]`, and add a `## Prompt 8 — Constitution reconciliation (Complete)` narrative section (mirroring Prompt 7's style at ~line 75) summarizing all three resolutions, that no application/CI code changed except `AppHost.cs`'s one health-check line, and that `.specify/memory/constitution.md` needed no wording change. In `ROADMAP.md`. Depends on: T008.
- [X] T010 Run quickstart.md's "Final check": confirm CLAUDE.md's flagged-tensions section shows all three items resolved (SC-001), ROADMAP.md's Status checklist shows Prompt 8 as `[x]` (SC-005), and `git diff .specify/memory/constitution.md` shows no changes (research.md's finding that no wording edit was needed). Depends on: T009.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)** / **Foundational (Phase 2)**: No tasks — nothing blocks the user stories.
- **User Stories (Phase 3-5)**: Fully independent of each other; may be done in any order or in parallel.
- **Polish (Phase 6)**: Depends on all three user stories' CLAUDE.md tasks (T001, T003, T006) being complete.

### User Story Dependencies

- **User Story 1 (P1)**: No dependencies on US2/US3.
- **User Story 2 (P2)**: No dependencies on US1/US3.
- **User Story 3 (P3)**: No dependencies on US1/US2.

### Within Each User Story

- US1: T001 and T002 are independent of each other (doc edit vs. test run) — both [P] where marked.
- US2: T003 (doc update, no skill change) before T004 (manual verify, re-reads the doc update).
- US3: T005 (AppHost.cs change) before T006 (doc update) and T007 (manual verify).

### Parallel Opportunities

- T002 (US1) can run in parallel with T001, and with US2/US3's tasks — different files, no shared state.
- T005 (US3) can run in parallel with US1's and US2's tasks — different files, no shared state.
- All three user stories (Phases 3-5) can be worked in parallel since they touch disjoint files, except each story's own CLAUDE.md task (T001/T003/T006) — all three land in the same file's "Flagged: Constitution Tensions" section, so do those sequentially even if the surrounding story work happens in parallel.

---

## Parallel Example: Across User Stories

```bash
# These can all be dispatched together — different files, no shared dependency:
Task: "Add .WithHttpHealthCheck(\"/\") to webfrontend in NexusOps.AppHost/AppHost.cs (T005)"
Task: "Run dotnet test NexusOps.deployable.slnf for US1 regression check (T002)"

# Then, once US3's code change lands, the three CLAUDE.md entry updates (T001, T003, T006)
# should be done one at a time (same file, same section) before Polish (T008-T010).
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 3 (US1): correct the notification-gating record. Zero code risk — it's the safety-critical tension and requires no application change.
2. **STOP and VALIDATE**: confirm tests are unaffected (T002).
3. This alone closes the most safety-relevant of the three tensions.

### Incremental Delivery

1. US1 (P1) → validate → this is already a coherent, shippable increment (a doc-only fix).
2. US2 (P2) → validate by re-reading CLAUDE.md's updated entry → shippable.
3. US3 (P3) → validate via the Aspire dashboard → shippable.
4. Polish (T008-T010) → close out CLAUDE.md's section-level record and ROADMAP.md's Prompt 8 once all three land.

### Parallel Team Strategy

With multiple contributors: since the three stories touch disjoint files (CLAUDE.md's three distinct entries aside), US1/US2/US3 can be assigned to three different people and done simultaneously; only the final CLAUDE.md section intro (T008) and ROADMAP.md update (T009) need all three finished first.

---

## Notes

- No `[Story]` label on Setup/Foundational/Polish tasks per convention — Polish tasks here (T008-T010) are cross-cutting, not story-specific.
- No tests were added because none were requested and no application behavior changed (US1, US2) or is testable via the existing automated suite (US3's dashboard health status is developer-tooling/infra, verified manually per quickstart.md).
- Every CLAUDE.md-touching task (T001, T003, T006, T008) edits the same file — treat as sequential even though they belong to otherwise-parallel stories, to avoid clobbering each other's edits.
