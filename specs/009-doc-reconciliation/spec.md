# Feature Specification: Documentation Reconciliation

**Feature Branch**: `009-doc-reconciliation`

**Created**: 2026-09-03

**Status**: Draft

**Input**: User description: "Reconcile README.md, CLAUDE.md, and ROADMAP.md against the actual implemented codebase, per ROADMAP.md's Prompt 7 (Final doc reconciliation). Documentation-only — no application code changes. Fix verified factual drift; flag (do not resolve) constitution tensions."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A new contributor trusts the docs to reflect the real system (Priority: P1)

A developer who has never seen NexusOps reads README.md and CLAUDE.md to understand what's built, what's planned, and how to run and test the system. Every claim they read — project list, CI behavior, example queries, roadmap status — matches what they find when they open the repo, run `dotnet test`, or inspect `.github/workflows/ci.yml`.

**Why this priority**: This is the entire point of Prompt 7. A doc that confidently states something false (a promised capability that doesn't exist, a feature marked "planned" that's actually done) is worse than no doc, because it costs the reader time to discover the mismatch themselves.

**Independent Test**: Pick any factual claim in README.md or CLAUDE.md that references a file, project, test count, CI step, or capability, and verify it against the live repo. All should match.

**Acceptance Scenarios**:

1. **Given** README.md's Project Structure block, **When** compared against the actual top-level directories, **Then** every directory referenced in code (including `NexusOps.IntegrationTests/`) is listed.
2. **Given** README.md's Roadmap section, **When** compared against what's actually implemented, **Then** no implemented capability (e.g. the integration test suite) is still listed under "Planned."
3. **Given** README.md's Example Queries table, **When** checked against the curated tool set and seed data, **Then** every example query maps to a capability that actually exists, using the real ID formats (`ORD-####`, `SKU-####`) used elsewhere in the docs.
4. **Given** CLAUDE.md's Solution Filter section, **When** compared against `NexusOps.deployable.slnf` and `NexusOps.sln`, **Then** the stated project count and the list of exclusions are both correct and internally consistent with the rest of CLAUDE.md.
5. **Given** CLAUDE.md's Repository Structure block, **When** compared against the actual top-level directories, **Then** `NexusOps.Evaluation/` and `specs/` both appear.
6. **Given** CLAUDE.md's CI table and Solution Filter section, **When** compared against `.github/workflows/ci.yml`, **Then** the "Build integration tests" compile-only step and the `integration-tests` job's `timeout-minutes: 30` are both reflected.
7. **Given** CLAUDE.md's header "Active Feature Plan" link, **When** this feature is implemented, **Then** it points at this feature's own plan rather than a completed prior feature.
8. **Given** ROADMAP.md's "definition of done," **When** compared against how the project is actually run locally (documented consistently elsewhere as `dotnet run --project NexusOps.AppHost`, since the local `aspire` CLI doesn't work against this project's Aspire package version), **Then** the definition of done uses the same command.

---

### User Story 2 - A reader can see the architecture as it actually stands (Priority: P2)

A developer reads README.md's architecture diagram to understand what talks to what. The diagram includes every live resource the Aspire AppHost actually provisions — not just the ones present when the diagram was first drawn.

**Why this priority**: The diagram is the single highest-leverage doc artifact (one picture, read first) but currently omits three resources the AppHost provisions today (Redis, the BFF server, and the frontend), understating the real topology.

**Independent Test**: Compare the diagram's nodes against `NexusOps.AppHost`'s resource registrations. Every resource should appear (or its omission should be a deliberate, stated scope decision, e.g. "scaffold placeholders are shown dimmed" — not a silent gap).

**Acceptance Scenarios**:

1. **Given** the Mermaid architecture diagram in README.md, **When** compared against `NexusOps.AppHost/AppHost.cs`'s resource registrations, **Then** Redis (the session store), `NexusOps.Server`, and the frontend all appear.

---

### User Story 3 - Governance tensions are visible, not silently resolved (Priority: P3)

A maintainer reviewing the constitution wants to know where current practice has drifted from a stated principle, without a documentation pass quietly rewriting the constitution (or the practice) to make the tension disappear. ROADMAP.md's Prompt 7 explicitly requires these to be flagged, not fixed, since resolving them is a governance decision for a human, not a doc-sync task.

**Why this priority**: Lowest priority because it changes no factual claim — it only makes an existing, real tension visible. Getting this wrong (silently "fixing" a principle, or fixing the practice without a deliberate decision) would overstep this feature's scope.

**Independent Test**: Read the flagged-tensions location in the docs and confirm each of the three known tensions is described accurately, with a pointer to the principle and the practice that diverges from it, and no code or constitution change accompanies it.

**Acceptance Scenarios**:

1. **Given** Constitution Principle III ("notifications" listed among mutations requiring the approval gate) and the fact that `NotificationRequested` is published unconditionally on every terminal outcome (the *action* is gated; the notification of its outcome is not), **When** a reader looks for this tension, **Then** it is stated plainly in one documented location, without the constitution or the notification behavior being changed.
2. **Given** the constitution's `###-short-name` branch-naming mandate and the repo's actual use of `chore/...`-style branches (plus a shipped `conventional-branch` skill), **When** a reader looks for this tension, **Then** it is stated plainly, without either convention being changed.
3. **Given** Principle VI's "no service ships without health checks registered ... via `WithHttpHealthCheck`" and `webfrontend`'s AppHost registration lacking one, **When** a reader looks for this tension, **Then** it is stated plainly, without a health check being added to `webfrontend` or the principle being narrowed.

### Edge Cases

- A claim in the known-drift list turns out to have already been fixed by other work since the list was compiled: the doc is left as-is (no false "fix" applied), and the entry is treated as verified-correct rather than forced to match a stale finding.
- A doc claim not on the known-drift list is found to be inaccurate during verification: it is corrected too — the known-drift list is a starting point, not an exhaustive boundary, since Prompt 7's own scope is "reconcile the docs," not "resolve this specific list."
- Two docs disagree with each other (not just with the code) on the same fact: both are brought to match the verified-true state, not just made to agree with each other.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: CLAUDE.md's Solution Filter section MUST state the correct total project count in `NexusOps.sln` and the correct, complete list of exclusions from `NexusOps.deployable.slnf` (`frontend.esproj` and `NexusOps.IntegrationTests`), consistent with what CLAUDE.md's own CI table already says elsewhere.
- **FR-002**: README.md's Roadmap section MUST NOT list "Integration test suite" under Planned; it MUST appear under Implemented, consistent with how CLAUDE.md already documents it.
- **FR-003**: README.md's Project Structure block MUST include an entry for `NexusOps.IntegrationTests/`.
- **FR-004**: CLAUDE.md's Repository Structure block MUST include entries for `NexusOps.Evaluation/` and `specs/`.
- **FR-005**: README.md's Example Queries table MUST only contain queries that map to an actually-curated tool and actually-seeded data, using the real ID formats (`ORD-####` for orders, `SKU-####` for inventory/products) used consistently elsewhere in the docs; it MUST NOT reference a customer-lookup capability that doesn't exist.
- **FR-006**: README.md's Mermaid architecture diagram MUST include Redis, `NexusOps.Server`, and the frontend as nodes, positioned to reflect their real role (session store; BFF/static host; client-facing UI, scaffold status noted if still applicable).
- **FR-007**: CLAUDE.md's CI documentation (table and/or Solution Filter section) MUST mention the dotnet job's compile-only "Build integration tests" step and the `integration-tests` job's `timeout-minutes: 30`, matching `.github/workflows/ci.yml`.
- **FR-008**: README.md's Testing section MUST document how to build/run `NexusOps.IntegrationTests` (or explicitly point to where that's documented, e.g. CLAUDE.md) and MUST NOT understate `ci.yml` as two commands running "on every push and pull request" when the real workflow has four jobs with different trigger conditions.
- **FR-009**: CLAUDE.md's header "Active Feature Plan" link MUST point at this feature's plan (`specs/009-doc-reconciliation/plan.md`) once this feature's plan exists, replacing the stale link to the completed feature 008.
- **FR-010**: ROADMAP.md's "definition of done" MUST reference the same local run command (`dotnet run --project NexusOps.AppHost`) that README.md and CLAUDE.md already use, rather than `aspire start`, which is documented elsewhere in ROADMAP.md itself as not working in this environment.
- **FR-011**: The three constitution tensions (Principle III / notification gating, branch-naming convention vs. actual practice, Principle VI / `webfrontend` health check) MUST each be stated in a single, clearly-labeled documentation location, describing the principle, the actual practice, and the fact that this feature intentionally leaves the tension unresolved. No constitution principle, AppHost registration, or notification-publishing behavior may change as part of this feature.
- **FR-012**: CLAUDE.md's Current Build State section MUST gain an entry for this feature (009) once implemented, following the existing pattern used by features 005–008.
- **FR-013**: Every other factual claim in README.md, CLAUDE.md, and ROADMAP.md discovered during verification to be inaccurate (not limited to the items above) MUST also be corrected, since this feature's scope is full reconciliation, not just the pre-identified list.
- **FR-014**: This feature MUST NOT modify any file outside README.md, CLAUDE.md, ROADMAP.md, `.specify/memory/constitution.md`'s own content (constitution itself stays unchanged; only *other* docs may reference it), and this feature's own `specs/009-doc-reconciliation/` artifacts. No application, test, CI, or configuration code changes.

### Key Entities

*(Not applicable — this feature has no data model; it modifies existing Markdown documentation files.)*

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A reader auditing README.md, CLAUDE.md, and ROADMAP.md against the live repository (project list, test counts, CI steps, example capabilities, architecture diagram) finds zero factual discrepancies, down from the 11 verified in the 2026-09-03 audit.
- **SC-002**: All three known constitution tensions are discoverable by a reader in one documented location each, with zero of them silently resolved (verified by diffing `.specify/memory/constitution.md` and the affected application code before/after — neither changes).
- **SC-003**: ROADMAP.md's Prompt 7 can be checked off as complete, since its stated done-criteria (accurate Current Build State, roadmap items marked done, architecture diagram matches reality, quickstart command verified, fintech-ops framing consistent, constitution violations flagged not fixed) are all met.
- **SC-004**: `dotnet test NexusOps.deployable.slnf` and the frontend/notification-service CI checks remain green after this feature (documentation-only change; no build or test regression is possible, but this confirms nothing was accidentally touched outside scope).

## Assumptions

- The 2026-09-03 audit's 11 factual-drift findings and 3 constitution tensions are the primary known scope, but are re-verified (not blindly trusted) against the current repo state as part of this feature, per FR-013.
- "Flag, not fix" for constitution tensions means: describe the tension in prose in the docs; it does not mean opening a separate governance decision process — that's left to the user, matching ROADMAP Prompt 7's own instruction.
- The architecture diagram's inclusion of Redis/Server/frontend can note their current status (e.g. frontend remaining a scaffold) without that becoming a second class of "planned vs. implemented" claim needing its own reconciliation — status notes elsewhere in the same docs already cover that distinction.
- No new spec numbering collisions: this is feature 009, following 008 (order-investigation-outbox) sequentially.
