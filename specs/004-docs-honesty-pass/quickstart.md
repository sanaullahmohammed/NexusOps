# Quickstart: Validating the Documentation Honesty Pass

This feature has no build/run steps — it is a documentation edit. Use this checklist to validate the finished edit against the spec's Success Criteria.

## Validation steps

1. **SC-001 — Why this project reads clearly**
   Open `README.md` and read only the new "Why this project" section. Confirm you can restate, without reading any other section: what NexusOps is a proof-of-concept for, and how at least two of (multi-source aggregation, maker-checker approval, compensation on partial failure) map to the system's design.

2. **SC-002 — Planned vs. implemented is scannable**
   Cover all prose paragraphs in `README.md`. Looking only at:
   - the Tech Stack table's Status column,
   - the architecture diagram's node labels/styling,
   - the Saga Designs section's status lines,

   confirm all of: Workflow Orchestrator, MassTransit, RabbitMQ, PostgreSQL, Notification Service, and the Evaluation runner are marked `Planned` — and no implemented component (Agent Host, domain services, Redis session store) is marked `Planned`.

3. **SC-003 — Cross-document consistency**
   For each component in the data-model.md "Consistency ledger" table, confirm the status recorded in `README.md` matches `ROADMAP.md` and `CLAUDE.md`. Any mismatch found must be fixed in the same change.

4. **SC-004 — No regression**
   Diff the new `README.md` against its pre-edit version. Confirm every existing "planned, not implemented" or "scaffold only" statement is still present (verbatim or equivalently), and none was softened, removed, or reworded to imply completion.

5. **FR-009 — Scope guard**
   Run `git diff --stat` against the base branch and confirm only `README.md`, `ROADMAP.md`, and (optionally) `CLAUDE.md` appear — no `.cs`, `.csproj`, `.esproj`, `.ts`, `.tsx`, or CI workflow files.

## Out of scope for this quickstart

No `dotnet run`, `aspire start`, or `npm` commands apply — this feature does not touch runnable code.
