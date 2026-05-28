# Implementation Readiness Checklist: Session Management

**Purpose**: Validate requirements quality across all feature domains before and during implementation. Usable as an author self-check (pre-implement) and a peer reviewer gate (PR review). Tests the requirements themselves — not the code.
**Created**: 2026-05-28
**Feature**: [spec.md](../spec.md) | [plan.md](../plan.md) | [contracts/chat-api.md](../contracts/chat-api.md)

---

## API Contract Requirements Quality

- [x] CHK001 Is the `sessionId` field in `ChatRequest` specified as optional with a documented sentinel (absent = new session) distinct from an empty string or null? [Clarity, Spec §FR-001, contracts/chat-api.md] — Resolved: contract now specifies absent, null, and empty string are all equivalent and all mint a new session.
- [x] CHK002 Is the distinction between "echo back caller-supplied sessionId" and "mint new sessionId" unambiguous — specifically for the case where a caller-supplied ID matches an expired session? [Clarity, contracts/chat-api.md, Spec §FR-007] — Resolved: contract now states "the returned sessionId will differ from the supplied one when the supplied ID was expired, unknown, or malformed."
- [x] CHK003 Is backward compatibility for callers that omit `sessionId` and ignore it in the response explicitly guaranteed in the contract rather than implied? [Completeness, contracts/chat-api.md] — Resolved: Backward Compatibility section now uses "Guaranteed:" language with explicit no-status-code-change promise.
- [x] CHK004 Are error response semantics (e.g., 500 vs. stateless 200) when the store is unavailable specified in the API contract — not only in the spec? [Completeness, Spec §FR-010, contracts/chat-api.md] — Resolved: contract already stated "Store unavailability does NOT produce a 500"; confirmed present.
- [x] CHK005 Is the `sessionId` format (opaque GUID string) documented in the contract so callers do not assume a structured or sortable value? [Clarity, Spec §Assumptions, contracts/chat-api.md] — Resolved: contract now explicitly marks sessionId as an opaque UUID v4 token with MUST NOT parse/sort/compare instruction.

---

## Session Lifecycle — Minting

- [x] CHK006 Is "globally-unique" in FR-002 defined with a specific mechanism (e.g., cryptographic random, UUID v4) or left open to implementation? If open, is that intentional? [Clarity, Spec §FR-002] — Resolved: FR-002 now specifies UUID v4; Assumptions document 128-bit entropy and its implications.
- [x] CHK007 Are concurrent minting requirements — two simultaneous requests with no `sessionId` each receiving a distinct ID — specified as a mandatory guarantee rather than an assumed property? [Completeness, Spec §US2 Acceptance Scenario 3] — Resolved: FR-002 now includes "generation MUST produce distinct identifiers for concurrent requests without coordination."
- [x] CHK008 Is the requirement that an unknown client-supplied `sessionId` results in a new ID (not a 4xx) clearly located in a Functional Requirement (FR-007) and not only in edge cases prose? [Traceability, Spec §FR-007 vs Edge Cases] — Resolved: FR-007 already contained this as a MUST; confirmed present. Edge cases now reference FR-007 explicitly.

---

## Session Lifecycle — Expiry & Deletion

- [x] CHK009 Is "inactivity" in FR-006 precisely defined — does the expiry window reset on reads only, writes only, or both? [Clarity, Spec §FR-006] — Resolved: FR-006 now says "not written to within that window" — TTL resets on write (history save) only.
- [x] CHK010 Is the SC-004 guarantee ("deleted within 1× window duration") achievable with Redis's lazy eviction model, or does it require an active eviction mechanism? Is this addressed in requirements or deferred? [Measurability, Spec §SC-004] — Resolved: SC-004 now specifies "inaccessible within 1× window (TTL expiry)"; physical reclamation explicitly not an observable guarantee.
- [x] CHK011 Are requirements defined for what happens to a request that arrives for a session whose TTL expires mid-flight (between history load and history save)? [Edge Case, Gap] — Resolved: edge cases now document that the save implicitly creates a new entry with the same ID; the session ID is NOT reminted.
- [x] CHK012 Is the permanent-deletion guarantee (no soft-delete, no archive) referenced in a Functional Requirement (FR-006) and consistent with the clarification recorded in the Clarifications section? [Consistency, Spec §FR-006, Spec §Clarifications] — Resolved: FR-006 aligned with SC-004 ("inaccessible / TTL expires"); Clarifications section unchanged as the intent is consistent.

---

## Session Lifecycle — Isolation

- [x] CHK013 Is "isolation" in FR-009 defined beyond per-key scoping — for example, does it address namespace collision risks when Redis is shared with future features (rate limiting, caching)? [Clarity, Spec §FR-009, data-model.md] — Resolved: FR-009 now specifies namespaced keys and that the namespace MUST NOT conflict with other system uses of the same store instance.
- [x] CHK014 Are requirements for session ID entropy (resistance to guessing or enumeration) documented, or is this gap explicitly deferred to the AuthN feature? [Security, Gap] — Resolved: Assumptions now document UUID v4 provides 122-bit entropy; enumeration is negligible at expected volumes. No additional entropy requirement mandated.

---

## Session Lifecycle — Bounded History

- [x] CHK015 Is the unit of "turn" unambiguous — does one turn equal a user+assistant message pair, or is each message counted individually? FR-008 and data-model.md should agree. [Clarity, Spec §FR-008, data-model.md] — Resolved: FR-008 now explicitly states "a turn is a single message (user OR assistant), not a pair; MaxTurns: 20 accommodates 10 complete exchanges."
- [x] CHK016 Is "oldest-first" drop behavior in FR-008 specified for the case where adding new turns would require dropping more than one turn simultaneously (e.g., batch append)? [Clarity, Spec §FR-008] — Resolved: FR-008 now states excess turns are dropped "in a single pass whenever the total exceeds MaxTurns, regardless of how many turns were appended in the current request."
- [x] CHK017 Are requirements defined for invalid configuration values (`MaxTurns: 0` or negative) — should the system reject configuration, default to a safe value, or disable history? [Edge Case, Gap] — Resolved: FR-008 now mandates startup failure with descriptive error for MaxTurns ≤ 0; Assumptions confirm disabling history is not supported.

---

## Session Lifecycle — Graceful Degradation

- [x] CHK018 Is "unavailable" in FR-010 precisely defined — does it cover connection timeout, connection refused, partial write failure, and serialisation error equally, or are some modes excluded? [Clarity, Spec §FR-010] — Resolved: FR-010 now enumerates all four failure modes explicitly.
- [x] CHK019 Are requirements defined for session state consistency after a partial failure — for example, if history was loaded but the save fails, is the caller aware their turn was not persisted? [Edge Case, Gap] — Resolved: FR-010 now states "the caller MUST NOT be notified; the response MUST appear identical to a successful stateless request." Edge cases document that the session reverts to the last successfully persisted state.
- [x] CHK020 Is the behavior when the store recovers mid-session specified — does the next successful request resume from pre-degradation history or start fresh? [Edge Case, Gap] — Resolved: edge cases now state the session resumes from last successfully persisted state; degraded turns are not recovered.

---

## Performance Requirements Quality

- [x] CHK021 Is the 100 ms overhead budget in SC-003 specified as a percentile (p50, p95, p99) or as an absolute ceiling? An unqualified "MUST NOT add more than 100 ms" is ambiguous under load. [Clarity, Spec §SC-003] — Resolved: SC-003 now specifies p95 percentile.
- [x] CHK022 Is the 100 ms budget allocated across the full session round-trip (GET + SET) or per operation? If one GET already costs 80 ms, is the SET budget 20 ms or another 100 ms? [Clarity, Spec §SC-003] — Resolved: SC-003 uses "combined session load + save overhead" — the 100 ms is the total ceiling for both operations together, not per operation.
- [x] CHK023 Are performance requirements defined for the worst-case payload — a session at maximum history (20 turns of large responses)? The spec defines the cap but not the size of each turn. [Coverage, Gap] — Resolved: Assumptions now document ≤4 KB per turn assumed; worst-case payload at MaxTurns=20 is ≤80 KB; 100 ms p95 budget applies at this size.
- [x] CHK024 Is there a requirement for what the system does when session operations exceed the 100 ms budget — timeout and degrade, or block and wait? [Completeness, Gap] — Resolved: FR-010 now states the system does not impose an explicit timeout beyond underlying client defaults; latency alone does not trigger degradation.

---

## Security Requirements Quality

- [x] CHK025 Is the known rate-limiting security gap documented with sufficient precision for the AuthN feature team to pick it up — including the specific attack vector (unbounded session creation), not just "deferred"? [Completeness, Spec §Assumptions] — Resolved: Known security gap assumption now specifies attack vector (unbounded creation from any network source) and potential impact (Redis memory exhaustion).
- [x] CHK026 Are requirements for session token transmission security (e.g., HTTPS-only, no session ID in URL query strings or logs) documented or explicitly out of scope? [Gap, Security] — Resolved: Assumptions now state HTTPS is infrastructure-level (out of scope), sessionId MUST NOT appear in URL paths or query params, and log output MUST truncate to first 8 characters. Contract also adds a log safety note.
- [x] CHK027 Is the scope of the deferred security gap explicitly bounded — i.e., does the spec clearly state what IS and IS NOT protected at this stage, so the gap cannot silently expand? [Clarity, Spec §Assumptions] — Resolved: new "Security scope boundary" assumption explicitly lists what is and is not protected by this feature.
- [x] CHK028 Are requirements for session ID entropy (bit length, generation algorithm) specified to prevent brute-force enumeration, or is the GUID assumption documented as the sole basis? [Security, Spec §Assumptions] — Resolved: Assumptions document UUID v4 = 122-bit entropy; enumeration probability negligible; no additional entropy requirement beyond UUID v4 mandated for this feature.

---

## Observability Requirements Quality

- [x] CHK029 Are the four required lifecycle events in FR-012 (session created, history loaded, history saved, degradation triggered) defined with a consistent field schema — or only named without structure? [Clarity, Spec §FR-012] — Resolved: FR-012 now defines each event with explicit required fields; contracts/chat-api.md event table is aligned.
- [x] CHK030 Is the logging level (Info, Warning, Error) for each of the four lifecycle events specified in FR-012, or left to implementer discretion? [Clarity, Gap] — Resolved: FR-012 now specifies Info (created), Debug (loaded, saved), Warning (degraded). Contract table is aligned.
- [x] CHK031 Are requirements defined for what the degradation event MUST capture — at minimum error type and whether history was partially loaded before failure — to make the event actionable for operators? [Completeness, Spec §FR-012] — Resolved: FR-012 degraded event now requires errorCategory, historyLoadedBeforeFailure (bool), and turnCountLoaded (int).
- [x] CHK032 Are observability requirements consistent between FR-012 (four events named) and plan.md Phase E (which names only three: created, history_loaded, history_saved — omitting degraded)? [Consistency, Spec §FR-012, plan.md §Phase E] — Resolved: plan.md Phase E now lists all four events including `session.degraded` with a store-outage trigger instruction.

---

## Edge Case & Error Flow Coverage

- [x] CHK033 Is the agent-failure partial persistence rule ("user turn saved, assistant turn not saved") expressed in Functional Requirements (FR-005), or only in the edge cases prose section? A requirement buried in edge cases may be missed during implementation. [Traceability, Spec §FR-005 vs Edge Cases] — Resolved: FR-005 now contains both the success-path and failure-path persistence rules as MUST statements.
- [x] CHK034 Is the last-write-wins concurrency decision documented as a conscious tradeoff with a clear trigger for re-evaluation (e.g., "revisit when AuthN ships"), not just as an implementation note? [Completeness, Spec §Edge Cases, Spec §Clarifications] — Resolved: edge cases now state the tradeoff "will be revisited when feature #3 (AuthN/AuthZ) binds sessions to user identities, enabling per-user serialisation or optimistic locking."
- [x] CHK035 Is malformed session ID handling (non-GUID, excessively long string) addressed in a Functional Requirement or only in edge cases prose? Validation boundaries should be in requirements. [Completeness, Spec §FR-001 vs Edge Cases] — Resolved: FR-007 now explicitly covers "expired, unknown, or malformed session ID" as a single MUST statement. Edge cases reference FR-007.

---

## Configuration & Defaults

- [x] CHK036 Are the default values for `MaxTurns` (20) and `SlidingExpirationMinutes` (30) documented with rationale — not just as numbers — so a reviewer can judge whether they are appropriate for the target workload? [Completeness, Spec §Assumptions] — Resolved: Assumptions now document rationale for both defaults (workflow duration, context window, payload size).
- [x] CHK037 Is the configuration key path (`Session:MaxTurns`, `Session:SlidingExpirationMinutes`) stable and documented in the spec or plan such that a future rename would be treated as a breaking change? [Clarity, plan.md §Phase D] — Resolved: plan.md Phase D now includes a stability note; Assumptions also document key name stability.

---

## Notes

- Check items off as completed: `[x]`
- Items marked `[Gap]` indicate requirements that may be absent from the spec — confirm whether intentional before closing
- Items marked `[Consistency]` flag potential conflicts between two source documents — resolve by updating the lower-authority document to match the higher
- Mandatory gate items before proceeding to `/speckit-implement`: CHK021–CHK028 (performance + security), CHK029–CHK032 (observability)
- **All 37 items resolved 2026-05-28** — spec, plan, and contract updated accordingly
