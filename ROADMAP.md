# NexusOps Roadmap & Delegation Prompts

Commit this file to the repo root. Run each prompt in a **fresh Claude Code session** (Sonnet for specs/implementation, Haiku for housekeeping/docs). Each prompt assumes the model can read this file and the repo.

## Locked Decisions (context for all prompts)

- Messaging: **MassTransit v8** (stay OSS; v9 is commercial) + **RabbitMQ** transport
- Saga persistence: **PostgreSQL + EF Core** saga repository, added to Aspire AppHost via `AddPostgres`, optimistic concurrency
- Approval gate: `POST /api/approvals/{id}/approve` and `/reject` on AgentHost; approval state lives in the saga; agent replies "pending approval, ref #X"; no UI
- Notification Service: minimal Node/TS RabbitMQ consumer that logs simulated emails — nothing more
- Eval harness: console project `NexusOps.Evaluation`, JSON dataset of 20–30 prompts, asserts correct tool selection and direct-vs-saga routing
- `NexusOps.Server` + `frontend/` remain scaffold reference artifacts; no frontend work
- Testing: one Aspire.Hosting.Testing integration test per saga, minimum
- Everything runs locally via `aspire run`; no deployment
- Resume framing: fintech ops → agentic workflows (investigation fan-out ≈ upstream aggregation; approval gate ≈ maker-checker; compensation ≈ reversing partial writes; curated tools ≈ governed API surface)

---

## Prompt 0 — Housekeeping (Haiku)

> Read ROADMAP.md. Review open PR #37 (ServiceDefaults extraction) for correctness against master, then merge it. Rebase/merge the open dependabot PRs on top, resolving conflicts caused by #37's file moves. Do not upgrade MassTransit-related packages beyond v8.x if any appear. Verify `dotnet build` succeeds on master afterward.

## Prompt 1 — README honesty pass (Haiku/Sonnet)

> Read ROADMAP.md, README.md, and CLAUDE.md. Rewrite README so it accurately reflects the current build state: sagas, MassTransit, RabbitMQ, Postgres, Notification Service, and the Evaluation runner are **planned, not implemented** — move them to a clearly labeled Roadmap section (use the roadmap in ROADMAP.md). Remove the run command for the nonexistent `packages/NexusOps.Evaluation`. Mark NexusOps.Server and frontend/ as scaffold reference artifacts. Add a short "Why this project" section framing NexusOps as a POC translating fintech operations engineering (multi-source aggregation, maker-checker approval, compensation on partial failure) into agentic-AI workflows. Keep the existing architecture diagram but label planned components as planned. Do not touch code.

## Prompt 2 — Spec 003: OrderInvestigationSaga (Sonnet)

> Read ROADMAP.md, .specify/memory/constitution.md, and specs/002-session-management/ as a style reference. Using the spec-kit workflow, create spec 003-workflow-orchestrator: a new `NexusOps.WorkflowOrchestrator` project hosting `OrderInvestigationSaga` on MassTransit v8 + RabbitMQ, saga state persisted in PostgreSQL via EF Core (optimistic concurrency). RabbitMQ and Postgres added to the Aspire AppHost. The saga fans out read requests to Order, Inventory, and Product services in parallel, aggregates results, and returns partial results with degradation flags if a service fails or times out. AgentHost gets a new curated tool `investigate_order_anomaly` (defined in NexusOps.Contracts per constitution Principle II) that publishes the command and awaits the saga result (request/response or polling — decide in plan.md). No approval gate in this spec. Honor constitution Principle I strictly: no LLM logic in the orchestrator, no saga logic in AgentHost.

## Prompt 3 — Implement 003 (Sonnet)

> Read ROADMAP.md and specs/003-*/plan.md and tasks.md. Implement the tasks in order. Verify with `aspire run`: RabbitMQ, Postgres, and WorkflowOrchestrator appear healthy in the Aspire dashboard, and a chat request like "investigate order ORD-1002" routes through the saga and returns aggregated results. Update CLAUDE.md's Current Build State when done.

## Prompt 4 — Spec 004 + implement: ActionSaga, approval gate, notification consumer (Sonnet)

> Read ROADMAP.md, the constitution, and specs/003-*. Create and then implement spec 004-approval-actions: `OrderActionSaga` in WorkflowOrchestrator handling refund/cancel/notify commands with a mandatory human approval gate (constitution Principle III). Saga pauses in AwaitingApproval state; AgentHost exposes `POST /api/approvals/{id}/approve` and `/reject`; agent tells the user the action is pending with a reference ID and never claims execution before approval. On approval, saga executes and publishes a NotificationRequested event. Add `notification-service/`: minimal Node.js + TypeScript + amqplib consumer that consumes that event and logs a simulated email; wire it into the Aspire AppHost. Include compensation for partial failure. New curated tools in Contracts for refund/cancel.

## Prompt 5 — Spec 005 + implement: Evaluation runner (Sonnet)

> Read ROADMAP.md and the constitution. Create and implement spec 005-evaluation: console project `NexusOps.Evaluation` with a JSON dataset of 20–30 realistic user prompts, each labeled with expected tool selection and expected path (direct vs saga). Runner sends each prompt to a running AgentHost, captures which tool the agent invoked, and reports pass/fail per case plus a summary table. Keep it dependency-light — no eval frameworks. Update the README's Evaluation section with the real run command.

## Prompt 6 — Integration tests (Sonnet)

> Read ROADMAP.md. Add a test project using Aspire.Hosting.Testing with at least: (1) InvestigationSaga happy path returns aggregated results; (2) InvestigationSaga returns partial results when one domain service is stopped; (3) ActionSaga blocks until approval, executes on approve; (4) ActionSaga rejects cleanly on reject. Wire `dotnet test` into the existing CI workflow.

## Prompt 7 — Final doc reconciliation (Haiku/Sonnet)

> Read ROADMAP.md, README.md, CLAUDE.md, the constitution, and all specs/. Reconcile them: Current Build State sections accurate, roadmap items marked done, architecture diagram matches reality, quickstart verified against actual `aspire run` behavior (all containers: Redis, RabbitMQ, Postgres, notification-service). Ensure the fintech-ops framing appears consistently. Flag—don't silently fix—any constitution violations you find.

---

**Definition of done:** `aspire run` locally → chat with agent → trigger an investigation → request a refund → approve via curl → see notification log → `NexusOps.Evaluation` passes → `dotnet test` green.