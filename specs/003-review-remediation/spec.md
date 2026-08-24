# Feature Specification: Review Remediation

**Feature Branch**: `003-review-remediation`

**Created**: 2026-08-24

**Status**: In Progress

**Input**: User description: "Address 19 code review findings across order anomaly contracts, session management correctness, health probes, CI coverage and documentation drift."

## Context

A code review of `master` at commit `7332fb3` raised 19 findings. Each was verified against the source before being accepted. Sixteen were confirmed as stated, two were real defects filed against the wrong evidence, and one did not reproduce. Two further defects were found during verification and are carried here as findings 20 and 21.

Finding 21 is a direct consequence of finding 10. A High-severity advisory had been sitting in the dependency graph unreported because no build in CI ever surfaced its warning as anything anyone read, and no test run existed to draw attention to it. It was found within minutes of the test project first compiling.

This feature is remediation, not new capability. It corrects behaviour that already contradicts specifications 001 and 002, so several requirements below are amendments to those specs rather than new ground. Where a fix changes a stated requirement, the originating spec is amended in the same commit as the code, so no commit leaves the specifications contradicting the implementation.

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Anomaly Queries Return Honest Results (Priority: P1)

An operator asks the agent for orders with failed payments. The agent returns orders that actually have failed payments. When the same operator later asks for missing orders, they get a different set — not the same order relabelled.

**Why this priority**: The current implementation derives the anomaly label from the query string rather than from the order, so a single cancelled order reports as `missing` in one request and `payment-failed` in the next. Any operator decision made on that output is founded on a fabricated distinction.

**Independent Test**: Can be fully tested by calling `GET /orders/anomalies` three times with each of the three status filters and asserting that the returned order sets are disjoint and that each order's `anomalyType` is identical to the value it reports when no filter is supplied.

**Acceptance Scenarios**:

1. **Given** the seeded order set, **When** `GET /orders/anomalies?status=payment-failed` is called, **Then** every returned order has `anomalyType` of `payment-failed` and no order returned by `?status=missing` appears in the result.
2. **Given** an order that appears in an unfiltered anomaly query, **When** the same order is fetched under any status filter that matches it, **Then** its `anomalyType` and `severity` are unchanged.
3. **Given** any anomaly result, **When** the caller inspects a returned record, **Then** it carries the customer, amount, expected delivery date and line items specified by the order service contract.
4. **Given** an anomaly result containing line items, **When** the agent cross-references it against `get_inventory_alerts`, **Then** the correlation can be performed on SKU without any additional per-order request.

---

### User Story 2 — Conversations Survive a Store Outage (Priority: P1)

An operator is midway through a multi-turn investigation when Redis becomes briefly unavailable. Their next message is answered without conversation history, but their session identifier is unchanged — so once the store recovers, the conversation continues where it left off rather than having silently restarted.

**Why this priority**: The conversation store currently reports a cache miss and a store failure identically, so every request during an outage mints a fresh session identifier. The operator is not told, and the thread is lost. This is the live feature's most damaging failure mode.

**Independent Test**: Can be fully tested by driving `POST /api/chat` against a conversation store stubbed to throw, and asserting that the returned session ID equals the supplied one across successive requests.

**Acceptance Scenarios**:

1. **Given** a caller-supplied session ID and a store that is failing, **When** a chat request is processed, **Then** the response returns the same session ID the caller supplied.
2. **Given** a caller-supplied session ID that has genuinely expired, **When** a chat request is processed, **Then** a new session ID is minted and returned, as specified by 002 FR-007.
3. **Given** a request with no session ID, **When** it is processed, **Then** no history read is attempted against the store and no `session.history_loaded` event is emitted.
4. **Given** a `session.created` log event, **When** an operator searches for the corresponding `session.degraded` event, **Then** both records carry the same session token and can be joined.

---

### User Story 3 — The Build Catches Regressions (Priority: P1)

A contributor changes session trimming behaviour and opens a pull request. CI fails, naming the broken expectation.

**Why this priority**: There is no test project in the repository, so the CI test step has always exited successfully without executing anything. Every defect in this remediation reached `master` through a green build. Verification must exist before the behavioural changes land, or they inherit the same absence of coverage.

**Independent Test**: Can be fully tested by deliberately inverting a trimming assertion and confirming `dotnet test` fails.

**Acceptance Scenarios**:

1. **Given** the solution filter used by CI, **When** `dotnet test` runs, **Then** it executes a non-zero number of tests.
2. **Given** the AppHost project, **When** CI runs, **Then** the AppHost is compiled, so a dependency update that breaks it fails the build rather than passing unobserved.

---

### User Story 4 — Deployed Services Report Their Health (Priority: P2)

An operator deploys the system outside Development. The orchestrator's readiness probes resolve, and the health payload matches what the service contracts document.

**Why this priority**: Four services currently register `/health` only in Development while the AppHost probes and waits on it unconditionally, so a non-Development start cannot reach a healthy state. Separately, the probe returns plain text where all three service contracts document a JSON body.

**Independent Test**: Can be fully tested by starting a service with `ASPNETCORE_ENVIRONMENT=Production` and asserting `/health` returns HTTP 200 with a JSON body.

**Acceptance Scenarios**:

1. **Given** any environment, **When** `/health` is requested on the Order, Inventory, Product or Server service, **Then** it returns HTTP 200.
2. **Given** a health request, **When** the response is inspected, **Then** its content type is JSON and its body matches the shape documented in the service contract.
3. **Given** a chat request that the client abandons, **When** the client disconnects, **Then** in-flight downstream tool calls are cancelled rather than run to completion.

---

### Edge Cases

- A prompt that is null, empty or entirely whitespace must be rejected before a session is minted and before the model is invoked, so a malformed request leaves no residue in the store and incurs no model cost.
- A `Session:SlidingExpirationMinutes` of zero or less must prevent startup. It currently throws on every store write instead, where it is swallowed and misreported as a Redis connection fault.
- An invalid `status` value on an anomaly query must reach the agent as a correctable argument error, not as a service outage — otherwise the agent has no signal to retry with a valid value.
- When the agent fails on a request that minted its own session ID, 002 FR-005 requires the user turn to be persisted. The identifier under which it was persisted must reach the caller, or the turn is unreachable for the lifetime of its TTL.
- Anomaly severity and days-overdue derive from the current date. Seed data fixed to absolute past dates causes both to drift further from plausibility every day the repository exists.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: An order's anomaly classification MUST be a property of the order, not of the query that retrieved it. The same order MUST report the same `anomalyType` under every filter that matches it and when no filter is supplied. *(Amends 001; finding 3.)*
- **FR-002**: The anomaly payload MUST carry the customer identifier, total amount, expected delivery date and line items, matching the shape already published in the order service contract. *(Implements the existing 001 contract; findings 1 and 2.)*
- **FR-003**: The seeded order set MUST contain at least one order for each anomaly reason, so every documented filter value returns a non-empty, distinct result. *(Amends 001; finding 3.)*
- **FR-004**: Anomaly severity MUST carry information beyond the anomaly type. `missing` and `payment-failed` are always high severity; `delayed` is high when more than seven days overdue and medium otherwise. *(Amends 001.)*
- **FR-005**: Seeded delivery dates MUST be expressed relative to the current date, and date-dependent logic MUST resolve the current date through an injectable time source so that derived values remain plausible indefinitely and remain deterministic under test. *(Amends 001; finding 20.)*
- **FR-006**: A downstream HTTP 400 MUST be surfaced to the agent as a correctable argument error naming the valid values, distinct from the service-unavailable result used for transport failures. Client errors MUST NOT be logged at error severity. *(Finding 11.)*
- **FR-007**: The chat endpoint MUST reject a null, empty or whitespace-only prompt with HTTP 400 before minting a session or invoking the agent, and MUST declare that response in its OpenAPI metadata. *(Implements the existing 002 chat contract; finding 5.)*
- **FR-008**: Session configuration MUST be validated at startup. Both `MaxTurns` and `SlidingExpirationMinutes` MUST be positive integers, and a violation MUST prevent the application from starting. *(Amends 002 FR-008; finding 4.)*
- **FR-009**: The conversation store MUST report a retrieval miss distinctly from a retrieval failure. A miss retains the existing 002 FR-007 behaviour of minting a fresh session; a failure MUST preserve the caller's session identifier and process the request statelessly. *(Amends 002 FR-007 and FR-010; finding 12.)*
- **FR-010**: A request that supplies no session identifier MUST NOT attempt a history read, and MUST NOT emit a history-loaded event. *(Amends 002 FR-012; finding 13.)*
- **FR-011**: When the agent fails, the response MUST carry the active session identifier so that the user turn persisted under 002 FR-005 remains reachable. *(Amends 002 chat contract; finding 14.)*
- **FR-012**: All session lifecycle events MUST identify the session with the same derived token, so that events emitted from different components can be correlated. The token MUST NOT expose a recoverable portion of the raw identifier. *(Amends 002 FR-012; finding 9.)*
- **FR-013**: Health endpoints MUST be registered in all environments, and MUST return a JSON body matching the service contracts. Liveness endpoints remain Development-only. *(Amends 001 contracts; findings 7 and 17.)*
- **FR-014**: Every tool handler MUST accept and propagate a cancellation token to its downstream call, and MUST NOT reclassify a cancellation as a service failure. *(Finding 15.)*
- **FR-015**: The frontend dev server MUST resolve a usable proxy target when the orchestrator's environment variables are absent. *(Finding 16.)*
- **FR-016**: The repository MUST contain an automated test suite that executes as part of CI, covering the behaviour specified in FR-001 through FR-012. *(Finding 10.)*
- **FR-017**: Every project subject to automated dependency updates MUST be compiled by CI. *(Finding 8.)*
- **FR-018**: Credentials MUST have a documented configuration path that does not route them into a tracked file. *(Finding 6.)*
- **FR-019**: Project documentation MUST agree with the implementation on workflow inventory, order status vocabulary, tool names, delivered versus planned capability, and specification status. *(Finding 18.)*
- **FR-020**: No project in the solution filter MAY resolve a package carrying a known high or critical severity advisory. Transitive vulnerabilities are pinned within their existing major version where a patched release exists. *(Finding 21.)*

### Key Entities

- **AnomalyReason**: An explicit, nullable property of an order recording why it is anomalous — `Delayed`, `Missing` or `PaymentFailed`. Distinct from `OrderStatus`, which continues to describe lifecycle position. An order with no anomaly reason is not anomalous.
- **HistoryResult**: The outcome of a conversation history retrieval — the turns retrieved, plus whether the read found a session, found none, or could not reach the store. Replaces the bare turn list, which could not express the third case.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Each of the three anomaly filters returns a non-empty result set, and the three sets are pairwise disjoint.
- **SC-002**: An anomaly result can be joined to an inventory alert on SKU with no additional request per order.
- **SC-003**: Across a simulated store outage of any length, a caller supplying a session ID receives that same ID in every response.
- **SC-004**: A null, empty or whitespace prompt returns HTTP 400 with no model invocation and no store write.
- **SC-005**: A non-positive `MaxTurns` or `SlidingExpirationMinutes` prevents startup with a message naming the offending key.
- **SC-006**: `dotnet test` in CI executes a non-zero number of tests, and inverting any single behavioural assertion causes the build to fail.
- **SC-007**: Every project reachable by Dependabot is compiled by at least one CI job.
- **SC-008**: `/health` returns HTTP 200 with a JSON body under `ASPNETCORE_ENVIRONMENT=Production`.
- **SC-009**: `npm run dev` in `frontend/` starts with a resolved proxy target and no orchestrator environment variables present.
- **SC-010**: No statement in README.md or CLAUDE.md contradicts the implementation at the close of this feature.
- **SC-011**: `dotnet list package --vulnerable --include-transitive` reports no vulnerable package for any project in the solution filter, and a Release build emits zero warnings.

## Clarifications

### Session 2026-08-24

- Should the remediation be implemented or only triaged first? → Triaged first into a reviewed plan; implementation proceeds in five batches with a checkpoint after each.
- How should anomaly types be modelled, given the domain has no notion of a missing or payment-failed order? → Add an explicit nullable `AnomalyReason` to the order model and derive the anomaly type from it. `OrderStatus` remains a lifecycle enum and the order-details `status` string is unchanged.
- Where should the remediation be recorded, given several fixes change requirements owned by specs 001 and 002? → Both. Feature 003 records the work; 001 and 002 are amended wherever a fix changes a stated requirement, so the original specs never contradict the shipped code.
- Which side should give on the health-probe conflict — the services that hide `/health` outside Development, or the AppHost that probes it unconditionally? → The services. `/health` is registered in all environments, matching what the Agent Host already does; access is restricted at the ingress rather than by deleting the endpoint. `/alive` remains Development-only.
- Only one non-delayed anomalous order exists in the seed set, but three anomaly reasons need coverage. → Add `ORD-0011` so all three filters return distinct, non-empty results.
- What severity rule should replace the current constant? → `missing` and `payment-failed` are always high; `delayed` scales with days overdue, high past seven days and medium below.
- Should the stale seed dates be fixed in this feature? → Yes. Seed dates become relative to the current date and date logic resolves through an injectable time source.
- What test framework and layout? → xUnit, a single `NexusOps.Tests` project at the repository root, registered in both `NexusOps.sln` and `NexusOps.deployable.slnf`.
- How wide should the line-ending policy go, given 28 files are already committed with CRLF? → Narrow. Pin `frontend/package-lock.json` only; full-tree normalisation is recorded as a known issue rather than performed here.

## Assumptions

- The AppHost remains a development-time orchestrator. FR-013 is nonetheless treated as a defect because the AppHost is the only consumer of these probes and its behaviour is environment-independent, and because the probes are documented in the service contracts without an environment qualifier.
- Adding `ORD-0011` does not break existing consumers; no test or contract asserts a fixed order count. CLAUDE.md's stated count is updated as part of FR-019.
- Amending 002 FR-007 is an extension rather than a reversal. FR-007 addresses expired, unknown and malformed identifiers; a store outage is a fourth case it never contemplated.
- Finding 19 of the review ("no .NET SDK available") did not reproduce — the SDK is present at version 10.0.400 — so it yields no requirement. Its practical consequence is that every batch in this feature is compiled and tested before being reported complete.

## Known Issues Not Addressed

- **Mixed line endings.** 28 tracked files across `NexusOps.AppHost/`, `NexusOps.Server/`, `frontend/` and `aspire.config.json` are committed with CRLF while the rest of the tree uses LF. Only `frontend/package-lock.json` is pinned here, because full normalisation would rewrite all 28 files in a single commit and redirect `git blame` on each. Worth a deliberate, separate decision.
