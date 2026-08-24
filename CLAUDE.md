# NexusOps — Claude Development Guide

<!-- SPECKIT START -->

**Active Feature Plan**: [specs/003-review-remediation/plan.md](specs/003-review-remediation/plan.md)

## Project Overview

NexusOps is a domain-agnostic AI agent orchestrator that cleanly separates LLM cognition from durable workflow execution. An AI agent (Azure AI Foundry) handles natural language understanding and tool selection; a MassTransit saga orchestrator handles long-running workflows, approval gates, failure recovery, and compensation. They communicate through a message bus boundary.

The repository ships with an **E-Commerce Operations** sample domain. The orchestration core (Agent Host, Workflow Orchestrator, Aspire AppHost) is domain-agnostic — the domain is a swappable layer.

## Architecture

Every request follows one of two paths, chosen by the AI agent:

- **Direct Path** — Agent Host calls a domain service over HTTP. Single-service read queries. Synchronous, fast.
- **Saga Path** — Agent Host publishes a command to RabbitMQ. A MassTransit saga coordinates durable multi-service work, gates side effects behind human approval, and handles compensation.

Two saga designs:
- `OrderInvestigationSaga` — fans out reads across Order, Inventory, and Product services in parallel, returns partial results on degradation.
- `OrderActionSaga` — state-mutating operations (refund, cancel, notify) with a mandatory human approval gate before execution.

## Repository Structure

```
NexusOps.AppHost/          # Aspire AppHost — topology, service discovery, env wiring
NexusOps.AgentHost/        # ASP.NET Core + Microsoft Agent Framework — LLM reasoning, tool dispatch
NexusOps.Contracts/        # Shared library — ToolResult<T>, ToolNames, SeedDataConstants, response DTOs
NexusOps.OrderService/     # ASP.NET Core Minimal API — order read operations, in-memory seed data
NexusOps.InventoryService/ # ASP.NET Core Minimal API — inventory read operations, in-memory seed data
NexusOps.ProductService/   # ASP.NET Core Minimal API — product read operations, in-memory seed data
NexusOps.Server/           # ASP.NET Core — serves React frontend, placeholder API (scaffold only)
NexusOps.Tests/            # xUnit — unit tests across anomalies, sessions, and tool cancellation
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
| Durable Orchestration | MassTransit + RabbitMQ (planned) |
| Saga Persistence | PostgreSQL + EF Core (planned) |
| Frontend | React 19 + Vite + TypeScript |
| Notification Service | Node.js + Express + TypeScript + amqplib (planned) |
| Observability | OpenTelemetry via AgentHost extensions |

## Current Build State

**Implemented:**
- Aspire AppHost wires up AgentHost, Server, all three domain services, and Redis with health checks and service discovery
- AgentHost: Azure AI Foundry agent wired via `AzureOpenAIClient` → `AIAgent`, session-aware `POST /api/chat` endpoint
- **Session Management** (feature #2, corrected by feature #3): Redis-backed conversation history on `POST /api/chat`; client supplies optional `sessionId`, server mints a new UUID v4 if absent; history loaded/saved per request; 30-min sliding TTL; 20-turn cap (oldest-first trim); structured lifecycle logging (`session.created`, `session.history_loaded`, `session.history_saved`, `session.degraded`) with a single hashed session token shared by all emitters. The store reports `Found`/`Missing`/`Unavailable`: a missing session mints a replacement, an unreachable store **preserves the caller's `sessionId`** and runs the turn statelessly. A blank prompt returns 400; an agent failure returns 500 carrying the `sessionId` the user turn was persisted under
- **NexusOps.Contracts**: Shared library with `ToolResult<T>`, `ToolNames`, `SeedDataConstants`, and all response DTOs
- **NexusOps.OrderService**: ASP.NET Core Minimal API — `GET /orders/anomalies`, `GET /orders/{id}`, in-memory seed data (11 orders, dates relative to the current date via `TimeProvider`). An order's anomaly type comes from an explicit `AnomalyReason` on the order, never from the query filter
- **NexusOps.InventoryService**: ASP.NET Core Minimal API — `GET /inventory/alerts`, `GET /inventory/{sku}`, in-memory seed data (15 records)
- **NexusOps.ProductService**: ASP.NET Core Minimal API — `GET /products/{sku}`, `GET /products?category=`, in-memory seed data (15 products)
- **6 Direct-path tools** wired into AgentHost via `AIFunctionFactory.Create(...)`: `investigate_order_anomaly`, `get_order_details`, `get_inventory_alerts`, `get_inventory_level`, `get_product_details`, `list_products_by_category`
- Agent instructions updated with canonical tool routing rules and multi-tool cross-service composition guidance
- OTEL, health checks, and resilience extension methods implemented inline in `NexusOps.AgentHost/Extensions/` (`ServiceDefaultsExtensions.cs`, `OpenTelemetryExtensions.cs`, `HealthCheckExtensions.cs`) — no separate shared project
- Frontend: React + Vite scaffold with Aspire proxy integration (dev proxy falls back to localhost when the AppHost is not the launcher)
- **NexusOps.Tests**: xUnit suite covering anomaly classification and severity, order seed integrity, session resolution across store outages, turn trimming, startup config validation, and tool cancellation. Unit-level only — no Redis or Azure AI dependency, so it runs on fork PRs
- Health endpoints: `/health` is mapped in **all** environments across every service and returns `{"status":"healthy"}` as JSON; `/alive` remains Development-only. AgentHost's `/health` reports only checks tagged `ready` — the Redis check is deliberately excluded, because the service is designed to keep serving when the store is unreachable, and failing readiness for it would remove the pod from rotation exactly when it can still answer

**Planned (from roadmap):**
- Workflow Orchestrator: MassTransit sagas, PostgreSQL state
- Notification Service: Node.js/TypeScript
- React chat UI (replacing scaffold)
- Evaluation dataset + runner
- Integration tests, Kubernetes manifests

## Running the Application

**Prerequisites:** .NET 10 SDK, Node.js 24+, Docker Desktop, Azure AI Foundry credentials.

```bash
# Store the API key in user secrets — appsettings.Development.json is tracked by Git
# cd NexusOps.AgentHost && dotnet user-secrets set "AzureAI:ApiKey" "<your-api-key>"
# Endpoint and DeploymentName may live in appsettings; the key must not

dotnet run --project NexusOps.AppHost
```

Aspire starts all services and Redis. The developer dashboard opens automatically with distributed tracing, logs, and metrics.

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

This matches the pattern used by all existing projects (`NexusOps.AgentHost`, `NexusOps.AppHost`, `NexusOps.Server`, and all domain services). The root `.gitignore` covers IDE/OS/secrets patterns; per-project files cover build output.

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

`NexusOps.sln` includes `frontend.esproj` (Aspire JavaScript project type). When MSBuild processes the full solution it invokes npm, coupling Node tooling into the dotnet job. The `.slnf` solution filter scopes CI dotnet steps to the eight .NET projects, excluding only `frontend.esproj`.

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
