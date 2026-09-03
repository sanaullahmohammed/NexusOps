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

        // This saga's Initially(When(Requested)) publishes BeginInvestigationFanOut while consuming
        // the initial request -- without the transactional outbox, that publish is visible to
        // consumers before this SaveChanges commits, letting a fast OrderFindingReported reply race
        // ahead of the saga row it needs to attach to (008-order-investigation-outbox research.md
        // Decision 2; the identical problem OrderActionSaga was already fixed for in feature 006).
        // No UseBusOutbox() here: that opts into BusOutboxDeliveryService, a background poller for
        // publishes made *outside* a consume context -- this saga's only publish
        // (BeginInvestigationFanOut) is always inside one, delivered inline by the endpoint-level
        // UseEntityFrameworkOutbox<T>(context) configured in Program.cs instead. OrderAction's own
        // registration DOES still call UseBusOutbox() -- deliberately kept there, not duplicated
        // here: OrderInvestigationDbContext and OrderActionDbContext share the same physical
        // InboxState/OutboxState/OutboxMessage tables (research.md Decision 4), and a background
        // delivery service is scoped per-table, not per-context, so exactly one of the two
        // registrations should run it -- OrderAction's, since it's the one that actually owns those
        // tables' lifecycle (its migration creates them). DisableInboxCleanupService() here for the
        // same reason: OrderActionDbContext's own InboxCleanupService<OrderActionDbContext> already
        // cleans those rows regardless of which context wrote them; a second cleanup service here
        // would just poll the same rows redundantly.
        configurator.AddEntityFrameworkOutbox<OrderInvestigationDbContext>(o =>
        {
            o.UsePostgres();
            o.DisableInboxCleanupService();
        });

        configurator.AddConsumer<InvestigationFanOutConsumer>();

        configurator.AddRequestClient<RequestOrderFinding>();
        configurator.AddRequestClient<RequestInventoryFinding>();
        configurator.AddRequestClient<RequestProductFinding>();

        return configurator;
    }
}
