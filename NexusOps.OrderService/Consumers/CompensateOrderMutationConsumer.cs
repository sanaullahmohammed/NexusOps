using MassTransit;
using NexusOps.Contracts.Messages;
using NexusOps.OrderService.Data;
using NexusOps.OrderService.Models;

namespace NexusOps.OrderService.Consumers;

/// <summary>
/// Reverts an order's status back to what it was before an execution that a later, required
/// dependency (inventory restock) failed to complete (spec.md FR-011, User Story 4).
/// </summary>
public sealed class CompensateOrderMutationConsumer(OrderMutationOverlay overlay) : IConsumer<CompensateOrderMutation>
{
    public Task Consume(ConsumeContext<CompensateOrderMutation> context)
    {
        if (Enum.TryParse<OrderStatus>(context.Message.RevertToStatus, ignoreCase: true, out var revertTo))
        {
            overlay.Set(context.Message.OrderId, revertTo);
        }

        return context.RespondAsync(new OrderMutationCompensated(context.Message.CorrelationId, true));
    }
}
