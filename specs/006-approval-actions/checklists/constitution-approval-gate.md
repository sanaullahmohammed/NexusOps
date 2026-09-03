# Specification Checklist: Constitution Compliance & Approval-Gate Correctness

**Purpose**: Validate that spec.md/plan.md/data-model.md's *requirements* — not the implementation — are complete, unambiguous, and consistent enough to make the approval gate's mandatory nature and the constitution's principles verifiable, not merely asserted.
**Created**: 2026-09-02
**Feature**: [spec.md](../spec.md) | [plan.md](../plan.md)
**Depth**: Standard | **Audience**: Reviewer (PR)

## Requirement Completeness

- [x] CHK001 - Is a requirement present that the agent's tool response, not just the endpoint response, must never claim completion? [Completeness, Spec §FR-005]
- [x] CHK002 - Are requirements defined for what happens when a decision is submitted for a reference that exists but was created via a different action type (e.g., approving a cancellation reference that only ever supported refund fields)? [Gap] — N/A: `ApprovalReference` alone identifies a decided-or-not saga instance regardless of `ActionType`; no cross-type ambiguity exists in the data model (data-model.md `OrderActionSagaState.ActionType` is set once at creation and never reinterpreted).
- [x] CHK003 - Are requirements defined for the format/uniqueness guarantees of the "reference identifier" itself? [Completeness, Spec §FR-004] — Assumptions section ties it to the saga's own correlation mechanism; data-model.md confirms it is the `CorrelationId` GUID, so uniqueness is inherited from MassTransit's own guarantee rather than independently asserted — acceptable, but the spec itself doesn't say so explicitly.
- [x] CHK004 - Are compensation requirements scoped to name which specific dependency is compensable, rather than leaving "compensation" generic? [Clarity, Spec §FR-011] — Yes, FR-011 and User Story 4 name the order-then-inventory ordering explicitly.

## Requirement Clarity

- [x] CHK005 - Is "pending approval" language requirement (FR-005) specific enough to be objectively checkable, or does it rely on subjective judgment of phrasing? [Measurability, Spec §FR-005] — Objectively checkable: FR-005 forbids stating/implying completion and requires the reference be included; both are mechanically verifiable against a transcript.
- [x] CHK006 - Is "cleanly" in FR-013 ("execution MUST fail cleanly") quantified, or is it a vague adjective? [Ambiguity, Spec §FR-013] — Acceptable: the surrounding sentence clarifies "rather than silently succeeding or corrupting existing order data," which is a testable negative condition even without a positive metric.
- [x] CHK007 - Is the refund-amount default ("full total amount") stated as a hard rule or as an example? [Clarity, Spec Assumptions] — Stated as a hard rule with no hedging language.

## Requirement Consistency

- [x] CHK008 - Do FR-014's enumerated terminal outcomes match the four outcomes User Story 5 and data-model.md's `OrderActionExecutionOutcome`/notification `Outcome` vocabulary use? [Consistency, Spec §FR-014] — Now consistent after this session's remediation (FR-014 revised to scope explicitly to decided requests, matching the implementation's actual notification-publishing boundary).
- [x] CHK009 - Does the Assumptions section's "no authentication on approval endpoints" statement conflict with any FR implying an authorization check? [Consistency] — No conflict: FR-008/FR-009 gate on *reference validity and decision state*, never on caller identity; the Assumption is the sole word on authorization and nothing elsewhere contradicts it.
- [x] CHK010 - Is the "approver may or may not be the requester" assumption reflected consistently across every user story that mentions who acts on a reference? [Consistency] — Yes; User Stories 2/3 both say "an approver" generically, never assuming identity with the requester.

## Approval-Gate Scenario Coverage

- [x] CHK011 - Are requirements defined for a decision submitted for a reference belonging to an order that was independently mutated by some other means between request and decision? [Gap] — Out of scope by spec.md Assumptions ("this feature does not distinguish or restrict... "); the only mutation path in this system *is* this saga, so the scenario is currently unreachable — acceptable, but worth a one-line explicit exclusion if a second mutation path is ever added.
- [x] CHK012 - Are requirements defined for concurrent decisions on the *same* reference (race), separately from duplicate/retried *identical* decisions? [Coverage, Spec Edge Cases] — Yes, both are separately called out in Edge Cases and both map to distinct FRs (FR-008 vs. FR-009).
- [x] CHK013 - Are recovery/restart requirements distinguished for "pending, not yet decided" vs. "decided, executing" states? [Coverage, Spec §User Story 7] — Yes, both acceptance scenarios in User Story 7 address each case separately.
- [x] CHK014 - Are non-functional (timing/latency) expectations set for how long an approval call may reasonably block, given it now waits for real execution rather than acknowledging immediately? [Gap, Non-Functional] — Not stated in spec.md itself (by design — spec.md is implementation-agnostic and defers exact timeout figures to planning, matching 005's own precedent); plan.md/contracts carry the actual budget. Acceptable split, but a reader of spec.md alone wouldn't know an approval call can legitimately take several seconds.

## Constitution Alignment

- [x] CHK015 - Does the spec ever describe a path where a domain service could plausibly be reached without going through the saga, even for a "small" or "read-adjacent" operation? [Constitution III/IV] — No; every mutation-shaped requirement (FR-006, FR-009, FR-010) is phrased as routing exclusively through the approval-gated workflow, and FR-018 makes the exclusion explicit and absolute.
- [x] CHK016 - Is the boundary between "the agent's tool" and "the human approval channel" stated as a hard requirement (not just an implementation choice) that the agent cannot approve its own request? [Constitution III, Spec Non-Goals] — Yes, stated as a Non-Goal in `contracts/order-action-tools.md`, tied directly to Constitution Principle III's text.
- [x] CHK017 - Does FR-021 (domain-pluggability) define a concrete, falsifiable test for "removable," or is it aspirational language only? [Measurability, Spec §FR-021] — Falsifiable: mirrors FR-015 from 005 and plan.md's Constitution Check gives the exact mechanical test (delete the `OrderAction` folder + one registration line).

## Dependencies & Assumptions

- [x] CHK018 - Is the dependency on feature 005's `RequestOrderFinding` contract for this feature's validation step documented as a cross-feature dependency anywhere in spec.md, or only in research.md? [Traceability, Gap] — Only in research.md/data-model.md (design-level docs), not spec.md itself — acceptable since spec.md is deliberately implementation-agnostic, but a spec-only reader wouldn't know this reuse exists.
- [x] CHK019 - Is the assumption that "the compensating dependency (inventory) is always safely reversible" stated and justified, or silently presumed? [Assumption, Gap] — Silently presumed by the feature's framing (a restock is modeled as the exact inverse of a reservation); not stated as an explicit assumption in spec.md. Low risk for this domain, but worth naming.

## Ambiguities & Conflicts

- [x] CHK020 - Is there any remaining ambiguity about whether "notify" is a third curated tool? [Ambiguity] — Resolved explicitly in spec.md Assumptions; no ambiguity remains.
- [x] CHK021 - Is there any remaining ambiguity about what triggers compensation (cross-dependency mutation failure vs. notification-delivery failure, both mentioned in the original instruction)? [Ambiguity] — Resolved explicitly in research.md Decision 5 and reflected in FR-011/FR-014's scoping; spec.md itself doesn't restate the notification-failure alternative it rejected, which is fine (a spec states what *is* required, not every rejected alternative).

## Notes

- This checklist was run **after** implementation (spec-kit's full workflow — specify → clarify → plan → tasks → implement → analyze → checklist — was completed in one continuous session), so several items double as a retrospective consistency check rather than a pre-implementation gate. Three real gaps were found (CHK008's FR-014 scoping, plus two documentation-only drifts caught by `/speckit-analyze`) and corrected in spec.md/data-model.md/contracts/ before this checklist was written.
- No item in this checklist blocks anything further — all are either already resolved or are low-risk, explicitly-scoped-out gaps appropriate for a POC. CHK002, CHK011, CHK014, CHK018, and CHK019 are the only "worth a follow-up sentence someday" items, none urgent.
