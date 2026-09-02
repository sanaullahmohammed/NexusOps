using System.Text.Json;
using MassTransit;
using NexusOps.Contracts.Dtos;
using NexusOps.Contracts.Messages;

namespace NexusOps.WorkflowOrchestrator.OrderInvestigation;

/// <summary>
/// Owns persisted investigation state and finalization only. It never calls a domain service
/// itself — <see cref="InvestigationFanOutConsumer"/> does that — it just reacts to whichever of
/// the three <c>*FindingReported</c> events arrives, records it, and finalizes once all three
/// have reported (see <c>research.md</c> Decision 1).
/// </summary>
public sealed class OrderInvestigationSaga : MassTransitStateMachine<OrderInvestigationSagaState>
{
    public State Investigating { get; private set; } = null!;
    public State Completed { get; private set; } = null!;
    public State Failed { get; private set; } = null!;

    public Event<InvestigateOrderRootCause> Requested { get; private set; } = null!;
    public Event<OrderFindingReported> OrderReported { get; private set; } = null!;
    public Event<InventoryFindingReported> InventoryReported { get; private set; } = null!;
    public Event<ProductFindingReported> ProductReported { get; private set; } = null!;

    public OrderInvestigationSaga()
    {
        InstanceState(x => x.CurrentState);

        // InvestigateOrderRootCause carries no correlation id of its own -- every request starts a
        // brand-new investigation, so a fresh CorrelationId is minted right here.
        Event(() => Requested, x => x.CorrelateById(context => Guid.NewGuid()));

        Event(() => OrderReported, x =>
        {
            x.CorrelateById(context => context.Message.CorrelationId);
            // A finding for a CorrelationId with no matching instance (already finalized/removed,
            // or never existed) is a late or orphaned response -- discard it silently (FR-011).
            x.OnMissingInstance(m => m.Discard());
        });
        Event(() => InventoryReported, x =>
        {
            x.CorrelateById(context => context.Message.CorrelationId);
            x.OnMissingInstance(m => m.Discard());
        });
        Event(() => ProductReported, x =>
        {
            x.CorrelateById(context => context.Message.CorrelationId);
            x.OnMissingInstance(m => m.Discard());
        });

        Initially(
            When(Requested)
                .Then(context =>
                {
                    context.Saga.OrderId = context.Message.OrderId;
                    context.Saga.ResponseAddress = context.ResponseAddress;
                    context.Saga.RequestId = context.RequestId;
                    context.Saga.StartedAt = DateTimeOffset.UtcNow;
                })
                .Publish(context => new BeginInvestigationFanOut(context.Saga.CorrelationId, context.Saga.OrderId))
                .TransitionTo(Investigating));

        During(Investigating,
            When(OrderReported)
                .Then(context =>
                {
                    context.Saga.OrderFinding = context.Message.Status;
                    context.Saga.OrderResultJson = context.Message.Status == SourceFindingStatus.Succeeded
                        ? JsonSerializer.Serialize(context.Message.Order)
                        : null;
                })
                .ThenAsync(context => FinalizeIfCompleteAsync(context)),
            When(InventoryReported)
                .Then(context =>
                {
                    context.Saga.InventoryFinding = context.Message.Status;
                    context.Saga.InventoryResultJson = context.Message.Status == SourceFindingStatus.Succeeded
                        ? JsonSerializer.Serialize(context.Message.Levels)
                        : null;
                })
                .ThenAsync(context => FinalizeIfCompleteAsync(context)),
            When(ProductReported)
                .Then(context =>
                {
                    context.Saga.ProductFinding = context.Message.Status;
                    context.Saga.ProductResultJson = context.Message.Status == SourceFindingStatus.Succeeded
                        ? JsonSerializer.Serialize(context.Message.Products)
                        : null;
                })
                .ThenAsync(context => FinalizeIfCompleteAsync(context)));

        // A finding can still arrive after finalization -- e.g. InvestigationFanOutConsumer's
        // BeginInvestigationFanOut message is redelivered (broker blip, pod eviction) after the
        // saga already finalized from the first attempt's findings, and the rerun's findings land
        // on a saga instance that still exists. Without this, MassTransit's default unhandled-event
        // behavior faults the event instead of the silent discard research.md Decision 1 relies on
        // for restart survival. OnMissingInstance(Discard) above only covers a since-removed
        // instance; this covers one that is very much still here, just done.
        During(Completed, Failed,
            Ignore(OrderReported),
            Ignore(InventoryReported),
            Ignore(ProductReported));
    }

    private async Task FinalizeIfCompleteAsync<TData>(BehaviorContext<OrderInvestigationSagaState, TData> context)
        where TData : class
    {
        var saga = context.Saga;
        if (!saga.AllSourcesReported)
        {
            return;
        }

        var completeness = ComputeCompleteness(saga, out var degradedSources);

        var result = new RootCauseInvestigationResult(
            OrderId: saga.OrderId,
            OrderFinding: saga.OrderFinding,
            Order: saga.OrderResultJson is not null ? JsonSerializer.Deserialize<OrderSummary>(saga.OrderResultJson) : null,
            InventoryFinding: saga.InventoryFinding,
            InventoryLevels: saga.InventoryResultJson is not null
                ? JsonSerializer.Deserialize<InventoryLevel[]>(saga.InventoryResultJson) ?? []
                : [],
            ProductFinding: saga.ProductFinding,
            ProductDetails: saga.ProductResultJson is not null
                ? JsonSerializer.Deserialize<ProductDetail[]>(saga.ProductResultJson) ?? []
                : [],
            Completeness: completeness,
            DegradedSources: degradedSources);

        saga.CompletedAt = DateTimeOffset.UtcNow;

        // The saga responds from whatever consume context happens to finalize it -- never the
        // original request's context -- so the reply is sent explicitly to the address and
        // RequestId captured when InvestigateOrderRootCause was first consumed (research.md
        // Decision 2), rather than via the ConsumeContext.RespondAsync sugar.
        if (saga.ResponseAddress is not null)
        {
            var endpoint = await context.GetSendEndpoint(saga.ResponseAddress);
            await endpoint.Send(result, sendContext => sendContext.RequestId = saga.RequestId);
            saga.ResponseAddress = null;
        }

        await context.TransitionToState(completeness == InvestigationCompleteness.Failed ? Failed : Completed);
    }

    private static InvestigationCompleteness ComputeCompleteness(OrderInvestigationSagaState saga, out string[] degradedSources)
    {
        var degraded = new List<string>();

        if (saga.InventoryFinding is SourceFindingStatus.Unavailable or SourceFindingStatus.TimedOut)
        {
            degraded.Add("Inventory");
        }

        if (saga.ProductFinding is SourceFindingStatus.Unavailable or SourceFindingStatus.TimedOut)
        {
            degraded.Add("Product");
        }

        degradedSources = [.. degraded];

        // The order source itself could not be identified at all -- there is no order to report
        // on, so the whole investigation failed rather than merely degraded.
        if (saga.OrderFinding is SourceFindingStatus.Unavailable or SourceFindingStatus.TimedOut)
        {
            return InvestigationCompleteness.Failed;
        }

        return degraded.Count == 0 ? InvestigationCompleteness.Complete : InvestigationCompleteness.Degraded;
    }
}
