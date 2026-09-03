# Phase 0 Research: Evaluation Runner

## Decision 1: How the runner learns which tool the agent invoked

**Decision**: Extend `NexusOps.AgentHost`'s existing `IAgentService.SendAsync` return shape and the `POST /api/chat` `ChatResponse` DTO with an additive `ToolsInvoked` (`IReadOnlyList<string>`) field, populated by scanning the `AgentResponse.Messages` the Microsoft Agent Framework already returns for `Microsoft.Extensions.AI.FunctionCallContent` items and collecting their `Name`.

**Rationale**: Confirmed by reflecting over the installed `Microsoft.Agents.AI`/`Microsoft.Agents.AI.Abstractions` 1.19.0 packages: `AIAgent.RunAsync(...)` returns `Task<AgentResponse>`, and `AgentResponse.Messages` is `IList<ChatMessage>`; `ChatMessage.Contents` is `IList<AIContent>`, and `Microsoft.Extensions.AI.FunctionCallContent` (a concrete `AIContent`) carries the invoked function's `Name` and `Arguments`. `AgentService.SendAsync` already holds the full `AgentResponse` (`agentResponse` in the existing code) before collapsing it to `agentResponse.ToString()` — the tool-call data is already there, just discarded. No new call, no new round trip, no new package.

**Alternatives considered**:
- *A separate "debug"/"trace" endpoint on AgentHost returning tool-call history.* Rejected: doubles the request-response contract this feature needs to reason about, and duplicates data the existing endpoint's underlying object already carries in the same call.
- *Scraping OpenTelemetry traces from the Aspire dashboard.* Rejected: couples the evaluator to the tracing backend's storage/query surface, is not credential-free/offline-friendly, and is far heavier than reading a field already in memory.
- *Having the evaluator ask the agent to self-report the tool it used, in natural language.* Rejected: reintroduces exactly the unreliable-parsing problem this feature exists to avoid — the whole point is to observe the actual function call, not a paraphrase of it.

## Decision 2: Live-mode credential/reachability detection

**Decision**: Before sending any dataset prompt, live mode issues one `GET {baseUrl}/health` with a short timeout (3s). A non-success response, a connection failure, or a timeout is treated identically: live evaluation reports a skipped outcome with setup guidance and exits 0. Only a healthy response allows dataset prompts to be sent.

**Rationale**: `AgentHost`'s `AddAgentServices` (`AgentServiceExtensions.cs`) throws `InvalidOperationException` at *startup* if `AzureAI:Endpoint`, `AzureAI:DeploymentName`, or `AzureAI:ApiKey` is missing — the process never comes up, and nothing binds to its port. A reachable `/health` is therefore already a reliable proxy for "credentials are configured and the process started successfully," with no need for the evaluator to read `AgentHost`'s own configuration, user secrets, or environment out of band. This is exactly the signal FR-016/FR-017 call for, and it is the same `/health` endpoint every other service in this repository already exposes via `AddServiceDefaults()`/`MapDefaultEndpoints()` (Constitution VI) — no new surface needed.

**Alternatives considered**:
- *Check for an `AZURE_AI_FOUNDRY_API_KEY` environment variable or read AgentHost's user secrets directly.* Rejected: the evaluator is typically a separate process/session from AgentHost (e.g., a different terminal, or invoked against an Aspire-orchestrated instance whose environment isn't the evaluator's own) — env var presence in *this* process proves nothing about whether AgentHost itself is configured and running.
- *Attempt the first dataset prompt and treat any failure as "skip."* Rejected: conflates a genuine per-case failure (FR-018, e.g., a timeout on one flaky case) with total unreachability, and violates FR-016's requirement that reachability be determined *before* any dataset prompt is sent.

## Decision 3: AgentHost base URL configuration

**Decision**: `--base-url <url>` CLI argument, falling back to the `AGENTHOST_BASE_URL` environment variable, falling back to `http://localhost:5186` (AgentHost's own `http` launch profile in `Properties/launchSettings.json`).

**Rationale**: A fixed default only works for the common case of `dotnet run --project NexusOps.AgentHost` invoked directly. When AgentHost is launched through `NexusOps.AppHost` (Aspire), its externally-reachable port is assigned dynamically and is visible on the Aspire dashboard — no fixed default can predict it. Making the URL overridable via both a flag and an environment variable covers both the direct-run default and the Aspire-orchestrated case without adding any dependency (both are read with the BCL alone); this is documented explicitly in quickstart.md and README.md so a developer running via Aspire knows to pass it.

**Alternatives considered**:
- *Read AppHost's service-discovery configuration to resolve `agent-host` automatically.* Rejected: would require the evaluator to either run inside the Aspire app model (making it an AppHost resource, which Constitution V's domain-agnostic core and this feature's own "evaluates a running instance from outside" framing both argue against) or reimplement Aspire's discovery protocol for a console tool — far more than a 20-30-case dataset runner warrants.

## Decision 4: Source of truth for "recognized, supported tool names"

**Decision**: `ToolCatalog` reflects over `NexusOps.Contracts.ToolNames`'s public `const string` fields at startup (excluding the `*Description` constants, which are not tool names), building the known-tool set from that. A separate, hand-maintained `Dictionary<string, string>` in `NexusOps.Evaluation` maps each of those tool names to its Direct/Saga path, since `ToolNames` itself carries no path metadata.

**Rationale**: FR-007 requires validation to reject any dataset case naming a tool the project doesn't actually curate. Reflecting over the real constants (via a `ProjectReference`, not a copy-pasted string list) makes it structurally impossible for the known-tool set to drift out of sync with `NexusOps.Contracts` as tools are added or removed — the exact drift risk a hand-maintained list would otherwise reintroduce. The tool→path map has no existing authoritative source to reflect over (path is implicit in *how* each tool is wired in `ToolHandlerExtensions.cs`/`OrderTools.cs`, not declared as data), so it is asserted directly in code, reviewed like any other test fixture, and is exactly the kind of dataset-editing mistake (FR-008) this feature's own validation is designed to catch if it ever falls out of sync.

**Alternatives considered**:
- *Hand-copy the 9 tool name strings into the dataset validator.* Rejected: reintroduces the drift risk reflection eliminates for free.
- *Derive the path automatically from which HTTP client vs. which MassTransit request client each tool handler uses, via reflection over `OrderTools`/`InventoryTools`/`ProductTools`.* Rejected: needlessly fragile (couples validation to private implementation details of tool handler classes) for a 9-entry mapping that changes only when a new tool is added — at which point the dataset needs a new case anyway, and the map is a two-line diff next to it.

## Decision 5: Argument parsing and exit-code contract

**Decision**: Hand-rolled parsing over `string[] args` (no `System.CommandLine` or similar) recognizing `--validate-only`, `--dataset <path>`, `--base-url <url>`. Exit codes: `0` for "validation passed," "all live cases passed," or "live run skipped (AgentHost unreachable)"; `1` for "validation failed" or "one or more live cases failed."

**Rationale**: The feature description and ROADMAP.md both require "no eval frameworks" and "dependency-light"; three boolean/string flags do not justify a parsing library. Collapsing "skipped" into exit code `0` alongside "all passed" is deliberate and is the crux of FR-017/SC-004: CI must never observe a non-zero exit from a credential-absent run. The distinction between skipped and passed is carried in the console output (a distinct "SKIPPED" banner vs. a summary table), not in the exit code — an automated pipeline only needs the pass/fail signal, and a human reading the console output can always tell the two apart.

**Alternatives considered**:
- *A third, distinct exit code for "skipped."* Rejected: adds a code an automated pipeline would need to special-case for no benefit — CI only ever needs to know "did this break the build," and `--validate-only` (a separate, always-non-skippable mode) is what CI actually runs per FR-021, so live mode's exit code is a local-developer convenience, not a CI contract.

## Decision 6: Dataset case count, coverage, and format

**Decision**: 24 cases (within the specified 20-30 range) in `NexusOps.Evaluation/Data/eval-cases.json`, a plain JSON array, covering all 9 curated tools at least twice each with varied realistic phrasing (mirroring CLAUDE.md's own record of "five prompt-routing shapes" manually verified for features 005/006), plus a few edge-case prompts (a nonexistent order ID, an out-of-range category) whose *tool selection* is still deterministic even though the *tool's result* will be a not-found/empty response — tool routing, not result correctness, is what this feature scores (spec.md's Assumptions).

**Rationale**: FR-003/FR-004/SC-002 require every curated tool represented and both paths covered; going beyond single-coverage per tool (2+ phrasings) directly tests the routing-robustness concern CLAUDE.md's feature 005/006 completion notes already flag as the thing worth checking ("all five prompt-routing shapes ... selected the correct tool with zero regression"). Plain JSON (not YAML/CSV) needs no additional parsing package beyond `System.Text.Json`, already in the BCL.

**Alternatives considered**:
- *One case per tool (9 total).* Rejected: falls short of the 20-30 case range and each tool's routing is health-checked by exactly one phrasing, offering far weaker protection against an agent-instructions regression that breaks one specific phrasing while leaving another intact.
