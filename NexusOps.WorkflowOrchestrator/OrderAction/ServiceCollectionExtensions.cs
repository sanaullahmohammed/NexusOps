using MassTransit;
using NexusOps.Contracts.Messages;

namespace NexusOps.WorkflowOrchestrator.OrderAction;

/// <summary>
/// The single seam between the domain-agnostic host and this feature's Order-action-specific saga
/// (mirrors <c>OrderInvestigation/ServiceCollectionExtensions.cs</c>'s role for feature 005).
/// Deleting the <c>OrderAction</c> folder and this one registration call is the whole removal
/// story — nothing else in the host references it.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IBusRegistrationConfigurator AddOrderActionSaga(this IBusRegistrationConfigurator configurator)
    {
        configurator.AddSagaStateMachine<OrderActionSaga, OrderActionSagaState>()
            .EntityFrameworkRepository(r =>
            {
                r.ConcurrencyMode = ConcurrencyMode.Optimistic;
                r.ExistingDbContext<OrderActionDbContext>();
                r.UsePostgres();
            });

        // This saga publishes side-effecting commands (BeginOrderActionExecution ultimately drives
        // real order/inventory mutations), unlike feature 005's read-only saga -- a redelivered
        // message here must not be able to double-execute a mutation. The transactional outbox ties
        // the saga's own state commit and its outbound publish to one transaction (research.md
        // Decision 6).
        configurator.AddEntityFrameworkOutbox<OrderActionDbContext>(o =>
        {
            o.UsePostgres();
            o.UseBusOutbox();
        });

        configurator.AddConsumer<OrderActionValidationConsumer>();
        configurator.AddConsumer<OrderActionExecutionConsumer>();

        // Self-contained: this saga declares its own dependency on the shared RequestOrderFinding
        // contract rather than relying on OrderInvestigation's registration of the same client,
        // so OrderAction remains independently deletable (Constitution V) even though it reuses
        // 005's request/response contract for validation (research.md Decision 1).
        configurator.AddRequestClient<RequestOrderFinding>();
        configurator.AddRequestClient<ExecuteOrderMutation>();
        configurator.AddRequestClient<ExecuteInventoryRestock>();
        configurator.AddRequestClient<CompensateOrderMutation>();

        return configurator;
    }
}
