# Quickstart: Verifying the Order Investigation Saga Reliability Fix

## Before the fix (reproducing the bug)

```bash
dotnet test NexusOps.IntegrationTests/NexusOps.IntegrationTests.csproj \
  --filter "InvestigationSaga_HappyPath_ReturnsAggregatedResults"
```

Against unfixed code, this fails with `MassTransit.RequestTimeoutException` after ~30s. The Postgres
SQL trace (visible in the test run's captured console output) shows `InventoryFinding`/`ProductFinding`
written on the saga row, `OrderFinding` never written.

## After the fix

```bash
dotnet build NexusOps.deployable.slnf --configuration Release

dotnet test NexusOps.IntegrationTests/NexusOps.IntegrationTests.csproj \
  --configuration Release --no-build \
  --filter "InvestigationSaga_HappyPath_ReturnsAggregatedResults|InvestigationSaga_ReturnsPartialResults_WhenInventoryServiceIsStopped"
```

Both tests are expected to pass. `InvestigationSaga_HappyPath_ReturnsAggregatedResults` should
complete well under its 30s budget (the underlying saga call itself completes in a few seconds per
spec 005's own SC-006).

## Full regression check

```bash
dotnet test NexusOps.deployable.slnf --configuration Release --no-build
```

All four `NexusOps.IntegrationTests` tests, and all of `NexusOps.Tests` (which is unaffected — its
`OrderInvestigationSagaTests.cs` uses an in-memory saga repository with no outbox, so it cannot
exercise this race either way), are expected to pass.

## Manual/local verification (optional, requires Azure AI credentials)

```bash
dotnet run --project NexusOps.AppHost
# once healthy:
curl -X POST http://localhost:<agent-host-port>/api/chat \
  -H "Content-Type: application/json" \
  -d '{"prompt": "investigate the root cause for order ORD-0003"}'
```

Should return a correct, prompt result citing the SKU-ELEC-001 stockout — every time, including on
the very first request against a freshly-started stack (the specific condition that previously
triggered the race).
