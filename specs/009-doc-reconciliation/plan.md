# Implementation Plan: Documentation Reconciliation

**Branch**: `009-doc-reconciliation` | **Date**: 2026-09-03 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/009-doc-reconciliation/spec.md`

## Summary

Reconcile README.md, CLAUDE.md, and ROADMAP.md with the actual state of the codebase (project list, CI workflow, seed data, AppHost topology, constitution text), per ROADMAP.md's Prompt 7. Fix every verified factual discrepancy; separately, add a single flagged-tensions note describing three places where current practice diverges from a constitution principle, without changing the constitution or the code that diverges from it. Documentation-only — no `.cs`, `.ts`, `.yml`, or config file changes.

## Technical Context

**Language/Version**: N/A — Markdown documentation only

**Primary Dependencies**: N/A

**Storage**: N/A

**Testing**: Manual verification — every edited claim is re-diffed against its source of truth (`.slnf`/`.sln` contents, `.github/workflows/ci.yml`, `AppHost.cs` resource registrations, `constitution.md` text, seed data constants) after editing. No automated test exists for documentation accuracy; `dotnet test NexusOps.deployable.slnf` is re-run only to confirm this change didn't accidentally touch anything that affects the build (it shouldn't, since no non-Markdown file changes).

**Target Platform**: N/A

**Project Type**: Documentation change to an existing multi-service .NET/Node/React repository

**Performance Goals**: N/A

**Constraints**: Edits are confined to README.md, CLAUDE.md, ROADMAP.md, and this feature's own `specs/009-doc-reconciliation/` artifacts (spec §FR-014). `.specify/memory/constitution.md` itself is read but not edited — flagged tensions are recorded in the other docs, referencing the constitution, not by rewriting it.

**Scale/Scope**: 3 files edited (README.md, CLAUDE.md, ROADMAP.md) across roughly 14 discrete factual corrections (FR-001–FR-010, FR-012, FR-013) plus one flagged-tensions addition (FR-011) referenced from two of the three files.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

This feature makes no application, service, tool, saga, or AppHost changes, so principles I–V do not apply to it directly — it only *documents* the system those principles already govern. Principle VI's observability mandate governs services, not docs, and is likewise not implicated by this feature's own changes. The one place this feature touches constitution-adjacent territory is FR-011: it *describes* three existing gaps between the constitution and practice, but explicitly does not resolve them (spec Assumptions, FR-014) — recording a known gap is not itself a violation.

- [x] **I. Cognition/Durability boundary** — N/A. No Agent Host or WorkflowOrchestrator code changes.
- [x] **II. Curated tool boundaries** — N/A. No new tools, no `NexusOps.Contracts` changes.
- [x] **III. Approval-gated side effects** — N/A. No mutations introduced. (The existing notification-gating tension is flagged, per FR-011, not fixed — resolving it is out of scope by design.)
- [x] **IV. Message-driven service integration** — N/A. No saga-to-service communication changes.
- [x] **V. Domain pluggability** — N/A. No orchestration-core or domain-pack changes.
- [x] **VI. Observability first** — N/A. No new services; the existing `webfrontend` health-check gap is flagged, per FR-011, not fixed.

No violations. Complexity Tracking is not needed.

## Project Structure

### Documentation (this feature)

```text
specs/009-doc-reconciliation/
├── plan.md              # This file
├── quickstart.md         # How to re-verify the reconciled docs
├── checklists/
│   └── requirements.md
└── tasks.md              # Phase 2 output (/speckit-tasks command)
```

`research.md`, `data-model.md`, and `contracts/` are omitted: there are no unresolved technical unknowns (every fact was verified against the live repo before the spec was written — see spec.md's Assumptions), no data entities (this feature edits prose, not a data model), and no external interface contracts (Markdown files are not an API).

### Source Code (repository root)

```text
README.md      # Project Structure, Roadmap, Example Queries, architecture diagram, Testing, Key Design Decisions
CLAUDE.md      # Header (Active Feature Plan), Repository Structure, CI table, Solution filter, Current Build State
ROADMAP.md     # Definition of done
```

No `src/`, `backend/`, `frontend/`, or `tests/` code trees are touched — this is the "single project" template option, degenerate to a documentation-only edit set. `.specify/memory/constitution.md` is read as a reference source but not written to.

**Structure Decision**: Direct edits to the three root-level Markdown files named in the spec, plus this feature's own `specs/009-doc-reconciliation/` artifacts. No other files change.

## Complexity Tracking

*Not applicable — no constitution violations.*
