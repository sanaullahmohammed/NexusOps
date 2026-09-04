# Implementation Plan: Constitution Reconciliation

**Branch**: `010-constitution-reconciliation` | **Date**: 2026-09-03 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/010-constitution-reconciliation/spec.md`

## Summary

Close ROADMAP.md's Prompt 8 by resolving the three governance tensions CLAUDE.md's "Flagged: Constitution Tensions" section has been carrying since feature 009. All three decisions (made during `/speckit-specify`/`/speckit-clarify`) align practice to the constitution as written:

1. **Notification gating** — reading `OrderActionSaga.cs`/`OrderActionSagaState.cs`/`OrderAction.cs` directly during this planning pass showed the code *already* complies: `NotificationRequested` is only ever published after a human `Reject` or `Approve` decision (`Rejected`, `Completed`, `Failed`-via-execution); the one path with no prior human touch — a pre-approval validation failure — already sends no notification and mutates nothing. **No application code changes.** This is a documentation correction: CLAUDE.md's "unconditionally on every terminal outcome" claim is replaced with an accurate description.
2. **Branch naming** — Revised 2026-09-04: `conventional-branch` implements the general [Conventional Branch](https://conventional-branch.github.io) spec, reused across the maintainer's other projects, not authored for NexusOps — reworking it is out of this feature's scope. **No skill code changes.** `.claude/skills/conventional-branch/SKILL.md` stays as-is; the fix is documentation only, in CLAUDE.md's flagged-tensions entry: NexusOps branches are created via `/speckit-git-feature` (already `###-short-name`-compliant), and `/conventional-branch` is noted as an out-of-scope general-purpose tool, not a second, conflicting convention.
3. **webfrontend health check** — `NexusOps.AppHost/AppHost.cs` gains `.WithHttpHealthCheck("/")` on the `webfrontend` resource, targeting the HTTP endpoint `Aspire.Hosting.JavaScript`'s `AddViteApp` already registers.

CLAUDE.md's flagged-tensions section is updated per item (resolved, with rationale), and ROADMAP.md's Prompt 8 checkbox is marked complete. `.specify/memory/constitution.md` needs **no wording change** — none of the three tensions turned out to require one; the gaps were in practice/tooling and in CLAUDE.md's own description, not in the constitution's text.

## Technical Context

**Language/Version**: C# / .NET 10 (AppHost.cs edit only); Markdown (doc updates) — no new language introduced.

**Primary Dependencies**: `Aspire.Hosting.JavaScript` 13.5.3 (already referenced; provides `AddViteApp`'s `WithHttpHealthCheck` extension point). No new package references.

**Storage**: N/A — no new or changed persisted state.

**Testing**: Manual verification only, consistent with existing precedent:
  - webfrontend health check (SC-004): verified via the Aspire dashboard with `dotnet run --project NexusOps.AppHost` — no automated test exists for any resource's dashboard health status, and `NexusOps.IntegrationTests`' `WorkflowOrchestratorFixture` explicitly *removes* `webfrontend` from its topology before boot (npm tooling not needed once `agent-host` is gone), so it structurally cannot cover this.
  - Branch naming (SC-003): no code/skill change to verify — confirmed by re-reading CLAUDE.md's updated entry and confirming `/speckit-git-feature` still produces `###-short-name` (unchanged behavior).
  - Notification gating (SC-002): no behavior changed, so existing coverage (`NexusOps.Tests`' `OrderActionSaga` lifecycle tests, `NexusOps.IntegrationTests`' refund/cancellation scenarios) already covers it; re-run to confirm no regression.

**Target Platform**: Existing repo, dev-only scope (no Kubernetes/production target exists yet).

**Project Type**: Documentation/governance + single-line config change across an existing monorepo — no new project, service, or module.

**Performance Goals**: N/A.

**Constraints**: Touches only `NexusOps.AppHost/AppHost.cs`, `CLAUDE.md`, `ROADMAP.md`; `.claude/skills/conventional-branch/SKILL.md`, `NexusOps.WorkflowOrchestrator`, and `notification-service` are explicitly NOT touched (verified already-compliant, or out of scope per the 2026-09-04 revision); `.specify/memory/constitution.md` is touched only if a later review finds real ambiguity remaining (current assessment: it will not need to change).

**Scale/Scope**: 3 flagged tensions, 3 files.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- [x] **I. Cognition/Durability boundary** — N/A. No Agent Host or WorkflowOrchestrator logic is added or moved; the notification-gating resolution is a documentation correction with zero code change.
- [x] **II. Curated tool boundaries** — N/A. No new domain capability or tool is introduced.
- [x] **III. Approval-gated side effects** — This feature exists to verify and document compliance with this exact principle. Verified: every notification `OrderActionSaga` actually sends already carries a prior human `Approve`/`Reject` decision as its consent; nothing mutates, and nothing is sent, on the one path with no human decision. No new mutation path is introduced.
- [x] **IV. Message-driven service integration** — N/A. No saga-to-service communication is added or changed.
- [x] **V. Domain pluggability** — The one code change (webfrontend's health check) is orchestration-core wiring (observability), not domain logic, and does not require any change to AgentHost/WorkflowOrchestrator internals.
- [x] **VI. Observability first** — This feature closes an existing gap against this exact principle: `webfrontend` gains its missing `WithHttpHealthCheck`, bringing every AppHost resource into compliance.

No violations. Complexity Tracking table is not needed.

## Project Structure

### Documentation (this feature)

```text
specs/010-constitution-reconciliation/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md         # Phase 1 output (no new entities — documents why)
├── quickstart.md         # Phase 1 output
└── tasks.md              # Phase 2 output (/speckit-tasks — not created by /speckit-plan)
```

No `contracts/` directory: this feature exposes no new API, tool, message contract, or other external interface. It corrects documentation and changes one AppHost resource registration; no internal developer-tooling skill is modified.

### Source Code (repository root)

This feature modifies existing files only — no new project, service, test suite, or source directory is created:

```text
NexusOps.AppHost/
└── AppHost.cs                        # Add .WithHttpHealthCheck("/") to the webfrontend resource

CLAUDE.md                             # "Flagged: Constitution Tensions" section updated per item;
                                       # Active Feature Plan pointer updated (this file)
ROADMAP.md                            # Prompt 8 checkbox marked complete + completion note
```

`.claude/skills/conventional-branch/SKILL.md` is explicitly NOT touched (2026-09-04 revision — it's a general-purpose skill, out of NexusOps's scope; see spec.md's Clarifications).

**Structure Decision**: No source tree changes apply — this is a targeted edit to three existing files (`CLAUDE.md`, `ROADMAP.md`, `NexusOps.AppHost/AppHost.cs`) plus this feature's own `specs/010-constitution-reconciliation/` artifacts. None of the template's Option 1/2/3 layouts apply.

## Complexity Tracking

*No Constitution Check violations — table not needed.*
