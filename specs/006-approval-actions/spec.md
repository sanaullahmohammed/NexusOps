# Feature Specification: Approval-Gated Order Actions

**Feature Branch**: `006-approval-actions`

**Created**: 2026-09-02

**Status**: Draft

**Input**: User description: "Read ROADMAP.md, .specify/memory/constitution.md, and specs/005-workflow-orchestrator/ as the style and architecture reference. Using the full spec-kit workflow, create spec 006-approval-actions: `OrderActionSaga` in the workflow layer handling refund, cancel, and notify commands with a mandatory human approval gate (constitution Principle III). The saga pauses in an AwaitingApproval state after being requested; AgentHost exposes `POST /api/approvals/{id}/approve` and `POST /api/approvals/{id}/reject`; the agent tells the user the action is pending approval with a reference ID and never claims the action was executed before approval is granted. On approval, the saga executes the action and publishes a NotificationRequested event. Add a new `notification-service/` project: a minimal Node.js + TypeScript + amqplib consumer that consumes NotificationRequested and logs a simulated email; wire it into the Aspire AppHost as a new resource. Include compensation logic for partial failure (e.g., the saga's action succeeds against one dependency but fails against another, or the notification step fails after the action already executed). Add two distinct curated tools in NexusOps.Contracts for refund and cancellation (e.g. request_order_refund, request_order_cancellation) that publish to the saga and return a pending-approval reference, never claiming completion. Complete every constitution check, especially Principles I, II, III, IV, V, and VI. This spec is read+write (the first mutating saga in the system) — every acceptance scenario must make the approval gate's mandatory, blocking nature explicit and testable."

## Context

Every capability the system offers today is read-only: anomaly listing, single-order/inventory/product lookups, and the cross-service root-cause investigation (feature 005) all answer questions without changing anything. This feature introduces the system's first capability that changes real-world state — refunding or cancelling an order — and with it, the system's first mandatory human approval gate (constitution Principle III).

An operator can ask the agent to refund or cancel a specific order. The agent must never execute that request on its own judgment: it hands the request to a durable workflow that records it, gives the operator (or a separate approver) a reference to track it by, and waits — indefinitely, safely, and durably — for an explicit human decision. Only an explicit approval unblocks execution; an explicit rejection permanently and cleanly prevents it. Because a cancellation touches more than one system (the order itself, and the inventory it reserved), this feature also introduces the system's first compensation logic: if one dependency's change succeeds and a later, required dependency's change fails, the already-applied change is reversed rather than left half-done. Every terminal outcome — executed, rejected, or failed (compensated or not) — is reported through a notification event consumed by a new, minimal Notification Service.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Requesting a Refund or Cancellation Creates a Pending Action, Never an Executed One (Priority: P1)

An operator asks the agent to refund or cancel a specific order. The agent does not perform the mutation itself. It hands the request to the workflow layer, which creates a durably tracked, not-yet-executed action request and returns a reference identifier. The agent tells the operator the action is pending approval and gives them the reference — it never states or implies that the refund or cancellation has happened.

**Why this priority**: This is the load-bearing guarantee of the entire feature. If the agent could ever say "done" before a human approved anything, the approval gate would be decorative rather than mandatory, directly violating constitution Principle III. Every other story in this feature depends on this one holding.

**Independent Test**: Can be fully tested by asking the agent to refund (or cancel) a known order and confirming the response (a) includes a reference identifier, (b) explicitly states the action is pending approval, and (c) contains no language claiming the action was completed — verified for both refund and cancellation phrasing, and confirming the underlying order record is unchanged immediately afterward.

**Acceptance Scenarios**:

1. **Given** a specific, existing order ID, **When** the operator asks the agent to refund that order, **Then** the response includes a reference identifier and states the refund is pending approval, and the order's own data is unchanged immediately afterward.
2. **Given** a specific, existing order ID, **When** the operator asks the agent to cancel that order, **Then** the response includes a reference identifier and states the cancellation is pending approval, and neither the order's nor the affected inventory's data is changed immediately afterward.
3. **Given** an order ID that does not exist, **When** the operator asks to refund or cancel it, **Then** the agent reports that the order could not be found and no pending action reference is created for it.
4. **Given** a pending refund or cancellation reference has just been created, **When** the operator asks the agent about that order again before any approval decision is made, **Then** the agent continues to describe the action as pending, never as completed.

---

### User Story 2 - Approval Unblocks Execution, and Only Approval Does (Priority: P1)

A person with the authority to approve actions (the approver — who may or may not be the same person who requested the action) reviews a pending reference and submits an explicit approval. Only at this point does the requested mutation actually run against the order (and, for a cancellation, the inventory it reserved).

**Why this priority**: Without a working approval path, nothing in this feature can ever complete — the gate would block forever with no way through. Equally important, this story is where "the gate is mandatory" becomes falsifiable: the mutation must be provably absent before this decision and provably present after it.

**Independent Test**: Can be fully tested by creating a pending refund (or cancellation), confirming the order is unchanged, submitting an approval for its reference, and confirming the order (and, for cancellation, the relevant inventory) now reflects the mutation.

**Acceptance Scenarios**:

1. **Given** a pending refund reference for an existing order, **When** an approval is submitted for that reference, **Then** the order is updated to reflect the refund, where it was not before.
2. **Given** a pending cancellation reference for an existing order, **When** an approval is submitted for that reference, **Then** the order is updated to reflect the cancellation and the inventory reserved by its line items is released.
3. **Given** a reference that does not exist, **When** an approval is submitted for it, **Then** the system reports that the reference is unknown and nothing is executed.
4. **Given** a reference that has already been approved (or already rejected), **When** a second approval is submitted for the same reference, **Then** the system reports the action has already been decided and does not execute the mutation a second time.

---

### User Story 3 - Rejection Cleanly and Permanently Prevents Execution (Priority: P1)

An approver reviews a pending reference and explicitly rejects it. The requested mutation never runs, now or later, for that reference.

**Why this priority**: An approval gate that can only ever say "yes" is not a gate — proving that "no" is just as real and just as final as "yes" is what makes the mandatory nature of the gate meaningful rather than a formality.

**Independent Test**: Can be fully tested by creating a pending refund (or cancellation), submitting a rejection for its reference, and confirming the order (and any related inventory) remains unchanged — including after enough time has passed that an approval would ordinarily have completed by now.

**Acceptance Scenarios**:

1. **Given** a pending refund reference, **When** a rejection is submitted for that reference, **Then** the order remains unchanged and the reference is marked rejected.
2. **Given** a pending cancellation reference, **When** a rejection is submitted for that reference, **Then** neither the order nor its reserved inventory is changed, and the reference is marked rejected.
3. **Given** a reference that has already been rejected (or already approved), **When** a second decision of either kind is submitted for the same reference, **Then** the system reports the action has already been decided and takes no further action.

---

### User Story 4 - Partial Failure Is Compensated, Never Left Half-Done (Priority: P1)

An approved cancellation's first dependency (the order) is updated successfully, but its second, required dependency (releasing the reserved inventory) fails. The system does not leave the order looking cancelled while the inventory position silently disagrees; it reverses the change it already made and reports the action as failed, not as succeeded.

**Why this priority**: This is the specific trustworthiness property that separates a "workflow that mutates two things" from a durable saga. Explicitly called out as required by this feature's own instructions, and directly tests the constitution's expectation that a mutating saga handles failure recovery, not just the happy path.

**Independent Test**: Can be fully tested by approving a cancellation while forcing the inventory-release step to fail, then confirming the order has been reverted to its pre-cancellation state (not left cancelled) and the outcome is reported as failed rather than succeeded.

**Acceptance Scenarios**:

1. **Given** an approved cancellation whose order update succeeds but whose inventory release fails, **When** the workflow finishes handling the failure, **Then** the order is reverted to the state it was in before the cancellation began.
2. **Given** the compensation in the prior scenario has completed, **When** the operator or approver checks the outcome, **Then** it is reported as failed, not as a successful cancellation.
3. **Given** an approved refund or cancellation whose very first dependency fails (nothing was changed yet), **When** the workflow finishes handling the failure, **Then** no compensating action is attempted (there is nothing to reverse) and the outcome is reported as failed.

---

### User Story 5 - Every Terminal Outcome Produces a Notification (Priority: P2)

Whatever ultimately happens to a requested action — executed successfully, rejected, or failed (whether compensated or not) — a notification reflecting that specific outcome is generated and durably recorded as a simulated email by the Notification Service, without needing the operator to ask.

**Why this priority**: This is the feature's observability guarantee — every decision has an auditable trail — but it is not on the critical path of the gate itself: the approval-vs-rejection-vs-compensation behavior is already fully meaningful and testable (Stories 1-4) without the notification arriving. Lower priority than the gate's own correctness, still required for this feature to be complete.

**Independent Test**: Can be fully tested by driving one action reference through each of the four possible terminal outcomes (executed, rejected, failed-uncompensated, failed-and-compensated) and confirming exactly one notification record appears for each, correctly labeled with that outcome.

**Acceptance Scenarios**:

1. **Given** an approved action that executes successfully, **When** it finishes, **Then** a notification recording a successful outcome is recorded.
2. **Given** a rejected action, **When** the rejection is recorded, **Then** a notification recording the rejection is recorded.
3. **Given** an approved action that fails and is compensated, **When** it finishes, **Then** a notification recording a failed-and-reversed outcome is recorded, distinct from a successful outcome.
4. **Given** the Notification Service is temporarily unavailable when a notification is generated, **When** the service becomes available again, **Then** the notification is still recorded rather than permanently lost.

---

### User Story 6 - Refund and Cancellation Requests Route Distinctly From Everything Else (Priority: P2)

An operator's request is correctly recognized as a refund intent, a cancellation intent, or one of the system's existing read/investigation intents, and is routed to the correct distinct capability every time — without ever mistaking a request to change something for a request to merely look something up, or vice versa.

**Why this priority**: A misrouted mutation intent (treated as a read) silently fails to protect the user's expectation that something is happening; a misrouted read intent (treated as a mutation) would create an unwanted pending approval. Lower priority than Stories 1-4 because it is a routing-correctness property layered on top of behavior those stories already establish, not new execution logic of its own.

**Independent Test**: Can be fully tested by sending a mixed batch of prompts covering refund phrasing, cancellation phrasing, and the system's existing read/investigation phrasing, and confirming each is answered by the correct capability with zero change in behavior for prompts the agent already handled correctly before this feature existed.

**Acceptance Scenarios**:

1. **Given** a prompt asking to refund a specific, named order, **When** the agent responds, **Then** it uses the refund capability and not the cancellation capability or any read-only tool.
2. **Given** a prompt asking to cancel a specific, named order, **When** the agent responds, **Then** it uses the cancellation capability and not the refund capability or any read-only tool.
3. **Given** a prompt asking only to look up or investigate an order (no refund or cancellation intent), **When** the agent responds, **Then** it uses the existing read-only capability unchanged, and no pending action reference is created.

---

### User Story 7 - Pending and In-Flight Actions Survive a Process Restart (Priority: P3)

While an action is pending approval, or while an approved action is being executed, the orchestrating process restarts unexpectedly. No request, decision, or execution is silently lost: a pending reference remains approvable afterward, and an in-flight execution resolves to a definite terminal outcome rather than hanging or executing twice.

**Why this priority**: Approval decisions can arrive a long time after a request is made — potentially much longer than a single process's uptime — so this durability property matters more here than it did for feature 005's sub-15-second investigations, but it is a property of the underlying plumbing rather than new user-facing behavior, consistent with this story's priority in feature 005.

**Independent Test**: Can be fully tested by creating a pending reference, restarting the orchestrating process, and confirming the reference can still be approved or rejected normally afterward; and by approving an action, restarting mid-execution, and confirming the mutation resolves to exactly one terminal outcome, never zero and never two.

**Acceptance Scenarios**:

1. **Given** a pending reference exists and the orchestrating process is restarted, **When** the process comes back up, **Then** the reference can still be approved or rejected and resolves normally.
2. **Given** an approval has just been submitted and the orchestrating process restarts before execution finishes, **When** the process comes back up, **Then** the action reaches exactly one terminal outcome — it is never left permanently pending and never executed a second time.

---

### Edge Cases

- What happens when the operator does not specify a refund amount? → The refund defaults to the order's full total amount; the agent states this default when confirming the pending reference.
- What happens when the order targeted for refund or cancellation is already refunded, already cancelled, or otherwise not eligible? → The approval can still be submitted, but execution fails cleanly (nothing to compensate, since nothing was changed) and the outcome is reported as failed with the reason, not as a silent success.
- What happens if two decisions for the same reference are submitted at nearly the same time (e.g., one approval and one rejection racing)? → Exactly one decision is honored; the other is reported as "already decided" per User Stories 2 and 3, and no double execution or conflicting state occurs.
- What happens if the same approval request is submitted twice in a row (e.g., a retried network call)? → The second submission is either a no-op reporting "already decided" (if the first already registered) or, in the rare case both reach the system as genuinely concurrent duplicates, at most one execution ever occurs — never two.
- What happens if an approver submits a decision for a reference that never existed (typo, wrong ID)? → The system reports the reference is unknown; nothing is created, changed, or executed.
- How long can a pending action remain un-decided before it is no longer approvable? → Indefinitely, for this feature; no automatic expiration of a pending reference is introduced.
- What happens to a rejected or already-executed reference if the same order is targeted again in a brand-new request? → It is treated as an entirely new, independent action with its own reference; this feature does not deduplicate or link separate requests against the same order.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST provide a distinct capability for an operator to request a refund for a specific, existing order, optionally specifying an amount and a reason.
- **FR-002**: The system MUST provide a distinct capability for an operator to request the cancellation of a specific, existing order, optionally specifying a reason.
- **FR-003**: Neither the refund nor the cancellation capability MUST execute the requested mutation immediately; both MUST place the request into a durably tracked, not-yet-executed, pending-approval state.
- **FR-004**: Every refund or cancellation request MUST be answered with a distinct reference identifier that can later be used to approve or reject that specific request.
- **FR-005**: The agent's response to a refund or cancellation request MUST state plainly that the action is pending approval and MUST NOT state or imply that the action has already been executed.
- **FR-006**: The system MUST provide an approval mechanism that, given a valid pending reference, causes the requested mutation to execute.
- **FR-007**: The system MUST provide a rejection mechanism that, given a valid pending reference, permanently prevents that request's mutation from ever executing.
- **FR-008**: An approval or rejection submitted for a reference that does not exist, or that has already received a decision, MUST be rejected by the system without executing or re-executing any mutation.
- **FR-009**: A decision (approval or rejection) recorded for a given reference MUST be applied at most once, even if the approval or rejection request is retried, redelivered, or submitted concurrently more than once for the same reference.
- **FR-010**: On approval, cancellation execution MUST update both the order and the inventory positions reserved by that order's line items; refund execution MUST update the order.
- **FR-011**: If a cancellation's order update succeeds but its subsequent inventory release fails, the system MUST reverse (compensate) the order update rather than leave the order reflecting a cancellation the inventory data does not corroborate.
- **FR-012**: If the first dependency of an approved action's execution fails, the system MUST NOT attempt to compensate anything (nothing was yet changed) and MUST report the outcome as failed.
- **FR-013**: If an order targeted by an approved action is not eligible for that action (e.g., already refunded or already cancelled), execution MUST fail cleanly and be reported as failed rather than silently succeeding or corrupting existing order data.
- **FR-014**: Every terminal outcome of a requested action that reached a human decision point — executed, rejected, failed without compensation, or failed with compensation — MUST produce a distinct, durably delivered notification event describing that outcome. A request that never reaches `AwaitingApproval` (e.g., the named order could not be confirmed to exist during validation, per User Story 1) is not itself one of these four outcomes: the requester already receives that answer synchronously, and no approval was ever pending for a notification to meaningfully report on.
- **FR-015**: The system MUST provide a component that consumes notification events and durably records (logs) a simulated notification for each one, distinctly labeled by outcome, and MUST NOT lose a notification event that arrives while that component is temporarily unavailable.
- **FR-016**: The agent MUST route a refund intent naming a specific order to the refund capability, a cancellation intent naming a specific order to the cancellation capability, and MUST continue routing every existing read/investigation intent to its existing, unchanged capability.
- **FR-017**: Communication between the workflow layer and every domain service it mutates (including the Notification Service) MUST use the same durable, message-based mechanism already established for this system's saga-to-service communication, and MUST NOT bypass it with direct service-to-service calls.
- **FR-018**: No domain service MUST accept a refund or cancellation mutation directly from the Agent Host; every such mutation MUST arrive only via the approval-gated workflow, consistent with constitution Principle III.
- **FR-019**: A pending action request and every decision made against it MUST be durably recorded such that an unexpected restart of the orchestrating process loses neither the pending request nor an already-recorded decision, and an in-flight execution resolves to exactly one terminal outcome rather than hanging indefinitely or executing twice.
- **FR-020**: The refund and cancellation tool contracts, and the approval/rejection request-response contracts, MUST be defined in the same shared contracts package as this system's existing tool and saga-message contracts, using names and shapes that are unambiguous and distinct from every existing contract.
- **FR-021**: The components that make refund and cancellation specific to the order domain (the workflow's execution and compensation logic, the request/response contract shapes, and any order-specific routing rules) MUST be structured so that removing this domain would not require changes to the durable-orchestration or agent-hosting infrastructure this feature depends on — only the removal of this domain's own action-handling logic.

### Key Entities

- **Order Action Request**: A single, durable record of one refund or cancellation request for one order, from the moment it is requested through its final outcome. Tracks the action type, the target order, the requested details (amount for a refund, reason if given), its reference identifier, its current status (pending approval, approved-executing, executed, rejected, or failed), and, once decided, who/when it was decided and by which decision.
- **Approval Decision**: The outcome of a human's review of one Order Action Request — either an approval or a rejection — recorded at most once per request.
- **Action Execution Outcome**: The result of actually carrying out an approved action, including which dependencies succeeded, which (if any) failed, and whether a compensating reversal was applied.
- **Notification Event**: A durable record of one terminal outcome, intended for delivery to and logging by the Notification Service, describing which action, which order, and which of the terminal outcomes occurred.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of refund and cancellation requests receive a reference identifier and an explicit "pending approval" statement, with no language claiming completion, verified across a set of test requests covering both action types.
- **SC-002**: 100% of approved actions whose every dependency succeeds result in the order (and, for cancellations, the affected inventory) correctly reflecting the mutation, verified by comparing state before and after approval.
- **SC-003**: 100% of rejected actions result in zero change to order or inventory data, verified by comparing state before and after rejection, including after a delay long enough that an approval would ordinarily have completed.
- **SC-004**: When a cancellation's inventory-release step is made to fail after its order update has already succeeded, 100% of tested failure-injection runs leave the order back in its original, pre-cancellation state — never left showing a cancellation the inventory disagrees with.
- **SC-005**: Across the four possible terminal outcomes (executed, rejected, failed-uncompensated, failed-and-compensated), each produces exactly one correctly labeled notification record, verified once per outcome type.
- **SC-006**: Across a mixed batch of prompts covering refund intent, cancellation intent, and the system's existing read/investigation intents, the agent selects the correct capability 100% of the time, with zero regressions to prompts the system already handled correctly before this feature.
- **SC-007**: Simulating a restart of the orchestrating process between a request and its approval, and separately between an approval and its execution finishing, results in the request or execution reaching a normal, single, definite resolution in 100% of tested restart scenarios — never lost, never duplicated.
- **SC-008**: Submitting a duplicate or redelivered approval decision for the same reference never results in more than one execution of the underlying mutation, verified by redelivery simulation.

## Assumptions

- **Locked technical decisions** (from `ROADMAP.md`, not open for reinterpretation by this spec): the approval gate is exposed as `POST /api/approvals/{id}/approve` and `POST /api/approvals/{id}/reject` on Agent Host; approval state lives in the saga itself, not a separate store; the agent's reply pattern is "pending approval, ref #X"; there is no UI for approval — the approval endpoints are called directly (e.g., via `curl`) by whoever holds approval authority; the Notification Service is a minimal Node.js/TypeScript AMQP consumer that logs a simulated email and does nothing more; `/speckit-plan` selects the specific saga/consumer collaboration shape and message contracts within these constraints, following the precedent set by `specs/005-workflow-orchestrator`.
- "Notify" in this feature's originating instruction refers to the notification step every refund or cancellation produces once it reaches a terminal outcome (`NotificationRequested`), not a third, independently invokable curated tool — the roadmap's own tool list for this feature names only a refund tool and a cancellation tool, and this spec follows that scope exactly.
- A refund with no amount specified defaults to the order's full total amount; this default is stated back to the requester when the pending reference is created.
- No expiration or time-limit is placed on a pending action reference in this feature; an approval or rejection may arrive at any point after the request, including well after the process that created it has restarted.
- Approval and rejection are not gated by any authentication or authorization mechanism in this feature — anyone able to call the approval endpoints is treated as authorized to decide, consistent with this project's current no-UI, credential-light operating mode (`specs/003-authn-authz-identity` is a separate, not-yet-implemented concern) and with this feature's Notification Service and approval flow both being demonstrated via direct API calls rather than a built UI.
- A cancellation's second dependency is the inventory reserved by that order's line items, released (restocked) on cancellation; a refund has only the order itself as a dependency and therefore has no compensation scenario of its own — the "notify" and Notification Service requirements apply identically to both, but the compensation requirement (FR-011, User Story 4) is exercised specifically through cancellation's two-dependency shape.
- The requester of an action (the operator, via the agent) and the approver of that action (whoever calls the approval endpoints) may be the same person or different people; this feature does not distinguish or restrict who may approve based on who requested.
- Every acceptance scenario and success criterion in this feature is verified against a real, running system per this project's established practice (`ROADMAP.md` Prompt 3/4's live-verification precedent); this spec does not require building a separate simulation harness.
- Frontend changes are out of scope; this feature targets the agent's tool layer, the workflow orchestrator, the domain services' mutation-handling consumers, the approval endpoints, and the new Notification Service, consistent with `frontend/` and `NexusOps.Server` remaining scaffold artifacts.
- The Evaluation runner (`ROADMAP.md` Prompt 5, feature 007) is this project's designated home for automated, repeatable regression coverage of tool-selection prompts (SC-006); this feature verifies SC-006 manually, following the same precedent `specs/005-workflow-orchestrator` set for its own routing success criterion.
