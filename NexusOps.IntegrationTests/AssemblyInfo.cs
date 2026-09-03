using Xunit;

// These tests share one real, expensive DistributedApplication instance (real RabbitMQ, Postgres,
// and domain-service processes) and one of them stops/restarts a domain service mid-run
// (WorkflowOrchestratorIntegrationTests.InvestigationSaga_ReturnsPartialResults_WhenInventoryServiceIsStopped) --
// xUnit's default parallelization would race that against the other tests' own requests against
// the same shared infrastructure. Disabling it here, assembly-wide, is what actually guarantees
// the sequential ordering the tests are written to assume (a bare IClassFixture does not).
[assembly: CollectionBehavior(DisableTestParallelization = true)]
