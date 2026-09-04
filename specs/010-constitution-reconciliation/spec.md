# Feature Specification: Constitution Reconciliation

**Feature Branch**: `010-constitution-reconciliation`

**Created**: 2026-09-03

**Status**: Draft

**Input**: User description: "Prompt 8 — Constitution reconciliation (three flagged tensions; see CLAUDE.md's 'Flagged: Constitution Tensions' — a human governance decision, not agent work). Resolve three unresolved gaps between .specify/memory/constitution.md and actual practice: (1) Principle III lists notifications as a gated mutation, but NotificationRequested publishes unconditionally on every terminal outcome — only the refund/cancellation itself is gated; (2) the constitution mandates ###-short-name branches, but the repo also has a shipped conventional-branch skill and a real history of chore/...-style branches, with no stated precedence; (3) Principle VI requires WithHttpHealthCheck for every AppHost service, but webfrontend has none. Each tension needs a decision — amend the constitution to match practice, or change practice to match the constitution — made by a human, not picked unilaterally by the agent."

## Clarifications

### Session 2026-09-03

- Q: Should every terminal outcome get a brand-new notification-approval step, or can a decision a human already made (approve/reject) double as notification consent? → A: Reuse the existing decision as notification consent (approve/reject also authorizes that outcome's notification); a new approval gate is added only for the one outcome with no prior human touch — a pre-approval validation `Failed`.
- Q: Should the `conventional-branch` skill be removed entirely, or kept and reworked to emit `###-short-name`-compliant names? → A: Keep it, reworked so `/conventional-branch` produces `###-short-name`-compliant branch names — an alternate entry point to the same convention, not a competing one. **(Superseded below — see 2026-09-04 entry.)**
- Q: (2026-09-04, revisited before implementation) `conventional-branch` and `conventional-commit` implement the general [Conventional Branch](https://conventional-branch.github.io)/[Conventional Commits](https://www.conventionalcommits.org) specs — reused across the maintainer's projects, not authored for NexusOps. Should NexusOps still rework `conventional-branch` to emit `###-short-name`? → A: No. Leave the skill untouched; it stays a general-purpose tool, not a NexusOps-specific one. NexusOps branch creation already has its own tool for the constitution's convention (`/speckit-git-feature`), so the fix is purely documentation: state that `/conventional-branch` is out of scope for NexusOps branch naming, not that it produces a competing convention that needs reconciling.
- Q: (Discovered during `/speckit-plan`, reading `OrderActionSaga.cs` directly) `NotificationRequested` is only ever published from `HandleRejectAsync` (after a human `Reject`) and `HandleExecutionCompletedAsync` (only reachable after a human `Approve`) — so `Rejected`/`Completed`/`Failed`-via-execution notifications already have prior human consent today; a validation-failure `Failed` (order not found / service unavailable) sends **no notification at all** and mutates nothing. Given that, should this feature add any new notification/approval behavior for validation-failure `Failed`? → A: Leave it as-is — no notification, nothing to gate, since nothing mutated. This makes the notification-gating half of this feature a documentation correction (CLAUDE.md's "every terminal outcome" claim was inaccurate) rather than a code change.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Correct the notification-gating record to match verified behavior (Priority: P1)

Principle III already states that notifications, alongside refunds and cancellations, must route through `OrderActionSaga` with a human approval gate. `NotificationRequested` is published from exactly two places in `OrderActionSaga`: after a human `Reject` decision (`Rejected` outcome), and after a human `Approve` decision leads to execution finishing (`Completed`, or `Failed`/`FailedAndCompensated` execution outcomes — both land in the saga's single `Failed` state). Every notification that is actually sent today already has a prior human decision behind it. The one terminal path with no prior human touch — a pre-approval validation failure (order not found / service unavailable) landing in `Failed` with no `ExecutionOutcome` set — sends no notification at all today, and mutates nothing. The decision: leave that behavior as-is (nothing to gate, since nothing is sent and nothing mutated), and correct CLAUDE.md's inaccurate claim that notifications fire unconditionally "on every terminal outcome."

**Why this priority**: Principle III is the constitution's statement of this system's central safety guarantee ("side effects require approval"). Confirming the code already honors it, and fixing the record that incorrectly said otherwise, is the most safety-relevant of the three tensions and should be resolved first — even though it turns out to require no application code change.

**Independent Test**: Can be fully tested by driving all three of `OrderActionSaga`'s notification-producing outcomes (`Rejected` via reject, `Completed` via approve, `Failed`-via-execution via approve) and confirming each notification is already preceded by the corresponding human decision with no code change needed — and by confirming a validation-failure `Failed` produces no notification, unchanged.

**Acceptance Scenarios**:

1. **Given** a human rejects or approves an order action, **When** that decision produces a terminal outcome (`Rejected`, `Completed`, or `Failed`-via-execution), **Then** `notification-service` logs that outcome exactly as it does today — behavior is unchanged, since the human decision already stands as the notification's consent.
2. **Given** an order action fails validation before ever reaching `AwaitingApproval` (no prior human decision, nothing mutated), **When** the saga reaches `Failed` this way, **Then** no notification is published — behavior is unchanged.
3. **Given** the documentation correction is complete, **When** Principle III is re-read against the code, **Then** its existing wording ("refunds, cancellations, order modifications, notifications ... MUST route through `OrderActionSaga` with a human approval gate") is accurate — satisfied by each notification reusing its action's own approval/rejection decision — and CLAUDE.md's flagged-tensions entry #1 is marked resolved with this clarification.

---

### User Story 2 - Clarify that `###-short-name` is the sole NexusOps branch convention (Priority: P2)

The constitution's Development Workflow section mandates the spec-kit `###-short-name` branch convention, but a large share of the repo's real branch history uses `conventional-branch`-style names (`chore/...`, etc.), and the repo carries a `conventional-branch` skill that produces exactly those names. Revisited before implementation (2026-09-04): `conventional-branch` (and `conventional-commit`) implement general, cross-project conventions the maintainer reuses elsewhere — not something authored for NexusOps — so reworking it for this one repo is out of scope. The decision: `###-short-name` remains the one convention for every NexusOps branch, enforced via the tool this project already has for it (`/speckit-git-feature`); `conventional-branch` is left untouched and documented as a general-purpose tool this project doesn't use for branch naming, not a competing convention needing reconciliation.

**Why this priority**: Affects every future branch created in the repo, but is lower safety impact than the notification-gating tension — it's a workflow-consistency question, not a mutation-safety one.

**Independent Test**: Can be fully tested by reading CLAUDE.md's flagged-tensions entry #2 after the change and confirming it states, without ambiguity, which tool NexusOps branches are created with (`/speckit-git-feature`) and that `/conventional-branch` is explicitly out of scope for this repo — without needing to consult a maintainer or prior branch history.

**Acceptance Scenarios**:

1. **Given** the `conventional-branch` skill exists today and produces `feature/`, `bugfix/`, `chore/`, etc. names, **When** this feature is complete, **Then** the skill is unchanged — it remains a general-purpose tool — and CLAUDE.md documents that NexusOps branches are created via `/speckit-git-feature`, not `/conventional-branch`.
2. **Given** a contributor is naming a new branch of any kind after this change, **When** they consult the constitution and CLAUDE.md, **Then** both unambiguously state `###-short-name` (via `/speckit-git-feature`) for all NexusOps branches, with `/conventional-branch` explicitly noted as out of scope rather than a silently competing convention.
3. **Given** the change is complete, **When** CLAUDE.md's flagged-tensions entry #2 is reviewed, **Then** it is marked resolved, noting that historical `chore/...` branches predate the fix and are not retroactively renamed, and that the resolution is a scope clarification, not a skill rework.

---

### User Story 3 - Add a health check for webfrontend (Priority: P3)

Principle VI requires every service to have a health check registered in the Aspire AppHost via `WithHttpHealthCheck`. Every resource in `NexusOps.AppHost/AppHost.cs` has one except `webfrontend`. The decision: add a workable health check for `webfrontend` so it complies with Principle VI as written — no constitutional exception is needed.

**Why this priority**: Narrowest blast radius of the three — it concerns one resource in one file — so it can safely be resolved last without blocking the other two.

**Independent Test**: Can be fully tested by inspecting `AppHost.cs` after the change and confirming `webfrontend` now has a `WithHttpHealthCheck` registration, then starting the app and confirming the Aspire dashboard reports a real, non-static health status for `webfrontend`.

**Acceptance Scenarios**:

1. **Given** `webfrontend` (the Vite dev server) currently has no health check, **When** this feature is complete, **Then** `AppHost.cs` registers a `WithHttpHealthCheck` for `webfrontend` that reflects whether the Vite dev server is actually responding, not a hardcoded always-healthy stub.
2. **Given** the health check is registered, **When** the Aspire dashboard is viewed with the dev server running normally, **Then** `webfrontend` shows healthy; if the dev server is stopped or unreachable, `webfrontend` shows unhealthy.
3. **Given** the change is complete, **When** CLAUDE.md's flagged-tensions entry #3 is reviewed, **Then** it is marked resolved, and Principle VI needs no wording change since practice now matches it exactly.

---

### Edge Cases

- What happens to branches that already violate `###-short-name` (e.g., merged `chore/...` branches)? Retroactive renaming of merged/historical branches is out of scope — only the rule and tooling going forward need to be unambiguous.
- What happens if `webfrontend`'s Vite dev server has no reachable HTTP endpoint in some environments (e.g., before `npm install` completes)? The health check should report unhealthy/unknown rather than block AppHost startup — consistent with how other resources' health checks behave today.
- What happens if, after resolution, a new contradiction between the constitution and practice is found that isn't one of these three? Out of scope for this feature; it would be flagged the same way feature 009 flagged these three, for a future reconciliation pass.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: `OrderActionSaga`'s notification behavior MUST remain unchanged by this feature: `Rejected`, `Completed`, and `Failed`-via-execution notifications continue to be published exactly as they are today, immediately following their triggering human decision (`Reject` or `Approve`), with no new approval step added; a validation-failure `Failed` continues to publish no notification.
- **FR-002**: CLAUDE.md and any other doc asserting that `NotificationRequested` fires "unconditionally on every terminal outcome" MUST be corrected to state precisely which outcomes produce a notification (`Rejected`, `Completed`, `Failed`-via-execution) and that each already carries its triggering human decision as consent, and that a validation-failure `Failed` produces none.
- **FR-003**: The `conventional-branch` skill MUST NOT be modified by this feature — it remains a general-purpose implementation of the [Conventional Branch](https://conventional-branch.github.io) spec, out of NexusOps's branch-naming scope. CLAUDE.md MUST document that NexusOps branches are created via `/speckit-git-feature` (the tool that already produces `###-short-name`), not `/conventional-branch`.
- **FR-004**: The constitution's Development Workflow section MUST unambiguously state `###-short-name` as the convention for every NexusOps branch (features, chores, fixes alike); no wording change is expected since the text already says this, but CLAUDE.md's own record of the tension MUST no longer describe `conventional-branch` as a competing convention requiring reconciliation.
- **FR-005**: `NexusOps.AppHost/AppHost.cs` MUST register a `WithHttpHealthCheck` for `webfrontend` that reflects the Vite dev server's actual reachability, matching every other resource in the topology.
- **FR-006**: CLAUDE.md's "Flagged: Constitution Tensions" section MUST be updated per tension once resolved — each entry marked resolved with a pointer to the decision (all three: align practice to the constitution) and its rationale — so the section no longer represents these three as open.
- **FR-007**: ROADMAP.md's Status checklist MUST mark Prompt 8 complete once all three tensions' practice changes are made and verified.
- **FR-008**: `.specify/memory/constitution.md`'s own text MUST NOT need to change in substance for any of the three principles (III, Development Workflow, VI) — since all three decisions align practice to the existing constitution — but MAY receive a clarifying, non-substantive edit (e.g., Development Workflow explicitly naming `###-short-name` as the sole convention) if ambiguity remains after the practice changes.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: CLAUDE.md's "Flagged: Constitution Tensions" section shows zero tensions still described as open/unresolved.
- **SC-002**: For every `OrderActionSaga` outcome that produces a notification (`Rejected`, `Completed`, `Failed`-via-execution), `notification-service` logs nothing without a prior human decision behind it — verified for all three, not just the happy path — and this is confirmed to already hold true with no code change.
- **SC-003**: CLAUDE.md unambiguously documents `/speckit-git-feature` as the tool NexusOps branches are created with, and `/conventional-branch` as an out-of-scope general-purpose tool this project doesn't use for branch naming — no reader is left to wonder which convention governs a new NexusOps branch.
- **SC-004**: The Aspire dashboard reports a live, non-static health status for `webfrontend` that changes correctly when the Vite dev server is stopped and restarted.
- **SC-005**: ROADMAP.md's Status checklist shows Prompt 8 as complete.

## Assumptions

- The three tensions in scope are exactly those already catalogued in CLAUDE.md's "Flagged: Constitution Tensions" section as of 2026-09-03 (added by feature 009); no new tensions are introduced by this feature.
- All three decisions were made by the project maintainer during specification (2026-09-03), each choosing to align practice with the constitution as written. For notification gating specifically, verification during planning (reading `OrderActionSaga.cs` directly) found the code already complies once "reuse the triggering decision as consent" is the accepted reading — the only remaining action is correcting CLAUDE.md's inaccurate description, confirmed by the maintainer rather than adding new gating code.
- `###-short-name` remains the sole NexusOps branch convention; `conventional-branch` is left untouched as an out-of-scope general-purpose skill, not reworked; `webfrontend` gains a health check. None of the three requires a substantive constitution wording change.
- Implementation will touch CLAUDE.md, ROADMAP.md, and `NexusOps.AppHost/AppHost.cs`; `.claude/skills/conventional-branch/` is explicitly NOT touched (2026-09-04 revision — see spec.md's Clarifications). `.specify/memory/constitution.md` needs at most a non-substantive clarifying edit. `NexusOps.WorkflowOrchestrator` and `notification-service` are NOT touched by this feature — their behavior was verified correct as-is.
- Retroactively renaming or reconciling already-merged branches that predate the `###-short-name`-only rule is out of scope; only the rule and tooling going forward need to be unambiguous.
