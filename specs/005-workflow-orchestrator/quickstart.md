# Quickstart: Order Root-Cause Investigation Workflow

**Branch**: `005-workflow-orchestrator`

## Prerequisites

Same as the repository root quickstart, plus: Docker Desktop running (RabbitMQ and PostgreSQL are container resources provisioned by Aspire — no separate installation needed).

## Run

```bash
dotnet run --project NexusOps.AppHost
```

The Aspire dashboard should show `rabbitmq`, `postgres` (and its `workfloworchestrator` database), and `workflow-orchestrator` all healthy, alongside the existing `redis`, `order-service`, `inventory-service`, `product-service`, and `agent-host` resources.

## Verify the saga end-to-end (no Azure AI credentials required)

1. **Happy path** — with all three domain services healthy, send a chat request naming a specific order:
   ```bash
   curl -X POST http://localhost:<agent-host-port>/api/chat \
     -H "Content-Type: application/json" \
     -d '{"prompt": "Why is order ORD-1002 having problems?"}'
   ```
   Expect a response synthesizing order, inventory, and product findings (requires Azure AI credentials to phrase the natural-language answer; the underlying tool call itself does not).

2. **Degraded path** — stop the Inventory service (`docker stop` its container, or terminate the process from the Aspire dashboard) and repeat step 1. Expect the response to still address the order and product findings, with an explicit note that inventory data was unavailable.

3. **Full-failure path** — stop all three domain services and repeat step 1. Expect the agent to report that the investigation could not be completed, not a fabricated or empty-looking answer.

4. **Regression check** — confirm `investigate_order_anomaly` is unaffected:
   ```bash
   curl -X POST http://localhost:<agent-host-port>/api/chat \
     -H "Content-Type: application/json" \
     -d '{"prompt": "Show me all delayed orders"}'
   ```
   Response shape and behavior must be identical to before this feature (SC-004).

## Unit test coverage (credential-free, CI-safe)

```bash
dotnet test --filter "FullyQualifiedName~WorkflowOrchestrator"
```

Covers, via MassTransit's in-memory test harness (no real broker/Postgres): the saga's happy-path finalize, degraded finalize (one/two sources down), full-failure finalize, the concurrent-finding optimistic-concurrency race, and the fan-out consumer's per-source timeout/fault mapping.
