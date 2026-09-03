# Feature Specification: Order Investigation Saga Reliability Fix

**Feature Branch**: `008-order-investigation-outbox`

**Created**: 2026-09-03

**Status**: Draft

**Input**: User description: "Fix a real, reproducible race condition in OrderInvestigationSaga (feature 005) uncovered by feature 007-adjacent Aspire.Hosting.Testing integration tests. OrderInvestigationSaga's Initially(When(Requested)) handler publishes BeginInvestigationFanOut to kick off the fan-out consumer before the saga's own row-creating INSERT commits to PostgreSQL. Because the fan-out consumer's order lookup is a single sequential call, its OrderFindingReported reply is consistently the first of the three *FindingReported events to come back — early enough to reliably race ahead of the saga's own INSERT commit. When it wins that race, OrderInvestigationSaga's OnMissingInstance(m => m.Discard()) for the OrderReported event silently discards it, so the investigation never receives its OrderFinding, never finalizes, and the caller times out. OrderActionSaga (feature 006) already hit this identical class of problem and was fixed with a transactional EF Core outbox. Apply the equivalent fix to OrderInvestigationSaga."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A root-cause investigation always finalizes (Priority: P1)

A user (via the AI agent, or any other caller of the investigation capability) asks for a specific order's root-cause investigation. Today, this request can silently hang forever from the caller's point of view — no error, no degraded result, just a timeout — because of a timing race inside the workflow orchestrator that has nothing to do with whether the order, inventory, or product data was actually available. After this fix, every investigation request reaches a real, timely, correct terminal outcome (complete, degraded, or failed) that reflects what the three sources actually reported, never a caller-side timeout caused by the orchestrator's own internal bookkeeping.

**Why this priority**: This is the entire reason the fix exists. Without it, the investigation capability is unreliable in exactly the environment it is meant to work in (a freshly-started, real deployment), independent of any actual service outage — an investigation can fail even when Order, Inventory, and Product are all healthy and answered correctly.

**Independent Test**: Send an investigation request for an order that exists and has valid line items, against a freshly-started orchestrator, and confirm a `Complete` result is returned within the caller's normal timeout budget, repeatably (not just "usually").

**Acceptance Scenarios**:

1. **Given** a freshly-started workflow orchestrator and a healthy Order, Inventory, and Product service, **When** an investigation request is sent for an order that exists with valid line items, **Then** the caller receives a `Complete` result within the same timeout budget the capability was originally specified to meet (spec 005, SC-006: a few seconds), every time, not intermittently.
2. **Given** a freshly-started workflow orchestrator, **When** an investigation request is sent for an order and one of Inventory or Product is genuinely unreachable, **Then** the caller receives a `Degraded` result naming the unavailable source, not a timeout — the same outcome spec 005 already promised, now actually reachable reliably from a cold start.
3. **Given** an investigation is in flight, **When** the workflow orchestrator process is restarted immediately after accepting the request (before it finalizes), **Then** no request is left permanently stuck: either the original caller times out cleanly (as before, an accepted pre-existing limitation) or a redelivered fan-out is handled correctly without double-processing or a discarded finding for the *new* attempt.

---

### Edge Cases

- What happens when an investigation is retried by the message broker (e.g., a redelivered `BeginInvestigationFanOut` after a broker blip) after the original attempt already finalized? Findings for the earlier, now-finalized instance must continue to be discarded safely (existing, unchanged behavior) — this fix must not reintroduce double-processing for that case.
- What happens if the underlying database is briefly unavailable at the exact moment the saga's own row would be persisted? The request should fail or retry visibly (via the existing message-retry policy), not silently drop a finding.
- What happens to an investigation started before this fix is deployed and still in flight during a deployment? Out of scope — this is a POC with no in-flight-migration requirement (consistent with existing project assumptions).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The workflow orchestrator MUST guarantee that a fan-out request for a new investigation is not visible to (processable by) any consumer until the investigation's own durable record has been committed, so that no reply to that fan-out can ever race ahead of the record it needs to attach to.
- **FR-002**: The workflow orchestrator MUST NOT silently discard a valid finding for an investigation that is genuinely still in progress. A finding may only be discarded when it truly belongs to no known investigation (already finalized and reaped, or never requested) — never merely because the durable record had not yet finished committing.
- **FR-003**: Existing, correct behavior MUST be preserved: a finding that arrives for an investigation that has already reached a terminal state (complete, degraded, or failed) continues to be safely ignored, exactly as before.
- **FR-004**: The fix MUST apply uniformly to all three source findings (order, inventory, product) — the mechanism must not depend on which finding happens to arrive first, since that ordering is an implementation detail of the fan-out, not a guarantee.
- **FR-005**: The fix MUST NOT change the investigation capability's external contract: the tool name, its request/response shape, and the meaning of `Complete`/`Degraded`/`Failed` all remain exactly as specified in feature 005.
- **FR-006**: The fix MUST NOT introduce a new approval gate, a new curated tool, or any mutation — this remains a read-only capability (Constitution Principle III is unaffected).

### Key Entities

- **Investigation record**: The durable, per-request state an investigation's outcome is built up from (which sources have reported, what they reported, and the investigation's current status). Already exists (feature 005); this fix changes only the durability/visibility guarantee around when downstream work related to a record may begin, not the record's shape.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An investigation request against a freshly-started deployment (no prior warm-up) succeeds with a correct, timely result on the first attempt, 100% of the time across repeated trials — not merely "most of the time."
- **SC-002**: The fix introduces no observable change in behavior for any investigation that was already completing correctly before this fix (same result shape, same approximate latency).
- **SC-003**: A caller of the investigation capability never observes a timeout that is attributable to the orchestrator's own internal timing rather than an actual unavailable or slow source.

## Assumptions

- This fix targets `OrderInvestigationSaga` only. `OrderActionSaga` (feature 006) already has the equivalent protection and is out of scope for changes, though it remains the reference precedent for the mechanism used here.
- The fix is expected to be a configuration/wiring change to the existing saga's message-bus endpoint and its durable-storage schema (an additive schema change), not a change to the saga's decision logic, its message contracts, or any consumer's business logic.
- As with feature 005's original scope, there is no requirement to preserve or migrate any investigation that was already in flight at the moment this fix is deployed.
- This is a reliability fix to already-shipped functionality, not a new user-facing capability — no new spec-kit-visible feature is being added to the product surface.
