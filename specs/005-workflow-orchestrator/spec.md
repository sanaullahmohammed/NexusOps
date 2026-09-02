# Feature Specification: Order Root-Cause Investigation Workflow

**Feature Branch**: `005-workflow-orchestrator`

**Created**: 2026-09-01

**Status**: Draft

**Input**: User description: "Read ROADMAP.md, .specify/memory/constitution.md, specs/002-session-management/ as a style reference, and the existing specs/003-review-remediation/ and specs/004-docs-honesty-pass/ to avoid numbering collisions. Using the full spec-kit workflow, create spec 005-workflow-orchestrator: a new `NexusOps.WorkflowOrchestrator` host using MassTransit v8 + RabbitMQ, with `OrderInvestigationSaga` state persisted in PostgreSQL through EF Core using optimistic concurrency. Add RabbitMQ and Postgres to the Aspire AppHost. The saga fans out AMQP read requests to Order, Inventory, and Product services in parallel, aggregates results, and returns partial results with degradation flags if a service fails or times out. Preserve the existing Direct-path `investigate_order_anomaly` tool and its contract. Add a distinct Saga-path tool named `investigate_order_root_cause` in NexusOps.Contracts for a specific order; AgentHost publishes its command and awaits the saga result using the response strategy chosen in plan.md. Update agent routing instructions to distinguish anomaly listing from cross-service root-cause investigation. No approval gate belongs in this spec. Complete every constitution check, especially Principles I, II, IV, V, and VI; explicitly resolve how Order-specific saga code remains outside the domain-agnostic orchestration core."

## Context

Today, an operator can ask the agent to list orders in an abnormal state (`investigate_order_anomaly`, Direct path) and can ask about a single order, a single SKU's stock, or a single product in isolation. None of these answer "why" a specific order is broken — that requires manually cross-referencing order, inventory, and product data by hand.

This feature adds a second, distinct investigation capability: given one specific order, gather everything relevant to explaining its problem — the order itself, the stock position of the items on it, and the product details for those items — from all three domain services, and hand back one consolidated picture. Because it reaches across three services instead of one, this capability must tolerate one (or more) of those services being slow or unavailable without failing the whole investigation or inventing an answer.

This is a read-only investigation. It does not change any data, and it introduces no approval step — approval-gated mutation (refunds, cancellations) is out of scope and reserved for a later feature.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Cross-Service Root-Cause Investigation (Priority: P1)

An operator asks the agent to investigate why a specific order is having problems (e.g., "why is ORD-1002 stuck?" or "investigate the root cause for order ORD-1002"). Instead of answering from order data alone, the agent gathers the order's own details, the current stock position for every item on the order, and the product details for those items, then returns one consolidated explanation that draws on all three.

**Why this priority**: This is the entire value of the feature. Without cross-service aggregation, an operator must ask three separate questions and manually connect the dots — exactly the manual correlation work this feature exists to remove.

**Independent Test**: Can be fully tested by asking the agent to investigate a known order with an out-of-stock item, and confirming the response cites both the order's own condition and the specific item's stock shortfall, without the operator asking a follow-up question.

**Acceptance Scenarios**:

1. **Given** a specific, existing order ID, **When** the operator asks the agent to investigate why that order is problematic, **Then** the response reflects information from the order, the stock levels of its items, and the product details of its items — not order data alone.
2. **Given** an order whose items are all in healthy stock and match their product records, **When** the operator investigates it, **Then** the response reflects that no inventory- or product-side cause was found, rather than omitting those checks silently.
3. **Given** an order ID that does not exist, **When** the operator asks to investigate it, **Then** the agent reports that the order could not be found rather than returning a partial or fabricated investigation.

---

### User Story 2 - Investigation Survives a Degraded Service (Priority: P1)

While investigating an order, the Inventory service (or any one of the three) is temporarily slow or unreachable. The operator still receives a useful answer built from the services that did respond, along with a clear statement that the inventory picture is incomplete and why.

**Why this priority**: A three-service fan-out fails more often than a single-service read purely because there are three chances for something to go wrong. Without graceful degradation, one flaky dependency would make the whole feature unreliable — equally critical to the aggregation itself.

**Independent Test**: Can be fully tested by stopping one downstream service, investigating an order, and confirming the response still contains findings from the two healthy services plus an explicit note identifying which source was unavailable.

**Acceptance Scenarios**:

1. **Given** the Inventory service is stopped or times out, **When** the operator investigates an order, **Then** the response includes the order and product findings, plus a clear indication that the inventory portion could not be completed.
2. **Given** two of the three services are unavailable, **When** the operator investigates an order, **Then** the response includes whatever the one healthy service returned, plus a clear indication of which two portions are missing.
3. **Given** all three services are unavailable, **When** the operator investigates an order, **Then** the agent reports that the investigation could not be completed rather than returning an empty or misleadingly confident answer.
4. **Given** a degraded investigation was returned, **When** the operator re-asks after the failed service recovers, **Then** a fresh investigation returns the complete picture (the earlier degradation does not persist as a stale cached result).

---

### User Story 3 - Anomaly Listing and Root-Cause Investigation Stay Distinct (Priority: P1)

An operator asks a broad question ("show me all delayed orders") versus a narrow one about a single order ("why is ORD-1002 delayed?"). The agent routes the first to the existing fast anomaly-listing capability and the second to the new cross-service investigation, without the operator needing to know that these are two different underlying mechanisms.

**Why this priority**: These two capabilities look similar to a casual reader ("anomaly" vs. "root cause") but have very different cost and behavior — one is a fast single-service read, the other fans out across three services and tolerates partial failure. Routing the wrong kind of question to the wrong capability either wastes the expensive path on a simple list or gives a single-order question a shallow, single-service answer. This must hold from day one, so it shares P1 with the two capabilities it protects.

**Independent Test**: Can be fully tested by sending a batch of prompts that mix broad anomaly-listing phrasing with specific per-order root-cause phrasing, and confirming each is answered by the correct capability with no change in behavior for prompts the agent already handled correctly before this feature existed.

**Acceptance Scenarios**:

1. **Given** a prompt asking to list or filter orders in an abnormal state generally, **When** the agent responds, **Then** it uses the existing anomaly-listing capability and its response shape is unchanged from before this feature.
2. **Given** a prompt naming one specific order and asking why it is broken, stuck, or failing, **When** the agent responds, **Then** it uses the new cross-service investigation capability.
3. **Given** a prompt that names a specific order but only asks for its basic status (not "why"), **When** the agent responds, **Then** it uses the existing single-order lookup rather than triggering a full cross-service investigation.

---

### User Story 4 - Investigation Reliability Under Process Restart (Priority: P3)

While an investigation is in flight (services have been asked, results are still being collected), the orchestrating process restarts unexpectedly. No investigation is silently lost: the operator either receives a completed or clearly-failed result rather than the request hanging indefinitely with no eventual answer.

**Why this priority**: Read-only operations are already forgiving of loss (the operator can just ask again), so this is lower value than the three P1 stories above. It matters for overall trustworthiness of the durable-execution layer this feature introduces, and for laying groundwork the mutation-based sagas of a later feature will depend on more heavily.

**Independent Test**: Can be fully tested by starting an investigation, restarting the orchestrating process mid-flight, and confirming the operator's request eventually resolves to either a complete result or a clear failure — never an indefinite hang with no response.

**Acceptance Scenarios**:

1. **Given** an investigation has been started and the orchestrating process is restarted before it completes, **When** the process comes back up, **Then** the in-flight investigation's state is not lost, and the request eventually resolves.
2. **Given** an investigation record exists, **When** two updates to it are attempted at the same time (e.g., two of the three service responses arriving in quick succession), **Then** both updates are reflected correctly and neither silently overwrites the other's data.

---

### Edge Cases

- What happens when the requested order exists but has no line items? → The order and product/inventory checks report "nothing to check" rather than an error; the investigation still completes.
- What happens when an order references a SKU that no longer exists in the Product or Inventory service? → That line item is reported as "reference not found" within its portion of the investigation; it does not cause the whole investigation to fail, and it is distinct from a service being unavailable.
- What happens if a downstream service responds successfully but after the investigation has already given up waiting on it (a very late response)? → The late response is discarded; the investigation has already returned its (degraded) result and does not retroactively revise an answer the operator has already seen.
- What happens if the operator sends the same root-cause investigation request twice in quick succession for the same order? → Each request is treated as an independent investigation with its own result; this feature does not deduplicate concurrent requests for the same order.
- What happens when the operator asks to investigate an order that IS anomalous (e.g., payment failed) but all three domain services are healthy? → The investigation completes fully and the response explains the cause using the order's own anomaly information plus any corroborating inventory/product findings, without needing degradation language.
- How does the investigation distinguish "the order doesn't exist" from "the Order service is unavailable"? → These are reported differently: a confirmed not-found is a completed finding ("no such order"), while an unavailable Order service is a degraded/failed source, because the underlying question ("does this order exist?") could not be answered either way in the second case.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST provide a distinct investigation capability, separate from the existing anomaly-listing capability, that accepts a single specific order identifier and returns a consolidated finding drawing on that order's own data, the stock position of its line items, and the product details of its line items.
- **FR-002**: The existing anomaly-listing capability (`investigate_order_anomaly`) MUST remain unchanged in name, input, and response shape. This feature MUST NOT alter its behavior, and every acceptance scenario that passed for it before this feature MUST continue to pass unmodified.
- **FR-003**: Gathering the order's data, the inventory data for its line items, and the product data for its line items MUST happen concurrently rather than one after another, so that one slow source does not multiply the total wait time by three.
- **FR-004**: If one or two of the three sources fail, time out, or reference a SKU the source doesn't recognize, the investigation MUST still return the findings that did succeed, together with an explicit indication of which source(s) are incomplete and why (unavailable/timed out vs. not found).
- **FR-005**: If all three sources fail or time out, the investigation MUST report that it could not be completed. It MUST NOT return an empty or fabricated result that could be mistaken for "no problem found."
- **FR-006**: A confirmed "no such order" result MUST be reported distinctly from "the order source was unavailable" — the former is a completed finding, the latter is a degraded/failed source, per the Edge Cases section.
- **FR-007**: The agent's routing instructions MUST distinguish between three request shapes: (a) a broad request to list orders in an abnormal state (existing anomaly-listing capability, unaffected), (b) a request to investigate why one specific, named order is broken (new cross-service investigation), and (c) a request for a specific order's basic status only, with no "why" (existing single-order lookup, unaffected).
- **FR-008**: Every investigation request MUST produce a durable, trackable record of that investigation's progress, so that a source's completion or failure can be recorded and detected regardless of the order in which the three sources respond.
- **FR-009**: When two updates to the same investigation record are attempted concurrently, the system MUST NOT allow one update to silently discard the other; a conflicting concurrent update MUST be detected and safely resolved (retried or reconciled) rather than corrupting the record.
- **FR-010**: An investigation in progress MUST NOT be lost if the process performing it restarts; the investigation MUST resume or safely fail, and the operator's request MUST eventually resolve (per User Story 4) rather than hang indefinitely.
- **FR-011**: A source that responds after the investigation has already returned its result to the operator (i.e., after the investigation's timeout has elapsed) MUST have its late response discarded rather than used to retroactively alter a result already delivered.
- **FR-012**: This capability MUST require no human approval step before returning its result — it is read-only and completes automatically, consistent with all other Direct- and Saga-path read operations in the system. It MUST NOT be gated the way a mutating operation (e.g., a future refund or cancellation capability) would be.
- **FR-013**: Communication between the investigation workflow and each of the Order, Inventory, and Product services MUST use the same durable, message-based communication mechanism already established for this system's orchestration layer, MUST NOT bypass it with direct service-to-service calls, and MUST carry the delivery guarantees (retry, dead-letter) that mechanism provides.
- **FR-014**: The investigation capability's request/response contract MUST be defined in the same shared contracts package as the existing anomaly-listing tool, using a name and shape that is unambiguous and distinct from it.
- **FR-015**: The components that make this capability specific to the order domain (the investigation's data-gathering logic, the request/response contract shape, and any order-specific routing rules) MUST be structured so that removing this domain would not require changes to the durable-orchestration or agent-hosting infrastructure that this feature adds — only the removal of this domain's own investigation logic.

### Key Entities

- **Root-Cause Investigation**: A single, durable record of one investigation-in-progress or investigation-result for one order. Tracks which of the three sources (order, inventory, product) have responded, succeeded, failed, or timed out, and the point at which the investigation is considered finished (all three responded, or the overall wait budget elapsed).
- **Source Finding**: The outcome of asking one of the three services for its portion of the investigation. Has a status (succeeded / not found / unavailable / timed out) and, when successful, the data that service contributed.
- **Consolidated Result**: The single answer returned to the operator for a completed investigation: the order's own finding, the findings for each line item's inventory position, the findings for each line item's product details, and an overall completeness indicator (complete vs. degraded, and if degraded, which source(s) are missing).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An operator investigating a specific order's root cause receives one consolidated answer covering order, inventory, and product context, without needing to ask three separate follow-up questions — verified against a set of investigation prompts covering orders with different anomaly types.
- **SC-002**: When exactly one of the three sources is unavailable, the operator still receives the findings from the other two, with a plainly stated indication of what is missing — verified by simulating a single-service outage.
- **SC-003**: When all three sources are unavailable, the operator is told the investigation could not be completed, never a result that looks complete but isn't — verified by simulating a full outage.
- **SC-004**: 100% of existing anomaly-listing test prompts continue to produce the same tool selection and response shape as before this feature, with zero regressions.
- **SC-005**: Across a mixed batch of test prompts covering broad anomaly listing, narrow "why is this order broken" investigation, and plain single-order status lookups, the agent selects the correct capability for each prompt shape 100% of the time.
- **SC-006**: A normal (non-degraded) investigation returns to the operator within 3 seconds of the request under typical local development conditions, not noticeably slower than asking about order, inventory, and stock individually would be combined.
- **SC-007**: Simulating a restart of the orchestrating process mid-investigation results in the operator's request eventually resolving (complete or clearly failed) rather than an indefinite hang, in 100% of tested restart scenarios.

## Assumptions

- **Locked technical decisions** (from `ROADMAP.md`, not open for reinterpretation by this spec): durable messaging is MassTransit v8 over RabbitMQ; investigation state is persisted in PostgreSQL via Entity Framework Core with optimistic concurrency; these are added to the Aspire AppHost as first-class resources alongside the existing Redis resource. `/speckit-plan` selects the specific response-delivery mechanism AgentHost uses to await the saga's result (e.g., request/response correlation vs. polling) within these constraints.
- The per-source wait budget (how long the investigation waits for any one of Order/Inventory/Product before treating it as timed out) and the overall investigation budget are implementation parameters tuned in planning, not fixed by this spec; SC-006's "a few seconds" is the user-facing expectation they must satisfy.
- "Investigate the root cause" always targets exactly one order per request; investigating multiple orders in one request is out of scope for this feature.
- The new capability is read-only: it does not create, modify, or cancel any order, inventory, or product record. No compensation logic is required because there is nothing to compensate.
- No approval gate applies to this feature, per explicit instruction; Principle III of the constitution (approval-gated mutation) does not apply because nothing here mutates state. A future feature (approval-gated actions) will introduce mutating, approval-gated capabilities separately.
- A "line item" on an order references a product by SKU; inventory and product lookups for an investigation are keyed by the SKUs found on the target order.
- The existing `investigate_order_anomaly` tool, its Direct-path execution model, and its response contract are treated as frozen for the duration of this feature — any change to them would be a breaking change to feature 001/003's contract and is explicitly out of scope.
- Concurrent identical requests (the same order investigated twice at once) each run to completion independently; the cost of a duplicate in-flight investigation is accepted rather than engineered away, matching this system's existing "simplest strategy first" precedent for concurrency (see `specs/002-session-management`'s last-write-wins decision).
- The Notification Service, refund/cancellation capabilities, and the human approval workflow are out of scope for this feature and are reserved for a subsequent feature (per `ROADMAP.md` Prompt 4).
- SC-005's 100% routing-selection claim is verified manually in this feature (`tasks.md` T056). Automated, repeatable regression coverage for tool-selection prompts is deferred to feature 007's Evaluation runner (`ROADMAP.md` Prompt 5) — that is the project's designated home for this kind of test, not a gap introduced by this feature.
- Frontend changes are out of scope; this feature targets the agent's tool layer and the new orchestration host only, consistent with `frontend/` and `NexusOps.Server` remaining scaffold artifacts.
