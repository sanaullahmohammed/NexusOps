# Quickstart: Approval-Gated Order Actions

**Branch**: `006-approval-actions`

## Prerequisites

Same as `specs/005-workflow-orchestrator/quickstart.md`, plus: Docker Desktop running (`notification-service` is a new container/process resource provisioned by Aspire; no separate installation needed beyond Node.js for local `npm install`).

## Run

```bash
dotnet run --project NexusOps.AppHost
```

The Aspire dashboard should show `notification-service` healthy alongside every resource from feature 005 (`rabbitmq`, `postgres`, `workflow-orchestrator`, `redis`, the three domain services, `agent-host`).

## Verify the approval gate end-to-end (no Azure AI credentials required for the AMQP-level checks; the chat-phrased steps need them)

1. **Request creates a pending reference, not an executed action** — via chat:
   ```bash
   curl -X POST http://localhost:<agent-host-port>/api/chat \
     -H "Content-Type: application/json" \
     -d '{"prompt": "Refund order ORD-0003"}'
   ```
   Expect the agent's reply to state the refund is pending approval and include a reference GUID. Confirm the order is unchanged:
   ```bash
   curl http://localhost:<order-service-port>/orders/ORD-0003
   ```
   `status` must still read `processing` (or whatever it was before this call).

2. **Approval executes the mutation and returns the real outcome**:
   ```bash
   curl -X POST http://localhost:<agent-host-port>/api/approvals/<reference>/approve
   ```
   Expect `200 OK` with `DecisionStatus: "Approved"`, `ExecutionOutcome: "Executed"`. Re-check the order — `status` now reads `refunded`. Check `notification-service`'s logs in the Aspire dashboard for a structured log line with `outcome: "Executed"` and this reference's `correlationId`.

3. **Rejection permanently blocks execution** — repeat step 1 for a different order (or ask to cancel one), then:
   ```bash
   curl -X POST http://localhost:<agent-host-port>/api/approvals/<reference>/reject
   ```
   Expect `DecisionStatus: "Rejected"`. Confirm the order is unchanged, and that a second `/approve` call against the same reference now returns `DecisionStatus: "AlreadyDecided"` rather than executing anything.

4. **Compensation on partial failure** — request a cancellation for an order with line items, then stop the Inventory service (same technique as feature 005's degraded-path verification) *before* approving:
   ```bash
   curl -X POST http://localhost:<agent-host-port>/api/approvals/<reference>/approve
   ```
   Expect `ExecutionOutcome: "FailedAndCompensated"`. Confirm the order's status was reverted to what it was before this call (not left `cancelled`), and that `notification-service` logged an outcome of `"FailedAndCompensated"`, distinct from `"Executed"`.

5. **Unknown/already-decided references are reported, not silently ignored**:
   ```bash
   curl -X POST http://localhost:<agent-host-port>/api/approvals/00000000-0000-0000-0000-000000000000/approve
   ```
   Expect `DecisionStatus: "NotFound"`.

6. **Regression check** — confirm every feature 001/005 read capability is unaffected:
   ```bash
   curl -X POST http://localhost:<agent-host-port>/api/chat \
     -H "Content-Type: application/json" \
     -d '{"prompt": "Why is order ORD-0003 having problems?"}'
   ```
   Response shape and behavior must be identical to before this feature (spec.md SC-006).

## Unit test coverage (credential-free, CI-safe)

```bash
dotnet test --filter "FullyQualifiedName~WorkflowOrchestrator"
```

Covers, via MassTransit's in-memory test harness (no real broker/Postgres/Node process needed): the saga's validation-to-`AwaitingApproval` transition, not-found short-circuit, approve/reject/already-decided/not-found response paths, execution success/failure/compensation finalize logic, and the execution consumer's per-leg timeout/fault mapping and compensation trigger.

## Notification Service local development

```bash
cd notification-service
npm install
npm run build && npm start   # or `npm run dev` for a watch-mode run outside Aspire
```

`ConnectionStrings__rabbitmq` must be set (Aspire injects it automatically when launched via `dotnet run --project NexusOps.AppHost`; for a standalone run, copy the value shown for `rabbitmq` in the Aspire dashboard's connection strings).
