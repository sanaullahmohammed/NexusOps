using MassTransit;
using NexusOps.Contracts.Messages;

namespace NexusOps.WorkflowOrchestrator.OrderInvestigation;

/// <summary>
/// The single seam between the domain-agnostic host and this feature's Order-specific saga
/// (research.md Decision 5). Deleting the <c>OrderInvestigation</c> folder and this one
/// registration call is the whole removal story -- nothing else in the host references it.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IBusRegistrationConfigurator AddOrderInvestigationSaga(this IBusRegistrationConfigurator configurator)
    {
        configurator.AddSagaStateMachine<OrderInvestigationSaga, OrderInvestigationSagaState>()
            .EntityFrameworkRepository(r =>
            {
                r.ConcurrencyMode = ConcurrencyMode.Optimistic;
                r.ExistingDbContext<OrderInvestigationDbContext>();
                r.UsePostgres();
            });

        configurator.AddConsumer<InvestigationFanOutConsumer>();

        configurator.AddRequestClient<RequestOrderFinding>();
        configurator.AddRequestClient<RequestInventoryFinding>();
        configurator.AddRequestClient<RequestProductFinding>();

        return configurator;
    }
}
