# Phase 0 Research: Constitution Reconciliation

No `NEEDS CLARIFICATION` markers remain in the Technical Context — this feature's unknowns were governance decisions, already resolved during `/speckit-specify` and `/speckit-clarify` (see spec.md's `## Clarifications`). This file records the implementation-level research that followed once planning began reading the actual code.

## Decision 1: Notification gating requires no code change

**Decision**: Leave `OrderActionSaga`'s notification publishing exactly as it is. Correct CLAUDE.md's "Flagged: Constitution Tensions" entry #1 to accurately describe the existing behavior instead of the inaccurate "unconditionally on every terminal outcome" claim.

**Rationale**: Direct inspection of `NexusOps.WorkflowOrchestrator/OrderAction/OrderActionSaga.cs` shows `NotificationRequested` is published from exactly two call sites:
- `HandleRejectAsync` — only reachable via the `Reject` event, i.e. only after a human explicitly rejects. Produces the `Rejected` outcome.
- `HandleExecutionCompletedAsync` — only reachable via the `Executing` state, which is only entered via the `Approve` event's transition. Produces `Completed` (execution outcome `Executed`) or `Failed` (execution outcome `Failed` or `FailedAndCompensated` — both land in the same saga state; `OrderActionSagaState.ExecutionOutcome` distinguishes them).

`HandleValidationCompletedAsync`, the one path that reaches the `Failed` state with **no** prior human decision (order not found or the validation service was unreachable), explicitly does not publish a notification — the code comment states this directly: *"No notification is published here: nothing was ever pending a human decision."* Nothing mutates on this path either.

So every notification the system actually sends today already carries a human decision (`Approve` or `Reject`) as its consent. Under the maintainer's Q1 answer (reuse the existing decision as consent), this is already full compliance with Principle III's "notifications ... MUST route through `OrderActionSaga` with a human approval gate" — the gate is the `Approve`/`Reject` event, and the notification is downstream of it, not independent of it.

**Alternatives considered**:
- *Add a second, dedicated approval step for every notification* (spec's original FR-001, before this discovery) — rejected: would add a redundant approval click for outcomes a human already decided, with no compliance gap to justify it.
- *Add a new notification + approval gate for the validation-failure `Failed` case* — considered and explicitly rejected by the maintainer (clarification during `/speckit-plan`): nothing mutates on that path, so Principle III's mutation list doesn't reach it, and adding one would be new functionality, not a fix.

**Scope of the doc fix**: Limited to CLAUDE.md's "Flagged: Constitution Tensions" section (the mechanism this feature exists to close out), not every historical echo of "notification on every terminal outcome" elsewhere in CLAUDE.md (e.g. the Architecture overview line, the feature-006 implementation log). Those are coarser, historically-accurate-enough summaries outside this feature's stated scope; rewriting them is unrelated doc polish, not required to resolve the flagged tension.

## Decision 2 (revised 2026-09-04): Leave `conventional-branch` untouched; resolve by documenting scope, not by reworking the skill

**Decision**: Do not modify `.claude/skills/conventional-branch/SKILL.md`. It implements the general [Conventional Branch](https://conventional-branch.github.io) spec, which the maintainer reuses across projects — it was never authored for NexusOps specifically, so bending it to this one repo's numbering convention is out of scope. Instead, CLAUDE.md's flagged-tensions entry #2 is corrected to state which tool NexusOps branches are actually created with (`/speckit-git-feature`, already `###-short-name`-compliant) and that `/conventional-branch` is a general-purpose tool this project doesn't use for branch naming — not a second convention needing reconciliation.

**Rationale**: The original Decision 2 (rework the skill to delegate to `create-new-feature.sh`) assumed `conventional-branch` was NexusOps-specific tooling that had simply drifted from the constitution. The maintainer corrected that assumption: the skill is general-purpose, so the actual gap was never "the skill produces the wrong format" — it was "CLAUDE.md never said which tool governs NexusOps branch naming, leaving `/conventional-branch`'s presence in the repo looking like an unresolved second convention." That's a documentation gap, not a tooling one, and closing it requires no code change — the same shape as Decision 1 (notification gating) turning out to be a doc fix once the actual behavior was checked.

**Alternatives considered**:
- *Rework the skill to delegate to `create-new-feature.sh`* — the original decision (this file's prior revision); superseded once the maintainer clarified the skill isn't NexusOps-specific. Reworking a general-purpose tool for one repo's convention would make it worse for the maintainer's other projects.
- *Remove the skill entirely* — considered during original `/speckit-clarify` (Q2 answer B), not chosen then and not revisited now — removal was never about scope, and the scope question is what changed.
- *Keep the skill's own independent branch-numbering logic, just changing the output format string* — moot now that the skill isn't being changed at all.

## Decision 3: `webfrontend` health check via `.WithHttpHealthCheck("/")`

**Decision**: Add `.WithHttpHealthCheck("/")` to the `webfrontend` resource definition in `NexusOps.AppHost/AppHost.cs`, targeting the Vite dev server's root path on the HTTP endpoint `AddViteApp` (from `Aspire.Hosting.JavaScript` 13.5.3) already registers.

**Rationale**: Every other resource in `AppHost.cs` that exposes HTTP already uses this exact pattern (`.WithHttpHealthCheck("/health")` for the .NET/Node services). `webfrontend` has no `/health` endpoint of its own (it's a Vite dev server, not an app with custom routes), but its root path (`/`) reliably returns `200` whenever the dev server is up and `ECONNREFUSED`/unreachable when it is not — the same liveness signal `frontend/vite.config.ts`'s dev-proxy setup already depends on implicitly. That the resource already has a working HTTP endpoint is evidenced by `server.PublishWithContainerFiles(webfrontend, "wwwroot")`, which depends on that endpoint existing.

**Alternatives considered**:
- *Add a custom `/health` route to the Vite dev server* — rejected: would require a custom Vite plugin for a dev-only resource, adding complexity with no benefit over checking `/`, which already proves the server is accepting requests.
- *No health check, amend Principle VI with a frontend-dev-server exception* — the alternative decision path from `/speckit-specify`'s clarification question; not chosen (the maintainer chose to align practice, not amend the constitution).
