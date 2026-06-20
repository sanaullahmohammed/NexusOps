# NexusOps

A domain-agnostic AI agent orchestrator that separates cognition from durable workflow execution. An AI agent handles natural language understanding and tool selection. A message-driven saga orchestrator handles long-running workflows, approval gates, failure recovery, and compensation. The two meet at a clean boundary — the agent publishes commands to a message bus when work needs durability, and queries workflow status when results are needed.

Ships with an **E-Commerce Operations** sample domain. The same orchestration core can be swapped to any domain by replacing the domain services, seed data, and tool definitions.

---

## Architecture

```mermaid
graph TD
    %% Define Nodes
    Foundry[Azure AI Foundry<br/><i>LLM Inference, Eval, Tracing</i>]
    Client([Client])
    
    subgraph Host [Agent Host]
        AH[ASP.NET Core + Agent Framework]
        AH_D[• Session & context management<br/>• Tool selection & reasoning<br/>• Middleware redaction, telemetry]
    end

    subgraph Direct [Direct Tools - HTTP]
        Prod[Product Service]
        Order[Order Service]
        Inv[Inv. Svc]
    end

    subgraph Async [Workflow Tools - AMQP]
        RMQ[[RabbitMQ]]@{ "type" : "queue" }
        subgraph Orch [Workflow Orchestrator]
            MT[MassTransit Sagas]
            MT_D[• OrderInvestigation<br/>• OrderAction]
        end
        PG[(PostgreSQL<br/>saga state)]
    end

    Notify[Notification Service<br/><i>Node.js/TS</i>]

    %% Connections
    Foundry <==> |HTTPS| AH
    Client <==> |HTTP| AH

    AH ==> |Direct Tools HTTP| Prod
    AH ==> |Direct Tools HTTP| Order
    AH ==> |Direct Tools HTTP| Inv

    AH ==> |Workflow Tools AMQP| RMQ
    RMQ ==> MT
    MT -.-> |PostgreSQL| PG
    
    %% Async Back-channel
    MT ==> |AMQP| Prod
    MT ==> |AMQP| Order
    MT ==> |AMQP| Inv
    MT ==> |AMQP| Notify

    %% Styles
    style AH fill:#2d3436,color:#fff,stroke:#fff
    style Foundry fill:#0984e3,color:#fff,stroke:#74b9ff
    style RMQ fill:#e67e22,color:#fff,stroke:#d35400
    style MT fill:#2d3436,color:#fff,stroke:#fff
```

### Two Communication Paths

Every request follows one of two paths, decided by the AI agent:

**Direct Path** — Simple, single-service read queries. Agent Host calls a domain service via HTTP, LLM synthesizes the answer. Fast, synchronous.

**Saga Path** — Complex multi-service investigations or actions with side effects. Agent Host publishes a command to RabbitMQ. A MassTransit saga coordinates the work durably across services, handles partial failure, and gates side effects behind human approval.

---

## Tech Stack

| Component | Technology |
|---|---|
| AI Reasoning | Microsoft Agent Framework |
| Durable Orchestration | MassTransit + RabbitMQ |
| Model Provider & Evaluation | Azure AI Foundry |
| App Orchestration & Observability | Aspire |
| Agent Host | ASP.NET Core |
| Workflow Orchestrator | ASP.NET Core + Entity Framework Core |
| Domain Services (Product, Order, Inventory) | ASP.NET Core Minimal APIs |
| Notification Service | Node.js + Express (TypeScript) + amqplib |
| Saga Persistence | PostgreSQL |
| Message Broker | RabbitMQ |

---

## Sample Domain: E-Commerce Operations

The sample domain simulates the backend systems of an e-commerce platform. A user types natural language queries and the agent handles everything — from simple lookups to multi-service investigations with approval-gated actions.

### Domain Services

**Product Service** — Product catalog: name, description, price, category, rating. Read-only.

**Order Service** — Orders and order items with statuses: placed, confirmed, shipped, delivered, delayed, cancelled, refunded.

**Inventory Service** — Stock levels per product. Supports investigation queries like "why was this order delayed" (stock was zero at time of order).

**Notification Service** — Sends order confirmations, refund confirmations, low-stock alerts. The only service with side effects. Built in Node.js/TypeScript to demonstrate polyglot interop with MassTransit's wire protocol.

### Example Queries

| Query | Path |
|---|---|
| "Show me recent orders for customer Alice" | Direct → Order Service |
| "What's the current stock for wireless headphones?" | Direct → Inventory Service |
| "Why was order #4521 delayed?" | Saga → OrderInvestigationSaga fans out to Order + Inventory + Product services |
| "Refund order #4521 and notify the customer" | Saga → OrderActionSaga with approval gate |

---

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) (or current supported version)
- [Node.js 24+](https://nodejs.org/)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/)
- Azure AI Foundry credentials (endpoint URL, API key, deployment name)

---

## Getting Started

### 1. Clone the repository

```bash
git clone https://github.com/<your-username>/NexusOps.git
cd NexusOps
```

### 2. Configure Azure AI Foundry credentials

Add your credentials to `NexusOps.AgentHost/appsettings.Development.json`:

```json
{
  "AzureAI": {
    "Endpoint": "<your-endpoint>",
    "ApiKey": "<your-api-key>",
    "DeploymentName": "<your-deployment>"
  }
}
```

Alternatively set `AZURE_AI_FOUNDRY_API_KEY` as an environment variable (the endpoint and deployment name still come from appsettings).

### 3. Run the application

```bash
dotnet run --project NexusOps.AppHost
```

This starts all implemented services and Redis with service discovery and telemetry wired automatically via Aspire.

### 4. Open the Aspire Dashboard

The Aspire developer dashboard launches automatically and provides distributed tracing, structured logs, metrics, and container monitoring across all services in a single view.

### 5. Send a query

```bash
# New session — server mints a sessionId
curl -X POST http://localhost:<port>/api/chat \
  -H "Content-Type: application/json" \
  -d '{"prompt": "Show me all delayed orders"}'
# Response: { "response": "...", "sessionId": "<guid>" }

# Continue the conversation
curl -X POST http://localhost:<port>/api/chat \
  -H "Content-Type: application/json" \
  -d '{"prompt": "What is the status of the second one?", "sessionId": "<guid>"}'
```

---

## Key Design Decisions

**LLM for cognition, bus for durability.** The AI agent decides what to do. MassTransit guarantees it gets done. They never cross responsibilities.

**Curated tools over raw Swagger.** The LLM sees high-level tools like `investigate_delayed_order` instead of `GET /orders?status=delayed`. Better tool selection, simpler prompts, safer boundaries.

**Side effects require approval.** Any operation that changes real-world state (refund, notification) goes through the OrderActionSaga with a human approval gate. Read operations auto-execute.

**Saga communication over AMQP.** When sagas dispatch work to domain services, commands flow over RabbitMQ — not HTTP. Full delivery guarantees, retry, and dead-letter handling.

**Domain-pluggable architecture.** The orchestration core (Agent Host, Workflow Orchestrator, Aspire AppHost) is domain-agnostic. The E-Commerce domain is a swappable sample pack. Same architecture works for FinOps, ServiceOps, or SupportOps.

---

## Project Structure

```
NexusOps.AppHost/          # Aspire AppHost — topology, service discovery, env wiring
NexusOps.ServiceDefaults/  # Shared class library — OTEL, health checks, resilience (referenced by all services)
NexusOps.AgentHost/        # ASP.NET Core + Agent Framework — LLM reasoning, tool dispatch, session management
NexusOps.Contracts/        # Shared library — ToolResult<T>, ToolNames, SeedDataConstants, response DTOs
NexusOps.OrderService/     # ASP.NET Core Minimal API — order read operations, in-memory seed data
NexusOps.InventoryService/ # ASP.NET Core Minimal API — inventory read operations, in-memory seed data
NexusOps.ProductService/   # ASP.NET Core Minimal API — product read operations, in-memory seed data
NexusOps.Server/           # ASP.NET Core — serves React frontend, placeholder API (scaffold only)
frontend/                  # React 19 + Vite + TypeScript — chat UI (scaffold only)
.specify/                  # Spec-kit configuration, templates, memory, extensions
specs/                     # Feature specifications, plans, and task lists
```

> **Planned but not yet implemented:** `NexusOps.WorkflowOrchestrator` (MassTransit sagas), Notification Service (Node.js/TS), evaluation runner, full React chat UI.

---

## Saga Designs

### OrderInvestigationSaga

Coordinates parallel data gathering from multiple services for complex read queries.

```
Requested → Dispatching → WaitingForResults → Aggregating → Completed / PartiallyCompleted / TimedOut
```

Fans out to Order, Inventory, and Product services simultaneously. Returns partial results with degradation notes if a service is unavailable.

### OrderActionSaga

Handles operations with real-world side effects through an approval gate.

```
Requested → AwaitingApproval → Approved → Executing → Completed / Compensating
```

Pauses for human approval before executing. Compensates if execution fails partway through (e.g., refund succeeded but notification failed).

---

## Evaluation

An evaluation dataset with test cases covering simple reads, multi-step investigations, action queries, and degraded scenarios. Uses Azure AI Foundry agent evaluators for tool selection accuracy, task completion, and tool call correctness.

```bash
dotnet run --project packages/NexusOps.Evaluation
```

---

## Roadmap

**Implemented:**
- [x] Redis-backed session management (multi-turn conversation continuity, 30-min TTL, 20-turn cap, graceful degradation)
- [x] CI/CD pipeline (build, CodeQL, dependency review, Dependabot)

**Planned:**
- [ ] Workflow Orchestrator (MassTransit sagas, PostgreSQL state)
- [ ] Notification Service (Node.js/TypeScript)
- [ ] React chat UI with AG-UI streaming
- [ ] Integration test suite
- [ ] Evaluation dataset + runner
- [ ] Kubernetes deployment (Helm manifests)
- [ ] Kafka audit/event stream
- [ ] Second domain pack
- [ ] Multiple agent personas

---

## License

[MIT](LICENSE)