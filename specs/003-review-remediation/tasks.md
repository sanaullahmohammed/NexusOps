# Tasks: Review Remediation

**Input**: Design documents from `specs/003-review-remediation/`

**Prerequisites**: spec.md ✅ | plan.md ✅

**Tests**: Explicitly required by FR-016. Test tasks are generated, and Phase 1 exists precisely so that verification precedes the behavioural changes.

**Organization**: Phases correspond to the five delivery batches. Each phase lands as one commit carrying both its code change and the specification amendments it implies, then pauses for review.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: Which user story this task belongs to (US1–US4 matching spec.md)
- **(F#)**: Which review finding the task closes
- All paths are project-relative from repo root

---

## Phase 0: Pre-work

**Purpose**: Housekeeping that would otherwise contaminate every later diff.

- [x] T001 Revert the CRLF churn in `frontend/package-lock.json` (5,493 changed lines, ~13 real)
- [x] T002 Add `.gitattributes` pinning `frontend/package-lock.json` to LF, scoped to that one path (F-lockfile)
- [x] T003 Create branch `003-review-remediation` via the spec-kit git extension
- [x] T004 Write `spec.md` and `plan.md` for this feature

**Checkpoint**: Working tree clean; branch created; no renormalisation triggered elsewhere.

---

## Phase 1: Batch D — Verification First (US3)

**Purpose**: Establish that CI can actually fail. No behavioural work begins until this phase is complete.

**⚠️ CRITICAL**: Phases 2–4 depend on this phase — they are specified to land with tests.

- [ ] T005 [US3] Create `NexusOps.Tests/NexusOps.Tests.csproj` — xUnit, net10.0, project references to `NexusOps.AgentHost`, `NexusOps.OrderService` and `NexusOps.Contracts` (F10)
- [ ] T006 [P] [US3] Create `NexusOps.Tests/.gitignore` with `bin/`, `obj/`, `out/`, `*.nupkg`, `*.lscache` per the CLAUDE.md project convention
- [ ] T007 [US3] Register `NexusOps.Tests` in `NexusOps.sln`
- [ ] T008 [US3] Add `NexusOps.Tests` and `NexusOps.AppHost` to `NexusOps.deployable.slnf` (F8, F10)
- [ ] T009 [P] [US3] Add a placeholder assertion test so the suite is non-empty and the step is demonstrably non-vacuous
- [ ] T010 [US3] Update the `NexusOps.deployable.slnf` paragraph in `CLAUDE.md` — record that the AppHost is now compiled, and that the exclusion rationale was what left it unverified (F8)

**Checkpoint**: `dotnet build NexusOps.deployable.slnf` compiles the AppHost; `dotnet test` reports a non-zero test count.

---

## Phase 2: Batch A — Order Contract and Anomaly Semantics (US1)

**Purpose**: Make an order's anomaly classification a property of the order.

- [ ] T011 [US1] Add `AnomalyReason` enum (`Delayed`, `Missing`, `PaymentFailed`) and nullable `Order.AnomalyReason` property in `NexusOps.OrderService/Models/Order.cs`; leave `OrderStatus` unchanged (F3)
- [ ] T012 [US1] Add `SeedDataConstants.Ord0011` in `NexusOps.Contracts/SeedDataConstants.cs` (F3)
- [ ] T013 [US1] In `NexusOps.OrderService/Data/OrderStore.cs`: set `AnomalyReason` on ORD-0001, ORD-0002 (`Delayed`) and ORD-0009; add ORD-0011 covering the remaining reason (F3)
- [ ] T014 [US1] Convert seed `ExpectedDelivery`, `ActualDelivery` and `CreatedAt` to offsets from the current date so derived values stop drifting (F20)
- [ ] T015 [US1] Register `TimeProvider.System` in `NexusOps.OrderService/Program.cs` (F20)
- [ ] T016 [US1] Widen `NexusOps.Contracts/Dtos/OrderAnomaly.cs` to the eight fields the order service contract already publishes, reusing the existing `OrderLineItem` record (F1, F2)
- [ ] T017 [US1] Rewrite the `/orders/anomalies` handler in `NexusOps.OrderService/Endpoints/OrderEndpoints.cs` — select on `AnomalyReason`, derive `anomalyType` from the order, populate the full payload, and resolve today's date via injected `TimeProvider` (F1, F2, F3, F20)
- [ ] T018 [US1] Implement the severity rule — `Missing` and `PaymentFailed` always high; `Delayed` high past seven days overdue, medium below (F3)
- [ ] T019 [US1] Add a `BadRequest` catch clause to `OrderTools.InvestigateOrderAnomalyAsync` returning a correctable message naming the valid filter values; log 4xx at Warning rather than Error (F11)
- [ ] T020 [P] [US1] Tests: three filters return disjoint non-empty sets; `anomalyType` invariant across filters; full payload present; `daysOverdue` deterministic under a faked `TimeProvider`; severity boundary at exactly seven days
- [ ] T021 [P] [US1] Amend `specs/001-ecommerce-domain-services/data-model.md` — `AnomalyReason`, relative seed dates, eleven orders
- [ ] T022 [P] [US1] Amend `specs/001-ecommerce-domain-services/contracts/order-service-api.md` — anomaly semantics and severity rule
- [ ] T023 [P] [US1] Amend `specs/001-ecommerce-domain-services/contracts/tool-definitions.md` — 400 handling on the anomaly tool

**Checkpoint**: All Phase 2 tests pass; `?status=missing` and `?status=payment-failed` return different orders.

---

## Phase 3: Batch B — Chat and Session Correctness (US2)

**Purpose**: Make sessions survive a store outage and make the endpoint honour its own contract.

- [ ] T024 [US2] Guard the prompt in `NexusOps.AgentHost/Endpoints/ChatEndpoints.cs` — reject null, empty or whitespace with `Results.ValidationProblem` before minting a session or calling the agent; add `.ProducesValidationProblem()` (F5)
- [ ] T025 [US2] Add validation attributes to `ConversationSessionOptions` and replace the ad-hoc `MaxTurns` guard in `Program.cs` with a `ValidateOnStart` validator covering both `MaxTurns` and `SlidingExpirationMinutes` (F4)
- [ ] T026 [US2] Add an `ArgumentOutOfRangeException => "configuration"` arm to the error-category switch in `RedisConversationStore.LogDegraded` so a misconfiguration can never be reported as a connection fault (F4)
- [ ] T027 [US2] Introduce `HistoryResult` (turns + `Found`/`Missing`/`Unavailable`) and widen `IConversationStore.GetHistoryAsync` in `NexusOps.AgentHost/Services/IConversationStore.cs` (F12)
- [ ] T028 [US2] Update `RedisConversationStore.GetHistoryAsync` to return `Missing` on a cache miss and `Unavailable` on a caught exception (F12)
- [ ] T029 [US2] Update `AgentService.SendAsync` — mint on `Missing`, preserve the caller's ID on `Unavailable`, and skip the store read entirely when no ID was supplied (F12, F13)
- [ ] T030 [US2] Emit `session.history_loaded` only when history was actually loaded (F13)
- [ ] T031 [US2] Extract the hashed session-token helper to one shared internal static used by both `AgentService` and `RedisConversationStore`, retaining the CRLF-stripping guard (F9)
- [ ] T032 [US2] Return the active session ID on the agent-failure path via a ProblemDetails `sessionId` extension, so the turn persisted under 002 FR-005 is reachable (F14)
- [ ] T033 [P] [US2] Tests: failing store preserves the caller's ID across successive requests; expired session still mints; blank prompt returns 400 with no store write; non-positive options prevent startup; trimming at the `MaxTurns` boundary; both loggers emit the same token
- [ ] T034 [P] [US2] Amend `specs/002-session-management/spec.md` — FR-007 store-unavailable clause, FR-008 sliding-expiration validation, FR-010 identifier preservation, FR-012 token consistency
- [ ] T035 [P] [US2] Amend `specs/002-session-management/contracts/chat-api.md` — document the 400 response and the `sessionId` extension on the 500

**Checkpoint**: All Phase 3 tests pass; a stubbed store outage no longer rotates session identifiers.

---

## Phase 4: Batch C — Health, Cancellation, Proxy (US4)

**Purpose**: Make deployed services observable and in-flight work cancellable.

- [ ] T036 [US4] Register `/health` unconditionally in `NexusOps.OrderService/Extensions.cs`, keeping `/alive` Development-only and rewriting the security comment to state why the probe was kept (F7)
- [ ] T037 [P] [US4] Same change in `NexusOps.InventoryService/Extensions.cs` (F7)
- [ ] T038 [P] [US4] Same change in `NexusOps.ProductService/Extensions.cs` (F7)
- [ ] T039 [P] [US4] Same change in `NexusOps.Server/Extensions.cs` (F7)
- [ ] T040 [US4] Add a shared JSON health response writer emitting `{"status":"healthy"}` with the correct content type, applied at all four call sites (F17)
- [ ] T041 [US4] Add a trailing `CancellationToken` to all six handlers in `Tools/{Order,Inventory,Product}Tools.cs` and forward it to each `GetFromJsonAsync` (F15)
- [ ] T042 [US4] Add `catch (OperationCanceledException) { throw; }` ahead of each generic catch so cancellation is not relabelled as a service outage (F15)
- [ ] T043 [P] [US4] Give the `/api` proxy target a localhost fallback in `frontend/vite.config.ts` and log the resolved target at config time (F16)
- [ ] T044 [P] [US4] Tests: health payload shape; a cancelled token surfaces as cancellation rather than as a `ToolResult` failure
- [ ] T045 [P] [US4] Amend the `/health` section of all three `specs/001-…/contracts/*-service-api.md` files (F13, F17)

**Checkpoint**: All Phase 4 tests pass; `/health` returns JSON under a Production environment name.

---

## Phase 5: Batch E — Secrets Hygiene and Documentation

**Purpose**: Close the credential footgun and end the documentation drift. Sequenced last so the docs are written once, against the final vocabulary.

- [ ] T046 Add a `UserSecretsId` to `NexusOps.AgentHost/NexusOps.AgentHost.csproj`, matching the pattern the AppHost already uses (F6)
- [ ] T047 Make `dotnet user-secrets` the primary documented credential path in `README.md`, with the environment variable as the CI and container alternative (F6)
- [ ] T048 [P] Correct the workflow count in the `CLAUDE.md` CI/CD section — the sentence says four, the table lists three (F18)
- [ ] T049 [P] Repoint `CLAUDE.md` **Active Feature Plan** from `specs/002-session-management/plan.md` to this feature (F18)
- [ ] T050 [P] Sync the order status vocabulary at `README.md:99` to the actual `OrderStatus` enum (F18)
- [ ] T051 [P] Correct the stale tool name `investigate_delayed_order` at `README.md:183` to `investigate_order_anomaly` (F18)
- [ ] T052 [P] Put the evaluation-runner paragraph at `README.md:238` in the future tense, consistent with the "not yet implemented" note at `README.md:208` (F18)
- [ ] T053 [P] Set `Status: Draft` → `Implemented` in `specs/001-ecommerce-domain-services/spec.md` (F18)
- [ ] T054 Update `CLAUDE.md` Repository Structure and Current Build State — add `NexusOps.Tests`, the eleven-order seed count, and the health-endpoint change
- [ ] T055 Set `Status: In Progress` → `Implemented` in `specs/003-review-remediation/spec.md`

**Checkpoint**: `git grep` finds no surviving reference to the corrected vocabulary; no README or CLAUDE.md statement contradicts the code.

---

## Phase 6: Delivery

- [ ] T056 Confirm with the user, then push `003-review-remediation` to origin
- [ ] T057 Open a pull request against `master` with a body mapping each of the 19 findings to the commit that closed it
- [ ] T058 Verify both CI jobs pass on the branch — in particular that the dotnet job now reports a real test count

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 0** — complete. No dependencies.
- **Phase 1 (D)** — depends on Phase 0. Blocks Phases 2–4, which are specified to land with tests.
- **Phase 2 (A)** — depends on Phase 1. Independent of Phases 3 and 4.
- **Phase 3 (B)** — depends on Phase 1. Independent of Phases 2 and 4.
- **Phase 4 (C)** — depends on Phase 1. T041–T042 touch `OrderTools.cs`, which Phase 2 also edits at T019; sequence Phase 4 after Phase 2 to avoid a conflict in that file.
- **Phase 5 (E)** — depends on Phases 2 and 4, whose vocabulary and health-endpoint changes the documentation describes.
- **Phase 6** — depends on all preceding phases and on explicit user confirmation.

### Within Each Phase

Tasks marked **[P]** touch different files and may proceed together. Unmarked tasks within a phase are sequential where they share a file — notably T011→T013→T017 in `OrderStore.cs`/`OrderEndpoints.cs`, and T027→T028→T029 across the store abstraction and its consumer.

### Parallel Opportunities

```bash
# Phase 4 — four independent service files:
T037, T038, T039   # Inventory, Product, Server Extensions.cs

# Phase 5 — documentation edits in different files:
T048, T049, T050, T051, T052, T053
```

## Notes

- Every phase is compiled and tested before being reported complete. Review finding 19 claimed no SDK was available; it is present at 10.0.400, so nothing here is verified by inspection alone.
- Findings 1 and 2 are the same defect and its remedy, so the twenty findings resolve to nineteen work items.
- Finding 19 produces no task. Findings 6 and 18 produce tasks against corrected evidence — no credential leaked, and the disputed workflow count is in CLAUDE.md, not README.md.
