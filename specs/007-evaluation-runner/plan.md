# Implementation Plan: Evaluation Runner

**Branch**: `007-evaluation-runner` | **Date**: 2026-09-03 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/007-evaluation-runner/spec.md`

## Summary

Add `NexusOps.Evaluation`, a dependency-light .NET console project holding a checked-in JSON dataset of 20-30 labeled prompts (`Data/eval-cases.json`) and a runner with two modes: `--validate-only` (credential-free, offline — validates schema, unique case IDs, recognized tool names sourced by reflection from `NexusOps.Contracts.ToolNames`, and expected-path consistency; this is what CI runs on every push) and live mode, the default, which POSTs each prompt to a running `NexusOps.AgentHost`'s `/api/chat` and compares the tool(s) the agent actually invoked against each case's expectation. Making "which tool did the agent invoke" observable requires one small, additive change to `NexusOps.AgentHost`: `AgentService.SendAsync` already holds the full `AgentResponse` from the Microsoft Agent Framework, whose `Messages` already contain `FunctionCallContent` per invoked tool — this is surfaced as a new `toolsInvoked` array on the existing `ChatResponse` DTO, with no new endpoint and no behavior change to the existing `response`/`sessionId` fields. Live mode determines whether a live run is possible by probing `/health` before sending any dataset prompt; an unreachable AgentHost produces a distinct skipped outcome (exit 0, setup guidance printed) rather than a failure, so CI — which never starts the full application — can never be broken by this mode even if invoked by mistake.

## Technical Context

**Language/Version**: C# / .NET 10 (console project, matching every other project in the solution)

**Primary Dependencies**: None beyond the BCL (`System.Text.Json`, `System.Net.Http.Json`) and a `ProjectReference` to `NexusOps.Contracts` (for `ToolNames`, reflected over rather than hand-copied, so the known-tool set can never drift from the actual curated tool list). No third-party evaluation/testing framework, per the feature description and ROADMAP.md's explicit "dependency-light — no eval frameworks" instruction. `NexusOps.AgentHost` gains no new package — the tool-invocation data already exists on the object the Microsoft Agent Framework returns.

**Storage**: A single checked-in JSON file, `NexusOps.Evaluation/Data/eval-cases.json`, copied to the build output directory. No database.

**Testing**: `dotnet test` (xUnit, via `NexusOps.Tests`) covers dataset-loading/validation logic and the tool-invocation extraction added to `AgentService`, using the same `FakeAgent`/`ITestHarness`-free pattern the existing suite already uses — no live AgentHost, no Azure AI credentials, matching the project's existing credential-free unit-test precedent. The live HTTP path itself is exercised by the tool's own `--validate-only`/skip-detection behavior, not by a separate test project — there is no live infrastructure in CI to test against, mirroring the deferral both feature 005 and 006 already made for their own live-only paths.

**Target Platform**: Linux container/CLI (a `dotnet run`-able console tool, not an Aspire-hosted service — it evaluates a running instance of `NexusOps.AgentHost` from outside, so it is deliberately **not** added as an AppHost resource)

**Performance Goals**: Dataset validation completes in well under 5s (SC-001) with no I/O beyond reading one local file. A live case's HTTP call is bounded by a 60s client timeout — generous headroom above `OrderTools.cs`'s own worst-case saga budgets (`RootCauseTimeout` 12s, `ActionRequestTimeout` 10s) plus real Azure AI Foundry model latency, which the two existing sagas' internal timeouts do not need to account for.

**Constraints**: `--validate-only` MUST NOT touch the network, a message broker, a database, or any credential (FR-005) — enforced structurally by never constructing an `HttpClient` on that path. Live mode MUST NOT be able to fail an automated pipeline (FR-017) — enforced by probing reachability before any dataset prompt is sent and mapping "unreachable" to exit code 0, distinct from the "some cases failed" exit code. Running live mode repeatedly against the same environment MUST NOT itself execute a real-world mutation (FR-019) — already guaranteed by feature 006's own design: `request_order_refund`/`request_order_cancellation` only ever reach `AwaitingApproval` and are never approved by anything in this feature.

**Scale/Scope**: One new console project; one new JSON dataset file (20-30 cases); one additive field (`ToolsInvoked`) threaded through `IAgentService.SendAsync`'s return shape and the `ChatResponse` DTO in `NexusOps.AgentHost`; one new CI step; one README section rewrite.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design — see note after each item.*

- [x] **I. Cognition/Durability boundary** — Not applicable to this feature's own code (it adds no saga, no LLM call, no workflow logic), and it does not touch the boundary in `AgentHost`/`WorkflowOrchestrator` either: the one `AgentHost` change reads data the agent framework already produced (`FunctionCallContent` on the existing `AgentResponse`) — it adds no new cognition or durability logic on either side of the boundary. *Re-checked post-design: `data-model.md` confirms the `ToolsInvoked` extraction is a pure projection over already-returned data, not a new decision point.*
- [x] **II. Curated tool boundaries** — This feature defines no new tool; it reads the existing curated set (`ToolNames`) to validate a dataset against it, which is the opposite direction of a violation — it makes drift between the dataset and the real tool set structurally impossible to miss (FR-007). *Re-checked post-design: `research.md` Decision 4 confirms the tool-name source of truth is reflection over `ToolNames`, never a hand-maintained copy.*
- [x] **III. Approval-gated side effects** — The two mutating dataset cases (`request_order_refund`, `request_order_cancellation`) exercise tools that already never execute anything without a separate, explicit approval call outside this feature (feature 006); this feature adds no approval and calls no approval endpoint, so repeated live runs create only inert `AwaitingApproval` references, never a mutation (FR-019). *Re-checked post-design: `data-model.md`'s dataset schema carries no field or code path capable of calling `/api/approvals/{id}/approve`.*
- [x] **IV. Message-driven service integration** — Not applicable: this feature adds no saga-to-service communication of any kind. Its only network call is HTTP from a standalone console tool to `AgentHost`'s existing public `/api/chat` and `/health` endpoints, the same surface a human `curl` already uses in the documented quickstart. *Re-checked post-design: `contracts/agent-chat-response.md` confirms no new endpoint is added; the console tool is a client of the existing one.*
- [x] **V. Domain pluggability** — `NexusOps.Evaluation` is a wholly new, standalone project with a single `ProjectReference` to `NexusOps.Contracts` (to read `ToolNames`, itself the domain-agnostic core's own contract surface); it is not registered into `AppHost`, `AgentHost`, or `WorkflowOrchestrator`, and deleting it leaves every other project compiling and running unchanged. *Re-checked post-design: `plan.md`'s Project Structure section places 100% of new code under `NexusOps.Evaluation/`, with the sole cross-project touch being the additive `ToolsInvoked` field in `AgentHost`'s own DTO.*
- [x] **VI. Observability first** — Not applicable in the principle's literal sense (this is a CLI tool, not a hosted service, so it has no `/health` endpoint of its own to add) — its console output (per-case pass/fail plus a summary table, FR-014/FR-015) is that observability surface for a one-shot tool. It calls no new service needing `AddServiceDefaults()`/Aspire health-check registration. *Re-checked post-design: `quickstart.md` shows the tool's own reachability probe (`GET /health` against `AgentHost`) reuses that service's existing health endpoint rather than inventing a new one.*

No violations. Complexity Tracking is not needed.

## Project Structure

### Documentation (this feature)

```text
specs/007-evaluation-runner/
├── plan.md                                 ← this file
├── research.md                             ← Phase 0 decisions
├── data-model.md                           ← dataset schema + run-result shapes
├── quickstart.md                           ← Phase 1 output
├── contracts/
│   └── agent-chat-response.md              ← the one additive AgentHost contract change
└── tasks.md                                ← generated by /speckit-tasks
```

### Source Code Changes

```text
NexusOps.Evaluation/                                  ← new console project
├── NexusOps.Evaluation.csproj                         ← Exe, net10.0, ProjectReference → NexusOps.Contracts
├── .gitignore                                          ← bin/, obj/, out/, *.nupkg, *.lscache
├── Program.cs                                          ← arg parsing, mode dispatch, exit codes
├── Data/
│   └── eval-cases.json                                 ← 20-30 labeled cases (CopyToOutputDirectory)
├── EvaluationCase.cs                                   ← case record + dataset (de)serialization
├── DatasetValidator.cs                                 ← --validate-only logic (credential-free)
├── ToolCatalog.cs                                       ← reflects NexusOps.Contracts.ToolNames; tool→path map
├── LiveRunner.cs                                        ← reachability probe, per-case HTTP calls, reporting
└── ConsoleReport.cs                                     ← per-case lines + summary table rendering

NexusOps.AgentHost/
├── Services/IAgentService.cs                            ← SendAsync return shape gains ToolsInvoked
├── Services/AgentService.cs                             ← extracts FunctionCallContent names from AgentResponse.Messages
└── Endpoints/ChatEndpoints.cs                           ← ChatResponse DTO gains ToolsInvoked (additive JSON field)

NexusOps.Tests/
└── Evaluation/                                          ← new: dataset validation + ToolsInvoked extraction tests

NexusOps.sln, NexusOps.deployable.slnf                   ← add NexusOps.Evaluation
.github/workflows/ci.yml                                 ← new step: dotnet run --project NexusOps.Evaluation -- --validate-only
README.md                                                 ← rewrite Evaluation section
CLAUDE.md                                                 ← Current Build State updated
```

**Structure Decision**: Single new console project (`NexusOps.Evaluation`) alongside the existing flat per-project repository layout — no `src/`/`tests/` nesting, matching every other project in this solution (`NexusOps.OrderService`, `NexusOps.WorkflowOrchestrator`, etc., all live at the repo root). It is a client of `AgentHost`'s existing public HTTP surface, not an Aspire-managed resource, so it is intentionally absent from `NexusOps.AppHost/AppHost.cs`.
