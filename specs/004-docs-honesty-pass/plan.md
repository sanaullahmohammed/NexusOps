# Implementation Plan: Documentation Honesty Pass

**Branch**: `004-docs-honesty-pass` | **Date**: 2026-09-01 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/004-docs-honesty-pass/spec.md`

**Note**: This template is filled in by the `/speckit-plan` command. See `.specify/templates/plan-template.md` for the execution workflow.

## Summary

Add a "Why this project" section to README.md framing NexusOps as a fintech-operations-to-agentic-AI proof of concept, and add explicit status marking (Implemented vs. Planned) to the Tech Stack table, architecture diagram, and Saga Designs section so no planned component (Workflow Orchestrator/MassTransit, RabbitMQ, PostgreSQL, Notification Service, Evaluation runner) can be mistaken for running code. This is a documentation-content change only — no source code, project files, or CI configuration are touched. CLAUDE.md is touched only if cross-checking against README.md/ROADMAP.md surfaces a genuine status inconsistency.

## Technical Context

<!--
  ACTION REQUIRED: Replace the content in this section with the technical details
  for the project. The structure here is presented in advisory capacity to guide
  the iteration process.
-->

**Language/Version**: N/A — Markdown + Mermaid diagram syntax, no application code

**Primary Dependencies**: N/A — edits existing prose/tables/diagrams in `README.md`; no new tooling introduced

**Storage**: N/A

**Testing**: Manual review against spec Success Criteria SC-001–SC-004 (readability of framing, correct classification of every planned component, cross-doc consistency, no regression of existing honesty statements); no automated test suite applies to prose

**Target Platform**: GitHub-rendered Markdown (README.md as viewed on the repository's GitHub page, including Mermaid diagram rendering)

**Project Type**: Documentation-only change — no application, library, or service code affected

**Performance Goals**: N/A

**Constraints**: MUST NOT modify any source code, `.csproj`/`.esproj`, or CI/workflow file (FR-009); the Mermaid diagram MUST remain valid, GitHub-renderable syntax after status markers are added

**Scale/Scope**: `README.md` (primary edits: new section, table, diagram, saga section); `ROADMAP.md` (verify/preserve consistency, no content contradicting current status); `CLAUDE.md` edited only if an inconsistency is found during cross-check (none expected — its Tech Stack table already marks planned rows)

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

All six core principles govern the runtime architecture (Agent Host, Workflow Orchestrator, message bus, domain services). This feature introduces zero code, so each gate is satisfied vacuously — recorded explicitly rather than skipped, per the Development Workflow's compliance requirement.

- [x] **I. Cognition/Durability boundary** — N/A. No Agent Host or Workflow Orchestrator code is touched; only documentation prose describing the (unchanged) architecture.
- [x] **II. Curated tool boundaries** — N/A. No tool definitions are added, removed, or changed.
- [x] **III. Approval-gated side effects** — N/A. No mutation logic exists in this feature; it does not affect `OrderActionSaga` or its approval gate.
- [x] **IV. Message-driven service integration** — N/A. No saga-to-service communication code is added or changed.
- [x] **V. Domain pluggability** — N/A. This feature does not modify `NexusOps.AppHost`, `NexusOps.AgentHost`, or `NexusOps.WorkflowOrchestrator`; it documents them more precisely.
- [x] **VI. Observability first** — N/A. No new service is introduced, so no health checks or OTEL wiring are required.

## Project Structure

### Documentation (this feature)

```text
specs/[###-feature]/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output (/speckit-plan command)
├── data-model.md        # Phase 1 output (/speckit-plan command)
├── quickstart.md        # Phase 1 output (/speckit-plan command)
├── contracts/           # Phase 1 output (/speckit-plan command)
└── tasks.md             # Phase 2 output (/speckit-tasks command - NOT created by /speckit-plan)
```

### Source Code (repository root)

```text
README.md     # primary edits: "Why this project" section, Tech Stack status
              # column, architecture diagram status markers, Saga Designs
              # status notes
ROADMAP.md    # verified for consistency only; edited only if a contradiction
              # with README.md is found (none expected — content already accurate)
CLAUDE.md     # verified for consistency only; edited only if a contradiction
              # is found (none expected — Tech Stack table already marks
              # planned rows)
```

**Structure Decision**: No source code directories are affected. This feature edits repository-root documentation files only, per FR-009's scope constraint. No `src/`, `tests/`, `backend/`, or `frontend/` structure applies.

## Complexity Tracking

No constitution violations — all six gates are satisfied vacuously (documentation-only change, zero code). Table not applicable.
