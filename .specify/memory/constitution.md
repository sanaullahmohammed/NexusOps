<!--
SYNC IMPACT REPORT
==================
Version change: (template) → 1.0.0
Modified principles: N/A — initial ratification from blank template
Added sections:
  - Core Principles (I–VI)
  - Tech Stack Constraints
  - Development Workflow
  - Governance
Templates updated:
  - .specify/templates/plan-template.md ✅ Constitution Check gates documented below
  - .specify/templates/spec-template.md ✅ No structural changes required
  - .specify/templates/tasks-template.md ✅ No structural changes required
Deferred TODOs: none
-->

# NexusOps Constitution

## Core Principles

### I. Separation of Concerns: Cognition vs. Durability

The AI agent (Agent Host) owns reasoning, tool selection, and natural language understanding.
MassTransit (Workflow Orchestrator) owns durable execution, retry, failure recovery, and compensation.
These responsibilities MUST NEVER cross:

- Agent Host MUST NOT contain saga state machines or workflow coordination logic.
- Workflow Orchestrator MUST NOT contain LLM calls, prompt engineering, or tool definitions.
- The boundary between them is the message bus. The agent publishes commands; it does not call
  the orchestrator directly.

### II. Curated Tool Boundaries

The agent MUST interact with the domain exclusively through curated, high-level tool definitions.
Raw HTTP proxy tools (e.g., a generic `GET /orders` passthrough) are prohibited.

- Tool definitions MUST be owned by the `NexusOps.Contracts` package.
- Tool names MUST express intent at the domain level (e.g., `investigate_order_anomaly`,
  not `get_order_by_id`).
- Each tool MUST map unambiguously to either the Direct path (single-service HTTP read) or
  the Saga path (multi-service or state-mutating workflow).
- Adding a new domain capability MUST start with a tool definition in Contracts, not a service endpoint.

### III. Approval-Gated Side Effects

Any operation that mutates real-world state (refunds, cancellations, order modifications,
notifications) MUST route through `OrderActionSaga` with a human approval gate.

- The agent MUST inform the user that the request is pending approval — never claim the action
  was completed autonomously.
- Read-only operations (Direct path) are exempt from approval gating.
- No domain service MUST accept a mutation command directly from Agent Host. All mutations
  MUST arrive via AMQP from a saga.

### IV. Message-Driven Service Integration

All communication between the Workflow Orchestrator and domain services MUST use AMQP (RabbitMQ
via MassTransit). Direct HTTP calls from saga code to domain services are prohibited.

- Domain services MUST expose MassTransit consumers for saga-dispatched commands.
- Full delivery guarantees, retry policies, and dead-letter handling MUST be configured on
  all saga-to-service queues.
- The Notification Service (Node.js) MUST interoperate with MassTransit's wire protocol (AMQP)
  to remain part of the same message fabric.

### V. Domain Pluggability

The orchestration core — `NexusOps.AppHost`, `NexusOps.AgentHost`, and
`NexusOps.WorkflowOrchestrator` — MUST remain domain-agnostic.

- Domain-specific code (service implementations, tool definitions, seed data, agent instructions)
  MUST be isolated so it can be swapped without modifying the orchestration core.
- New domain packs MUST be additive: adding a domain MUST NOT require changes to AppHost,
  AgentHost, or WorkflowOrchestrator internals.
- The E-Commerce Operations domain is a sample pack, not a core dependency.

### VI. Observability First

Every service MUST emit structured telemetry. Observability is not optional.

- All .NET services MUST call `AddServiceDefaults()` (from `NexusOps.ServiceDefaults`) on startup.
  This wires OpenTelemetry traces, metrics, structured logging, and health checks.
- Every service MUST expose a `/health` HTTP health check endpoint.
- No service ships without health checks registered in Aspire AppHost via `WithHttpHealthCheck`.
- The Node.js Notification Service MUST emit structured JSON logs compatible with the
  Aspire telemetry pipeline.

## Tech Stack Constraints

The following technology choices are fixed for the orchestration core and MUST NOT be substituted
without a constitution amendment:

| Concern | Mandated Technology |
|---|---|
| AI Reasoning | Microsoft Agent Framework (`Microsoft.Agents.AI`) |
| Model Provider | Azure AI Foundry (AzureOpenAIClient) |
| App Orchestration | .NET Aspire |
| Durable Orchestration | MassTransit + RabbitMQ |
| Saga Persistence | PostgreSQL via Entity Framework Core |
| Agent Host / Services | ASP.NET Core Minimal APIs |
| Notification Service | Node.js + TypeScript + amqplib |
| Frontend | React 19 + Vite + TypeScript |
| Observability | OpenTelemetry (via Aspire ServiceDefaults) |

Domain packs MAY introduce additional libraries provided they do not conflict with the above.

## Development Workflow

- Features MUST be developed on a dedicated branch created via `/speckit-git-feature`.
- Every feature MUST pass through the full spec-kit workflow:
  `specify → clarify → plan → tasks → implement → analyze → checklist`.
- The **Constitution Check** in `plan.md` MUST be completed before Phase 0 research begins
  and re-validated after Phase 1 design. The gates are:
  - [ ] Does the feature respect the Cognition/Durability boundary (Principle I)?
  - [ ] Are all new domain capabilities expressed as curated tool definitions (Principle II)?
  - [ ] Do any mutations route through an approval-gated saga (Principle III)?
  - [ ] Does saga-to-service communication use AMQP only (Principle IV)?
  - [ ] Is the orchestration core left domain-agnostic (Principle V)?
  - [ ] Are health checks and OTEL wired for any new service (Principle VI)?
- Commit messages MUST follow Conventional Commits (`type(scope): description`).
- Branch names MUST follow the spec-kit sequential numbering convention (`###-short-name`).

## Governance

This constitution supersedes all other project practices. When a practice conflicts with a
stated principle, the constitution governs.

**Amendments**:
- MAJOR bump: backward-incompatible removal or redefinition of a principle.
- MINOR bump: new principle or section added, or materially expanded guidance.
- PATCH bump: clarification, wording, or non-semantic refinement.
- All amendments MUST document the motivation and update `Last Amended` date.

**Compliance**:
- Every PR plan MUST include a completed Constitution Check (see Development Workflow above).
- Complexity that violates a principle MUST be justified in the plan's Complexity Tracking table.
- The constitution is the authoritative runtime guidance file referenced by `CLAUDE.md`.

**Version**: 1.0.0 | **Ratified**: 2026-05-28 | **Last Amended**: 2026-05-28
