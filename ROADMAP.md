# NexusOps Roadmap & Delegation Prompts

Run each remaining prompt in a **fresh Claude Code session** (Sonnet for specs/implementation, Haiku for housekeeping/docs). Each prompt assumes the model can read this file and the repo.

## Status

- [x] Prompt 0 — Housekeeping (PR #37 merged; dependency PRs cleared)
- [x] Prompt 1 — README honesty pass (spec-kit feature `004-docs-honesty-pass`)
- [ ] Prompt 2 — Spec 005: OrderInvestigationSaga (renumbered from 004 — that slot was consumed by Prompt 1's spec-kit feature)
- [ ] Prompt 3 — Implement 005
- [ ] Prompt 4 — Spec 006 + implement: approval actions
- [ ] Prompt 5 — Spec 007 + implement: evaluation runner
- [ ] Prompt 6 — Integration tests
- [ ] Prompt 7 — Final documentation reconciliation

## Locked Decisions (context for all prompts)

- Messaging: **MassTransit v8** (stay OSS; v9 is commercial) + **RabbitMQ** transport
- Saga persistence: **PostgreSQL + EF Core** saga repository, added to Aspire AppHost via `AddPostgres`, optimistic concurrency
- Approval gate: `POST /api/approvals/{id}/approve` and `/reject` on AgentHost; approval state lives in the saga; agent replies "pending approval, ref #X"; no UI
- Notification Service: minimal Node/TS RabbitMQ consumer that logs simulated emails — nothing more
- Eval harness: console project `NexusOps.Evaluation`, JSON dataset of 20–30 prompts, asserts correct tool selection and direct-vs-saga routing; it MUST support credential-free dataset validation separately from live model evaluation
- `NexusOps.Server` + `frontend/` remain scaffold reference artifacts; no frontend work
- Testing: one Aspire.Hosting.Testing integration test per saga, minimum
- Local orchestration uses `aspire start` for delegated agent sessions or `dotnet run --project NexusOps.AppHost` for the documented human quickstart; no deployment
- Azure AI credentials are available locally. Build, unit, saga, approval, and Aspire integration tests MUST NOT require them (CI runs credential-free). Live chat and model-based evaluation MUST skip with a clear message when credentials are absent so CI never needs them, but they are expected to pass in local runs where credentials are present.
- When MassTransit is introduced, pin all `MassTransit*` packages to v8.x and add a Dependabot major-version ignore rule; v9 is commercial and out of scope.
- Resume framing: fintech ops → agentic workflows (investigation fan-out ≈ upstream aggregation; approval gate ≈ maker-checker; compensation ≈ reversing partial writes; curated tools ≈ governed API surface)

---

## Prompt 0 — Housekeeping (Complete)

Completed on 2026-08-31: PR #37 was reviewed, corrected, and merged as `3ea131d`. No dependency PRs remain open. The merged solution builds all nine .NET projects and passes all 100 tests.

## Prompt 1 — README honesty pass (Haiku/Sonnet)

> Read ROADMAP.md, README.md, and CLAUDE.md. Preserve the existing honest current-state and Roadmap sections: sagas, MassTransit, RabbitMQ, Postgres, Notification Service, and the Evaluation runner remain **planned, not implemented**, and NexusOps.Server plus frontend/ remain scaffold reference artifacts. Add a short "Why this project" section framing NexusOps as a POC translating fintech operations engineering (multi-source aggregation, maker-checker approval, compensation on partial failure) into agentic-AI workflows. Mark planned components directly in the Tech Stack table, architecture diagram, and Saga Designs section so they cannot be mistaken for implemented components. Do not touch code.

## Prompt 2 — Spec 005: OrderInvestigationSaga (Sonnet)

> Read ROADMAP.md, .specify/memory/constitution.md, specs/002-session-management/ as a style reference, and the existing specs/003-review-remediation/ and specs/004-docs-honesty-pass/ to avoid numbering collisions. Using the full spec-kit workflow, create spec 005-workflow-orchestrator: a new `NexusOps.WorkflowOrchestrator` host using MassTransit v8 + RabbitMQ, with `OrderInvestigationSaga` state persisted in PostgreSQL through EF Core using optimistic concurrency. Add RabbitMQ and Postgres to the Aspire AppHost. The saga fans out AMQP read requests to Order, Inventory, and Product services in parallel, aggregates results, and returns partial results with degradation flags if a service fails or times out. Preserve the existing Direct-path `investigate_order_anomaly` tool and its contract. Add a distinct Saga-path tool named `investigate_order_root_cause` in NexusOps.Contracts for a specific order; AgentHost publishes its command and awaits the saga result using the response strategy chosen in plan.md. Update agent routing instructions to distinguish anomaly listing from cross-service root-cause investigation. No approval gate belongs in this spec. Complete every constitution check, especially Principles I, II, IV, V, and VI; explicitly resolve how Order-specific saga code remains outside the domain-agnostic orchestration core.

## Prompt 3 — Implement 005 (Sonnet)

> Read ROADMAP.md and specs/005-*/plan.md and tasks.md. Implement the tasks in order. Verify with `aspire start` that RabbitMQ, Postgres, and WorkflowOrchestrator appear healthy. Prove the route through unit and integration tests: the `investigate_order_root_cause` handler publishes the command, the saga fans out over AMQP, and the aggregated response returns. Since Azure AI credentials are available, also run the chat smoke test "investigate the root cause for order ORD-1002" and include its output in the completion report. Update CLAUDE.md's Current Build State when done.

## Prompt 4 — Spec 006 + implement: ActionSaga, approval gate, notification consumer (Sonnet)

> Read ROADMAP.md, the constitution, and specs/005-*. Create and then implement spec 006-approval-actions: `OrderActionSaga` in the workflow layer handling refund/cancel/notify commands with a mandatory human approval gate (constitution Principle III). The saga pauses in AwaitingApproval state; AgentHost exposes `POST /api/approvals/{id}/approve` and `/reject`; the agent tells the user the action is pending with a reference ID and never claims execution before approval. On approval, the saga executes and publishes a NotificationRequested event. Add `notification-service/`: minimal Node.js + TypeScript + amqplib consumer that consumes that event and logs a simulated email; wire it into the Aspire AppHost. Include compensation for partial failure. Add distinct curated tools in Contracts for refund and cancellation. Complete the full spec-kit workflow and all constitution checks before implementation.

## Prompt 5 — Spec 007 + implement: Evaluation runner (Sonnet)

> Read ROADMAP.md and the constitution. Create and implement spec 007-evaluation: console project `NexusOps.Evaluation` with a JSON dataset of 20–30 realistic user prompts, each labeled with expected tool selection and expected path (direct vs saga). Keep it dependency-light — no eval frameworks. The runner MUST provide a credential-free `--validate-only` mode that validates dataset schema, unique case IDs, supported tool names, and expected-path values — this is what CI runs. Live mode sends prompts to a running AgentHost, captures which tool the agent invoked, and reports pass/fail per case plus a summary table; live mode MUST exit as skipped with setup guidance (not a failure) when credentials are absent, so CI never breaks. Local runs with credentials available are expected to execute live mode and record results. Update README's Evaluation section with both commands and their credential requirements.

## Prompt 6 — Integration tests (Sonnet)

> Read ROADMAP.md and specs/005-* through specs/007-*. Add a test project using Aspire.Hosting.Testing with at least: (1) InvestigationSaga happy path returns aggregated results; (2) InvestigationSaga returns partial results when one domain service is stopped; (3) ActionSaga blocks until approval and executes on approve; (4) ActionSaga rejects cleanly on reject. These tests MUST exercise application and message-bus boundaries directly and MUST NOT require Azure AI credentials. Wire them into the existing `dotnet test` CI path.

## Prompt 7 — Final doc reconciliation (Haiku/Sonnet)

> Run only after Prompt 6 completes. Read ROADMAP.md, README.md, CLAUDE.md, the constitution, and all specs/. Reconcile them: Current Build State sections accurate, roadmap items marked done, architecture diagram matches reality, and the quickstart verified against actual `aspire start` behavior (all resources healthy: Redis, RabbitMQ, Postgres, WorkflowOrchestrator, domain services, and notification-service). Ensure the fintech-ops framing appears consistently. Flag — do not silently fix — any constitution violations you find.

---

**Credential-free definition of done:** `aspire start` locally → all resources healthy → investigation saga integration tests pass, including degraded partial results → refund remains pending until approval → approve via `POST /api/approvals/{id}/approve` → notification log observed → rejection path passes → `NexusOps.Evaluation --validate-only` passes → `dotnet test` green → README, CLAUDE.md, constitution, and specs are reconciled.

**Live-Azure acceptance (required locally, skipped in CI):** Chat with the agent → trigger a root-cause investigation → request a refund → approve it via curl → observe the notification log → run the live `NexusOps.Evaluation` suite and record its pass rate. Azure AI credentials are available locally; this acceptance check MUST be completed before the roadmap is considered done. It is not a CI gate — CI remains credential-free.