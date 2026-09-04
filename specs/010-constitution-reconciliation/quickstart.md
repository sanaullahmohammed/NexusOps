# Quickstart: Verifying Constitution Reconciliation

Three independent checks, one per tension — each matches its User Story's Independent Test in spec.md. No automated test covers any of these (see plan.md's Technical Context); all three are manual, one-time verifications appropriate to a governance/config fix.

## 1. Notification gating (User Story 1) — confirm no regression

No code changes ship for this item, so this is a regression check, not new-behavior verification.

```bash
dotnet test NexusOps.deployable.slnf
```

Confirm the `OrderActionSaga` lifecycle tests in `NexusOps.Tests` and (if Docker is available) the refund/cancellation scenarios in `NexusOps.IntegrationTests` still pass unchanged — proving notification publishing behavior is untouched.

Then re-read `.specify/memory/constitution.md`'s Principle III against `NexusOps.WorkflowOrchestrator/OrderAction/OrderActionSaga.cs`: confirm `NotificationRequested` is published only from `HandleRejectAsync` and `HandleExecutionCompletedAsync`, both reachable only after a human `Reject`/`Approve` decision.

## 2. Branch naming (User Story 2) — CLAUDE.md documents the actual convention, no skill change

No code/skill change ships for this item (revised 2026-09-04 — `conventional-branch` is a general-purpose skill, out of scope for NexusOps-specific rework).

Re-read CLAUDE.md's flagged-tensions entry #2 and confirm it states: NexusOps branches are created via `/speckit-git-feature` (already `###-short-name`-compliant), and `/conventional-branch` is explicitly out of scope for this repo's branch naming rather than a silently competing convention. Optionally confirm `/speckit-git-feature` still produces a `###-short-name` branch (unchanged behavior — not something this feature touches):

```bash
# from repo root, on a throwaway base
/speckit-git-feature   # request e.g. "a chore to update dependencies"
```

```bash
git checkout 010-constitution-reconciliation
git branch -D <throwaway-branch>
```

## 3. `webfrontend` health check (User Story 3) — live dashboard check

```bash
dotnet run --project NexusOps.AppHost
```

Open the Aspire dashboard. Confirm `webfrontend` now shows a real (non-static) health status — healthy while the Vite dev server is running. Stop the `webfrontend` resource from the dashboard (or kill the underlying `npm run dev` process) and confirm its status flips to unhealthy, then restart it and confirm it recovers.

## Final check — CLAUDE.md / ROADMAP.md consistency

- CLAUDE.md's "Flagged: Constitution Tensions" section shows all three items marked resolved with rationale (SC-001).
- ROADMAP.md's Status checklist shows Prompt 8 as `[x]` (SC-005).
- `.specify/memory/constitution.md` is unchanged (no wording needed edits, per research.md).
