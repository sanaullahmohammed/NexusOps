# Tasks: Evaluation Runner

**Input**: Design documents from `/specs/007-evaluation-runner/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: Included — `NexusOps.Tests` already covers every prior feature credential-free; this feature's dataset-validation and tool-extraction logic are pure functions well suited to the same pattern, and CI's entire trust in `--validate-only` depends on that logic being correct.

**Organization**: Tasks are grouped by user story (spec.md): US1 = CI dataset validation (P1), US2 = developer live-accuracy measurement (P2), US3 = live mode's unattended-safety guarantee (P1).

## Format: `[ID] [P?] [Story] Description`

## Phase 1: Setup

**Purpose**: Project scaffolding shared by every story.

- [x] T001 Create `NexusOps.Evaluation/NexusOps.Evaluation.csproj` (Exe, `net10.0`, `ImplicitUsings`/`Nullable` enabled, `ProjectReference` to `..\NexusOps.Contracts\NexusOps.Contracts.csproj`, no other package references)
- [x] T002 [P] Create `NexusOps.Evaluation/.gitignore` per project convention (`bin/`, `obj/`, `out/`, `*.nupkg`, `*.lscache`)
- [x] T003 Add `NexusOps.Evaluation` to `NexusOps.sln` (new project entry + `ProjectConfigurationPlatforms` block, matching the existing `{FAE04EC0-...}` project-type GUID used by other non-web class-library-style console/library projects) and to `NexusOps.deployable.slnf`'s `projects` array

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Shared data model, tool catalog, and the dataset content itself — required by every user story below.

**⚠️ CRITICAL**: No user story phase can be verified until this phase is complete.

- [x] T004 [P] Implement `EvaluationCase` record (`Id`, `Prompt`, `ExpectedTool`, `ExpectedPath`, `Notes?`) and dataset (de)serialization helpers in `NexusOps.Evaluation/EvaluationCase.cs`, per data-model.md's field table (case-insensitive JSON property matching, no field defaults substituted for missing required fields)
- [x] T005 [P] Implement `ToolCatalog` in `NexusOps.Evaluation/ToolCatalog.cs`: reflect `NexusOps.Contracts.ToolNames`'s public `const string` fields (excluding any ending in `Description`) for the known-tool set, plus the fixed tool→path (`Direct`/`Saga`) map from data-model.md's table
- [x] T006 Author `NexusOps.Evaluation/Data/eval-cases.json`: 24 cases covering all 9 curated tools at least twice each with varied realistic phrasing, both Direct and Saga paths represented, plus a nonexistent-order-ID and an out-of-range-category edge case (per research.md Decision 6); mark `<None Include="Data/eval-cases.json"><CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory></None>` in `NexusOps.Evaluation/NexusOps.Evaluation.csproj`

**Checkpoint**: `NexusOps.Evaluation` compiles, the dataset file exists and is copied to the output directory, and `EvaluationCase`/`ToolCatalog` are ready for both validation and live-mode consumption.

---

## Phase 3: User Story 1 - CI validates the evaluation dataset on every push (Priority: P1) 🎯 MVP

**Goal**: A fully credential-free, offline `--validate-only` mode that checks the dataset's schema, unique case IDs, recognized tool names, and expected-path consistency, reporting every defect found in one pass.

**Independent Test**: Run `dotnet run --project NexusOps.Evaluation -- --validate-only` against the checked-in dataset (passes, reports case count) and against a deliberately corrupted copy (fails, names the specific defects) — no network, no credentials, no other services running.

### Tests for User Story 1

- [x] T007 [P] [US1] Unit tests for `DatasetValidator` in `NexusOps.Tests/Evaluation/DatasetValidatorTests.cs`: valid dataset passes; duplicate `id` is caught (names both cases); unrecognized `expectedTool` is caught; `expectedPath` not `Direct`/`Saga` is caught; `expectedPath` inconsistent with the tool's actual path is caught; missing/empty required field is caught; case count outside [20, 30] is caught; a tool or path with zero covering cases is caught; multiple defects in one dataset are all reported in a single run (not just the first)

### Implementation for User Story 1

- [x] T008 [US1] Implement `ValidationIssue` and `DatasetValidator` in `NexusOps.Evaluation/DatasetValidator.cs` per data-model.md's Validation categories, using `EvaluationCase` (T004) and `ToolCatalog` (T005); collects every issue in one pass rather than stopping at the first (FR-010)
- [x] T009 [US1] Implement `NexusOps.Evaluation/Program.cs`'s argument parsing (hand-rolled, no framework, per research.md Decision 5: recognizes `--validate-only`, `--dataset <path>`, `--base-url <url>`) and the `--validate-only` dispatch branch: load the dataset (default path `Data/eval-cases.json` relative to `AppContext.BaseDirectory`, overridable via `--dataset`), run `DatasetValidator`, print either a success message with the validated case count or every `ValidationIssue` found, and return exit code `0`/`1` accordingly
- [x] T010 [US1] Add a `dataset-validate` CI step to `.github/workflows/ci.yml`'s `dotnet` job (after the existing `Test` step): `dotnet run --project NexusOps.Evaluation --configuration Release --no-build -- --validate-only`, satisfying FR-021

**Checkpoint**: `dotnet run --project NexusOps.Evaluation -- --validate-only` fully works standalone; CI now runs it on every push/PR.

---

## Phase 4: User Story 3 - Live evaluation never breaks an unattended or credential-free run (Priority: P1)

**Goal**: The default (non-`--validate-only`) mode detects, before sending any dataset prompt, whether a live `AgentHost` is reachable; when it is not, it reports a distinct skipped outcome with setup guidance and exits `0` — never a failure.

**Independent Test**: Run `dotnet run --project NexusOps.Evaluation` (default mode) with nothing listening on the target address — confirm a `SKIPPED` banner, actionable guidance naming the exact commands from quickstart.md, and exit code `0`, with no dataset prompt ever sent (verifiable by pointing `--base-url` at an address with no listener and confirming no HTTP request beyond the failed health probe occurs).

### Tests for User Story 3

- [x] T011 [P] [US3] Unit tests for reachability handling in `NexusOps.Tests/Evaluation/LiveRunnerReachabilityTests.cs`, using an injectable `HttpMessageHandler` fake (matching the pattern already used in `NexusOps.Tests/Tools/ToolCancellationTests.cs`): an unreachable/non-2xx/timing-out `/health` produces a skipped result and the runner never issues a second HTTP call; a healthy `/health` allows the run to proceed

### Implementation for User Story 3

- [x] T012 [US3] Implement `LiveRunner.ProbeReachabilityAsync` in `NexusOps.Evaluation/LiveRunner.cs`: one `GET {baseUrl}/health` with a 3s timeout; any non-success status, connection failure, or timeout is treated as unreachable (research.md Decision 2)
- [x] T013 [US3] Implement the skip path in `NexusOps.Evaluation/ConsoleReport.cs` (a `WriteSkipped(string baseUrl)` banner naming the exact setup steps from quickstart.md) and wire it into `Program.cs`'s default-mode dispatch: resolve `--base-url` / `AGENTHOST_BASE_URL` / the `http://localhost:5186` default (research.md Decision 3), probe reachability first, and on failure print the skip banner and return exit code `0` without loading or sending any dataset case

**Checkpoint**: Live mode is safe to run (or run by mistake) in any environment with no `AgentHost` — including CI — without ever failing the build.

---

## Phase 5: User Story 2 - A developer measures live tool-routing accuracy (Priority: P2)

**Goal**: When `AgentHost` is reachable, live mode sends every dataset case's prompt, records which tool(s) the agent actually invoked, and reports per-case pass/fail plus an accurate summary (total/passed/failed/pass rate).

**Independent Test**: With `NexusOps.AppHost` (or `NexusOps.AgentHost` directly) running with valid Azure AI credentials, run live mode and confirm every case gets a result line naming the expected vs. actually-invoked tool(s), and the printed summary's total/passed/failed/pass-rate figures match the per-case results.

### Tests for User Story 2

- [x] T014 [P] [US2] Unit tests for tool-invocation extraction in `NexusOps.Tests/Sessions/AgentServiceTests.cs` (or a new `NexusOps.Tests/Sessions/AgentServiceToolsInvokedTests.cs`): a `FakeAgent` whose `RunCoreAsync` returns an `AgentResponse` containing `FunctionCallContent` items yields those names, in order, on `SendAsync`'s new `ToolsInvoked` result; an agent response with no `FunctionCallContent` yields an empty list, never null
- [x] T015 [P] [US2] Unit tests for `LiveRunner`'s per-case scoring in `NexusOps.Tests/Evaluation/LiveRunnerScoringTests.cs` using the same fake-handler pattern as T011: expected tool present in `toolsInvoked` → pass; absent (including an empty `toolsInvoked`) → fail; a case whose HTTP call throws or times out → fail with the error recorded, and the run continues to the next case rather than aborting (FR-018)

### Implementation for User Story 2

- [x] T016 [US2] Extend `IAgentService.SendAsync` in `NexusOps.AgentHost/Services/IAgentService.cs` to return `(string Response, string SessionId, IReadOnlyList<string> ToolsInvoked)`
- [x] T017 [US2] Implement the extraction in `NexusOps.AgentHost/Services/AgentService.cs`: after `_agent.RunAsync(...)` returns, scan `agentResponse.Messages`' `Contents` for `Microsoft.Extensions.AI.FunctionCallContent` and collect `.Name` (research.md Decision 1); return it as the third tuple element alongside the existing `responseText`/`activeSessionId`
- [x] T018 [US2] Update the 9 destructuring call sites in `NexusOps.Tests/Sessions/AgentServiceTests.cs` for the new 3-tuple return shape (add a discarded third element to each `var (a, b) = ...SendAsync(...)` call)
- [x] T019 [US2] Add `ToolsInvoked` (`IReadOnlyList<string>`) to the `ChatResponse` record and its construction in `NexusOps.AgentHost/Endpoints/ChatEndpoints.cs`, per contracts/agent-chat-response.md
- [x] T020 [US2] Implement `EvaluationResult`/`EvaluationSummary` and the per-case request/compare loop in `NexusOps.Evaluation/LiveRunner.cs` (continuing from T012's reachability probe): for each `EvaluationCase`, POST `{prompt}` with no `sessionId` to `{baseUrl}/api/chat` (fresh session per case, per research.md/spec.md's single-turn assumption), a 60s per-request timeout, catch request/timeout exceptions into `EvaluationResult.Error` rather than aborting the run, and score `Passed` as `ExpectedTool` contained in the response's `toolsInvoked`
- [x] T021 [US2] Implement per-case and summary rendering in `NexusOps.Evaluation/ConsoleReport.cs` (`WriteCaseResult`, `WriteSummary`): a pass/fail line per case naming expected vs. actual tool(s) or the error, then a summary table (total/passed/failed/pass rate); wire into `Program.cs`'s default-mode dispatch after T013's reachability gate, returning exit code `0` if every case passed or `1` if any failed

**Checkpoint**: Live mode is fully functional end-to-end: skips safely when unreachable (US3), scores and reports accurately when reachable (US2).

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Documentation and repository-wide consistency once all three stories are complete.

- [x] T022 [P] Rewrite the "## Evaluation" section of `README.md`: remove the "planned — not yet implemented" framing, document both `--validate-only` and live-mode commands (from quickstart.md) and their credential requirements, and cross-link to `NexusOps.Tests`' existing credential-free precedent in the Testing section
- [x] T023 [P] Move "Evaluation dataset + runner" from **Planned** to **Implemented** in `README.md`'s Roadmap section
- [x] T024 Update `CLAUDE.md`'s Current Build State: add a feature 007 bullet describing `NexusOps.Evaluation` (dataset scope, both modes, the `ToolsInvoked` contract addition, and `dotnet test`/CI results), matching the level of detail of the existing feature 005/006 bullets
- [x] T025 Update `ROADMAP.md`'s Status checklist: mark "Prompt 5 — Spec 007 + implement: evaluation runner" `[x]` with a completion note matching the style of Prompts 3/4's notes (dataset case count, test count, live-verification outcome)
- [x] T026 Run `dotnet build NexusOps.deployable.slnf` and `dotnet test NexusOps.deployable.slnf` and confirm a clean, fully-green result including the new `NexusOps.Evaluation`-adjacent tests
- [x] T027 Run quickstart.md's three scenarios end-to-end: `--validate-only` against the real dataset; default mode with nothing listening (confirm skip); and, if local Azure AI credentials are available per ROADMAP.md's Live-Azure acceptance note, a real live run against `dotnet run --project NexusOps.AppHost`, recording the observed pass rate

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies.
- **Foundational (Phase 2)**: Depends on Setup. Blocks every user story.
- **US1 (Phase 3)**: Depends on Foundational only. Fully independent of US2/US3.
- **US3 (Phase 4)**: Depends on Foundational only. Independent of US1. Independent of US2 in the sense that its own acceptance criteria (skip-when-unreachable) hold regardless of whether US2's scoring logic exists yet — but T013 establishes the `Program.cs` default-mode dispatch that US2's T020/T021 extend, so implement US3 before US2 in practice.
- **US2 (Phase 5)**: Depends on Foundational; in practice builds on US3's `Program.cs` dispatch and `LiveRunner` scaffold (T012/T013), so implement after US3.
- **Polish (Phase 6)**: Depends on all three user stories being complete (README/CLAUDE.md document both modes; the build/test/quickstart validation in T026/T027 exercises everything).

### User Story Dependencies

- **US1 (P1)**: No dependency on US2 or US3.
- **US3 (P1)**: No dependency on US1. Implemented before US2 for practical file-sharing reasons (see above), not a hard requirement of independent testability — US3's own acceptance criteria never require US2's scoring code to exist.
- **US2 (P2)**: Extends the `LiveRunner`/`Program.cs` scaffold US3 establishes; also touches `NexusOps.AgentHost` (T016-T019), which no other story touches.

### Suggested MVP Scope

Phase 1 + Phase 2 + Phase 3 (US1) alone already satisfies ROADMAP.md's credential-free definition-of-done item ("`NexusOps.Evaluation --validate-only` passes") and CI's actual gate (T010). Phases 4-5 (US3, US2) add the live-mode value on top without changing US1's behavior.

---

## Parallel Example: Foundational Phase

```text
Task: "Implement EvaluationCase record in NexusOps.Evaluation/EvaluationCase.cs"
Task: "Implement ToolCatalog in NexusOps.Evaluation/ToolCatalog.cs"
```

(T006, authoring the dataset content, is sequenced after both since case authoring benefits from `ToolCatalog`'s tool→path table being settled first, even though it's a data file, not code, so there's no compiler dependency — just an authoring-order preference.)

## Parallel Example: User Story 2

```text
Task: "Unit tests for tool-invocation extraction in NexusOps.Tests/Sessions/AgentServiceToolsInvokedTests.cs"
Task: "Unit tests for LiveRunner's per-case scoring in NexusOps.Tests/Evaluation/LiveRunnerScoringTests.cs"
```

---

## Notes

- No task touches `NexusOps.AppHost` — `NexusOps.Evaluation` is deliberately not an Aspire-managed resource (plan.md's Structure Decision).
- T016-T019 are the only tasks in this feature that touch `NexusOps.AgentHost`; every other implementation task is confined to the new `NexusOps.Evaluation` project.
- Per this project's established commit-workflow preference, no task in this list runs `git commit` — commits are left for the user to trigger explicitly once the feature is verified.
