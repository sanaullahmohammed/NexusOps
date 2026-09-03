# NexusOps — Claude Development Guide

<!-- SPECKIT START -->

**Active Feature Plan**: [specs/007-evaluation-runner/plan.md](specs/007-evaluation-runner/plan.md)

## Project Overview

NexusOps is a domain-agnostic AI agent orchestrator that cleanly separates LLM cognition from durable workflow execution. An AI agent (Azure AI Foundry) handles natural language understanding and tool selection; a MassTransit saga orchestrator handles long-running workflows, approval gates, failure recovery, and compensation. They communicate through a message bus boundary.

The repository ships with an **E-Commerce Operations** sample domain. The orchestration core (Agent Host, Workflow Orchestrator, Aspire AppHost) is domain-agnostic — the domain is a swappable layer.

## Architecture

Every request follows one of two paths, chosen by the AI agent:

- **Direct Path** — Agent Host calls a domain service over HTTP. Single-service read queries. Synchronous, fast.
- **Saga Path** — Agent Host publishes a command to RabbitMQ. A MassTransit saga coordinates durable multi-service work, gates side effects behind human approval, and handles compensation.

Two saga designs:
- `OrderInvestigationSaga` — fans out reads across Order, Inventory, and Product services in parallel, returns partial results on degradation. **Implemented** (feature 005).
- `OrderActionSaga` — state-mutating operations (refund, cancel) with a mandatory human approval gate before execution; a notification is published on every terminal outcome. **Implemented** (feature 006).

## Repository Structure

```
NexusOps.AppHost/          # Aspire AppHost — topology, service discovery, env wiring
NexusOps.ServiceDefaults/  # Shared class library — OTEL, health checks, resilience, service discovery
NexusOps.AgentHost/        # ASP.NET Core + Microsoft Agent Framework — LLM reasoning, tool dispatch
NexusOps.Contracts/        # Shared library — ToolResult<T>, ToolNames, SeedDataConstants, response DTOs
NexusOps.OrderService/     # ASP.NET Core Minimal API — order read operations, in-memory seed data
NexusOps.InventoryService/ # ASP.NET Core Minimal API — inventory read operations, in-memory seed data
NexusOps.ProductService/   # ASP.NET Core Minimal API — product read operations, in-memory seed data
NexusOps.WorkflowOrchestrator/ # MassTransit v8 + RabbitMQ saga host — OrderInvestigationSaga, OrderActionSaga, PostgreSQL/EF Core state
NexusOps.Server/           # ASP.NET Core — serves React frontend, placeholder API (scaffold only)
NexusOps.Tests/            # xUnit — unit tests across anomalies, sessions, tool cancellation, and both sagas
notification-service/      # Node.js + TypeScript + amqplib — logs a simulated email per OrderActionSaga terminal outcome
frontend/                  # React 19 + Vite + TypeScript — chat UI (scaffold only)
.specify/                  # Spec-kit configuration, templates, memory, extensions
```

> **Note:** `NexusOps.Server` and `frontend/` are currently scaffold placeholders. The implementation plan calls for replacing the weather-forecast API with a proper BFF and building out the React chat UI.

## Tech Stack

| Layer | Technology |
|---|---|
| AI Reasoning | Microsoft Agent Framework (`Microsoft.Agents.AI`) |
| Model Provider | Azure AI Foundry (AzureOpenAIClient) |
| App Orchestration | .NET Aspire |
| Agent Host | ASP.NET Core Minimal APIs |
| Session Store | Redis via `IDistributedCache` (Aspire-managed) |
| Durable Orchestration | MassTransit v8 + RabbitMQ |
| Saga Persistence | PostgreSQL + EF Core |
| Frontend | React 19 + Vite + TypeScript |
| Notification Service | Node.js + Express + TypeScript + amqplib (planned) |
| Observability | OpenTelemetry via `NexusOps.ServiceDefaults` (shared class library) |

## Current Build State

**Implemented:**
- Aspire AppHost wires up AgentHost, Server, all three domain services, WorkflowOrchestrator, notification-service, Redis, RabbitMQ, and PostgreSQL with health checks and service discovery
- **`NexusOps.Evaluation`** (feature 007): a dependency-light console project (no eval framework — `System.Text.Json`/`HttpClient` only) evaluating AgentHost's tool-routing accuracy against a checked-in JSON dataset (`Data/eval-cases.json`, 24 cases), each labeled with an expected tool and its Direct/Saga path. `--validate-only` is fully credential-free and offline: it loads the dataset, then checks schema completeness, unique case IDs, that every `expectedTool` is reflected live from `NexusOps.Contracts.ToolNames` (so the known-tool set can never drift from the real one), and that every `expectedPath` is valid and consistent with its tool's actual path — reporting every defect found in one pass, not just the first. This is the mode CI now runs on every push/PR (`.github/workflows/ci.yml`'s new `Validate evaluation dataset` step), right after `dotnet test`. Live mode (the default) first probes AgentHost's own `/health` endpoint before sending any dataset prompt; when nothing answers there, it prints a `SKIPPED` banner with exact setup steps and exits `0` — never a failure — which is what makes it safe for live mode to be the default at all. When AgentHost is reachable, each case is sent as a fresh, session-less turn to `POST /api/chat`, and the tool the agent actually invoked is compared against the case's expectation, then a per-case pass/fail line plus a total/passed/failed/pass-rate summary is printed, exiting `1` only if a case failed. The one change this feature makes to an existing service: `POST /api/chat`'s response gains an additive `toolsInvoked` field, populated in `AgentService.SendAsync` by scanning the `AgentResponse` the Microsoft Agent Framework already returns for `FunctionCallContent` items — no new endpoint, no second model call, no behavior change to the existing `response`/`sessionId` fields. `dotnet test`: 159/159 passing (21 new tests: dataset validation, tool-invocation extraction, and live-runner reachability/scoring, all credential-free via a fake `HttpMessageHandler`, matching this project's existing precedent — no live AgentHost needed to test the tool itself)
- **`OrderActionSaga`** (feature 006): this system's first mutating, approval-gated saga, added to `NexusOps.WorkflowOrchestrator` alongside (not replacing) feature 005's `OrderInvestigationSaga` — both are registered additively via their own `AddOrderInvestigationSaga(...)`/`AddOrderActionSaga(...)` calls on one bus, each independently deletable. Two curated tools, `request_order_refund` and `request_order_cancellation`, publish a request that the saga validates (reusing feature 005's `RequestOrderFinding` contract) and parks in `AwaitingApproval` with a reference GUID — no mutation ever happens at this point. `POST /api/approvals/{id}/approve` and `/reject` on AgentHost (backed by MassTransit request/response, not agent tools — approval is deliberately outside the LLM's reach) are the only path to a decision; approval blocks until a plain `OrderActionExecutionConsumer` finishes executing (refund: one dependency, `NexusOps.OrderService`; cancellation: two, `OrderService` then `NexusOps.InventoryService`'s stock restock) and returns the real outcome (`Executed`, `Failed`, or `FailedAndCompensated`). Every terminal outcome publishes `NotificationRequested`, consumed by a new `notification-service/` (minimal Node.js + TypeScript + amqplib, no framework) that logs one structured JSON line per outcome. A refund amount is validated (`> 0` and `<= order total`) and actually tracked (`Order.RefundedAmount`/`OrderSummary.RefundedAmount`), not just quoted back; an actioned (cancelled/refunded) order drops out of `/orders/anomalies` without teaching `AnomalySelector` about `Status` (it stays keyed on `AnomalyReason` alone, so a seed order born-`Cancelled` because of its own anomaly, e.g. ORD-0009, still correctly appears); a validation-leg outage (`OrderService` unreachable) is reported distinctly from a confirmed-nonexistent order (`OrderActionStatus.Unavailable` vs. `NotFound`), mirroring feature 005's own `SourceFindingStatus` distinction. Both domain services gained an in-memory mutation overlay (`OrderMutationOverlay`/`InventoryMutationOverlay`) layered on top of their previously-stateless seed data, applied at every existing read path too, so a refund/cancellation is visible through `GET /orders/{id}` and `investigate_order_root_cause` alike; `ExecuteInventoryRestockConsumer` guards against redelivery double-crediting the same restock via a per-`CorrelationId` idempotency check. The saga's `OrderActionSagaState` receive endpoint is configured manually in `Program.cs` (excluded from the generic `ConfigureEndpoints` sweep via a scoped `Exclude<OrderActionSagaState>()` filter) so it can carry MassTransit's EF Core transactional outbox (`UseEntityFrameworkOutbox<OrderActionDbContext>`) — `UseBusOutbox()` alone does not cover publishes made from *inside* a consume context, which is how the saga's own `Publish(BeginOrderActionExecution)` runs; without the endpoint-level outbox, two genuinely concurrent `Approve` calls could each publish once even though only one attempt's state transition commits. If cancellation's inventory leg *confirms* failure (a fault, not a timeout) after the order leg already succeeded, the order is reverted via a compensating call; a timeout on that leg is left uncompensated and reported honestly as unconfirmed, since compensating an operation that might still succeed risks a worse, silently-inconsistent state (inventory restocked, order un-reverted) than an honest "couldn't confirm." `notification-service` reconnects to RabbitMQ with backoff on a dropped connection and its `/health` reflects live AMQP connectivity, mirroring `WorkflowOrchestrator`'s own bus-dependent readiness precedent. Verified live end-to-end with real Azure AI credentials, RabbitMQ, and PostgreSQL, including a 10-way genuinely concurrent `/approve` race against the same reference (exactly 1 execution, 9 `AlreadyDecided` with the real outcome surfaced): a refund request left the order unchanged until approved, then correctly showed `refunded` with the right `refundedAmount`; a rejected cancellation left the order untouched and a later approval attempt correctly reported `AlreadyDecided`; a cancellation approved with `InventoryService` stopped correctly reported `FailedAndCompensated` and reverted the order to its prior status; `notification-service` stopped mid-flow durably queued its notification and logged it correctly once restarted, without ever blocking the approval itself; killing and relaunching `NexusOps.WorkflowOrchestrator` mid-lifecycle left a pending reference still approvable, proving Postgres persistence (not in-memory state) is what survives a restart; all five prompt-routing shapes (refund, cancellation, broad anomaly listing, plain status, root-cause "why") selected the correct tool with zero regression. `dotnet test`: 138/138 passing
- **NexusOps.WorkflowOrchestrator** (feature 005): new MassTransit v8 host running `OrderInvestigationSaga`, a `MassTransitStateMachine` persisted in PostgreSQL via EF Core with optimistic concurrency (`RowVersion` mapped to Postgres `xmin`). Order-specific code lives entirely in `NexusOps.WorkflowOrchestrator/OrderInvestigation/`, registered into the domain-agnostic host via a single `AddOrderInvestigationSaga(...)` call — deleting that folder and call leaves the host compiling and running with no domain knowledge. A separate `InvestigationFanOutConsumer` (not the saga itself) fans out three parallel/sequenced MassTransit request/response calls to new consumers in Order/Inventory/Product services (`RequestOrderFindingConsumer`, `RequestInventoryFindingConsumer`, `RequestProductFindingConsumer`), each bounded by its own 5s timeout; the saga reacts to the resulting `*FindingReported` events, finalizing as `Complete`, `Degraded` (naming which source is unavailable/timed out), or `Failed` (only when the order itself can't be identified). No approval gate — this saga is read-only
- New Saga-path tool `investigate_order_root_cause` (alongside the unchanged Direct-path `investigate_order_anomaly`): AgentHost holds a MassTransit `IRequestClient<InvestigateOrderRootCause>`; the saga responds by resolving the request's captured `ResponseAddress`/`RequestId` rather than `RespondAsync`, since it finalizes from a different consume context than the one that started it. Agent routing instructions now distinguish three shapes: broad anomaly listing, a specific order's plain status, and a specific order's cross-service "why" investigation. Verified live end-to-end with real Azure AI credentials, real RabbitMQ, and real PostgreSQL: a healthy investigation correctly cites the causing SKU's stockout; killing a domain service mid-investigation correctly returns a `Degraded` result naming the unavailable source rather than hanging or erroring; all three routing shapes select the correct tool
- AgentHost: Azure AI Foundry agent wired via `AzureOpenAIClient` → `AIAgent`, session-aware `POST /api/chat` endpoint
- **Session Management** (feature #2, corrected by feature #3): Redis-backed conversation history on `POST /api/chat`; client supplies optional `sessionId`, server mints a new UUID v4 if absent; history loaded/saved per request; 30-min sliding TTL; 20-turn cap (oldest-first trim); structured lifecycle logging (`session.created`, `session.history_loaded`, `session.history_saved`, `session.degraded`) with a single hashed session token shared by all emitters. The store reports `Found`/`Missing`/`Unavailable`: a missing session mints a replacement, an unreachable store **preserves the caller's `sessionId`** and runs the turn statelessly. A blank prompt returns 400; an agent failure returns 500 carrying the `sessionId` the user turn was persisted under
- **NexusOps.Contracts**: Shared library with `ToolResult<T>`, `ToolNames`, `SeedDataConstants`, and all response DTOs
- **NexusOps.OrderService**: ASP.NET Core Minimal API — `GET /orders/anomalies`, `GET /orders/{id}`, in-memory seed data (11 orders, dates relative to the current date via `TimeProvider`). An order's anomaly type comes from an explicit `AnomalyReason` on the order, never from the query filter
- **NexusOps.InventoryService**: ASP.NET Core Minimal API — `GET /inventory/alerts`, `GET /inventory/{sku}`, in-memory seed data (15 records)
- **NexusOps.ProductService**: ASP.NET Core Minimal API — `GET /products/{sku}`, `GET /products?category=`, in-memory seed data (15 products)
- **6 Direct-path tools** wired into AgentHost via `AIFunctionFactory.Create(...)`: `investigate_order_anomaly`, `get_order_details`, `get_inventory_alerts`, `get_inventory_level`, `get_product_details`, `list_products_by_category`; plus **2 Saga-path tools**: `investigate_order_root_cause` (feature 005, read-only) and, as of feature 006, `request_order_refund`/`request_order_cancellation` (mutating, approval-gated — see above)
- Agent instructions updated with canonical tool routing rules, multi-tool cross-service composition guidance, and (feature 006) an explicit constraint that a mutation tool's result must always be reported as pending approval, never as completed
- **NexusOps.ServiceDefaults**: shared class library — `AddServiceDefaults()`, `ConfigureOpenTelemetry()`, `AddDefaultHealthChecks()`, `MapDefaultEndpoints()` in `namespace Microsoft.Extensions.Hosting`; referenced by all five service projects
- Frontend: React + Vite scaffold with Aspire proxy integration (dev proxy falls back to localhost when the AppHost is not the launcher)
- **NexusOps.Tests**: xUnit suite covering anomaly classification and severity, order seed integrity, session resolution across store outages, turn trimming, startup config validation, tool cancellation, the investigation saga (feature 005), the action saga's full approve/reject/execute/compensate lifecycle plus concurrent-decision safety (feature 006), and (feature 007) `NexusOps.Evaluation`'s dataset validation, tool-invocation extraction, and live-runner reachability/scoring. Unit-level only — no Redis, Postgres, RabbitMQ, or Azure AI dependency (MassTransit's in-memory test harness, a fake `HttpMessageHandler`), so it runs on fork PRs; 159/159 passing
- Health endpoints: `/health` is mapped in **all** environments across every service and returns `{"status":"healthy"}` as JSON; `/alive` remains Development-only. AgentHost's `/health` reports only checks tagged `ready` — the Redis check is deliberately excluded, because the service is designed to keep serving when the store is unreachable, and failing readiness for it would remove the pod from rotation exactly when it can still answer

**Planned (from roadmap):**
- React chat UI (replacing scaffold)
- `Aspire.Hosting.Testing` integration tests covering both sagas end-to-end against real infrastructure, Kubernetes manifests

## Running the Application

**Prerequisites:** .NET 10 SDK, Node.js 24+, Docker Desktop, Azure AI Foundry credentials.

```bash
# Store the API key in user secrets — appsettings.Development.json is tracked by Git
# cd NexusOps.AgentHost && dotnet user-secrets set "AzureAI:ApiKey" "<your-api-key>"
# Endpoint and DeploymentName may live in appsettings; the key must not

dotnet run --project NexusOps.AppHost
```

Aspire starts all services, Redis, RabbitMQ, and PostgreSQL. The developer dashboard opens automatically with distributed tracing, logs, and metrics.

```bash
# Send a chat request (new session — server mints a sessionId)
curl -X POST http://localhost:<port>/api/chat \
  -H "Content-Type: application/json" \
  -d '{"prompt": "Show me all delayed orders"}'
# Response: { "response": "...", "sessionId": "<guid>" }

# Continue the conversation (supply the sessionId from the previous response)
curl -X POST http://localhost:<port>/api/chat \
  -H "Content-Type: application/json" \
  -d '{"prompt": "What is the status of the second one?", "sessionId": "<guid>"}'
```

## Project Conventions

**Adding a new .NET project**

Every new `.csproj` project must have a `.gitignore` file in its project directory containing at minimum:

```
bin/
obj/
out/
*.nupkg
*.lscache
```

This matches the pattern used by all existing projects (`NexusOps.AgentHost`, `NexusOps.AppHost`, `NexusOps.ServiceDefaults`, `NexusOps.Server`, and all domain services). The root `.gitignore` covers IDE/OS/secrets patterns; per-project files cover build output.

---

## Key Design Decisions

- **LLM for cognition, bus for durability.** The agent decides; MassTransit guarantees. Responsibilities never cross.
- **Curated tools over raw Swagger.** Agent sees `investigate_order_anomaly`, not `GET /orders?status=delayed`. Better routing, safer boundaries.
- **Side effects require approval.** Any mutation goes through `OrderActionSaga` with a human approval gate. Reads auto-execute.
- **AMQP for saga-to-service communication.** Sagas dispatch commands to domain services via RabbitMQ — full delivery guarantees, retry, dead-letter.
- **Domain-pluggable core.** AppHost + AgentHost + WorkflowOrchestrator are domain-agnostic. E-Commerce is a replaceable sample pack.
- **Session history as a cache, not a store.** Conversation history lives in Redis with a sliding TTL; the agent is always stateless on the compute side. Store failures degrade gracefully to stateless operation — the endpoint never returns 5xx due to Redis unavailability.

## Configuration

Azure AI settings bind from the `AzureAI` configuration section:
- `AzureAI:Endpoint` — Cognitive Services endpoint URL
- `AzureAI:ApiKey` — **store in user secrets**, never in the tracked `appsettings.Development.json`. AgentHost carries a `UserSecretsId`; `dotnet user-secrets set "AzureAI:ApiKey" "<key>"`. Falls back to the `AZURE_AI_FOUNDRY_API_KEY` environment variable for CI and containers
- `AzureAI:DeploymentName` — model deployment name
- `AzureAI:AgentName` / `AzureAI:AgentInstructions` — optional overrides (defaults in `AzureAIOptions.cs`)

Session management is configured via `Session` section in appsettings (class: `ConversationSessionOptions`):
- `Session:MaxTurns` — maximum turns retained per session (default: `20`)
- `Session:SlidingExpirationMinutes` — inactivity window before Redis evicts the session (default: `30`)

Both are validated at startup via `ValidateOnStart`; a value ≤ 0 for either key prevents the application from starting, naming the offending key.

## CI/CD

Three GitHub Actions workflows under `.github/workflows/`:

| File | Trigger | Purpose |
|---|---|---|
| `ci.yml` | push/PR → `master` | Build + test (.NET and frontend in parallel) |
| `codeql.yml` | push/PR → `master`, weekly | SAST static analysis (C# + TypeScript) |
| `dependency-review.yml` | PR → `master` | Block PRs introducing high/critical CVEs |

Dependabot config at `.github/dependabot.yml` keeps NuGet, npm, and GitHub Actions versions current (weekly, Monday).

**Solution filter — `NexusOps.deployable.slnf`**

`NexusOps.sln` includes `frontend.esproj` (Aspire JavaScript project type). When MSBuild processes the full solution it invokes npm, coupling Node tooling into the dotnet job. The `.slnf` solution filter scopes CI dotnet steps to the nine .NET projects, excluding only `frontend.esproj`.

`NexusOps.AppHost` was previously excluded on the grounds that it is a dev-only Aspire orchestrator. That rationale was wrong in practice: Dependabot is rooted at `/` and does update the AppHost's version-sensitive Aspire packages (commit `244b47d` bumped Aspire 13.3.5 → 13.4.3), so excluding it meant those updates landed in a project no workflow ever compiled. The AppHost has no project reference to `frontend.esproj` — it reaches the frontend through `AddViteApp` with a path — so including it does not couple npm into the dotnet job. Local development is unaffected; open `NexusOps.sln` as normal.

**Azure AI credentials in CI**

`appsettings.Development.json` contains a placeholder API key. GitHub runners use `Production` environment, so that file is never loaded. `dotnet build` and `dotnet test` never start the ASP.NET Core host, so the credential validation in `AgentServiceExtensions.cs` is never triggered. No Azure AI secrets are needed in CI for build or unit tests. When integration tests are added, isolate them in a job gated to `push` to `master` only (not fork PRs).

## Spec-Kit Workflow

This project uses [Spec-Kit](https://github.com/github/spec-kit) for structured feature development. Artifacts live in `.specify/`.

**Standard feature workflow:**

1. `/speckit-specify` — write the feature specification (`spec.md`)
2. `/speckit-clarify` — resolve ambiguities in the spec
3. `/speckit-plan` — generate implementation plan (`plan.md`)
4. `/speckit-tasks` — break plan into ordered tasks (`tasks.md`)
5. `/speckit-implement` — execute tasks
6. `/speckit-analyze` — cross-artifact consistency check
7. `/speckit-checklist` — generate completion checklist

**Git integration (via spec-kit git extension):**
- `/speckit-git-feature` — create a numbered feature branch before specifying
- `/speckit-git-commit` — auto-commit after each spec-kit command
- `/speckit-git-validate` — verify branch naming conventions

**Constitution:** The project constitution (principles, constraints, governance) lives at `.specify/memory/constitution.md`. Fill this in via `/speckit-constitution` before starting feature work — it shapes all downstream artifacts.

<!-- SPECKIT END -->
